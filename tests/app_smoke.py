"""Test a built File2MD through HTTP and Chromium."""
import json, os, shutil, socket, subprocess, time, urllib.request, urllib.error
from pathlib import Path
from playwright.sync_api import sync_playwright, expect
from sample_documents import create_samples
ROOT = Path(__file__).resolve().parents[1]
ARTIFACTS = ROOT / 'artifacts' / 'app-smoke'
ARTIFACTS.mkdir(parents=True, exist_ok=True)
SAMPLES = ARTIFACTS / 'samples'
create_samples(SAMPLES)
DOTNET = os.environ.get('DOTNET_BIN', 'dotnet')
with socket.socket() as reserved:
    reserved.bind(('127.0.0.1', 0)); port = reserved.getsockname()[1]
origin = f'http://127.0.0.1:{port}'
log = (ARTIFACTS / 'server.log').open('w')
process = subprocess.Popen([DOTNET, str(ROOT/'bin/Debug/net9.0/File2MD.dll'), '--no-browser', '--port', str(port), '--path', str(SAMPLES)], cwd=ROOT, stdout=log, stderr=log)
def api(path, body=None, expected=200):
    request = urllib.request.Request(origin+path, data=json.dumps(body).encode() if body is not None else None, headers={'Content-Type':'application/json'})
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            assert response.status == expected
            return json.load(response)
    except urllib.error.HTTPError as error:
        assert error.code == expected, (path, error.code, error.read())
try:
    for _ in range(100):
        try: status = api('/api/status'); break
        except (ConnectionError, urllib.error.URLError): time.sleep(.1)
    else: raise AssertionError('server did not start')
    assert status['application'] == 'File2MD' and status['initialMode'] == 'markdown'
    for path in ['/api/optical/start', '/api/optical/receive/frame', '/api/vba/extract', '/api/vba/inspect']:
        api(path, {}, 404)
    api('/api/folder?mode=optical', expected=400)
    api('/api/folder?mode=vba', expected=400)
    print('PASS application identity and rejection of unsupported APIs and modes')
    with sync_playwright() as pw:
        browser = pw.chromium.launch(headless=True, executable_path=os.environ.get('PLAYWRIGHT_CHROMIUM_EXECUTABLE') or shutil.which('google-chrome') or shutil.which('chromium'), args=['--no-sandbox'])
        page = browser.new_page(service_workers='block')
        errors=[]; page.on('pageerror', lambda error: errors.append(str(error)))
        page.goto(origin)
        expect(page.locator("#markdownFiles .file-row")).to_have_count(5)
        assert page.title() == 'File2MD' and page.locator('[data-page-panel]').count() == 1
        with page.expect_response('**/api/markdown') as reply: page.click('#convertMarkdown')
        result=reply.value.json()
        assert result['convertedCount'] == 5 and result['failedCount'] == 0, result
        content=Path(result['outputPath']).read_text(encoding='utf-8-sig')
        for text in ['Sample document', 'Sample slide', 'Sample cell', 'Sample PDF', 'Sample text', 'excluded.png']:
            assert text in content, text
        assert page.locator('#markdownFiles .file-row').count() == 0
        print('PASS real DOCX/XLSX/PPTX/PDF/text conversion, omission names and UI reset')
        folder=ARTIFACTS/'large-input';folder.mkdir(exist_ok=True)
        (folder/'small.txt').write_text('small content\n')
        (folder/'large.txt').write_text('L'*1048577)
        (folder/'omit.svg').write_text('<svg/>')
        for choice in ['exclude','include']:
            api('/api/folder', {'mode':'markdown','path':str(folder)})
            page.reload();expect(page.locator("#markdownFiles .file-row")).to_have_count(2)
            page.click('#convertMarkdown');page.locator('#markdownLargeDialog').wait_for(state='visible')
            page.keyboard.press('Escape')
            assert page.locator('#markdownFiles [aria-checked=true]').count() == 2
            page.click('#convertMarkdown')
            with page.expect_response('**/api/markdown') as reply:page.click(f'#markdownLargeDialog button[value={choice}]')
            result=reply.value.json();content=Path(result['outputPath']).read_text(encoding='utf-8-sig')
            assert ('L'*100 in content) == (choice=='include')
            assert 'omit.svg' in content and 'large.txt' in content
        print('PASS grouped large-file cancel/exclude/include against real API')
        only=ARTIFACTS/'omissions-only';only.mkdir(exist_ok=True);(only/'omit.svg').write_text('<svg/>')
        api('/api/folder', {'mode':'markdown','path':str(only)})
        page.reload();expect(page.locator("#convertMarkdown")).to_have_text("Export omitted file list")
        with page.expect_response('**/api/markdown') as reply:page.click('#convertMarkdown')
        assert 'omit.svg' in Path(reply.value.json()['outputPath']).read_text(encoding='utf-8-sig')
        assert not errors,errors
        print('PASS omissions-only export; no browser script errors')
        browser.close()
    # A different application's status must never authorize process replacement.
    from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
    import threading
    class Foreign(BaseHTTPRequestHandler):
        def do_GET(self):
            body=json.dumps(dict(status, application='UnrelatedApp', processId=os.getpid())).encode()
            self.send_response(200);self.end_headers();self.wfile.write(body)
        def log_message(self,*args):pass
    foreign=ThreadingHTTPServer(('127.0.0.1',0),Foreign)
    thread=threading.Thread(target=foreign.serve_forever,daemon=True);thread.start()
    try:
        refused=subprocess.run([DOTNET,str(ROOT/'bin/Debug/net9.0/File2MD.dll'),'--no-browser','--port',str(foreign.server_port)],cwd=ROOT,capture_output=True,timeout=15)
        assert refused.returncode != 0
        print('PASS refuses occupied port without replacing the other application')
    finally:foreign.shutdown();foreign.server_close()
finally:
    process.terminate()
    try:process.wait(timeout=5)
    except subprocess.TimeoutExpired:process.kill();process.wait()
    log.close()

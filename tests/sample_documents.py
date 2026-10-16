"""Create minimal, anonymous document inputs using only the Python standard library."""
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED
import struct
import zlib

REL = 'http://schemas.openxmlformats.org/package/2006/relationships'
OFFICE = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'
WORD = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'
SHEET = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'
DRAWING = 'http://schemas.openxmlformats.org/drawingml/2006/main'
SLIDE = 'http://schemas.openxmlformats.org/presentationml/2006/main'


def package(path, main, content_types, entries):
    types = ['<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">',
             '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>',
             '<Default Extension="xml" ContentType="application/xml"/>']
    types += [f'<Override PartName="/{name}" ContentType="{kind}"/>' for name, kind in content_types.items()]
    types.append('</Types>')
    with ZipFile(path, 'w', ZIP_DEFLATED) as archive:
        archive.writestr('[Content_Types].xml', ''.join(types))
        archive.writestr('_rels/.rels', f'<Relationships xmlns="{REL}"><Relationship Id="rId1" Type="{OFFICE}/officeDocument" Target="{main}"/></Relationships>')
        for name, text in entries.items():
            archive.writestr(name, '<?xml version="1.0" encoding="UTF-8"?>' + text)


def create_samples(directory):
    directory = Path(directory)
    directory.mkdir(parents=True, exist_ok=True)
    package(directory/'sample.docx', 'word/document.xml', {
        'word/document.xml': 'application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml'
    }, {
        'word/document.xml': f'<w:document xmlns:w="{WORD}"><w:body>'
        '<w:p><w:r><w:t>Sample document</w:t></w:r></w:p>'
        '<w:tbl><w:tr><w:tc><w:p><w:r><w:t>Sample table</w:t></w:r></w:p></w:tc>'
        '<w:tc><w:p><w:r><w:t>1</w:t></w:r></w:p></w:tc></w:tr></w:tbl>'
        '<w:sectPr/></w:body></w:document>'
    })
    package(directory/'sample.xlsx', 'xl/workbook.xml', {
        'xl/workbook.xml': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml',
        'xl/worksheets/sheet1.xml': 'application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml',
        'xl/sharedStrings.xml': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml'
    }, {
        'xl/workbook.xml': f'<workbook xmlns="{SHEET}" xmlns:r="{OFFICE}"><sheets><sheet name="Sample" sheetId="1" r:id="rId1"/></sheets></workbook>',
        'xl/_rels/workbook.xml.rels': f'<Relationships xmlns="{REL}"><Relationship Id="rId1" Type="{OFFICE}/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="{OFFICE}/sharedStrings" Target="sharedStrings.xml"/></Relationships>',
        'xl/sharedStrings.xml': f'<sst xmlns="{SHEET}" count="1" uniqueCount="1"><si><t>Sample cell</t></si></sst>',
        'xl/worksheets/sheet1.xml': f'<worksheet xmlns="{SHEET}"><sheetData><row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1"><v>1</v></c></row></sheetData></worksheet>'
    })
    package(directory/'sample.pptx', 'ppt/presentation.xml', {
        'ppt/presentation.xml': 'application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml',
        'ppt/slides/slide1.xml': 'application/vnd.openxmlformats-officedocument.presentationml.slide+xml'
    }, {
        'ppt/presentation.xml': f'<p:presentation xmlns:p="{SLIDE}" xmlns:r="{OFFICE}"><p:sldIdLst><p:sldId id="256" r:id="rId1"/></p:sldIdLst></p:presentation>',
        'ppt/_rels/presentation.xml.rels': f'<Relationships xmlns="{REL}"><Relationship Id="rId1" Type="{OFFICE}/slide" Target="slides/slide1.xml"/></Relationships>',
        'ppt/slides/slide1.xml': f'<p:sld xmlns:p="{SLIDE}" xmlns:a="{DRAWING}"><p:cSld><p:spTree><p:sp><p:nvSpPr><p:cNvPr id="2" name="Sample"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr/><p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:t>Sample slide</a:t></a:r></a:p></p:txBody></p:sp></p:spTree></p:cSld></p:sld>'
    })
    stream = b'BT /F1 12 Tf 50 750 Td (Sample PDF) Tj ET\n'
    objects = [b'<< /Type /Catalog /Pages 2 0 R >>',
               b'<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
               b'<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>',
               b'<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>',
               b'<< /Length ' + str(len(stream)).encode() + b' >>\nstream\n' + stream + b'endstream']
    pdf = bytearray(b'%PDF-1.4\n'); offsets = [0]
    for number, body in enumerate(objects, 1):
        offsets.append(len(pdf)); pdf.extend(f'{number} 0 obj\n'.encode()+body+b'\nendobj\n')
    start = len(pdf)
    pdf.extend(f'xref\n0 {len(offsets)}\n0000000000 65535 f \n'.encode())
    for offset in offsets[1:]: pdf.extend(f'{offset:010} 00000 n \n'.encode())
    pdf.extend(f'trailer\n<< /Size {len(offsets)} /Root 1 0 R >>\nstartxref\n{start}\n%%EOF\n'.encode())
    (directory/'sample.pdf').write_bytes(pdf)
    (directory/'sample.txt').write_text('Sample text\nサンプル\n', encoding='utf-8')
    # A single blank pixel, with no textual metadata, exercises binary exclusion.
    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind+data))
    png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', 1, 1, 8, 2, 0, 0, 0))
    png += chunk(b'IDAT', zlib.compress(b'\x00\xff\xff\xff')) + chunk(b'IEND', b'')
    (directory/'excluded.png').write_bytes(png)

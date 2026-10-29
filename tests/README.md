# Tests

The regression suite requires the .NET 9 SDK. Its 14 tests cover text encodings,
source files, output naming, omitted files, and large files. Test sources use
C# 5 syntax for compatibility with Windows PowerShell 5.1.

```bash
dotnet build File2MD.csproj
dotnet run --project tests/File2MD.Regression.csproj -c Release
```

The browser tests require Python, Playwright, and Chromium or Google Chrome.
Build the app in the default Debug configuration before running them:

```bash
python3 -m pip install -r tests/requirements.txt
python3 -m playwright install chromium
python3 tests/app_smoke.py
```

Set `DOTNET_BIN` or `PLAYWRIGHT_CHROMIUM_EXECUTABLE` to use a specific executable.
Otherwise, the script uses `dotnet` from `PATH`, and Google Chrome or Chromium
from `PATH` if available, falling back to Playwright's bundled browser.

`app_smoke.py` creates sample DOCX, XLSX, PPTX, PDF, and text files and converts
them through the UI. It also checks large-file cancellation, exclusion and
inclusion, exports containing only omitted files, rejection of unsupported API
routes, and refusal to replace an unrelated application on an occupied port.
Inputs and logs are saved in `artifacts/app-smoke`; conversion output is beside
the built app under `bin/Debug/net9.0/output`.

To check the Windows context menu scripts:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test-desktop-context.ps1
```

This checks PowerShell syntax and the generated HKCU registration and removal
entries without writing to the registry or launching Explorer. It also runs
on Linux with PowerShell 7 (`pwsh -NoProfile -File ./test-desktop-context.ps1`).

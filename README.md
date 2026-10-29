# File2MD

File2MD converts Office documents, PDFs, and text files into a single Markdown
file. It runs locally and does not require Microsoft Office.

## Windows

Download and extract the repository, or clone it. Keep the folder contents
together and double-click `File2MD.vbs`.

The desktop app requires 64-bit Windows, Windows PowerShell 5.1, and Microsoft
Edge WebView2 Runtime. The launcher compiles the included C# source and caches the result;
you do not need the .NET SDK. Use `File2MD.cmd` to see console messages.
Startup errors are logged in `%LOCALAPPDATA%\File2MD\logs`.

### Optional context menu

Run `install.bat` to add **File2MD** to the current user's context menus.
Administrator access is not required.

- On files and folders, **Open** loads the selection in the app;
  **Convert to Markdown** converts it in a console and shows progress and the output path.
- On a folder background or the desktop, the menu opens the app.
- On Windows 11, these entries appear under **Show more options**.

Run `install.bat` again if you move the folder. To remove an entry, run
`uninstall.bat`, select it with the arrow keys, and press Enter. Press Esc to
cancel. The uninstaller lists all context menu entries registered for the
current user. It backs up the chosen entry in `output\context-menu-backups`
before removing it and displays the backup path.

## Convert files

Choose files or a folder, adjust the selection, and click **Convert to Markdown**.
The app saves the result in `output/<input>_YYYYMMDD-HHmm/`, creating a separate
folder if the name is already in use. Existing output is not overwritten.

Supported inputs:

- Word: `.docx`, `.docm`
- Excel: `.xlsx`, `.xlsm`
- PowerPoint: `.pptx`, `.pptm`
- PDFs with extractable text
- Markdown, plain text, CSV, source code, and text files without extensions
- UTF-8 and UTF-16 with a byte order mark; UTF-8 and CP932 without one

Older Office binary formats and encrypted PDFs are not supported. File2MD does
not perform OCR; scanned PDFs and some PDF encodings may yield no text or
incomplete text.

For selected files larger than 1 MiB, choose **Exclude all** or **Include all**
before exporting. Closing the dialog or pressing Esc keeps the selection and
writes no output. The noninteractive CLI excludes these files by default, as
does the API unless `excludeLargeFiles` is set to `false`.

The output lists omitted files with their relative paths and reasons, including
unselected files, unsupported formats, binary data, and conversion failures.
You can export this list even when no files can be converted. Hidden files are
skipped during folder scans; explicitly selected hidden text files can be converted.
Files with `.svg`, `.trx`, `.lock`, `.sha256`, `.sha512`, `.metadata`, `.map`, or
`.bak` extensions, including backup suffixes, are excluded from the contents.

## Command line

```powershell
.\File2MD.cmd --path ".\input" --mode markdown
.\File2MD.cmd --cli --path ".\input" --mode markdown
.\File2MD.cmd --no-browser
```

`--path` opens a file or folder in the app. Add `--cli` to convert it in the
console. Use `--no-browser` to run only the local HTTP server, `--port` to set
its port (default: 18423), or `--help` to list the options.

## Build and run on Linux

Install the .NET 9 SDK, then run:

```bash
dotnet build File2MD.csproj
./File2MD.sh --no-browser
./File2MD.sh --cli --path ./input
```

Open the printed localhost URL in a browser. File and folder dialogs require
`zenity` or `kdialog`; `--path` and the CLI work without them.

With the Windows PowerShell launcher, `output` is inside the downloaded folder.
With an SDK build, it is beside the compiled executable or DLL.

[Tests](tests/README.md) · [License](LICENSE) · [Third-party notices](THIRD-PARTY-NOTICES.md)

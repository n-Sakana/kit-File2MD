@echo off
setlocal
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -STA -File "%~dp0File2MD.ps1" %*
set "File2MD_EXIT=%ERRORLEVEL%"
if not "%File2MD_EXIT%"=="0" (
    echo.
    echo File2MD stopped with exit code %File2MD_EXIT%.
    pause
)
endlocal & exit /b %File2MD_EXIT%

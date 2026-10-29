' Keep this file ASCII: wscript.exe reads scripts without a UTF-16 byte order
' mark using the system ANSI code page.

Option Explicit

Dim shell
Dim fileSystem
Dim baseDir
Dim scriptPath
Dim command
Dim exitCode
Dim logDir

Set shell = CreateObject("WScript.Shell")
Set fileSystem = CreateObject("Scripting.FileSystemObject")

baseDir = fileSystem.GetParentFolderName(WScript.ScriptFullName)
scriptPath = fileSystem.BuildPath(baseDir, "File2MD.ps1")
command = "powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File " _
    & Chr(34) & scriptPath & Chr(34)

shell.CurrentDirectory = baseDir

' Wait for File2MD so a failure before the WPF window opens can still be
' reported instead of disappearing with the hidden launcher.
exitCode = shell.Run(command, 0, True)

' File2MD.ps1 reserves exit code 3 for failures before the window can report
' them itself. Normal shutdown and replacement of a running File2MD stay quiet.
If exitCode = 3 Then
    logDir = fileSystem.BuildPath( _
        shell.ExpandEnvironmentStrings("%LOCALAPPDATA%"), "File2MD\logs")
    MsgBox _
        "File2MD could not start." & vbCrLf & vbCrLf & _
        "The reason was written to the newest log file in:" & vbCrLf & _
        logDir, _
        vbExclamation, "File2MD"
End If

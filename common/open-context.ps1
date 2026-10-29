# common/open-context.ps1 -- reuse a running File2MD or start it from Explorer.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("app", "markdown")]
    [string]$Mode,
    [string]$Target
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"
$script:File2MDRoot = Split-Path $PSScriptRoot -Parent
$script:StatusUri = "http://127.0.0.1:18423/api/status"
$script:ActivateUri = "http://127.0.0.1:18423/api/activate"

function Get-HttpStatusCode {
    param($ErrorRecord)
    if ($null -eq $ErrorRecord -or $null -eq $ErrorRecord.Exception -or
        $null -eq $ErrorRecord.Exception.Response) {
        return 0
    }

    try {
        return [int]$ErrorRecord.Exception.Response.StatusCode
    }
    catch {
        return 0
    }
}

function Get-RunningFile2MD {
    try {
        $status = Invoke-RestMethod -Method Get -Uri $script:StatusUri -TimeoutSec 2
        if ($null -eq $status -or $status.application -ne "File2MD" -or $status.device -isnot [string] -or
            $status.platform -isnot [string] -or $status.version -isnot [string] -or
            $null -eq $status.capabilities -or [int]$status.processId -lt 1) {
            return $null
        }
        return $status
    }
    catch {
        return $null
    }
}

function Invoke-RunningFile2MD {
    param($Status, [string]$Mode, [string]$Path)

    $request = @{}
    if ($Mode -eq "markdown") {
        $request["mode"] = "markdown"
        $request["path"] = $Path
    }
    $body = ConvertTo-Json $request -Compress
    try {
        $result = Invoke-RestMethod `
            -Method Post `
            -Uri $script:ActivateUri `
            -ContentType "application/json; charset=utf-8" `
            -Headers @{ "X-File2MD-Process-Id" = [string]$Status.processId } `
            -Body ([Text.Encoding]::UTF8.GetBytes($body)) `
            -TimeoutSec 5
        return $null -ne $result -and [bool]$result.activated
    }
    catch {
        $statusCode = Get-HttpStatusCode $_
        if ($statusCode -eq 404 -or $statusCode -eq 409 -or
            $statusCode -eq 503) {
            return $false
        }
        throw
    }
}

try {
    $targetPath = $null
    if ($Mode -eq "markdown") {
        if ([string]::IsNullOrWhiteSpace($Target)) {
            throw "No file or folder was selected for Markdown conversion."
        }
        # A quoted root path ending in '\' can arrive with the final slash changed
        # to '"'. Windows paths cannot contain '"', so restoring it is unambiguous.
        if ($Target.EndsWith('"')) {
            $Target = $Target.Substring(0, $Target.Length - 1) + "\"
        }

        $targetPath = [IO.Path]::GetFullPath($Target)
        if (-not (Test-Path -LiteralPath $targetPath)) {
            throw "Selected item not found: $targetPath"
        }
    }

    $running = Get-RunningFile2MD
    if ($null -ne $running) {
        for ($attempt = 0; $attempt -lt 20; $attempt++) {
            if (Invoke-RunningFile2MD $running $Mode $targetPath) {
                exit 0
            }
            Start-Sleep -Milliseconds 250
            $latest = Get-RunningFile2MD
            if ($null -ne $latest) {
                $running = $latest
            }
        }
        throw "Could not activate the running File2MD desktop window."
    }

    $File2MDScript = Join-Path $script:File2MDRoot "File2MD.ps1"
    if ($Mode -eq "app") {
        & $File2MDScript
    }
    else {
        & $File2MDScript --path $targetPath --mode markdown
    }
    exit $LASTEXITCODE
}
catch {
    try {
        $localData = [Environment]::GetFolderPath(
            [Environment+SpecialFolder]::LocalApplicationData)
        $logDirectory = Join-Path $localData "File2MD\logs"
        New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
        $logPath = Join-Path $logDirectory (
            "File2MD_" + (Get-Date -Format "yyyyMMdd") + ".log")
        $message = "[" + (Get-Date -Format "HH:mm:ss") +
            "] [ERROR] context menu: " + $_.Exception.ToString()
        [IO.File]::AppendAllText(
            $logPath,
            $message + [Environment]::NewLine,
            (New-Object Text.UTF8Encoding($false)))
    }
    catch {
    }
    exit 1
}

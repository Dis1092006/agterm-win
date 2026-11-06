<#
.SYNOPSIS
Installs the copilot-session-resume recipe: copies copilot-resume-hook.ps1 and registers it as a
GitHub Copilot CLI sessionStart and sessionEnd hook.

.DESCRIPTION
The hooks go into a file of their own, hooks\copilot-session-resume.json under the Copilot
configuration directory (COPILOT_HOME, or ~\.copilot). No other file is changed. Running the
installer again changes nothing.

.PARAMETER Uninstall
Removes the hooks file and the copied script instead.
#>
param(
    [switch]$Uninstall,
    [string]$HooksPath = (Join-Path $(if ($env:COPILOT_HOME) { $env:COPILOT_HOME } else { Join-Path $env:USERPROFILE '.copilot' }) 'hooks\copilot-session-resume.json'),
    [string]$InstallDirectory = (Join-Path $env:USERPROFILE '.config\agterm\copilot-restore')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

$scriptName = 'copilot-resume-hook.ps1'
$hookPath = Join-Path $InstallDirectory $scriptName

if ($Uninstall) {
    if (Test-Path -LiteralPath $HooksPath) { Remove-Item -LiteralPath $HooksPath }
    if (Test-Path -LiteralPath $hookPath) { Remove-Item -LiteralPath $hookPath }
    Write-Host "Removed the copilot-session-resume hooks ($HooksPath)"
    exit 0
}

# ConvertTo-Json in Windows PowerShell 5.1 indents to the column of each key, so the file is
# written from a template; each value is a JSON string from ConvertTo-Json.
function Get-Entry([string]$name) {
    $arguments = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $hookPath, $name) |
        ForEach-Object { ConvertTo-Json $_ }
    return @"
    "$name": [
      {
        "type": "command",
        "exec": "powershell.exe",
        "args": [$($arguments -join ', ')],
        "timeoutSec": 10
      }
    ]
"@
}

$text = "{`n  `"version`": 1,`n  `"hooks`": {`n" + (@((Get-Entry 'sessionStart'), (Get-Entry 'sessionEnd')) -join ",`n") + "`n  }`n}`n"
$text = $text -replace "`r`n", "`n"

# UTF-8 without a BOM both ways: Windows PowerShell 5.1 reads a BOM-less file as ANSI by default
$utf8 = New-Object System.Text.UTF8Encoding($false)
$original = $null
if (Test-Path -LiteralPath $HooksPath) { $original = [IO.File]::ReadAllText($HooksPath, $utf8) }
if ($text -ne $original) {
    $directory = Split-Path -Parent $HooksPath
    if (-not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory | Out-Null }
    [IO.File]::WriteAllText($HooksPath, $text, $utf8)
}

if (-not (Test-Path -LiteralPath $InstallDirectory)) { New-Item -ItemType Directory -Path $InstallDirectory | Out-Null }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot $scriptName) -Destination $hookPath -Force
Write-Host "Installed $hookPath as a sessionStart and sessionEnd hook in $HooksPath"
if (-not $env:AGTERMCTL -and -not (Get-Command agtermctl -ErrorAction SilentlyContinue)) {
    Write-Warning 'agtermctl is not on PATH, so the hook cannot pin anything. In agterm-win use Help > Install Command Line Tool, or set AGTERMCTL to agtermctl.exe.'
}

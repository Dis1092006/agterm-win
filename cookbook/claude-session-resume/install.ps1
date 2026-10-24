<#
.SYNOPSIS
Installs the claude-session-resume recipe: copies claude-resume-hook.ps1 and registers it as a
Claude Code SessionStart and SessionEnd hook.

.DESCRIPTION
Other settings and hooks are kept. A settings file that changes is backed up first, as
settings.json.<timestamp>.bak beside it. Running the installer again changes nothing. A hook
from the earlier pin-only version of this recipe (pin-claude-resume.ps1) is replaced.

.PARAMETER Uninstall
Removes the two hooks and the copied script instead.
#>
param(
    [switch]$Uninstall,
    [string]$SettingsPath = (Join-Path $(if ($env:CLAUDE_CONFIG_DIR) { $env:CLAUDE_CONFIG_DIR } else { Join-Path $HOME '.claude' }) 'settings.json'),
    [string]$InstallDirectory = (Join-Path $HOME '.config\agterm\claude-restore')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

$scriptName = 'claude-resume-hook.ps1'
$ownedNames = @($scriptName, 'pin-claude-resume.ps1')
$events = @('SessionStart', 'SessionEnd')
$hookPath = Join-Path $InstallDirectory $scriptName
# UTF-8 without a BOM both ways: Windows PowerShell 5.1 reads a BOM-less file as ANSI by default
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Test-Owned($handler) {
    $words = @()
    if ($handler.PSObject.Properties['command']) { $words += [string]$handler.command }
    if ($handler.PSObject.Properties['args']) { $words += @($handler.args | ForEach-Object { [string]$_ }) }
    foreach ($word in $words) {
        foreach ($name in $ownedNames) {
            if ($word.EndsWith($name, [StringComparison]::OrdinalIgnoreCase)) { return $true }
        }
    }
    return $false
}

# ConvertTo-Json in Windows PowerShell 5.1 indents to the column of each key; this re-indents
# compressed JSON by two spaces per level, as Claude Code writes the file.
function Format-Json([string]$json) {
    $out = New-Object System.Text.StringBuilder
    $depth = 0
    $inString = $false
    for ($i = 0; $i -lt $json.Length; $i++) {
        $c = $json[$i]
        if ($inString) {
            [void]$out.Append($c)
            if ($c -eq [char]92) { [void]$out.Append($json[++$i]) }  # a backslash escapes the next character
            elseif ($c -eq '"') { $inString = $false }
            continue
        }
        switch ($c) {
            '"' { $inString = $true; [void]$out.Append($c) }
            { $_ -eq '{' -or $_ -eq '[' } {
                $close = if ($c -eq '{') { '}' } else { ']' }
                if ($json[$i + 1] -eq $close) { [void]$out.Append($c).Append($close); $i++; break }
                $depth++
                [void]$out.Append($c).Append("`n").Append('  ' * $depth)
            }
            { $_ -eq '}' -or $_ -eq ']' } {
                $depth--
                [void]$out.Append("`n").Append('  ' * $depth).Append($c)
            }
            ',' { [void]$out.Append(",`n").Append('  ' * $depth) }
            ':' { [void]$out.Append(': ') }
            default { [void]$out.Append($c) }
        }
    }
    return $out.ToString() + "`n"
}

$original = $null
if (Test-Path -LiteralPath $SettingsPath) { $original = [IO.File]::ReadAllText($SettingsPath, $utf8) }

if ($original -and $original.Trim()) {
    try { $settings = $original | ConvertFrom-Json }
    catch { throw "Claude settings are not valid JSON: $SettingsPath" }
    if ($settings -isnot [System.Management.Automation.PSCustomObject]) { throw "Claude settings must be a JSON object: $SettingsPath" }
}
else {
    $settings = New-Object PSObject
}

if (-not $settings.PSObject.Properties['hooks']) {
    $settings | Add-Member -NotePropertyName hooks -NotePropertyValue (New-Object PSObject)
}
$hooks = $settings.hooks
if ($hooks -isnot [System.Management.Automation.PSCustomObject]) { throw "Claude hooks must be a JSON object: $SettingsPath" }

foreach ($event in $events) {
    $groups = New-Object System.Collections.ArrayList
    if ($hooks.PSObject.Properties[$event]) {
        foreach ($group in @($hooks.$event)) {
            if ($group -isnot [System.Management.Automation.PSCustomObject] -or -not $group.PSObject.Properties['hooks']) {
                throw "Claude $event contains an invalid hook group: $SettingsPath"
            }
            $kept = @(@($group.hooks) | Where-Object { -not (Test-Owned $_) })
            if ($kept.Count -eq 0) { continue }
            $group.hooks = $kept
            [void]$groups.Add($group)
        }
    }
    if (-not $Uninstall) {
        [void]$groups.Add([PSCustomObject]@{
            hooks = @([PSCustomObject][ordered]@{
                type    = 'command'
                command = 'powershell.exe'
                args    = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $hookPath)
                # SessionEnd hooks share a 1.5 s budget; a longer timeout raises it, and a cold
                # Windows PowerShell start can take most of 1.5 s on its own
                timeout = 10
            })
        })
    }
    # an event keeps its place among the others; one left without groups is dropped
    if ($groups.Count -eq 0) { $hooks.PSObject.Properties.Remove($event) }
    elseif ($hooks.PSObject.Properties[$event]) { $hooks.$event = $groups.ToArray() }
    else { $hooks | Add-Member -NotePropertyName $event -NotePropertyValue $groups.ToArray() }
}

$text = Format-Json ($settings | ConvertTo-Json -Depth 100 -Compress)
if ($text -ne $original) {
    $directory = Split-Path -Parent $SettingsPath
    if (-not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory | Out-Null }
    if ($null -ne $original) {
        Copy-Item -LiteralPath $SettingsPath -Destination ("$SettingsPath.{0:yyyyMMddHHmmss}.bak" -f (Get-Date))
    }
    [IO.File]::WriteAllText($SettingsPath, $text, $utf8)
}

if ($Uninstall) {
    if (Test-Path -LiteralPath $hookPath) { Remove-Item -LiteralPath $hookPath }
    Write-Host "Removed the claude-session-resume hooks from $SettingsPath"
    exit 0
}

if (-not (Test-Path -LiteralPath $InstallDirectory)) { New-Item -ItemType Directory -Path $InstallDirectory | Out-Null }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot $scriptName) -Destination $hookPath -Force
Write-Host "Installed $hookPath as a SessionStart and SessionEnd hook in $SettingsPath"
if (-not $env:AGTERMCTL -and -not (Get-Command agtermctl -ErrorAction SilentlyContinue)) {
    Write-Warning 'agtermctl is not on PATH, so the hook cannot pin anything. In agterm-win use Help > Install Command Line Tool, or set AGTERMCTL to agtermctl.exe.'
}

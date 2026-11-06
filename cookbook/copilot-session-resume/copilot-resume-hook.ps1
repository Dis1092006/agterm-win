# GitHub Copilot CLI hook for agterm-win: a pane that runs Copilot comes back to the same session
# after an app restart, and a pane you left Copilot in comes back as a plain shell.
#
#   sessionStart  pins `copilot --resume=<id>` as the pane's restore command. Copilot fires it at
#                 the first prompt of a session, so the pin follows /clear and /resume.
#   sessionEnd    clears that pin when the session ends with reason user_exit (/exit, /clear,
#                 Ctrl+C, Ctrl+D) or complete (copilot -p). A pin that names another session stays.
#                 Closing the pane also ends Copilot with user_exit, but an app quit stops the
#                 control pipe for a window before it closes the panes, so the clear is refused
#                 and the pin survives the restart.
#
# install.ps1 registers this file for both events and passes the event name as the argument.
# Outside agterm-win it does nothing.
param([string]$EventName)

if (-not $env:AGTERM_SESSION_ID) { exit 0 }
if ($EventName -notin 'sessionStart', 'sessionEnd') { exit 0 }

$hook = [Console]::In.ReadToEnd() | ConvertFrom-Json
# a subagent runs inside its parent's session and pane
if ($hook.agentId -or $hook.parentAgentId) { exit 0 }
# The pin is shell code typed on every launch: only a UUID-shaped id goes in it.
if ([string]$hook.sessionId -notmatch '^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$') { exit 0 }
$pin = "copilot --resume=$($hook.sessionId)"

$tool = if ($env:AGTERMCTL) { $env:AGTERMCTL } else { 'agtermctl' }
$socket = @()
if ($env:AGTERM_SOCKET) { $socket = @('--socket', $env:AGTERM_SOCKET) }
$pane = @('--target', $env:AGTERM_SESSION_ID)
if ($env:AGTERM_PANE_ID) { $pane += @('--pane-id', $env:AGTERM_PANE_ID) }
if ($env:AGTERM_PANE) { $pane += @('--pane', $env:AGTERM_PANE) }

if ($EventName -eq 'sessionStart') {
    & $tool session restore $pin @pane @socket | Out-Null
    exit 0
}

if ($hook.reason -notin 'user_exit', 'complete') { exit 0 }

# Clear only a pin that names this session. After /clear the old session can end after the new
# one has pinned itself.
$windows = @()
if ($env:AGTERM_WINDOW_ID) { $windows += , @('--window', $env:AGTERM_WINDOW_ID) }
$windows += , @()
$node = $null
foreach ($window in $windows) {
    $output = & $tool tree --json @window @socket 2>$null
    if ($LASTEXITCODE -ne 0) { continue }
    try { $tree = (($output -join "`n") | ConvertFrom-Json).result.tree } catch { continue }
    $node = @($tree.workspaces | ForEach-Object { $_.sessions } |
        Where-Object { $_.id -eq $env:AGTERM_SESSION_ID }) | Select-Object -First 1
    if ($node) { break }
}
if (-not $node) { exit 0 }

$kind = $env:AGTERM_PANE
if ($env:AGTERM_PANE_ID) {
    $surface = @($node.surfaces | Where-Object { $_.paneID -eq $env:AGTERM_PANE_ID }) | Select-Object -First 1
    if ($surface) { $kind = $surface.kind }
}
$current = switch ($kind) {
    'right' { $node.splitRestoreCommand }
    'scratch' { $null }
    default { $node.restoreCommand }
}
if ($current -cne $pin) { exit 0 }

& $tool session restore --clear @pane @socket 2>$null | Out-Null
exit 0

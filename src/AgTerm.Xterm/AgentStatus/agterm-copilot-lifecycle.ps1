# agterm-win-copilot-status-hook
param([string]$EventName)

if (-not $env:AGTERM_SESSION_ID) { exit 0 }
$ErrorActionPreference = 'Stop'
$mutex = $null
$locked = $false

function Get-Hash([string]$Text) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text))).Replace('-', '') }
    finally { $sha.Dispose() }
}

try {
    $inputEvent = [Console]::In.ReadToEnd() | ConvertFrom-Json
    if ($inputEvent.agentId -or $inputEvent.parentAgentId) { exit 0 }
    if (-not ($inputEvent.sessionId -is [string]) -or -not $inputEvent.sessionId -or
        $null -eq $inputEvent.timestamp -or $inputEvent.timestamp -is [string]) {
        throw 'Invalid lifecycle input'
    }
    $timestamp = [long]$inputEvent.timestamp
    $session = Get-Hash $inputEvent.sessionId
    $scope = Get-Hash (@($PSScriptRoot, $env:AGTERM_SOCKET, $env:AGTERM_SESSION_ID,
        $env:AGTERM_PANE, $env:AGTERM_PANE_ID) | ConvertTo-Json -Compress)
    $mutex = New-Object Threading.Mutex($false, "Local\agterm-copilot-$scope")
    try { $locked = $mutex.WaitOne(5000) }
    catch [Threading.AbandonedMutexException] { $locked = $true }
    if (-not $locked) { throw 'Status lock timed out' }

    $directory = Join-Path $PSScriptRoot 'copilot-state'
    $path = Join-Path $directory "$scope.json"
    $previous = $null
    if (Test-Path -LiteralPath $path) {
        $previous = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        if ($timestamp -lt $previous.timestamp) { exit 0 }
        if ($previous.session -ne $session -and $EventName -notin @('sessionStart', 'userPromptSubmitted')) { exit 0 }
        if ($previous.session -eq $session -and $previous.ended -and $EventName -ne 'sessionStart') { exit 0 }
    }
    $status = 'idle'
    if ($previous -and $previous.session -eq $session) { $status = $previous.status }
    $send = $null
    $flag = ''
    $ended = $false
    switch ($EventName) {
        'sessionStart' {
            if ($previous -and $previous.session -ne $session -and $previous.status -in @('active', 'blocked')) {
                $send = 'idle'
            }
        }
        'userPromptSubmitted' { $send = 'active'; $flag = '--blink' }
        { $_ -in @('postToolUse', 'postToolUseFailure') } { $send = 'active'; $flag = '--blink' }
        'agentStop' { $send = 'completed'; $flag = '--auto-reset' }
        'notification' {
            if ($inputEvent.notification_type -in @('permission_prompt', 'elicitation_dialog')) { $send = 'blocked' }
        }
        'errorOccurred' {
            if ($inputEvent.recoverable -is [bool] -and -not $inputEvent.recoverable -and $status -ne 'idle') {
                $send = 'idle'
            }
        }
        'sessionEnd' {
            $ended = $true
            if ($status -ne 'idle' -and
                ($status -ne 'completed' -or $inputEvent.reason -notin @('complete', 'user_exit'))) { $send = 'idle' }
        }
        default { throw 'Unknown lifecycle event' }
    }
    if ($send) { $status = $send }
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    # Persist only ordering/ownership metadata, never prompts, errors or transcript paths.
    $contents = @{ session = $session; timestamp = $timestamp; status = $status; ended = $ended } |
        ConvertTo-Json -Compress
    $temporary = "$path.$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        [IO.File]::WriteAllText($temporary, $contents)
        # Windows PowerShell converts $null to an empty path for this string argument.
        if ([IO.File]::Exists($path)) { [IO.File]::Replace($temporary, $path, [NullString]::Value) }
        else { [IO.File]::Move($temporary, $path) }
    }
    finally { if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) } }
    if ($send) {
        & (Join-Path $PSScriptRoot 'agterm-agent-status.cmd') $send $flag 'agterm-win' | Out-Null
    }
}
catch {
    $message = 'Copilot status hook failed (invalid input or unavailable local state).'
    try { Add-Content -LiteralPath (Join-Path $PSScriptRoot 'agent-status.log') -Value $message }
    catch { [Console]::Error.WriteLine('Could not write agent-status.log. ' + $message) }
}
finally {
    if ($locked) { $mutex.ReleaseMutex() }
    if ($mutex) { $mutex.Dispose() }
}
exit 0

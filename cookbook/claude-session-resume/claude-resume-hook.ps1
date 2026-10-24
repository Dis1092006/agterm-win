# Claude Code hook for agterm-win: a pane that runs Claude comes back to the same conversation
# after an app restart, and a pane you left Claude in comes back as a plain shell.
#
#   SessionStart  pins `claude --resume <live id>` as the pane's restore command. It fires on
#                 startup, /resume, /clear and compaction, so the pin tracks the live conversation.
#   SessionEnd    clears the pin when you leave Claude yourself (/exit, Ctrl+D, /logout). Closing
#                 the pane, which an app quit does, ends Claude with reason "other" and keeps it.
#
# install.ps1 registers this file for both events. Outside agterm-win it does nothing.
if (-not $env:AGTERM_SESSION_ID) { exit 0 }

$hook = [Console]::In.ReadToEnd() | ConvertFrom-Json

$restore = switch ($hook.hook_event_name) {
    'SessionStart' {
        # The pin is shell code typed on every launch: only a UUID-shaped id goes in it.
        if ($hook.session_id -notmatch '^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$') { exit 0 }
        @("claude --resume $($hook.session_id)")
    }
    'SessionEnd' {
        if ($hook.reason -notin 'prompt_input_exit', 'logout') { exit 0 }
        @('--clear')
    }
    default { exit 0 }
}

$tool = if ($env:AGTERMCTL) { $env:AGTERMCTL } else { 'agtermctl' }
$arguments = @('session', 'restore') + $restore + @('--target', $env:AGTERM_SESSION_ID)
if ($env:AGTERM_PANE_ID) { $arguments += @('--pane-id', $env:AGTERM_PANE_ID) }
if ($env:AGTERM_PANE) { $arguments += @('--pane', $env:AGTERM_PANE) }
if ($env:AGTERM_SOCKET) { $arguments += @('--socket', $env:AGTERM_SOCKET) }

& $tool @arguments
exit 0

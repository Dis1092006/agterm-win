# agterm-win shell integration for PowerShell (5.1 and 7+).
#
# Reports the working directory to the terminal as OSC 9;9 on every prompt, which is what keeps
# Session.CurrentCwd live and lets a restored session reopen where it left off. Windows shells do
# not report their cwd on their own; upstream gets the equivalent OSC 7 from libghostty's own
# shell integration, which a WebView2/xterm.js surface has no analogue of (see PLATFORM-NOTES).
# Also reports when PSReadLine reads input (OSC 7777, see the PSConsoleHostReadLine wrapper below).
#
# Dot-sourced by the spawned shell via `-NoExit -Command`, so it runs AFTER the user's profile:
# whatever set $function:prompt (Oh My Posh, starship, posh-git, a hand-written one) is already in
# place and gets wrapped rather than replaced.
#
# Deliberately does NOT emit OSC 0/1/2. Session.DisplayName prefers oscTitle over the cwd
# basename, so auto-titling would overwrite every sidebar row with a noisy path — upstream
# disables it for the same reason (`shell-integration-features = no-title`).

# Nothing to report to: a plain shell outside agterm sources this as a no-op.
if (-not $env:AGTERM_ENABLED) { return }

# Idempotent: a second dot-source would otherwise wrap our own wrapper and emit twice per prompt.
if ($global:__AgTermState -and $global:__AgTermState.OriginalPrompt) { return }

# ConstrainedLanguage forbids the scriptblock work below. Bail out whole rather than half-install.
if ($ExecutionContext.SessionState.LanguageMode -ne 'FullLanguage') { return }

$global:__AgTermState = @{
    # Captured as a VALUE, not by name: the wrapper below calls this scriptblock, and looking
    # `prompt` up by name at call time would find the wrapper itself and recurse forever.
    OriginalPrompt = $function:prompt
}

function global:prompt {
    # The OSC goes straight to the console, never into the returned string: PSReadLine measures
    # the prompt it gets back to place the cursor, and counting invisible bytes would corrupt
    # line editing on wrap.
    #
    # Only a real filesystem location is reported. In a registry or certificate provider
    # ($PWD.Provider.Name -ne 'FileSystem') ProviderPath is meaningless as a spawn directory.
    try {
        if ($PWD.Provider.Name -eq 'FileSystem') {
            $p = $PWD.ProviderPath
            [Console]::Write("$([char]27)]9;9;`"$p`"$([char]7)")
        }
    }
    catch {
        # A prompt that throws leaves the user without a usable shell, so reporting is
        # best-effort: on any failure the original prompt below still renders.
    }

    # $? and $LASTEXITCODE belong to the user's last command. Reading $PWD above does not disturb
    # them, but the original prompt is invoked last so it observes the state it would have seen.
    & $global:__AgTermState.OriginalPrompt
}

# PSReadLine reads each command line through PSConsoleHostReadLine. The wrapper tells the terminal,
# with the private OSC 7777, when PSReadLine reads input and which key runs its Paste function. While
# it does, the terminal sends that key to PSReadLine instead of typing the clipboard in: PSReadLine
# then inserts the text as one edit, so one Ctrl+Z undoes the paste and a newline does not run the
# line. Ctrl+letter keys on a non-Latin layout are then sent with the Latin letter, so PSReadLine's
# bindings such as Ctrl+z run. Without PSReadLine (a screen reader session) the function does not
# exist and nothing changes.
if (Test-Path Function:\PSConsoleHostReadLine) {
    # Captured as a value, the same as the prompt above.
    $global:__AgTermState.OriginalReadLine = $function:PSConsoleHostReadLine

    function global:PSConsoleHostReadLine {
        [System.Diagnostics.DebuggerHidden()]
        param()
        $lastRunStatus = $?   # the user's last command; read first, every statement resets $?

        # Checked on every read, so Set-PSReadLineKeyHandler and -EditMode apply at the next prompt.
        # The key is compared exactly: Vi's command-mode '<Ctrl+v>' is not the insert-mode 'Ctrl+v'.
        # Shift+Insert comes first because ConPTY reads its sequence in every keyboard mode.
        $paste = ''
        try {
            $bound = Get-PSReadLineKeyHandler -Bound
            if ($bound | Where-Object { $_.Key -eq 'Shift+Insert' -and $_.Function -eq 'Paste' }) {
                $paste = ';paste=shift-insert'
            }
            elseif ($bound | Where-Object { $_.Key -eq 'Ctrl+v' -and $_.Function -eq 'Paste' }) {
                $paste = ';paste=ctrl-v'
            }
        }
        catch {
            # Without the binding the terminal pastes as before.
        }

        [Console]::Write("$([char]27)]7777;agterm-readline=1$paste$([char]7)")
        try {
            # PSReadLine's own function reads $? first and uses it, for example for the
            # -PromptText error color. The statements above set $? to True, so after a failed
            # command a failing statement sets it back to False. -ErrorAction Ignore keeps the
            # error out of $Error.
            if (-not $lastRunStatus) { Write-Error 'agterm' -ErrorAction Ignore }
            & $global:__AgTermState.OriginalReadLine
        }
        finally {
            # Also when the line is accepted, cancelled with Ctrl+C, or ReadLine throws.
            [Console]::Write("$([char]27)]7777;agterm-readline=0$([char]7)")
        }
    }
}

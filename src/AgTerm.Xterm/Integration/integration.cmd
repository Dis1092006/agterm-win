@echo off
rem agterm-win shell integration for cmd.exe.
rem
rem Reports the working directory to the terminal as OSC 9;9 on every prompt, which is what keeps
rem Session.CurrentCwd live and lets a restored session reopen where it left off.
rem
rem Attached with `cmd /k call <this file>`. Unlike the other shells this needs no wrapper
rem function: cmd has no prompt hook, but its PROMPT variable is itself a template expanded on
rem every redraw, so embedding the escape in the template IS the per-prompt hook.
rem   $E - ESC        $P - the current drive and path        $G - '>'
rem cmd expands these when it draws the prompt, so $P is always the live directory.
rem
rem Deliberately does NOT emit OSC 0/1/2: Session.DisplayName prefers oscTitle over the cwd
rem basename, so auto-titling would overwrite every sidebar row with a noisy path.

rem Nothing to report to: running this outside agterm leaves the prompt untouched.
if not defined AGTERM_ENABLED goto :eof

rem Idempotent: a second call would prepend the escape twice and report the cwd twice per prompt.
if defined AGTERM_PROMPT_WRAPPED goto :eof

rem A user PROMPT stays visible: the escape is invisible, so it is prefixed rather than replacing
rem the template. An unset PROMPT means cmd's built-in default, which is "$P$G".
if not defined PROMPT set "PROMPT=$P$G"
set "PROMPT=$E]9;9;"$P"$E\%PROMPT%"
set "AGTERM_PROMPT_WRAPPED=1"

rem The path is quoted in the payload because a cmd path may contain spaces, and the terminator is
rem ST ($E\) rather than BEL: cmd's own $E\ is the portable spelling here, and the parser accepts
rem either terminator.
goto :eof

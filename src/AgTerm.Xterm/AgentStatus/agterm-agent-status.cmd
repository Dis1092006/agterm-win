@echo off
rem agterm-win-status-hook
if not defined AGTERM_SESSION_ID exit /b 0
setlocal EnableExtensions EnableDelayedExpansion
if defined AGTERMCTL (set "tool=!AGTERMCTL!") else set "tool=__AGTERMCTL_PATH__"
set "socketArgs="
set "paneArgs="
set "paneIdArgs="
if defined AGTERM_SOCKET set "socketArgs=--socket "!AGTERM_SOCKET!""
if defined AGTERM_PANE set "paneArgs=--pane "!AGTERM_PANE!""
if defined AGTERM_PANE_ID set "paneIdArgs=--pane-id "!AGTERM_PANE_ID!""
set "statusFlag="
if "%~2"=="--blink" set "statusFlag=--blink"
if "%~2"=="--auto-reset" set "statusFlag=--auto-reset"
set "errorFile=!TEMP!\agterm-status-!RANDOM!-!RANDOM!-!RANDOM!.tmp"
(type nul >"!errorFile!") 2>nul
if not exist "!errorFile!" (
    call "!tool!" session status "%~1" --target "!AGTERM_SESSION_ID!" !socketArgs! !paneArgs! !paneIdArgs! !statusFlag! >nul
    echo Could not capture agent-status stderr in a temporary file. 1>&2
    exit /b 0
)
call "!tool!" session status "%~1" --target "!AGTERM_SESSION_ID!" !socketArgs! !paneArgs! !paneIdArgs! !statusFlag! >nul 2>"!errorFile!"
set "statusExit=!errorlevel!"
if not "!statusExit!"=="0" (
    set "statusLog=%~dp0agent-status.log"
    powershell.exe -NoLogo -NoProfile -NonInteractive -Command "try { [IO.File]::AppendAllText($env:statusLog, ('[{0:o}] Status delivery failed: exit code {1}' -f [DateTime]::UtcNow, $env:statusExit) + [Environment]::NewLine + [IO.File]::ReadAllText($env:errorFile)) } catch { [Console]::Error.WriteLine('Could not write agent-status.log; status delivery exit code ' + $env:statusExit + '.'); [Console]::Error.Write([IO.File]::ReadAllText($env:errorFile)) }"
)
del /q "!errorFile!" >nul 2>&1
exit /b 0

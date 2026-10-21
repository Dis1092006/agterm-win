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
call "!tool!" session status "%~1" --target "!AGTERM_SESSION_ID!" !socketArgs! !paneArgs! !paneIdArgs! !statusFlag! >nul 2>&1
exit /b 0

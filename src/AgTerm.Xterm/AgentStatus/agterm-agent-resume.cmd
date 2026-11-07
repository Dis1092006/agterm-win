@echo off
rem agterm-win-resume-hook
if not defined AGTERM_SESSION_ID exit /b 0
setlocal EnableExtensions EnableDelayedExpansion
if defined AGTERMCTL (set "tool=!AGTERMCTL!") else set "tool=__AGTERMCTL_PATH__"
rem agent-hook always exits 0, so a problem shows only on stderr.
set "errorFile=!TEMP!\agterm-resume-!RANDOM!-!RANDOM!-!RANDOM!.tmp"
(type nul >"!errorFile!") 2>nul
if not exist "!errorFile!" (
    call "!tool!" agent-hook resume "%~1" "%~2" >nul 2>nul
    exit /b 0
)
call "!tool!" agent-hook resume "%~1" "%~2" >nul 2>"!errorFile!"
for %%F in ("!errorFile!") do set "errorSize=%%~zF"
if not "!errorSize!"=="0" (
    >>"%~dp0agent-resume.log" (
        echo [%date% %time%] agent-hook resume %~1 %~2
        type "!errorFile!"
    ) 2>nul
)
del /q "!errorFile!" >nul 2>&1
exit /b 0

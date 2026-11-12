@echo off
setlocal
set "DYNA_UNINSTALL_SCRIPT=%~dp0uninstall.ps1"
(
 powershell.exe -NoProfile -File "%DYNA_UNINSTALL_SCRIPT%"
 pause
 exit /b
)

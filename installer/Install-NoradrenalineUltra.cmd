@echo off
setlocal
set "SCRIPT=%~dp0Install-NoradrenalineUltra.ps1"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%"
if errorlevel 1 (
    echo.
    echo Installation failed. See the error above.
    pause
    exit /b 1
)
exit /b 0

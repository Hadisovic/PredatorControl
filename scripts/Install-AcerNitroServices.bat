@echo off
setlocal
rem Explicit repair goes through the application signature and consent checks.
set "APP=%~dp0..\PredatorControlApp.exe"
if not exist "%APP%" (
    echo PredatorControlApp.exe was not found. Use Repair OEM Services in the application.
    exit /b 1
)
"%APP%" --repair-services
exit /b %errorlevel%

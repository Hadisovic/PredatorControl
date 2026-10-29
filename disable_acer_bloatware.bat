@echo off
setlocal
if not exist "%~dp0PredatorControlApp.exe" (
    echo PredatorControlApp.exe was not found. Use Optimize Acer Services in the application.
    exit /b 1
)
"%~dp0PredatorControlApp.exe" --optimize-services
exit /b %errorlevel%

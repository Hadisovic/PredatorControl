@echo off
setlocal
:: Check for administrative rights
net session >nul 2>&1
if %errorLevel% NEQ 0 (
    echo =======================================================
    echo   Requesting Administrator privileges...
    echo =======================================================
    powershell -Command "Start-Process cmd -ArgumentList '/c \"\"%~f0\"\"' -Verb RunAs"
    exit /b
)

title Predator Control - Restore Acer Services
cls
echo =================================================================
echo        Predator Control - Restore Acer Services
echo =================================================================
echo.
echo Re-enabling and starting all Acer OEM services...

sc config AASSvc start= auto >nul 2>&1
net start AASSvc >nul 2>&1

sc config AcerLightingService start= auto >nul 2>&1
net start AcerLightingService >nul 2>&1

sc config AcerCCAgentSvis start= auto >nul 2>&1
net start AcerCCAgentSvis >nul 2>&1

sc config AcerQAAgentSvis start= auto >nul 2>&1
net start AcerQAAgentSvis >nul 2>&1

sc config AcerDIAgentSvis start= auto >nul 2>&1
net start AcerDIAgentSvis >nul 2>&1

sc config ASMSvc start= auto >nul 2>&1
net start ASMSvc >nul 2>&1

sc config AcerServiceSvc start= auto >nul 2>&1
net start AcerServiceSvc >nul 2>&1

sc config AcerDeviceEnablingServiceV2 start= auto >nul 2>&1
net start AcerDeviceEnablingServiceV2 >nul 2>&1

echo.
echo [OK] All Acer OEM services have been restored to Automatic and started.
echo =================================================================
echo.
pause

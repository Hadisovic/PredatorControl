@echo off
:: ============================================================================
:: PredatorControl - Acer Nitro OEM Hardware Services Restorer & Battery Limit
:: ============================================================================
:: Run this as Administrator on an Acer Nitro laptop if OEM services were
:: disabled, uninstalled, or if battery 80% limit / CoolBoost needs activation.

echo [1/4] Checking Administrator Privileges...
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: Administrator privileges required!
    echo Please right click this file and select "Run as administrator".
    pause
    exit /b 1
)

echo [2/4] Enabling and Starting Hardware Services for Nitro...
:: ASMSvc (Acer System Management Service for battery charge limit)
sc config "ASMSvc" start= auto >nul 2>&1
net start "ASMSvc" >nul 2>&1

:: AcerDeviceEnablingService / V2 (AcerIO.sys kernel driver bridge)
sc config "AcerDeviceEnablingServiceV2" start= auto >nul 2>&1
net start "AcerDeviceEnablingServiceV2" >nul 2>&1
sc config "AcerDeviceEnablingService" start= auto >nul 2>&1
net start "AcerDeviceEnablingService" >nul 2>&1

:: AcerServiceSvc
sc config "AcerServiceSvc" start= auto >nul 2>&1
net start "AcerServiceSvc" >nul 2>&1

:: AcerLightingService (if 4-zone RGB model)
sc config "AcerLightingService" start= auto >nul 2>&1
net start "AcerLightingService" >nul 2>&1

echo [3/4] Configuring Battery 80%% Health Limit Registry Keys...
reg add "HKLM\SOFTWARE\OEM\AcerCareCenter\Battery" /v StopCharging /t REG_DWORD /d 80 /f >nul 2>&1
reg add "HKLM\SOFTWARE\OEM\AcerCareCenter\Battery" /v HealthControl /t REG_DWORD /d 1 /f >nul 2>&1
reg add "HKLM\SOFTWARE\OEM\AcerCareCenter\Battery" /v BatteryLimit /t REG_DWORD /d 1 /f >nul 2>&1
reg add "HKLM\SOFTWARE\OEM\AcerCareCenter\Battery" /v LimitPercent /t REG_DWORD /d 80 /f >nul 2>&1

reg add "HKLM\SOFTWARE\WOW6432Node\OEM\AcerCareCenter\Battery" /v StopCharging /t REG_DWORD /d 80 /f >nul 2>&1
reg add "HKLM\SOFTWARE\WOW6432Node\OEM\AcerCareCenter\Battery" /v HealthControl /t REG_DWORD /d 1 /f >nul 2>&1
reg add "HKLM\SOFTWARE\WOW6432Node\OEM\AcerCareCenter\Battery" /v BatteryLimit /t REG_DWORD /d 1 /f >nul 2>&1
reg add "HKLM\SOFTWARE\WOW6432Node\OEM\AcerCareCenter\Battery" /v LimitPercent /t REG_DWORD /d 80 /f >nul 2>&1

echo [4/4] Checking DriverStore for any unregistered Acer OEM drivers...
for /r "C:\Windows\System32\DriverStore\FileRepository" %%i in (acerdeviceenablingservicecomponent.inf acerservicecomponent.inf) do (
    if exist "%%i" (
        echo Found OEM Component: %%i
        pnputil /add-driver "%%i" /install >nul 2>&1
    )
)

echo.
echo ============================================================================
echo DONE! Hardware bridging services and battery 80%% limit have been enabled.
echo You can now launch PredatorControl-standalone.exe!
echo ============================================================================
pause

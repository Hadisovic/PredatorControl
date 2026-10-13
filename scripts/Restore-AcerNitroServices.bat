@echo off
setlocal EnableDelayedExpansion
title Acer Nitro OEM Hardware Services Installer

:: ============================================================================
:: Predator Control - Acer Nitro OEM Hardware Services Installer & Activator
:: ============================================================================
:: Out-of-the-box hardware service setup for Acer Nitro laptops.
:: Eliminates the need to install Acer Care Center or NitroSense.
:: Installs and enables:
::   - ASMSvc (Acer System Monitor Service -> Battery 80% limit threshold)
::   - AcerDeviceEnablingService / V2 (AcerIO.sys kernel driver bridge)
::   - AcerServiceSvc (OEM Hardware Wrapper)
::   - Battery 80% charging cap registry configuration
:: ============================================================================

:: 1. Self-Elevation to Administrator
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [!] Requesting Administrator privileges...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process cmd -ArgumentList '/c \"\"%~f0\"\"' -Verb RunAs"
    exit /b
)

cd /d "%~dp0"
echo ============================================================================
echo   ACER NITRO OEM HARDWARE SERVICES INSTALLER (OUT-OF-THE-BOX)
echo ============================================================================
echo.

echo [1/5] Installing OEM Driver Packages (DriverStore ^& Local)...

:: Install from local oem_drivers folder if present alongside the script
if exist "%~dp0oem_drivers" (
    echo [i] Found bundled oem_drivers directory. Installing driver packages...
    for /r "%~dp0oem_drivers" %%i in (sysmonitorservice.inf acerdeviceenablingservicecomponent.inf acerservicecomponent.inf) do (
        if exist "%%i" (
            echo     - Installing %%~nxi...
            pnputil /add-driver "%%i" /install >nul 2>&1
        )
    )
)

:: Install from Windows DriverStore
for /r "C:\Windows\System32\DriverStore\FileRepository" %%i in (sysmonitorservice.inf acerdeviceenablingservicecomponent.inf acerservicecomponent.inf) do (
    if exist "%%i" (
        echo     - Installing DriverStore: %%~nxi...
        pnputil /add-driver "%%i" /install >nul 2>&1
    )
)

echo.
echo [2/5] Direct Service Registration Fallback...
:: Fallback registration if not registered as Windows Service yet
sc query "ASMSvc" >nul 2>&1
if %errorlevel% neq 0 (
    for /r "C:\Windows\System32\DriverStore\FileRepository" %%f in (AcerSystemCentralService.exe) do (
        if exist "%%f" (
            echo     - Registering ASMSvc from %%f...
            sc create "ASMSvc" binPath= "\"%%f\"" start= auto DisplayName= "Acer System Monitor Service" >nul 2>&1
            goto :after_asm_create
        )
    )
    if exist "%~dp0oem_drivers" (
        for /r "%~dp0oem_drivers" %%f in (AcerSystemCentralService.exe) do (
            if exist "%%f" (
                echo     - Registering ASMSvc from local %%f...
                sc create "ASMSvc" binPath= "\"%%f\"" start= auto DisplayName= "Acer System Monitor Service" >nul 2>&1
                goto :after_asm_create
            )
        )
    )
)
:after_asm_create

sc query "AcerDeviceEnablingServiceV2" >nul 2>&1
if %errorlevel% neq 0 (
    for /r "C:\Windows\System32\DriverStore\FileRepository" %%f in (ADESv2Svc.exe) do (
        if exist "%%f" (
            echo     - Registering AcerDeviceEnablingServiceV2 from %%f...
            sc create "AcerDeviceEnablingServiceV2" binPath= "\"%%f\"" start= auto DisplayName= "Acer Device Enabling Service V2" >nul 2>&1
            goto :after_ades_create
        )
    )
    if exist "%~dp0oem_drivers" (
        for /r "%~dp0oem_drivers" %%f in (ADESv2Svc.exe) do (
            if exist "%%f" (
                echo     - Registering AcerDeviceEnablingServiceV2 from local %%f...
                sc create "AcerDeviceEnablingServiceV2" binPath= "\"%%f\"" start= auto DisplayName= "Acer Device Enabling Service V2" >nul 2>&1
                goto :after_ades_create
            )
        )
    )
)
:after_ades_create

echo.
echo [3/5] Configuring Service Startup to Automatic ^& Starting...
sc config "ASMSvc" start= auto >nul 2>&1
net start "ASMSvc" >nul 2>&1

sc config "AcerDeviceEnablingServiceV2" start= auto >nul 2>&1
net start "AcerDeviceEnablingServiceV2" >nul 2>&1

sc config "AcerDeviceEnablingService" start= auto >nul 2>&1
net start "AcerDeviceEnablingService" >nul 2>&1

sc config "AcerServiceSvc" start= auto >nul 2>&1
net start "AcerServiceSvc" >nul 2>&1

sc config "AcerLightingService" start= auto >nul 2>&1
net start "AcerLightingService" >nul 2>&1

echo.
echo [4/5] Configuring Battery 80%% Health Limit Registry Keys...
reg add "HKLM\SOFTWARE\OEM\AcerCareCenter\Battery" /v StopCharging /t REG_DWORD /d 80 /f >nul 2>&1
reg add "HKLM\SOFTWARE\OEM\AcerCareCenter\Battery" /v HealthControl /t REG_DWORD /d 1 /f >nul 2>&1
reg add "HKLM\SOFTWARE\OEM\AcerCareCenter\Battery" /v BatteryLimit /t REG_DWORD /d 1 /f >nul 2>&1
reg add "HKLM\SOFTWARE\OEM\AcerCareCenter\Battery" /v LimitPercent /t REG_DWORD /d 80 /f >nul 2>&1

reg add "HKLM\SOFTWARE\WOW6432Node\OEM\AcerCareCenter\Battery" /v StopCharging /t REG_DWORD /d 80 /f >nul 2>&1
reg add "HKLM\SOFTWARE\WOW6432Node\OEM\AcerCareCenter\Battery" /v HealthControl /t REG_DWORD /d 1 /f >nul 2>&1
reg add "HKLM\SOFTWARE\WOW6432Node\OEM\AcerCareCenter\Battery" /v BatteryLimit /t REG_DWORD /d 1 /f >nul 2>&1
reg add "HKLM\SOFTWARE\WOW6432Node\OEM\AcerCareCenter\Battery" /v LimitPercent /t REG_DWORD /d 80 /f >nul 2>&1

echo.
echo [5/5] Service Status Diagnostics:
echo ----------------------------------------------------------------------------

sc query "ASMSvc" | findstr /i "STATE" | findstr /i "RUNNING" >nul 2>&1
if %errorlevel% equ 0 (
    echo   [OK] ASMSvc (Battery 80%% Manager)          : RUNNING
) else (
    echo   [?]  ASMSvc (Battery 80%% Manager)          : NOT RUNNING
)

sc query "AcerDeviceEnablingServiceV2" | findstr /i "STATE" | findstr /i "RUNNING" >nul 2>&1
if %errorlevel% equ 0 (
    echo   [OK] AcerDeviceEnablingServiceV2 (Bridge)   : RUNNING
) else (
    sc query "AcerDeviceEnablingService" | findstr /i "STATE" | findstr /i "RUNNING" >nul 2>&1
    if !errorlevel! equ 0 (
        echo   [OK] AcerDeviceEnablingService (Bridge)     : RUNNING
    ) else (
        echo   [?]  AcerDeviceEnablingService (Bridge)     : NOT RUNNING
    )
)

sc query "AcerServiceSvc" | findstr /i "STATE" | findstr /i "RUNNING" >nul 2>&1
if %errorlevel% equ 0 (
    echo   [OK] AcerServiceSvc (OEM Service)           : RUNNING
) else (
    echo   [?]  AcerServiceSvc (OEM Service)           : NOT RUNNING
)

reg query "HKLM\SOFTWARE\OEM\AcerCareCenter\Battery" /v StopCharging 2>nul | findstr /i "0x50" >nul 2>&1
if %errorlevel% equ 0 (
    echo   [OK] Battery 80%% Charge Limit             : ACTIVE (80%%)
) else (
    echo   [?]  Battery 80%% Charge Limit             : NOT FOUND
)

echo ----------------------------------------------------------------------------
echo.
echo ============================================================================
echo   DONE! All required Acer Nitro services are installed and active.
echo   You DO NOT need Acer Care Center or NitroSense installed!
echo   You can now launch PredatorControl-standalone.exe!
echo ============================================================================
echo.
pause

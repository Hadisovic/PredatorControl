# ═══════════════════════════════════════════════════════════════════════════════
# Predator Control — Restore Acer Services
# Re-enables all Acer OEM background services
# ═══════════════════════════════════════════════════════════════════════════════

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "Requesting Administrator privileges..." -ForegroundColor Yellow
    Start-Process powershell.exe -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    exit
}

$services = @(
    "AASSvc",
    "AcerLightingService",
    "AcerCCAgentSvis",
    "AcerQAAgentSvis",
    "AcerDIAgentSvis",
    "ASMSvc",
    "AcerServiceSvc",
    "AcerDeviceEnablingServiceV2"
)

Write-Host "Re-enabling Acer OEM services..." -ForegroundColor Cyan
foreach ($name in $services) {
    $s = Get-Service -Name $name -ErrorAction SilentlyContinue
    if ($s) {
        Set-Service -Name $name -StartupType Automatic -ErrorAction SilentlyContinue
        Start-Service -Name $name -ErrorAction SilentlyContinue
        Write-Host "  [✓ ENABLED] $name" -ForegroundColor Green
    }
}

Write-Host "`nAll Acer services restored." -ForegroundColor Green
Write-Host "Press any key to close..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

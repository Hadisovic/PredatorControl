# Switch-PredatorVersion.ps1
# Helper script to cleanly stop running PredatorControl instances, manage scheduled tasks,
# and launch or switch between different versions of Predator Control.

param (
    [Parameter(Mandatory=$false)]
    [string]$TargetExePath
)

Write-Host "=== Predator Control Version Switcher & Clean Killer ===" -ForegroundColor Cyan

# 1. Kill any running instances
$procs = Get-Process -Name "*PredatorControl*" -ErrorAction SilentlyContinue
if ($procs) {
    Write-Host "[1/3] Terminating active PredatorControl instances..." -ForegroundColor Yellow
    foreach ($p in $procs) {
        Write-Host "      Killing PID $($p.Id): $($p.ProcessName)"
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    }
    Start-Sleep -Milliseconds 600
} else {
    Write-Host "[1/3] No active PredatorControl process found." -ForegroundColor Green
}

# 2. Check scheduled tasks
$task = Get-ScheduledTask -TaskName "PredatorControl" -ErrorAction SilentlyContinue
if ($task) {
    Write-Host "[2/3] Found Windows Scheduled Task 'PredatorControl':" -ForegroundColor Yellow
    Write-Host "      Current Executable: $($task.Actions.Execute)"
    if ($TargetExePath -and (Test-Path $TargetExePath)) {
        Write-Host "      Updating task to target: $TargetExePath" -ForegroundColor Cyan
        $fullPath = (Resolve-Path $TargetExePath).Path
        $workingDir = Split-Path $fullPath
        $action = New-ScheduledTaskAction -Execute $fullPath -Argument "-hidden" -WorkingDirectory $workingDir
        Set-ScheduledTask -TaskName "PredatorControl" -Action $action | Out-Null
        Write-Host "      Task successfully updated!" -ForegroundColor Green
    } else {
        Write-Host "      (Tip: Pass -TargetExePath <path_to_exe> to point the startup task to an older or newer version)" -ForegroundColor Gray
    }
} else {
    Write-Host "[2/3] No 'PredatorControl' startup task found." -ForegroundColor Green
}

# 3. Launch target version if specified
if ($TargetExePath) {
    if (Test-Path $TargetExePath) {
        Write-Host "[3/3] Launching target version: $TargetExePath" -ForegroundColor Cyan
        Start-Process -FilePath (Resolve-Path $TargetExePath).Path
        Write-Host "Launched successfully!" -ForegroundColor Green
    } else {
        Write-Host "[3/3] ERROR: Target path not found: $TargetExePath" -ForegroundColor Red
    }
} else {
    Write-Host "[3/3] System is clean. You can now launch any version of Predator Control." -ForegroundColor Green
}

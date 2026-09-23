# Measures steady-state RAM usage (WorkingSet and PrivateMemory) for PredatorControl
# Supports measuring both Classic WinForms UI and Jelli (WebView2/Chromium) UI

param(
    [string]$ExePath = ".\src\bin\Release\net10.0-windows\win-x64\PredatorControlApp.exe",
    [int]$DurationSeconds = 10,
    [int]$IntervalSeconds = 1
)

if (-not (Test-Path $ExePath)) {
    # Check alternate publish path
    $candidates = @(
        ".\src\bin\Release\net10.0-windows\PredatorControlApp.exe",
        ".\PredatorControl-win-x64.exe",
        ".\PredatorControl-standalone.exe"
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { $ExePath = $c; break }
    }
}

Write-Host "Target executable: $ExePath" -ForegroundColor Cyan
Write-Host "Launching process for sampling over $DurationSeconds seconds..." -ForegroundColor Cyan

try {
    $proc = Start-Process -FilePath $ExePath -Verb RunAs -PassThru
} catch {
    Write-Warning "Failed to start $ExePath: $_"
    exit 1
}

if (-not $proc) {
    Write-Warning "Could not obtain process handle for $ExePath"
    exit 1
}

Start-Sleep -Seconds 5 # Wait for steady state initialization

$workingSetSamples = @()
$privateMemSamples = @()

$elapsed = 0
try {
    while ($elapsed -lt $DurationSeconds) {
        if ($proc.HasExited) { break }

        # Get parent process
        $proc.Refresh()
        $totalWs = $proc.WorkingSet64
        $totalPm = $proc.PrivateMemorySize64

        # Add child WebView2 / Chromium processes if present
        $children = Get-CimInstance Win32_Process -Filter "ParentProcessId = $($proc.Id)" -ErrorAction SilentlyContinue
        foreach ($child in $children) {
            try {
                $cp = Get-Process -Id $child.ProcessId -ErrorAction SilentlyContinue
                if ($cp) {
                    $totalWs += $cp.WorkingSet64
                    $totalPm += $cp.PrivateMemorySize64
                }
            } catch { }
        }

        $wsMb = [math]::Round($totalWs / 1MB, 2)
        $pmMb = [math]::Round($totalPm / 1MB, 2)

        $workingSetSamples += $wsMb
        $privateMemSamples += $pmMb

        Write-Host "Sample at ${elapsed}s -> Working Set: ${wsMb} MB | Private Memory: ${pmMb} MB"
        Start-Sleep -Seconds $IntervalSeconds
        $elapsed += $IntervalSeconds
    }
}
finally {
    if (-not $proc.HasExited) {
        $proc.Kill()
    }
}

if ($workingSetSamples.Count -gt 0) {
    $avgWs = [math]::Round(($workingSetSamples | Measure-Object -Average).Average, 2)
    $minWs = ($workingSetSamples | Measure-Object -Minimum).Minimum
    $maxWs = ($workingSetSamples | Measure-Object -Maximum).Maximum

    $avgPm = [math]::Round(($privateMemSamples | Measure-Object -Average).Average, 2)
    $minPm = ($privateMemSamples | Measure-Object -Minimum).Minimum
    $maxPm = ($privateMemSamples | Measure-Object -Maximum).Maximum

    Write-Host "`n=== RAM Measurement Results ===" -ForegroundColor Green
    Write-Host "Working Set (RAM): Min=${minWs} MB, Avg=${avgWs} MB, Max=${maxWs} MB" -ForegroundColor Green
    Write-Host "Private Memory:    Min=${minPm} MB, Avg=${avgPm} MB, Max=${maxPm} MB" -ForegroundColor Green
}

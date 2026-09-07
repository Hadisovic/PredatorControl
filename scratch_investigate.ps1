$p = "C:\Users\youse\.gemini\antigravity-ide\brain\962653ec-6554-44d1-9753-e1dc6d7caebe\scratch\agent_service\AcerLightingService.exe"
$bytes = [System.IO.File]::ReadAllBytes($p)

$uni = [System.Text.Encoding]::Unicode.GetString($bytes)
$asc = [System.Text.Encoding]::ASCII.GetString($bytes)

$regex = [regex]'[A-Za-z0-9_]{3,80}'
$matches = ($regex.Matches($uni) + $regex.Matches($asc)) | ForEach-Object { $_.Value } | Where-Object { $_ -match 'Gaming|Keyboard|Backlight|LED|Zone|Static|Color|Mode|RGB|Set|Get|Behavior' } | Select-Object -Unique
Write-Host "Matches in AcerLightingService: $($matches.Count)"
$matches | ForEach-Object { Write-Host "  $_" }

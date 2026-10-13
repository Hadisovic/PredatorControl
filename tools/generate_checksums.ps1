# Computes SHA-256 hashes for all release artifacts
param(
    [string]$PublishDir = "..\publish\single"
)

$targetFiles = Get-ChildItem -Path $PublishDir -Filter "*.exe" -File
if ($targetFiles.Count -eq 0) {
    Write-Host "No executables found in $PublishDir" -ForegroundColor Yellow
    exit 0
}

$checksumPath = Join-Path $PublishDir "checksums.txt"
$results = @()

foreach ($file in $targetFiles) {
    $hash = Get-FileHash -Path $file.FullName -Algorithm SHA256
    $line = "$($hash.Hash)  $($file.Name)"
    $results += $line
    Write-Host $line -ForegroundColor Cyan
}

$results | Out-File -FilePath $checksumPath -Encoding utf8
Write-Host "`nSHA-256 Checksums saved to: $checksumPath" -ForegroundColor Green

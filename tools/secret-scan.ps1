# Standalone script to scan repository files for credentials and private transcripts
param([string]$Path = ".")

Write-Host "Scanning repository for leaked credentials and private transcripts..." -ForegroundColor Cyan

$forbiddenFiles = Get-ChildItem -Path $Path -Recurse -File -Include "*_EXPORT.md", "*TRANSCRIPT*.md", "*CONVERSATION*.md", "*.secret", "*.key", "*.token" -Exclude ".git", "node_modules", "bin", "obj"
if ($forbiddenFiles) {
    Write-Host "Found forbidden files:" -ForegroundColor Red
    $forbiddenFiles | ForEach-Object { Write-Host " - $($_.FullName)" -ForegroundColor Red }
    exit 1
}

Write-Host "No forbidden files detected." -ForegroundColor Green
exit 0

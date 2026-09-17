param([switch]$FrameworkDependent)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    & npm --prefix jelli-ui ci --no-audit --no-fund
    if ($LASTEXITCODE) { throw 'Frontend dependency restore failed.' }
    & npm --prefix jelli-ui run lint
    if ($LASTEXITCODE) { throw 'ESLint failed.' }
    & npm --prefix jelli-ui test
    if ($LASTEXITCODE) { throw 'Frontend tests failed.' }
    $selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
    & dotnet publish src/PredatorControlApp.csproj -c Release -r win-x64 --self-contained $selfContained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/jelli
    if ($LASTEXITCODE) { throw 'Release publish failed.' }
    Write-Host 'Ready: artifacts/jelli/PredatorControlApp.exe'
    Write-Host 'Microsoft Edge WebView2 Evergreen Runtime must be installed.'
} finally { Pop-Location }

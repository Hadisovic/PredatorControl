param([switch]$SkipInstall)
$ErrorActionPreference = 'Stop'
function Invoke-Check([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command failed with exit code $LASTEXITCODE" }
}
Push-Location (Join-Path $PSScriptRoot '..')
try {
    Push-Location 'jelli-ui'
    try {
        if (-not $SkipInstall) { Invoke-Check 'npm' @('ci', '--no-audit', '--no-fund') }
        Invoke-Check 'npm' @('run', 'build')
        Invoke-Check 'npm' @('run', 'type-check')
        Invoke-Check 'npm' @('test')
        Invoke-Check 'npm' @('run', 'lint')
    } finally { Pop-Location }
    Invoke-Check 'dotnet' @('restore', 'src/PredatorControlApp.csproj')
    Invoke-Check 'dotnet' @('build', 'src/PredatorControlApp.csproj', '-c', 'Debug', '-p:SkipJelliBuild=true')
    Invoke-Check 'dotnet' @('build', 'src/PredatorControlApp.csproj', '-c', 'Release', '-p:SkipJelliBuild=true')
    Invoke-Check 'dotnet' @('test', 'tests/Compatibility.Tests/Compatibility.Tests.csproj', '-c', 'Release', '-p:SkipJelliBuild=true')
    Invoke-Check 'dotnet' @('test', 'tests/Keyboard.Tests/Keyboard.Tests.csproj', '-c', 'Release')
} finally { Pop-Location }

param(
    [Parameter(Mandatory)][string]$Iscc,
    [string]$Output = "$PSScriptRoot\..\dist-release"
)
$ErrorActionPreference = 'Stop'
function Run([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command failed ($LASTEXITCODE)" }
}
$Output = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force $Output | Out-Null
$project = [IO.Path]::GetFullPath("$PSScriptRoot\..\src\PredatorControlApp.csproj")
$publish = @('publish', $project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:EnableCompressionInSingleFile=true')
$build = [IO.Path]::GetFullPath("$PSScriptRoot\..\local-build\1.4.4")
$publishDir = Join-Path $build 'publish-app'
Run 'dotnet' ($publish + @('-o', $publishDir))
$webViewPackage = (& dotnet msbuild $project '-getProperty:PkgMicrosoft_Web_WebView2' '-nologo').Trim()
if ($LASTEXITCODE -ne 0) { throw "Could not locate the restored WebView2 SDK package ($LASTEXITCODE)" }
$loader = Join-Path $webViewPackage 'runtimes\win-x64\native\WebView2Loader.dll'
if (!(Test-Path -LiteralPath $loader -PathType Leaf)) { throw "The WebView2 loader was not found: $loader" }
$app = Join-Path $publishDir 'PredatorControlApp.exe'

$portableDest = Join-Path $Output 'PredatorControl-Portable-1.4.4.exe'
if (Test-Path -LiteralPath $portableDest) {
    $portableOld = Join-Path $Output 'PredatorControl-Portable-1.4.4.old.exe'
    Remove-Item -LiteralPath $portableOld -Force -ErrorAction SilentlyContinue
    Move-Item -LiteralPath $portableDest -Destination $portableOld -Force -ErrorAction SilentlyContinue
}
Copy-Item -LiteralPath $app -Destination $portableDest -Force

$hash = (Get-FileHash -LiteralPath $app).Hash.ToLowerInvariant()
$installerOutput = Join-Path ([IO.Path]::GetTempPath()) ("PredatorControl-Setup-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $installerOutput | Out-Null
try {
    Run $Iscc @("/DAppExe=$app", "/DAppHash=$hash", "/DLoaderDll=$loader", "/DOutputDir=$installerOutput", "$PSScriptRoot\PredatorControl.iss")
    $setup = Join-Path $installerOutput 'PredatorControl-Setup-1.4.4.exe'
    $setupDest = Join-Path $Output 'PredatorControl-Setup-1.4.4.exe'
    if (Test-Path -LiteralPath $setupDest) {
        $setupOld = Join-Path $Output 'PredatorControl-Setup-1.4.4.old.exe'
        Remove-Item -LiteralPath $setupOld -Force -ErrorAction SilentlyContinue
        Move-Item -LiteralPath $setupDest -Destination $setupOld -Force -ErrorAction SilentlyContinue
    }
    Copy-Item -LiteralPath $setup -Destination $setupDest -Force
    Remove-Item -LiteralPath $setup
}
finally {
    if (!(Get-ChildItem -LiteralPath $installerOutput -Force)) { Remove-Item -LiteralPath $installerOutput }
}
Get-ChildItem $Output -Filter '*.exe' | Where-Object { $_.Name -notlike '*.old.exe' } | Sort-Object Name | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash $_.FullName).Hash.ToLowerInvariant(), $_.Name
} | Set-Content (Join-Path $Output 'checksums.txt') -Encoding ascii
Remove-Item (Join-Path $Output '*.old.exe') -Force -ErrorAction SilentlyContinue

param(
    [Parameter(Mandatory)][string]$RuntimeCab,
    [Parameter(Mandatory)][string]$Iscc,
    [string]$Output = "$PSScriptRoot\..\dist-release"
)
$ErrorActionPreference = 'Stop'
function Run([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command failed ($LASTEXITCODE)" }
}
$Output = [IO.Path]::GetFullPath($Output)
$cache = Join-Path $PSScriptRoot '..\local-build\browser'
New-Item -ItemType Directory -Force $cache,$Output | Out-Null
$expected = '857DCFD6F7035508A29645FA2978C3A6A841F220DF77CC366D318894E19F4792'
if ((Get-FileHash -LiteralPath $RuntimeCab).Hash -ne $expected) { throw 'Unexpected official WebView2 package hash' }
$expanded = Join-Path $cache 'expanded'
New-Item -ItemType Directory -Force $expanded | Out-Null
Run 'expand.exe' @($RuntimeCab, '-F:*', $expanded)
$runtime = Join-Path $expanded 'Microsoft.WebView2.FixedVersionRuntime.156.0.4314.58.x64'
$signature = Get-AuthenticodeSignature (Join-Path $runtime 'msedgewebview2.exe')
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') { throw 'Invalid Microsoft browser signature' }
Copy-Item "$PSScriptRoot\ThirdPartyNotices.txt" (Join-Path $runtime 'PredatorControl-ThirdPartyNotices.txt')
Copy-Item "$PSScriptRoot\dotnet-*.txt" $runtime
Copy-Item "$PSScriptRoot\WebView2-Runtime-Terms.html" $runtime
Run 'python' @("$PSScriptRoot\prepare_browser.py", $runtime, $cache)
Run 'dotnet' @('publish', "$PSScriptRoot\..\src\PredatorControlApp.csproj", '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:RequireBundledBrowser=true', "-p:BrowserPayloadDirectory=$([IO.Path]::GetFullPath($cache))", '-o', "$Output\app")
$app = Join-Path $Output 'PredatorControl-Portable-1.4.3.exe'
Copy-Item "$Output\app\PredatorControlApp.exe" $app -Force
$hash = (Get-FileHash -LiteralPath $app).Hash.ToLowerInvariant()
$installerOutput = [IO.Path]::GetFullPath((Join-Path $cache ("..\installer-" + [Guid]::NewGuid().ToString('N'))))
New-Item -ItemType Directory -Force $installerOutput | Out-Null
Run $Iscc @("/DAppExe=$app", "/DAppHash=$hash", "/DOutputDir=$installerOutput", "$PSScriptRoot\PredatorControl.iss")
Copy-Item (Join-Path $installerOutput 'PredatorControl-Setup-1.4.3.exe') $Output -Force
Get-ChildItem $Output -Filter '*.exe' | Sort-Object Name | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash $_.FullName).Hash.ToLowerInvariant(), $_.Name
} | Set-Content (Join-Path $Output 'checksums.txt') -Encoding ascii

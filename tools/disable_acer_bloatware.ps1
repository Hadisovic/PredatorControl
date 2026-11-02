# Explicit optimization uses the same service policy as the application.
$taskApp = Join-Path $PSScriptRoot '..\PredatorControlApp.exe'
if (-not (Test-Path -LiteralPath $taskApp -PathType Leaf)) {
    throw 'PredatorControlApp.exe was not found. Use Optimize Acer Services in the application.'
}
$taskProcess = Start-Process -FilePath $taskApp -ArgumentList '--optimize-services' -Verb RunAs -WindowStyle Hidden -PassThru -Wait
exit $taskProcess.ExitCode

# Python 3.10+ and Git are required. Output never includes matched secret values.
param([string]$Path = ".", [switch]$History, [string]$OutputPath, [string]$ExceptionsPath)
$ErrorActionPreference = "Stop"
$scanArgs = @((Join-Path $PSScriptRoot "secret_scan.py"), "--path", $Path)
if ($History) { $scanArgs += "--history" }
if ($OutputPath) { $scanArgs += @("--output", $OutputPath) }
if ($ExceptionsPath) { $scanArgs += @("--exceptions", $ExceptionsPath) }
& python @scanArgs
exit $LASTEXITCODE

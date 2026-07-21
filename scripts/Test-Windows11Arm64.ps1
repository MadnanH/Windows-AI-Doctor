param([Parameter(Mandatory)][string]$ApplicationPath)
& (Join-Path $PSScriptRoot 'Run-WaidManualValidation.ps1') -Scenario Windows11Arm64 -ApplicationPath $ApplicationPath

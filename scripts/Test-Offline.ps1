param([Parameter(Mandatory)][string]$ApplicationPath)
& (Join-Path $PSScriptRoot 'Run-WaidManualValidation.ps1') -Scenario Offline -ApplicationPath $ApplicationPath
exit $LASTEXITCODE

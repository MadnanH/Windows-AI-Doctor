param([Parameter(Mandatory)][string]$ApplicationPath)
& (Join-Path $PSScriptRoot 'Run-WaidManualValidation.ps1') -Scenario Administrator -ApplicationPath $ApplicationPath
exit $LASTEXITCODE

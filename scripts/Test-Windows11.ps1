param([Parameter(Mandatory)][string]$ApplicationPath)
& (Join-Path $PSScriptRoot 'Run-WaidManualValidation.ps1') -Scenario Windows11 -ApplicationPath $ApplicationPath
exit $LASTEXITCODE

param([Parameter(Mandatory)][string]$ApplicationPath)
& (Join-Path $PSScriptRoot 'Run-WaidManualValidation.ps1') -Scenario StandardUser -ApplicationPath $ApplicationPath
exit $LASTEXITCODE

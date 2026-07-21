param([Parameter(Mandatory)][string]$ApplicationPath)
& (Join-Path $PSScriptRoot 'Run-WaidManualValidation.ps1') -Scenario Windows10 -ApplicationPath $ApplicationPath
exit $LASTEXITCODE

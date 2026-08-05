param([string]$DotNetExecutable='dotnet')
$ErrorActionPreference='Stop'
& $DotNetExecutable test (Join-Path $PSScriptRoot '..\tests\WAID.Diagnosis.Tests\WAID.Diagnosis.Tests.csproj') -c Release --no-build --filter 'FullyQualifiedName~DiagnosticEngineTests'
if($LASTEXITCODE-ne0){exit $LASTEXITCODE}

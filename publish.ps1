[CmdletBinding()]
param([ValidateSet('x64','ARM64')][string]$Platform='x64',[string]$DotNetExecutable='dotnet')
$ErrorActionPreference='Stop'
$rid=if($Platform -eq 'ARM64'){'win-arm64'}else{'win-x64'}
$output=Join-Path $PSScriptRoot "artifacts\publish\$rid"
& $DotNetExecutable publish (Join-Path $PSScriptRoot 'src\WAID.Desktop\WAID.Desktop.csproj') -c Release -p:Platform=$Platform -r $rid --self-contained true -o $output
exit $LASTEXITCODE

[CmdletBinding()]
param([ValidateSet('x64','ARM64')][string]$Platform='x64',[string]$DotNetExecutable='dotnet',[switch]$Portable)
$ErrorActionPreference='Stop'
$rid=if($Platform -eq 'ARM64'){'win-arm64'}else{'win-x64'}
$edition=if($Portable){"portable"}else{"installed"}
$output=Join-Path $PSScriptRoot "artifacts\publish\$edition\$rid"
& $DotNetExecutable publish (Join-Path $PSScriptRoot 'src\WAID.Desktop\WAID.Desktop.csproj') -c Release -p:Platform=$Platform -r $rid --self-contained true -o $output
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
$cliOutput=Join-Path $output 'cli'
& $DotNetExecutable publish (Join-Path $PSScriptRoot 'src\WAID.Cli\WAID.Cli.csproj') -c Release -p:Platform=$Platform -r $rid --self-contained true -o $cliOutput
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
if($Portable){Set-Content -LiteralPath (Join-Path $output "waid.portable") -Value "Portable mode marker" -Encoding UTF8;Set-Content -LiteralPath (Join-Path $output "PORTABLE-README.txt") -Value "Run WAID.Desktop.exe --portable --workspace X:\\WAID-Workspace. All application data stays in that workspace. Close WAID before removing media." -Encoding UTF8}
exit 0

[CmdletBinding()]
param([ValidateSet('x64','ARM64')][string]$Platform='x64',[string]$DotNetExecutable='dotnet',[switch]$Portable,[ValidateSet('dev','beta','stable')][string]$Channel='dev',[string]$Version)
$ErrorActionPreference='Stop'
$versionFile=Join-Path $PSScriptRoot 'Directory.Build.props'
if(-not$Version){$properties=[xml](Get-Content -Raw -LiteralPath $versionFile);$Version=$properties.Project.PropertyGroup.VersionPrefix;if($properties.Project.PropertyGroup.VersionSuffix){$Version+="-$($properties.Project.PropertyGroup.VersionSuffix)"}}
$rid=if($Platform -eq 'ARM64'){'win-arm64'}else{'win-x64'}
$edition=if($Portable){'portable'}else{'installed'}
$output=Join-Path $PSScriptRoot "artifacts\publish\$edition\$rid"
$publishRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'artifacts\publish'));$resolvedOutput=[IO.Path]::GetFullPath($output);if(-not$resolvedOutput.StartsWith($publishRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Publish output escaped the managed artifacts root.'};if(Test-Path -LiteralPath $resolvedOutput){Remove-Item -LiteralPath $resolvedOutput -Recurse -Force}
& $DotNetExecutable publish (Join-Path $PSScriptRoot 'src\WAID.Desktop\WAID.Desktop.csproj') -c Release -p:Platform=$Platform -r $rid --self-contained true -p:DebugType=None -p:DebugSymbols=false -o $output
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
$cliOutput=Join-Path $output 'cli'
& $DotNetExecutable publish (Join-Path $PSScriptRoot 'src\WAID.Cli\WAID.Cli.csproj') -c Release -p:Platform=$Platform -r $rid --self-contained true -p:DebugType=None -p:DebugSymbols=false -o $cliOutput
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
if($Portable){Set-Content -LiteralPath (Join-Path $output 'waid.portable') -Value 'Portable mode marker' -Encoding UTF8;Set-Content -LiteralPath (Join-Path $output 'PORTABLE-README.txt') -Value 'Run WAID.Desktop.exe --portable --workspace X:\WAID-Workspace. All application data stays in that workspace. Close WAID before removing media.' -Encoding UTF8}
Import-Module (Join-Path $PSScriptRoot 'packaging\Waid.Release.psm1') -Force
Set-Content -LiteralPath (Join-Path $output 'UNSIGNED-DEVELOPMENT-BUILD.txt') -Value "Windows AI Doctor $Version $Channel. This artifact is unsigned and must not be used for production distribution." -Encoding UTF8
New-WaidArtifactManifest -Root $output -Version $Version -Channel $Channel -Architecture $Platform -Edition $edition -Signed $false|Out-Null
exit 0

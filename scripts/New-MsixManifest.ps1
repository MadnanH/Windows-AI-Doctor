[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('x64','ARM64')][string]$Architecture,[Parameter(Mandatory)][string]$Version,[Parameter(Mandatory)][string]$Publisher,[ValidateSet('dev','beta','stable')][string]$Channel='stable',[string]$OutputPath)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $root 'packaging\Waid.Release.psm1') -Force
if($Publisher-notmatch'^CN=.+'){throw 'Publisher must be the exact certificate subject beginning with CN=.'}
$output=if($OutputPath){[IO.Path]::GetFullPath($OutputPath)}else{Join-Path $root "artifacts\packaging\$Architecture\AppxManifest.xml"}
$template=Get-Content -Raw -LiteralPath (Join-Path $root 'packaging\Package.appxmanifest.template')
$display=if($Channel-eq'stable'){'Windows AI Doctor'}else{"Windows AI Doctor ($($Channel.ToUpperInvariant()) - Unsigned)"}
$rendered=$template.Replace('${ARCHITECTURE}',$Architecture.ToLowerInvariant()).Replace('${VERSION}',(ConvertTo-WaidPackageVersion $Version)).Replace('${PUBLISHER}',$Publisher).Replace('${DISPLAY_NAME}',$display)
if($rendered-match'\$\{'){throw 'The rendered MSIX manifest contains unresolved variables.'}
$directory=Split-Path -Parent $output;if($directory){New-Item -ItemType Directory -Path $directory -Force|Out-Null}
$rendered|Set-Content -LiteralPath $output -Encoding UTF8
[xml](Get-Content -Raw -LiteralPath $output)|Out-Null
Write-Output $output

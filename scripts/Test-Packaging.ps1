[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$desktop=[xml](Get-Content -Raw -LiteralPath (Join-Path $root 'src\WAID.Desktop\WAID.Desktop.csproj'))
$cli=[xml](Get-Content -Raw -LiteralPath (Join-Path $root 'src\WAID.Cli\WAID.Cli.csproj'))
foreach($project in @($desktop,$cli)){if($project.Project.PropertyGroup.RuntimeIdentifiers-notmatch'win-x64'-or$project.Project.PropertyGroup.RuntimeIdentifiers-notmatch'win-arm64'){throw 'Desktop and CLI projects must declare x64 and ARM64 runtime identifiers.'}}
$manifest=[xml](Get-Content -Raw -LiteralPath (Join-Path $root 'packaging\Package.appxmanifest.template'))
if(-not$manifest.Package.Identity.Name-or-not$manifest.Package.Properties.DisplayName){throw 'MSIX identity or display name is missing.'}
$publish=Get-Content -Raw -LiteralPath (Join-Path $root 'publish.ps1')
foreach($required in @('WAID.Desktop.csproj','WAID.Cli.csproj','--self-contained true','win-arm64','win-x64')){if($publish-notmatch[regex]::Escape($required)){throw "Publish contract is missing $required"}}
$forbidden=Get-ChildItem -Path $root -Recurse -File|Where-Object{$_.FullName-notmatch'[\\/](bin|obj|artifacts|\.git)[\\/]' -and $_.Extension-in'.pfx','.p12','.snk','.key'}
if($forbidden){throw "Private signing material is committed: $($forbidden.FullName -join ', ')"}
Write-Host 'Packaging contract passed for desktop and CLI x64/ARM64 artifacts; no private signing material found.'

[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$desktop=[xml](Get-Content -Raw -LiteralPath (Join-Path $root 'src\WAID.Desktop\WAID.Desktop.csproj'))
$cli=[xml](Get-Content -Raw -LiteralPath (Join-Path $root 'src\WAID.Cli\WAID.Cli.csproj'))
foreach($project in @($desktop,$cli)){if($project.Project.PropertyGroup.RuntimeIdentifiers-notmatch'win-x64'-or$project.Project.PropertyGroup.RuntimeIdentifiers-notmatch'win-arm64'){throw 'Desktop and CLI projects must declare x64 and ARM64 runtime identifiers.'}}
$templatePath=Join-Path $root 'packaging\Package.appxmanifest.template'
$manifest=[xml](Get-Content -Raw -LiteralPath $templatePath)
if(-not$manifest.Package.Identity.Name-or-not$manifest.Package.Properties.DisplayName){throw 'MSIX identity or display name is missing.'}
$template=Get-Content -Raw -LiteralPath $templatePath
foreach($variable in @('${VERSION}','${PUBLISHER}','${ARCHITECTURE}','${DISPLAY_NAME}')){if($template-notmatch[regex]::Escape($variable)){throw "MSIX template is missing $variable"}}
$contract=Get-Content -Raw -LiteralPath (Join-Path $root 'packaging\release-contract.json')|ConvertFrom-Json
if($contract.SchemaVersion-ne1-or$contract.defaultUninstallDataAction-ne'retain'-or$contract.architectures.Count-ne2){throw 'Release contract is invalid.'}
foreach($script in @('Build-WaidMsix.ps1','Sign-WaidMsix.ps1','New-MsixManifest.ps1','Test-PublishedArtifact.ps1')){if(-not(Test-Path -LiteralPath (Join-Path $root "scripts\$script"))){throw "Release script is missing: $script"}}
$signing=Get-Content -Raw -LiteralPath (Join-Path $root 'scripts\Sign-WaidMsix.ps1');foreach($required in @('CertificatePath','ExpectedPublisher','SHA256','verify','outside the repository')){if($signing-notmatch[regex]::Escape($required)){throw "Signing boundary is missing $required"}}
$packing=Get-Content -Raw -LiteralPath (Join-Path $root 'scripts\Build-WaidMsix.ps1');foreach($required in @('MakeAppxPath','StoreLogo.png','waid-artifact.json','unsigned.msix')){if($packing-notmatch[regex]::Escape($required)){throw "MSIX build boundary is missing $required"}}
$publish=Get-Content -Raw -LiteralPath (Join-Path $root 'publish.ps1')
foreach($required in @('WAID.Desktop.csproj','WAID.Cli.csproj','--self-contained true','win-arm64','win-x64','New-WaidArtifactManifest','UNSIGNED-DEVELOPMENT-BUILD.txt','Waid.Release.psm1')){if($publish-notmatch[regex]::Escape($required)){throw "Publish contract is missing $required"}}
$forbidden=Get-ChildItem -Path $root -Recurse -File|Where-Object{$_.FullName-notmatch'[\\/](bin|obj|artifacts|\.git)[\\/]' -and $_.Extension-in'.pfx','.p12','.snk','.key'}
if($forbidden){throw "Private signing material is committed: $($forbidden.FullName -join ', ')"}
& (Join-Path $root 'scripts\Test-InstallerLifecycle.ps1');if($LASTEXITCODE){exit $LASTEXITCODE}
$rendered=& (Join-Path $root 'scripts\New-MsixManifest.ps1') -Architecture x64 -Version '1.2.3-beta' -Publisher 'CN=WAID Packaging Test' -Channel beta -OutputPath (Join-Path $root 'artifacts\packaging-tests\AppxManifest.xml')
$xml=[xml](Get-Content -Raw -LiteralPath $rendered);if($xml.Package.Identity.Version-ne'1.2.3.0'-or$xml.Package.Properties.DisplayName-notmatch'Unsigned'){throw 'Rendered development manifest is invalid.'}
Write-Host 'Packaging contract passed for desktop and CLI x64/ARM64 artifacts, lifecycle behavior, manifest rendering, and protected signing boundaries.'

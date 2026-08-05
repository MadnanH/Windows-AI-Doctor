[CmdletBinding()]
param([Parameter(Mandatory)][string]$PublishedPath,[Parameter(Mandatory)][string]$AssetDirectory,[Parameter(Mandatory)][ValidateSet('x64','ARM64')][string]$Architecture,[Parameter(Mandatory)][string]$Version,[Parameter(Mandatory)][string]$Publisher,[ValidateSet('dev','beta','stable')][string]$Channel='stable',[Parameter(Mandatory)][string]$MakeAppxPath,[string]$OutputDirectory)
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path;$published=(Resolve-Path -LiteralPath $PublishedPath).Path;$assets=(Resolve-Path -LiteralPath $AssetDirectory).Path;$makeAppx=(Resolve-Path -LiteralPath $MakeAppxPath).Path
foreach($name in @('StoreLogo.png','Square150x150Logo.png','Square44x44Logo.png')){if(-not(Test-Path -LiteralPath (Join-Path $assets $name) -PathType Leaf)){throw "Required MSIX asset is missing: $name"}}
foreach($name in @('WAID.Desktop.exe','WAID.Desktop.runtimeconfig.json','waid-artifact.json')){if(-not(Test-Path -LiteralPath (Join-Path $published $name) -PathType Leaf)){throw "Published package input is missing: $name"}}
$destination=if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path $root 'artifacts\msix'};New-Item -ItemType Directory -Path $destination -Force|Out-Null
$staging=Join-Path ([IO.Path]::GetTempPath()) "WAID-Msix-$([guid]::NewGuid().ToString('N'))"
try{
    New-Item -ItemType Directory -Path $staging -Force|Out-Null
    Copy-Item -Path (Join-Path $published '*') -Destination $staging -Recurse -Force
    $stagedAssets=Join-Path $staging 'Assets';New-Item -ItemType Directory -Path $stagedAssets -Force|Out-Null;Copy-Item -Path (Join-Path $assets '*') -Destination $stagedAssets -Force
    & (Join-Path $root 'scripts\New-MsixManifest.ps1') -Architecture $Architecture -Version $Version -Publisher $Publisher -Channel $Channel -OutputPath (Join-Path $staging 'AppxManifest.xml')|Out-Null
    $package=Join-Path $destination "WAID-$Version-$Channel-$($Architecture.ToLowerInvariant())-unsigned.msix"
    & $makeAppx pack /d $staging /p $package /o;if($LASTEXITCODE){throw 'MakeAppx failed to build the MSIX artifact.'}
    [ordered]@{SchemaVersion=1;Artifact=[IO.Path]::GetFileName($package);Sha256=(Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash;Signed=$false;SignatureStatus='UNSIGNED - signing is a separate protected release stage'}|ConvertTo-Json|Set-Content -LiteralPath "$package.json" -Encoding UTF8
    Write-Output $package
}finally{if(Test-Path -LiteralPath $staging){Remove-Item -LiteralPath $staging -Recurse -Force}}

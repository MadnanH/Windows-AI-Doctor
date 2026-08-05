Set-StrictMode -Version Latest

function ConvertTo-WaidPackageVersion {
    param([Parameter(Mandatory)][string]$Version)
    $clean=($Version -split '[-+]')[0]
    $parts=$clean.Split('.')
    if($parts.Count -lt 2 -or $parts.Count -gt 4 -or @($parts|Where-Object{$_ -notmatch '^\d+$'}).Count -gt 0){throw "Invalid WAID release version: $Version"}
    $numbers=@($parts|ForEach-Object{[int]$_})
    while($numbers.Count-lt4){$numbers+=0}
    if(@($numbers|Where-Object{$_ -gt65535}).Count -gt 0){throw 'MSIX version components must not exceed 65535.'}
    return ($numbers-join'.')
}

function Get-WaidUpgradeDecision {
    param([Parameter(Mandatory)][string]$InstalledVersion,[Parameter(Mandatory)][string]$CandidateVersion)
    $installed=[version](ConvertTo-WaidPackageVersion $InstalledVersion)
    $candidate=[version](ConvertTo-WaidPackageVersion $CandidateVersion)
    if($candidate-lt$installed){return [pscustomobject]@{Allowed=$false;Kind='DowngradeBlocked';Message="Version $CandidateVersion is older than installed version $InstalledVersion."}}
    if($candidate-eq$installed){return [pscustomobject]@{Allowed=$true;Kind='Repair';Message='The same version may repair application files without changing user data.'}}
    return [pscustomobject]@{Allowed=$true;Kind='Upgrade';Message='Application files may be upgraded while user data remains in its existing data location.'}
}

function Get-WaidUninstallPlan {
    param([ValidateSet('retain','remove')][string]$DataAction='retain',[Parameter(Mandatory)][string]$DataRoot)
    if(-not[IO.Path]::IsPathRooted($DataRoot)){throw 'The uninstall data root must be absolute.'}
    return [pscustomobject]@{RemoveApplication=$true;DataAction=$DataAction;DataRoot=[IO.Path]::GetFullPath($DataRoot);RequiresExplicitDataApproval=($DataAction-eq'remove')}
}

function New-WaidArtifactManifest {
    param([Parameter(Mandatory)][string]$Root,[Parameter(Mandatory)][string]$Version,[Parameter(Mandatory)][ValidateSet('dev','beta','stable')][string]$Channel,[Parameter(Mandatory)][ValidateSet('x64','ARM64')][string]$Architecture,[Parameter(Mandatory)][ValidateSet('installed','portable')][string]$Edition,[bool]$Signed=$false)
    $resolved=(Resolve-Path -LiteralPath $Root).Path
    $manifestPath=Join-Path $resolved 'waid-artifact.json'
    $files=@(Get-ChildItem -LiteralPath $resolved -Recurse -File|Where-Object{$_.FullName-ne$manifestPath}|Sort-Object FullName|ForEach-Object{[pscustomobject]@{Path=$_.FullName.Substring($resolved.Length).TrimStart('\','/').Replace('\','/');Length=$_.Length;Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}})
    if(-not($files.Path-contains'WAID.Desktop.exe')-or-not($files.Path-contains'WAID.Desktop.runtimeconfig.json')){throw 'Published desktop runtime files are incomplete.'}
    if(-not($files.Path-contains'cli/waid.exe')-or-not($files.Path-contains'cli/waid.runtimeconfig.json')){throw 'Published CLI runtime files are incomplete.'}
    $label=if($Signed){'Signed release artifact'}else{'UNSIGNED DEVELOPMENT ARTIFACT - NOT FOR PRODUCTION DISTRIBUTION'}
    $manifest=[ordered]@{SchemaVersion=1;Product='Windows AI Doctor';Version=$Version;PackageVersion=(ConvertTo-WaidPackageVersion $Version);Channel=$Channel;Architecture=$Architecture;Edition=$Edition;Signed=$Signed;SignatureStatus=$label;Files=$files}
    $manifest|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $manifestPath -Encoding UTF8
    return $manifestPath
}

Export-ModuleMember -Function ConvertTo-WaidPackageVersion,Get-WaidUpgradeDecision,Get-WaidUninstallPlan,New-WaidArtifactManifest

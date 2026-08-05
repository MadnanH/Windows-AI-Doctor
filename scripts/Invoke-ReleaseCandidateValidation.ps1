[CmdletBinding()]
param(
    [string]$EvidenceDirectory=(Join-Path $PSScriptRoot '..\artifacts\release-evidence'),
    [string]$OutputPath=(Join-Path $PSScriptRoot '..\artifacts\release-validation\release-decision.json'),
    [string]$DotNetExecutable='dotnet',
    [switch]$SkipAutomaticExecution
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$manifest=Get-Content -Raw -LiteralPath (Join-Path $root 'release\release-candidate.json')|ConvertFrom-Json
if($manifest.schemaVersion-ne1-or-not$manifest.candidateId-or-not$manifest.productVersion){throw 'Release candidate manifest is invalid.'}
$properties=[xml](Get-Content -Raw -LiteralPath (Join-Path $root 'Directory.Build.props'))
$repositoryVersion="$($properties.Project.PropertyGroup.VersionPrefix)-$($properties.Project.PropertyGroup.VersionSuffix)"
if($repositoryVersion-ne$manifest.productVersion){throw "Release manifest version $($manifest.productVersion) does not match repository version $repositoryVersion."}

$automatic=[Collections.Generic.List[object]]::new()
function Add-AutomaticResult([string]$id,[bool]$passed,[string]$detail){$automatic.Add([pscustomobject]@{ControlId=$id;Passed=$passed;Detail=$detail})}
if($SkipAutomaticExecution){
    Add-AutomaticResult 'AUTO-FULL-BUILD' $false 'Not executed in this validation run.'
    Add-AutomaticResult 'AUTO-PACKAGING' $false 'Not executed in this validation run.'
}else{
    & (Join-Path $root 'build.ps1') -DotNetExecutable $DotNetExecutable
    $buildPassed=$LASTEXITCODE-eq0
    Add-AutomaticResult 'AUTO-FULL-BUILD' $buildPassed ($(if($buildPassed){'Full build and all repository gates passed.'}else{"build.ps1 exited $LASTEXITCODE."}))
    $packagingPassed=$buildPassed
    if($packagingPassed){
        foreach($target in @(
            @{Platform='x64';Portable=$false;Path='installed\win-x64';Skip=$false},
            @{Platform='x64';Portable=$true;Path='portable\win-x64';Skip=$false},
            @{Platform='ARM64';Portable=$false;Path='installed\win-arm64';Skip=$true},
            @{Platform='ARM64';Portable=$true;Path='portable\win-arm64';Skip=$true}
        )){
            $publishArgs=@{Platform=$target.Platform;DotNetExecutable=$DotNetExecutable}
            if($target.Portable){$publishArgs.Portable=$true}
            & (Join-Path $root 'publish.ps1') @publishArgs
            if($LASTEXITCODE-ne0){$packagingPassed=$false;break}
            $testArgs=@{ArtifactPath=(Join-Path $root "artifacts\publish\$($target.Path)")}
            if($target.Skip){$testArgs.SkipLaunch=$true}
            & (Join-Path $root 'scripts\Test-PublishedArtifact.ps1') @testArgs
            if($LASTEXITCODE-ne0){$packagingPassed=$false;break}
        }
    }
    Add-AutomaticResult 'AUTO-PACKAGING' $packagingPassed ($(if($packagingPassed){'All installed/portable x64 and ARM64 artifact checks passed.'}else{'Release artifact validation failed or was not reached.'}))
}

$requiredDocs=@('release-checklist.md','release-validation-matrix.md','release-known-issues.md','release-security-review.md','release-notes-v1-readiness.md')
$missingDocs=@($requiredDocs|Where-Object{-not(Test-Path -LiteralPath (Join-Path $root "docs\$_"))})
Add-AutomaticResult 'AUTO-SCOPE' ($missingDocs.Count-eq0) ($(if($missingDocs.Count-eq0){'Manifest and required release documents are present.'}else{"Missing: $($missingDocs -join ', ')."}))

$evidence=[Collections.Generic.List[object]]::new();$privacyPassed=$true
$profile=[Environment]::GetFolderPath('UserProfile');$currentCommit=(& git -C $root rev-parse HEAD).Trim();$now=[DateTimeOffset]::UtcNow
foreach($control in $manifest.evidenceControls){
    $candidate=Get-ChildItem -LiteralPath $EvidenceDirectory -Filter '*.json' -File -ErrorAction SilentlyContinue|ForEach-Object{
        try{$raw=Get-Content -Raw -LiteralPath $_.FullName;$document=$raw|ConvertFrom-Json;if($document.Scenario-eq$control.scenario){[pscustomobject]@{File=$_;Raw=$raw;Document=$document}}}catch{}
    }|Sort-Object{$_.Document.TimestampUtc}-Descending|Select-Object -First 1
    $passed=$false;$detail='No evidence supplied.';$reference=$null
    if($candidate){
        $forbiddenName=$candidate.Raw-match'(?i)"(password|token|productkey|product_key|userprofile|applicationpath)"\s*:'
        $containsProfile=[bool]($profile-and$candidate.Raw.IndexOf($profile,[StringComparison]::OrdinalIgnoreCase)-ge0)
        $privacyPassed=$privacyPassed-and-not$forbiddenName-and-not$containsProfile
        $timestampValid=$false
        try{$timestamp=[DateTimeOffset]::Parse([string]$candidate.Document.TimestampUtc,[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::AssumeUniversal);$timestampValid=$timestamp-ge$now.AddDays(-30)-and$timestamp-le$now.AddMinutes(5)}catch{}
        $candidateMatches=$candidate.Document.SchemaVersion-eq2-and$candidate.Document.CandidateId-eq$manifest.candidateId-and$candidate.Document.ProductVersion-eq$manifest.productVersion-and$candidate.Document.Commit-eq$currentCommit-and$timestampValid
        $passed=($candidate.Document.Passed-eq$true)-and$candidateMatches-and-not$forbiddenName-and-not$containsProfile
        $detail=if($passed){'Passing evidence validated.'}elseif(-not$candidateMatches){'Evidence is stale, future-dated, or does not match the exact candidate, version, and commit.'}elseif($forbiddenName-or$containsProfile){'Evidence failed privacy validation.'}else{'Evidence records a failed result.'}
        $reference=$candidate.File.Name
    }
    $evidence.Add([pscustomobject]@{ControlId=$control.id;Scenario=$control.scenario;Severity=$control.severity;Passed=$passed;Detail=$detail;EvidenceFile=$reference})
}
Add-AutomaticResult 'AUTO-PRIVACY' $privacyPassed ($(if($privacyPassed){'Supplied evidence passed sensitive-field and profile-path checks.'}else{'One or more evidence files contains prohibited sensitive metadata.'}))

$failedAutomatic=@($automatic|Where-Object{-not$_.Passed});$blockers=@($evidence|Where-Object{-not$_.Passed-and$_.Severity-in'critical','high'})
$decision=if($failedAutomatic.Count-eq0-and$blockers.Count-eq0){'GO'}else{'NO-GO'}
$commit=$currentCommit
$report=[ordered]@{SchemaVersion=1;CandidateId=$manifest.candidateId;ProductVersion=$manifest.productVersion;Commit=$commit;GeneratedUtc=[DateTimeOffset]::UtcNow.ToString('O');Decision=$decision;AutomaticResults=$automatic;EvidenceResults=$evidence;BlockingControlIds=@($failedAutomatic.ControlId)+@($blockers.ControlId);Notice='GO means every required automated and evidence control passed for this exact candidate. Missing evidence fails closed.'}
$destination=[IO.Path]::GetFullPath($OutputPath);$outputRoot=[IO.Path]::GetFullPath((Join-Path $root 'artifacts'))
if(-not$destination.StartsWith($outputRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Release decision output must remain under artifacts.'}
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination)|Out-Null
$report|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $destination -Encoding utf8
Write-Host "Release decision: $decision";Write-Host "Evidence: $destination"
if($decision-ne'GO'){exit 2}

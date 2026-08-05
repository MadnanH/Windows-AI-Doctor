[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$manifest=Get-Content -Raw -LiteralPath (Join-Path $root 'release\release-candidate.json')|ConvertFrom-Json
$ids=@($manifest.automaticControls.id)+@($manifest.evidenceControls.id)
if($ids.Count-ne(@($ids|Sort-Object -Unique)).Count){throw 'Release control identifiers must be unique.'}
if(@($manifest.evidenceControls|Where-Object{$_.severity-notin'critical','high'}).Count){throw 'Every required external evidence control must block release.'}
foreach($document in @('release-checklist.md','release-validation-matrix.md','release-known-issues.md','release-security-review.md','release-notes-v1-readiness.md')){if(-not(Test-Path -LiteralPath (Join-Path $root "docs\$document"))){throw "Release document is missing: $document"}}

$testRoot=Join-Path $root "artifacts\release-validation-tests\$([Guid]::NewGuid().ToString('N'))"
$resolvedArtifacts=[IO.Path]::GetFullPath((Join-Path $root 'artifacts'));$resolvedTest=[IO.Path]::GetFullPath($testRoot)
if(-not$resolvedTest.StartsWith($resolvedArtifacts+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Test output escaped artifacts.'}
try{
    $evidence=Join-Path $testRoot 'evidence';New-Item -ItemType Directory -Force -Path $evidence|Out-Null
    $decisionPath=Join-Path $testRoot 'decision.json'
    & (Join-Path $root 'scripts\Invoke-ReleaseCandidateValidation.ps1') -EvidenceDirectory $evidence -OutputPath $decisionPath -SkipAutomaticExecution
    if($LASTEXITCODE-ne2){throw 'An incomplete candidate must return exit code 2.'}
    $decision=Get-Content -Raw -LiteralPath $decisionPath|ConvertFrom-Json
    if($decision.Decision-ne'NO-GO'-or$decision.BlockingControlIds-notcontains'SIGNED-PACKAGE'){throw 'Missing release evidence must produce an explicit NO-GO blocker.'}

    @{Scenario='Windows10';TimestampUtc=[DateTimeOffset]::UtcNow;Passed=$true;Token='must-be-rejected'}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $evidence 'unsafe.json') -Encoding utf8
    & (Join-Path $root 'scripts\Invoke-ReleaseCandidateValidation.ps1') -EvidenceDirectory $evidence -OutputPath $decisionPath -SkipAutomaticExecution
    if($LASTEXITCODE-ne2){throw 'Privacy-invalid evidence must return exit code 2.'}
    $privacyDecision=Get-Content -Raw -LiteralPath $decisionPath|ConvertFrom-Json
    if([bool]($privacyDecision.AutomaticResults|Where-Object {$_.ControlId -eq 'AUTO-PRIVACY'}).Passed){throw 'Sensitive release evidence was not rejected.'}
    Remove-Item -LiteralPath (Join-Path $evidence 'unsafe.json') -Force
    @{SchemaVersion=2;CandidateId=$manifest.candidateId;ProductVersion=$manifest.productVersion;Commit=('0'*40);Scenario='Windows10';TimestampUtc=[DateTimeOffset]::UtcNow.ToString('O');Passed=$true}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $evidence 'wrong-commit.json') -Encoding utf8
    & (Join-Path $root 'scripts\Invoke-ReleaseCandidateValidation.ps1') -EvidenceDirectory $evidence -OutputPath $decisionPath -SkipAutomaticExecution
    if($LASTEXITCODE-ne2){throw 'Wrong-commit evidence must return exit code 2.'}
    $boundDecision=Get-Content -Raw -LiteralPath $decisionPath|ConvertFrom-Json
    $windowsResult=$boundDecision.EvidenceResults|Where-Object {$_.ControlId -eq 'WIN10-X64'}
    if($windowsResult.Passed-or$windowsResult.Detail-notmatch'exact candidate'){throw 'Evidence was not bound to the exact candidate commit.'}
}finally{if(Test-Path -LiteralPath $resolvedTest){Remove-Item -LiteralPath $resolvedTest -Recurse -Force}}
Write-Host 'Release candidate manifest, fail-closed decision, and evidence privacy tests passed.'
exit 0

[CmdletBinding()]
param([string]$ResultsDirectory=(Join-Path $PSScriptRoot '..\artifacts\test-results'),[ValidateRange(1,100)][int]$MinimumLinePercent=60)
$ErrorActionPreference='Stop'
$files=Get-ChildItem -Path $ResultsDirectory -Recurse -Filter 'coverage.cobertura.xml' -ErrorAction SilentlyContinue
if(-not$files){throw "No Cobertura coverage files were found under $ResultsDirectory."}
[long]$covered=0;[long]$valid=0
foreach($file in $files){$xml=[xml](Get-Content -Raw -LiteralPath $file.FullName);$covered+=[long]$xml.coverage.'lines-covered';$valid+=[long]$xml.coverage.'lines-valid'}
if($valid-le0){throw 'Coverage reports contain no valid lines.'}
$percent=[math]::Round(100*$covered/$valid,2)
Write-Host "Aggregate line coverage: $percent% ($covered/$valid), required: $MinimumLinePercent%."
if($percent-lt$MinimumLinePercent){throw "Coverage gate failed: $percent% is below $MinimumLinePercent%."}

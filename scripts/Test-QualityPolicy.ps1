[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$catalog=Get-Content -Raw -LiteralPath (Join-Path $root 'tests\test-catalog.json')|ConvertFrom-Json
if($catalog.schemaVersion-ne1){throw 'Unsupported test catalog schema.'}
$required=@('Unit','Integration','WindowsIntegration','UI','Security','Performance','Packaging','Architecture','CriticalPath','DestructiveVm')
foreach($name in $required){$layer=@($catalog.layers|Where-Object { $_.name -eq $name });if($layer.Count-ne1-or[string]::IsNullOrWhiteSpace($layer[0].command)){throw "Test layer $name is missing or ambiguous."}}
foreach($name in $required|Where-Object { $_ -ne 'DestructiveVm' }){if(-not($catalog.layers|Where-Object { $_.name -eq $name }).blocking){throw "Critical test layer $name must block CI."}}
if(($catalog.layers|Where-Object { $_.name -eq 'DestructiveVm' }).blocking){throw 'Destructive VM tests must not run in ordinary CI.'}
$flaky=Get-Content -Raw -LiteralPath (Join-Path $root 'tests\flaky-tests.json')|ConvertFrom-Json
if($flaky.schemaVersion-ne1){throw 'Unsupported flaky-test registry schema.'}
foreach($test in $flaky.tests){if([string]::IsNullOrWhiteSpace($test.name)-or[string]::IsNullOrWhiteSpace($test.owner)-or[string]::IsNullOrWhiteSpace($test.issue)-or[string]::IsNullOrWhiteSpace($test.reason)-or-not$test.expiryUtc){throw 'Every flaky-test entry requires name, owner, issue, reason, and expiryUtc.'};if([DateTimeOffset]$test.expiryUtc-le[DateTimeOffset]::UtcNow){throw "Flaky-test quarantine expired: $($test.name)"}}
$sources=Get-ChildItem -Path (Join-Path $root 'tests') -Recurse -Filter '*.cs'|ForEach-Object{Get-Content -Raw -LiteralPath $_.FullName}
if(($sources-join"`n")-match'\bThread\.Sleep\s*\('){throw 'Thread.Sleep is forbidden in automated tests.'}
if(($sources-join"`n")-match'\bTask\.Delay\s*\(\s*(?:\d+|TimeSpan\.From(?:Milliseconds|Seconds|Minutes))'){throw 'Real-duration Task.Delay is forbidden; use deterministic gates or cancellation.'}
Write-Host "Quality policy passed: $($required.Count) classified layers, $(@($flaky.tests).Count) quarantined tests, no flaky sleeps."

[CmdletBinding()]
param([Parameter(Mandatory)][string]$ArtifactPath,[switch]$SkipLaunch)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $ArtifactPath).Path
$manifestPath=Join-Path $root 'waid-artifact.json';if(-not(Test-Path -LiteralPath $manifestPath)){throw 'Artifact manifest is missing.'}
$manifest=Get-Content -Raw -LiteralPath $manifestPath|ConvertFrom-Json
if($manifest.SchemaVersion-ne1-or$manifest.Signed-ne$false-or$manifest.SignatureStatus-notmatch'UNSIGNED DEVELOPMENT'){throw 'Unsigned development labeling is invalid.'}
foreach($entry in $manifest.Files){$path=[IO.Path]::GetFullPath((Join-Path $root $entry.Path));if(-not$path.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Artifact entry escapes the package root.'};if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw "Artifact file is missing: $($entry.Path)"};if((Get-Item -LiteralPath $path).Length-ne$entry.Length-or(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash-ne$entry.Sha256){throw "Artifact integrity failed: $($entry.Path)"}}
if(-not$SkipLaunch){$help=& (Join-Path $root 'cli\waid.exe') help 2>&1;$helpText=$help-join[Environment]::NewLine;if($LASTEXITCODE-or$helpText-notmatch'Windows AI Doctor CLI'){throw 'Published CLI launch smoke test failed.'}}
Write-Host "Published $($manifest.Edition) $($manifest.Architecture) artifact passed integrity, labeling, dependency, and CLI launch smoke checks."

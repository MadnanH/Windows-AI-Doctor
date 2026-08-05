[CmdletBinding()]
param([string]$Configuration='Release',[string]$DotNetExecutable='dotnet',[int]$SoakIterations=5)
$ErrorActionPreference='Stop'
if($SoakIterations -lt 1 -or $SoakIterations -gt 100){throw 'SoakIterations must be between 1 and 100.'}
$root=Split-Path -Parent $PSScriptRoot
$project=Join-Path $root 'tests\WAID.Application.Tests\WAID.Application.Tests.csproj'
for($iteration=1;$iteration -le $SoakIterations;$iteration++){
    & $DotNetExecutable test $project -c $Configuration --no-build --filter 'Category=Performance' --logger "console;verbosity=minimal"
    if($LASTEXITCODE){throw "Performance validation failed on iteration $iteration."}
}
Write-Host "Performance budgets and $SoakIterations soak iterations passed."

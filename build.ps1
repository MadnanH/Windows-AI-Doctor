[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')][string]$Configuration='Release',
    [string]$DotNetExecutable='dotnet',
    [switch]$NoRestore
)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$solution=Join-Path $root 'WindowsAIDoctor.sln'
if(-not $NoRestore){& $DotNetExecutable restore $solution; if($LASTEXITCODE){exit $LASTEXITCODE}}
& $DotNetExecutable build $solution -c $Configuration --no-restore
if($LASTEXITCODE){exit $LASTEXITCODE}
& $DotNetExecutable test (Join-Path $root 'tests\WAID.Domain.Tests\WAID.Domain.Tests.csproj') -c $Configuration --no-build --filter 'Category!=DestructiveVm'
if($LASTEXITCODE){exit $LASTEXITCODE}
& $DotNetExecutable test (Join-Path $root 'tests\WAID.Application.Tests\WAID.Application.Tests.csproj') -c $Configuration --no-build --filter 'Category!=DestructiveVm'
if($LASTEXITCODE){exit $LASTEXITCODE}
& $DotNetExecutable test (Join-Path $root 'tests\WAID.Infrastructure.Tests\WAID.Infrastructure.Tests.csproj') -c $Configuration --no-build --filter 'Category!=DestructiveVm'
if($LASTEXITCODE){exit $LASTEXITCODE}
& $DotNetExecutable test (Join-Path $root 'tests\WAID.Diagnosis.Tests\WAID.Diagnosis.Tests.csproj') -c $Configuration --no-build --filter 'Category!=DestructiveVm'
if($LASTEXITCODE){exit $LASTEXITCODE}
& (Join-Path $root 'scripts\Test-QualityPolicy.ps1')
if($LASTEXITCODE){exit $LASTEXITCODE}
& (Join-Path $root 'scripts\Test-PerformanceBudgets.ps1') -Configuration $Configuration -DotNetExecutable $DotNetExecutable -SoakIterations 1
if($LASTEXITCODE){exit $LASTEXITCODE}
& (Join-Path $root 'scripts\Validate-KnowledgeBase.ps1') -DotNetExecutable $DotNetExecutable
if($LASTEXITCODE){exit $LASTEXITCODE}
& (Join-Path $root 'scripts\Test-AccessibilityNavigation.ps1')
if($LASTEXITCODE){exit $LASTEXITCODE}
& (Join-Path $root 'scripts\Test-Packaging.ps1')
if($LASTEXITCODE){exit $LASTEXITCODE}
& (Join-Path $root 'scripts\Test-ReleaseCandidate.ps1')
exit $LASTEXITCODE

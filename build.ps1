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
& $DotNetExecutable test (Join-Path $root 'tests\WAID.Domain.Tests\WAID.Domain.Tests.csproj') -c $Configuration --no-build
if($LASTEXITCODE){exit $LASTEXITCODE}
& $DotNetExecutable test (Join-Path $root 'tests\WAID.Application.Tests\WAID.Application.Tests.csproj') -c $Configuration --no-build
if($LASTEXITCODE){exit $LASTEXITCODE}
& $DotNetExecutable test (Join-Path $root 'tests\WAID.Infrastructure.Tests\WAID.Infrastructure.Tests.csproj') -c $Configuration --no-build
if($LASTEXITCODE){exit $LASTEXITCODE}
& $DotNetExecutable test (Join-Path $root 'tests\WAID.Diagnosis.Tests\WAID.Diagnosis.Tests.csproj') -c $Configuration --no-build
exit $LASTEXITCODE

[CmdletBinding()]
param([switch]$DisposableVmAcknowledged,[switch]$SnapshotAcknowledged,[string]$SnapshotId,[string]$ConfirmationText,[string]$DotNetExecutable='dotnet')
$ErrorActionPreference='Stop'
if(-not$IsWindows){throw 'Destructive validation is supported only on Windows.'}
$identity=[Security.Principal.WindowsIdentity]::GetCurrent();$admin=([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if($env:WAID_ALLOW_DESTRUCTIVE_VM_TESTS-ne'1'){throw 'Set WAID_ALLOW_DESTRUCTIVE_VM_TESTS=1 only inside the disposable test VM.'}
if(-not$DisposableVmAcknowledged-or-not$SnapshotAcknowledged-or[string]::IsNullOrWhiteSpace($SnapshotId)){throw 'Disposable VM and recoverable snapshot ID acknowledgements are required.'}
if($ConfirmationText-cne'RUN DESTRUCTIVE VM TEST'){throw 'The exact separate confirmation phrase was not supplied.'}
if(-not$admin){throw 'Destructive VM tests require an elevated administrator session.'}
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$listed=& $DotNetExecutable test (Join-Path $root 'WindowsAIDoctor.sln') -c Release --no-build --list-tests --filter 'Category=DestructiveVm' 2>&1
if($LASTEXITCODE){throw 'Could not enumerate destructive VM tests.'}
if(($listed-join"`n")-notmatch'WAID\..*Tests\.'){throw 'No destructive VM tests are registered in this release; no action was performed.'}
& $DotNetExecutable test (Join-Path $root 'WindowsAIDoctor.sln') -c Release --no-build --filter 'Category=DestructiveVm' --logger 'console;verbosity=detailed'
exit $LASTEXITCODE

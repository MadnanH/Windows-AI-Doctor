[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Import-Module (Join-Path $root 'packaging\Waid.Release.psm1') -Force
$temporary=Join-Path ([IO.Path]::GetTempPath()) "WAID-PackageTests-$([guid]::NewGuid().ToString('N'))"
try{
    $installed=Join-Path $temporary 'installed';$data=Join-Path $temporary 'data';$portable=Join-Path $temporary 'portable'
    New-Item -ItemType Directory -Path $installed,$data,$portable -Force|Out-Null
    Set-Content -LiteralPath (Join-Path $installed 'WAID.Desktop.exe') -Value 'v1';Set-Content -LiteralPath (Join-Path $data 'waid.db') -Value 'preserved-user-data';Set-Content -LiteralPath (Join-Path $portable '.waid-portable-workspace') -Value 'portable'
    $upgrade=Get-WaidUpgradeDecision -InstalledVersion '1.2.0' -CandidateVersion '1.3.0';if(-not$upgrade.Allowed-or$upgrade.Kind-ne'Upgrade'){throw 'Valid upgrade was rejected.'}
    Set-Content -LiteralPath (Join-Path $installed 'WAID.Desktop.exe') -Value 'v2';if((Get-Content -Raw (Join-Path $data 'waid.db')).Trim()-ne'preserved-user-data'){throw 'Upgrade changed user data.'}
    $repair=Get-WaidUpgradeDecision -InstalledVersion '1.3.0' -CandidateVersion '1.3.0';if(-not$repair.Allowed-or$repair.Kind-ne'Repair'){throw 'Repair-install classification failed.'}
    $down=Get-WaidUpgradeDecision -InstalledVersion '1.3.0' -CandidateVersion '1.2.9';if($down.Allowed-or$down.Kind-ne'DowngradeBlocked'){throw 'Downgrade was not blocked.'}
    $retain=Get-WaidUninstallPlan -DataAction retain -DataRoot $data;$remove=Get-WaidUninstallPlan -DataAction remove -DataRoot $data;if($retain.RequiresExplicitDataApproval-or-not$remove.RequiresExplicitDataApproval){throw 'Uninstall approval contract is invalid.'}
    if(-not(Test-Path (Join-Path $data 'waid.db'))-or-not(Test-Path (Join-Path $portable '.waid-portable-workspace'))){throw 'Installed and portable data did not coexist.'}
    Write-Host 'Installer lifecycle contract passed: clean layout, upgrade preservation, repair, downgrade block, uninstall choices, and portable coexistence.'
}finally{if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary -Recurse -Force}}

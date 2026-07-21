using WAID.Infrastructure.PowerShell;

namespace WAID.Infrastructure.Diagnostics;

public sealed class WindowsEventViewerScanner(IPowerShellRunner ps) : PowerShellDiagnosticScanner(ps)
{
    public override string Id => "waid.event-viewer"; public override string DisplayName => "Windows Event Viewer";
    protected override string Script => """
        $map=@{41='EVENT_41';55='NTFS_ERROR';7='DISK_ERROR';20='UPDATE_EVENT_20';25='UPDATE_EVENT_25';7000='EVENT_7000';7001='EVENT_7001';7031='EVENT_7031';4101='EVENT_4101'}
        @((Get-WinEvent -FilterHashtable @{LogName='System';StartTime=(Get-Date).AddDays(-7);Id=$map.Keys} -ErrorAction SilentlyContinue | Select-Object -First 100 | ForEach-Object {
          [pscustomobject]@{code=$map[$_.Id];title="System event $($_.Id)";description=$_.Message;severity=if($_.Level -le 2){'Critical'}else{'Warning'};repairId=$null;evidence=@{eventId="$($_.Id)";provider=$_.ProviderName;category='Windows';timeCreated="$($_.TimeCreated.ToUniversalTime().ToString('O'))"}}
        })) | ConvertTo-Json -Depth 5 -Compress
        """;
}

public sealed class ReliabilityMonitorScanner(IPowerShellRunner ps) : PowerShellDiagnosticScanner(ps)
{
    public override string Id => "waid.reliability"; public override string DisplayName => "Reliability Monitor";
    protected override string Script => """
        @((Get-CimInstance -ClassName Win32_ReliabilityRecords -ErrorAction SilentlyContinue | Where-Object {$_.TimeGenerated -ge (Get-Date).AddDays(-14) -and $_.SourceName -match 'Application|Windows'} | Select-Object -First 50 | ForEach-Object {
          [pscustomobject]@{code='APP_CRASH';title="Reliability failure: $($_.ProductName)";description=$_.Message;severity='Warning';repairId=$null;evidence=@{source=$_.SourceName;eventId="$($_.EventIdentifier)";category='Performance'}}
        })) | ConvertTo-Json -Depth 5 -Compress
        """;
}

public sealed class InstalledDriversScanner(IPowerShellRunner ps) : PowerShellDiagnosticScanner(ps)
{
    public override string Id => "waid.drivers"; public override string DisplayName => "Installed drivers";
    protected override string Script => """
        @((Get-CimInstance Win32_PnPEntity -ErrorAction SilentlyContinue | Where-Object {$_.ConfigManagerErrorCode -ne 0} | ForEach-Object {
          [pscustomobject]@{code='DRIVER_ERROR';title="Driver problem: $($_.Name)";description="Windows reports device error code $($_.ConfigManagerErrorCode).";severity=if($_.ConfigManagerErrorCode -in 22,28){'Warning'}else{'Critical'};repairId=$null;evidence=@{deviceId=$_.DeviceID;errorCode="$($_.ConfigManagerErrorCode)";category='Drivers'}}
        })) | ConvertTo-Json -Depth 5 -Compress
        """;
}

public sealed class InstalledSoftwareScanner(IPowerShellRunner ps) : PowerShellDiagnosticScanner(ps)
{
    public override string Id => "waid.software"; public override string DisplayName => "Installed software";
    protected override string Script => """
        $paths='HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*','HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*'
        $apps=@(Get-ItemProperty $paths -ErrorAction SilentlyContinue | Where-Object DisplayName)
        @($(if($apps.Count -gt 250){[pscustomobject]@{code='SOFTWARE_INVENTORY_LARGE';title='Large installed software inventory';description="$($apps.Count) installed applications were detected.";severity='Information';repairId=$null;evidence=@{count="$($apps.Count)";category='Performance'}}})) | ConvertTo-Json -Depth 5 -Compress
        """;
}

public sealed class WindowsUpdateScanner(IPowerShellRunner ps) : PowerShellDiagnosticScanner(ps)
{
    public override string Id => "waid.windows-update"; public override string DisplayName => "Windows Update";
    protected override string Script => """
        $session=New-Object -ComObject Microsoft.Update.Session
        $history=@($session.CreateUpdateSearcher().QueryHistory(0,50) | Where-Object {$_.Date -ge (Get-Date).AddDays(-30) -and $_.ResultCode -in 4,5})
        @($history | ForEach-Object {[pscustomobject]@{code='WINDOWS_UPDATE_FAILURE';title='Windows Update failed';description=$_.Title;severity='Warning';repairId='waid.windows-update-reset';evidence=@{resultCode="$($_.ResultCode)";hResult=('0x{0:X8}' -f ($_.HResult -band 0xffffffffL));category='Windows'}}}) | ConvertTo-Json -Depth 5 -Compress
        """;
}

public sealed class RunningServicesScanner(IPowerShellRunner ps) : PowerShellDiagnosticScanner(ps)
{
    public override string Id => "waid.services"; public override string DisplayName => "Running services";
    protected override string Script => """
        @((Get-CimInstance Win32_Service -ErrorAction SilentlyContinue | Where-Object {$_.StartMode -eq 'Auto' -and $_.State -ne 'Running' -and $_.Name -notmatch '^(MapsBroker|sppsvc|WbioSrvc)$'} | ForEach-Object {
          [pscustomobject]@{code=if($_.Name -in 'wuauserv','BITS'){ 'UPDATE_SERVICE_STOPPED' }else{'SERVICE_FAILURE'};title="Automatic service stopped: $($_.DisplayName)";description="The $($_.Name) service is configured for automatic startup but is $($_.State).";severity='Warning';repairId=$null;evidence=@{serviceName=$_.Name;state=$_.State;category='Windows'}}
        })) | ConvertTo-Json -Depth 5 -Compress
        """;
}

public sealed class StartupApplicationsScanner(IPowerShellRunner ps) : PowerShellDiagnosticScanner(ps)
{
    public override string Id => "waid.startup"; public override string DisplayName => "Startup applications";
    protected override string Script => """
        $items=@(Get-CimInstance Win32_StartupCommand -ErrorAction SilentlyContinue)
        @($(if($items.Count -gt 20){[pscustomobject]@{code='STARTUP_OVERLOAD';title='Heavy startup workload';description="$($items.Count) applications are configured to run at sign-in.";severity='Warning';repairId=$null;evidence=@{count="$($items.Count)";category='Performance'}}})) | ConvertTo-Json -Depth 5 -Compress
        """;
}

public sealed class RegistryHealthScanner(IPowerShellRunner ps) : PowerShellDiagnosticScanner(ps)
{
    public override string Id => "waid.registry-health"; public override string DisplayName => "Registry health";
    protected override string Script => """
        $pending=(Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager' -Name PendingFileRenameOperations -ErrorAction SilentlyContinue).PendingFileRenameOperations
        $servicing=Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending'
        @($(if($pending -or $servicing){[pscustomobject]@{code='REBOOT_PENDING';title='Windows restart is pending';description='Windows has pending servicing or file replacement operations.';severity='Information';repairId=$null;evidence=@{componentServicing="$servicing";category='Windows'}}})) | ConvertTo-Json -Depth 5 -Compress
        """;
}

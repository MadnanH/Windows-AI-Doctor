using WAID.Infrastructure.PowerShell;

namespace WAID.Infrastructure.Diagnostics;

public sealed class WindowsDefenderScanner(IPowerShellRunner ps) : PowerShellDiagnosticScanner(ps)
{
    public override string Id => "waid.defender"; public override string DisplayName => "Windows Defender";
    protected override string Script => """
        $status=Get-MpComputerStatus -ErrorAction SilentlyContinue
        @($(if($status -and (-not $status.AntivirusEnabled -or -not $status.RealTimeProtectionEnabled)){[pscustomobject]@{code='DEFENDER_DISABLED';title='Microsoft Defender protection is disabled';description='Antivirus or real-time protection is not active.';severity='Critical';repairId=$null;evidence=@{antivirusEnabled="$($status.AntivirusEnabled)";realTimeEnabled="$($status.RealTimeProtectionEnabled)";category='Security'}}})) | ConvertTo-Json -Depth 5 -Compress
        """;
}

public sealed class NetworkConfigurationScanner(IPowerShellRunner ps) : PowerShellDiagnosticScanner(ps)
{
    public override string Id => "waid.network"; public override string DisplayName => "Network configuration";
    protected override string Script => """
        $adapters=@(Get-NetIPConfiguration -ErrorAction SilentlyContinue | Where-Object {$_.NetAdapter.Status -eq 'Up'})
        $items=@()
        if($adapters.Count -eq 0){$items+=[pscustomobject]@{code='NETWORK_CONFIG_ERROR';title='No connected network adapter';description='Windows reports no active network adapter.';severity='Warning';repairId='waid.tcpip-reset';evidence=@{category='Network'}}}
        foreach($adapter in $adapters){if(-not $adapter.DNSServer.ServerAddresses){$items+=[pscustomobject]@{code='DNS_FAILURE';title="No DNS server on $($adapter.InterfaceAlias)";description='The connected adapter has no DNS server address.';severity='Warning';repairId='waid.dns-reset';evidence=@{adapter=$adapter.InterfaceAlias;category='Network'}}}}
        @($items) | ConvertTo-Json -Depth 5 -Compress
        """;
}

public sealed class StorageHealthScanner(IPowerShellRunner ps) : PowerShellDiagnosticScanner(ps)
{
    public override string Id => "waid.storage-health"; public override string DisplayName => "Storage health";
    protected override string Script => """
        @((Get-Volume -ErrorAction SilentlyContinue | Where-Object {$_.DriveType -eq 'Fixed' -and $_.Size -gt 0 -and ($_.SizeRemaining/$_.Size) -lt .1} | ForEach-Object {
          [pscustomobject]@{code='STORAGE_LOW';title="Low storage on $($_.DriveLetter):";description=("{0:P0} free space remains." -f ($_.SizeRemaining/$_.Size));severity=if(($_.SizeRemaining/$_.Size) -lt .05){'Critical'}else{'Warning'};repairId=$null;evidence=@{drive="$($_.DriveLetter):";freeBytes="$($_.SizeRemaining)";totalBytes="$($_.Size)";category='Storage'}}
        })) | ConvertTo-Json -Depth 5 -Compress
        """;
}

public sealed class SmartScanner(IPowerShellRunner ps) : PowerShellDiagnosticScanner(ps)
{
    public override string Id => "waid.smart"; public override string DisplayName => "SMART storage status";
    protected override string Script => """
        @((Get-PhysicalDisk -ErrorAction SilentlyContinue | Where-Object {$_.HealthStatus -ne 'Healthy' -or $_.OperationalStatus -notcontains 'OK'} | ForEach-Object {
          [pscustomobject]@{code='SMART_WARNING';title="Storage device warning: $($_.FriendlyName)";description="Health: $($_.HealthStatus); operational status: $($_.OperationalStatus -join ', ').";severity='Critical';repairId=$null;evidence=@{device=$_.FriendlyName;health=$_.HealthStatus.ToString();serial=$_.SerialNumber;category='Storage'}}
        })) | ConvertTo-Json -Depth 5 -Compress
        """;
}

public sealed class MemoryScanner(IPowerShellRunner ps) : PowerShellDiagnosticScanner(ps)
{
    public override string Id => "waid.memory"; public override string DisplayName => "Memory";
    protected override string Script => """
        $os=Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue
        $ratio=if($os.TotalVisibleMemorySize){$os.FreePhysicalMemory/$os.TotalVisibleMemorySize}else{1}
        @($(if($ratio -lt .1){[pscustomobject]@{code='MEMORY_LOW';title='Low available memory';description=("Only {0:P0} of physical memory is available." -f $ratio);severity=if($ratio -lt .05){'Critical'}else{'Warning'};repairId=$null;evidence=@{freeKilobytes="$($os.FreePhysicalMemory)";totalKilobytes="$($os.TotalVisibleMemorySize)";category='Performance'}}})) | ConvertTo-Json -Depth 5 -Compress
        """;
}

public sealed class CpuScanner(IPowerShellRunner ps) : PowerShellDiagnosticScanner(ps)
{
    public override string Id => "waid.cpu"; public override string DisplayName => "CPU";
    protected override string Script => """
        @((Get-CimInstance Win32_Processor -ErrorAction SilentlyContinue | Where-Object {$_.LoadPercentage -ge 90} | ForEach-Object {
          [pscustomobject]@{code='CPU_HIGH';title='Sustained high CPU load';description="CPU load is $($_.LoadPercentage) percent.";severity='Warning';repairId=$null;evidence=@{processor=$_.Name;load="$($_.LoadPercentage)";category='Performance'}}
        })) | ConvertTo-Json -Depth 5 -Compress
        """;
}

public sealed class GpuScanner(IPowerShellRunner ps) : PowerShellDiagnosticScanner(ps)
{
    public override string Id => "waid.gpu"; public override string DisplayName => "GPU";
    protected override string Script => """
        @((Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue | Where-Object {$_.ConfigManagerErrorCode -ne 0 -or $_.Status -notin 'OK',$null} | ForEach-Object {
          [pscustomobject]@{code='GPU_ERROR';title="Graphics adapter problem: $($_.Name)";description="Status: $($_.Status); device error: $($_.ConfigManagerErrorCode).";severity='Critical';repairId=$null;evidence=@{adapter=$_.Name;driverVersion=$_.DriverVersion;errorCode="$($_.ConfigManagerErrorCode)";category='Hardware'}}
        })) | ConvertTo-Json -Depth 5 -Compress
        """;
}

public sealed class BsodMinidumpScanner(IPowerShellRunner ps) : PowerShellDiagnosticScanner(ps)
{
    public override string Id => "waid.bsod"; public override string DisplayName => "Blue-screen minidumps";
    protected override string Script => """
        $path=Join-Path $env:SystemRoot 'Minidump'
        @((Get-ChildItem -LiteralPath $path -Filter '*.dmp' -File -ErrorAction SilentlyContinue | Where-Object {$_.LastWriteTimeUtc -ge [datetime]::UtcNow.AddDays(-30)} | Select-Object -First 20 | ForEach-Object {
          [pscustomobject]@{code='BSOD_DUMP';title='Recent blue-screen minidump';description="Windows created $($_.Name) after a system crash.";severity='Critical';repairId=$null;evidence=@{fileName=$_.Name;createdUtc=$_.LastWriteTimeUtc.ToString('O');category='Hardware'}}
        })) | ConvertTo-Json -Depth 5 -Compress
        """;
}

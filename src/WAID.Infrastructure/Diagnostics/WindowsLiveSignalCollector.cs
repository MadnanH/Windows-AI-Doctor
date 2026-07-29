using System.ComponentModel;
using System.Runtime.InteropServices;
using WAID.Application.Services;

namespace WAID.Infrastructure.Diagnostics;

public sealed class WindowsLiveSignalCollector : ILiveSignalCollector
{
    private readonly object _gate = new(); private ulong? _previousIdle; private ulong? _previousKernel; private ulong? _previousUser;
    public string Id => "waid.live.windows";
    public IReadOnlySet<string> SignalIds { get; } = new HashSet<string>(["cpu", "memory", "storage"], StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<LiveCollectorReading>> CollectAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var readings = new List<LiveCollectorReading>(3);
        readings.Add(new("cpu", ReadCpu(), "percent", "GetSystemTimes", "Processor busy time since the previous low-overhead sample."));
        readings.Add(new("memory", ReadMemory(), "percent", "GlobalMemoryStatusEx", "Physical memory load reported by Windows."));
        readings.Add(new("storage", ReadStorageFree(), "percent-free", "DriveInfo", "Lowest free-space percentage across ready fixed drives."));
        return Task.FromResult<IReadOnlyList<LiveCollectorReading>>(readings);
    }

    private double? ReadCpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var i = ToUInt64(idle); var k = ToUInt64(kernel); var u = ToUInt64(user);
        lock (_gate)
        {
            if (_previousIdle is null) { _previousIdle=i;_previousKernel=k;_previousUser=u;return null; }
            var idleDelta=i-_previousIdle.Value;var total=(k-_previousKernel!.Value)+(u-_previousUser!.Value);_previousIdle=i;_previousKernel=k;_previousUser=u;
            return total==0?null:Math.Clamp((total-idleDelta)*100d/total,0,100);
        }
    }
    private static double ReadMemory(){var status=new MemoryStatusEx();if(!GlobalMemoryStatusEx(status))throw new Win32Exception(Marshal.GetLastWin32Error());return Math.Clamp(status.MemoryLoad,0,100);}
    private static double? ReadStorageFree(){var values=DriveInfo.GetDrives().Where(d=>d.DriveType==DriveType.Fixed&&d.IsReady&&d.TotalSize>0).Select(d=>d.AvailableFreeSpace*100d/d.TotalSize).ToArray();return values.Length==0?null:Math.Clamp(values.Min(),0,100);}
    private static ulong ToUInt64(FileTime value)=>((ulong)value.HighDateTime<<32)|value.LowDateTime;
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool GetSystemTimes(out FileTime idle,out FileTime kernel,out FileTime user);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool GlobalMemoryStatusEx([In,Out]MemoryStatusEx buffer);
    [StructLayout(LayoutKind.Sequential)]private struct FileTime{public uint LowDateTime;public uint HighDateTime;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Auto)]private sealed class MemoryStatusEx{public uint Length=(uint)Marshal.SizeOf<MemoryStatusEx>();public uint MemoryLoad;public ulong TotalPhysical;public ulong AvailablePhysical;public ulong TotalPageFile;public ulong AvailablePageFile;public ulong TotalVirtual;public ulong AvailableVirtual;public ulong AvailableExtendedVirtual;}
}

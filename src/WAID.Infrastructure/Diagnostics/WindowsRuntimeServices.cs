using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Net.NetworkInformation;
using Microsoft.Win32;
using WAID.Application.Services;
using WAID.KnowledgeBase;

namespace WAID.Infrastructure.Diagnostics;

public sealed class WindowsSystemConditionService : ISystemConditionService
{
    private readonly object _sync = new();
    private ulong _lastIdle, _lastKernel, _lastUser;
    public bool IsBatterySaverEnabled() => GetSystemPowerStatus(out var status) && status.SystemStatusFlag != 0;
    public bool IsPluggedIn() => !GetSystemPowerStatus(out var status) || status.ACLineStatus != 0;
    public bool IsSystemIdle()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        return GetLastInputInfo(ref info) && unchecked((uint)Environment.TickCount - info.Time) >= 5 * 60 * 1000;
    }
    public bool IsNetworkAvailable() => NetworkInterface.GetIsNetworkAvailable();
    public double GetSystemLoadPercent()
    {
        lock (_sync)
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
            var currentIdle = ToUInt64(idle); var currentKernel = ToUInt64(kernel); var currentUser = ToUInt64(user);
            var total = currentKernel - _lastKernel + currentUser - _lastUser; var idleDelta = currentIdle - _lastIdle;
            _lastIdle = currentIdle; _lastKernel = currentKernel; _lastUser = currentUser;
            return total == 0 ? 0 : Math.Clamp((total - idleDelta) * 100d / total, 0, 100);
        }
    }
    private static ulong ToUInt64(FileTime value) => ((ulong)value.High << 32) | value.Low;
    [StructLayout(LayoutKind.Sequential)] private struct PowerStatus { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public uint BatteryLifeTime, BatteryFullLifeTime; }
    [StructLayout(LayoutKind.Sequential)] private struct LastInputInfo { public uint Size, Time; }
    [StructLayout(LayoutKind.Sequential)] private struct FileTime { public uint Low, High; }
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out PowerStatus status);
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInputInfo info);
    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
}

public sealed class WindowsStartupLaunchService(string executablePath) : IStartupLaunchService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WindowsAIDoctor";
    public bool IsEnabled() { using var key = Registry.CurrentUser.OpenSubKey(RunKey); return key?.GetValue(ValueName) is string; }
    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true) ?? throw new InvalidOperationException("The current-user startup registry key is unavailable.");
        if (enabled) key.SetValue(ValueName, $"\"{executablePath}\"", RegistryValueKind.String); else key.DeleteValue(ValueName, false);
    }
}

public sealed class MinidumpAnalyzer(DiagnosticKnowledgeBase knowledgeBase)
{
    public IReadOnlyCollection<CrashRecord> Discover(string directory, int maximum = 50)
    {
        if (!Directory.Exists(directory)) return [];
        return Directory.EnumerateFiles(directory, "*.dmp", SearchOption.TopDirectoryOnly).OrderByDescending(File.GetLastWriteTimeUtc).Take(maximum).Select(Parse).ToArray();
    }
    public CrashRecord Parse(string path)
    {
        var info = new FileInfo(path);
        uint? code = null; string? module = null; var crashTime = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt32() != 0x504D444D) throw new InvalidDataException("The file is not a Windows minidump.");
            _ = reader.ReadUInt32(); var streamCount = reader.ReadUInt32(); var directoryRva = reader.ReadUInt32(); _ = reader.ReadUInt32();
            var unixTime = reader.ReadUInt32(); if (unixTime > 0) crashTime = DateTimeOffset.FromUnixTimeSeconds(unixTime);
            for (var index = 0; index < streamCount; index++)
            {
                stream.Position = directoryRva + index * 12L; var type = reader.ReadUInt32(); var size = reader.ReadUInt32(); var rva = reader.ReadUInt32();
                if (type == 6 && size >= 16 && rva + 16 <= stream.Length) { stream.Position = rva + 8; code = reader.ReadUInt32(); }
                if (type == 4 && size >= 28 && rva + 28 <= stream.Length)
                {
                    stream.Position = rva; var count = Math.Min(reader.ReadUInt32(), 4096u); string? fallback = null;
                    for (var moduleIndex = 0u; moduleIndex < count; moduleIndex++)
                    {
                        var entry = rva + 4L + moduleIndex * 108L; if (entry + 24 > stream.Length) break;
                        stream.Position = entry + 20; var name = ReadDumpString(stream, reader, reader.ReadUInt32()); fallback ??= name;
                        if (name?.EndsWith(".sys", StringComparison.OrdinalIgnoreCase) == true && !name.EndsWith("ntoskrnl.exe", StringComparison.OrdinalIgnoreCase)) { module = name; break; }
                    }
                    module ??= fallback;
                }
            }
        }
        catch (IOException) { }
        var key = code.HasValue ? $"0x{code.Value:X8}" : null;
        var reference = key is null ? null : knowledgeBase.FindReference("BugCheck", key);
        return new(info.Name, crashTime, code, module, info.Length,
            reference?.Meaning ?? "Windows recorded a crash, but the available minidump metadata does not identify a known cause.",
            ["Update Windows and device drivers.", "Review correlated Event Viewer and hardware health evidence.", "Preserve the dump for advanced debugging."]);
    }
    public IReadOnlyCollection<CrashGroup> Group(IReadOnlyCollection<CrashRecord> crashes) => crashes.GroupBy(crash => $"{crash.BugCheckCode:X8}:{crash.SuspectedModule}", StringComparer.OrdinalIgnoreCase).Select(group =>
    {
        var first = group.Min(x => x.CrashTimeUtc); var last = group.Max(x => x.CrashTimeUtc); var days = Math.Max(1, (last - first).TotalDays + 1);
        return new CrashGroup(group.Key, group.Count(), first, last, Math.Round(group.Count() * 7 / days, 2), group.OrderByDescending(x => x.CrashTimeUtc).ToArray());
    }).OrderByDescending(group => group.Count).ToArray();
    private static string? ReadDumpString(Stream stream, BinaryReader reader, uint rva)
    {
        if (rva == 0 || rva + 4 > stream.Length) return null; stream.Position = rva; var bytes = reader.ReadUInt32();
        if (bytes == 0 || rva + 4L + bytes > stream.Length || bytes > 32768) return null;
        return System.Text.Encoding.Unicode.GetString(reader.ReadBytes((int)bytes));
    }
}

using WAID.Application.Abstractions;
namespace WAID.Infrastructure.Repairs;
public sealed class WindowsRecoveryStorageProbe : IRecoveryStorageProbe
{
    public bool HasAvailableSpace(string path, long requiredBytes) { try { var root = Path.GetPathRoot(Path.GetFullPath(path)); return !string.IsNullOrWhiteSpace(root) && new DriveInfo(root).AvailableFreeSpace >= requiredBytes; } catch { return false; } }
}
using WAID.Domain.Diagnostics;

namespace WAID.Domain.Tests;

public sealed class ScanSessionTests
{
    [Fact]
    public void Completed_session_is_immutable()
    {
        var started = DateTimeOffset.UtcNow;
        var session = new ScanSession(Guid.NewGuid(), started);
        session.Complete(started.AddSeconds(1));

        Assert.Throws<InvalidOperationException>(() => session.AddFindings([
            new DiagnosticFinding("disk", "DISK001", "Disk", "Low disk space", DiagnosticSeverity.Warning)]));
    }

    [Fact]
    public void Completion_cannot_precede_start()
    {
        var started = DateTimeOffset.UtcNow;
        var session = new ScanSession(Guid.NewGuid(), started);

        Assert.Throws<ArgumentOutOfRangeException>(() => session.Complete(started.AddSeconds(-1)));
    }

    [Fact]
    public void Finding_can_be_rehydrated_with_its_original_identity()
    {
        var id = Guid.NewGuid();

        var finding = new DiagnosticFinding(
            "disk", "DISK001", "Disk", "Low disk space",
            DiagnosticSeverity.Warning, id: id);

        Assert.Equal(id, finding.Id);
    }

    [Fact]
    public void Finding_rejects_an_empty_identity() =>
        Assert.Throws<ArgumentException>(() => new DiagnosticFinding(
            "disk", "DISK001", "Disk", "Low disk space",
            DiagnosticSeverity.Warning, id: Guid.Empty));
}

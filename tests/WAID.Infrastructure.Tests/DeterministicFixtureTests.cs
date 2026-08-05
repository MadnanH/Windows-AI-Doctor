using WAID.Testing;

namespace WAID.Infrastructure.Tests;

[Trait("Category",WaidTestCategories.Unit)]
public sealed class DeterministicFixtureTests
{
    [Fact]public void Fixed_time_advances_only_when_requested(){var start=new DateTimeOffset(2026,1,2,3,4,5,TimeSpan.Zero);var time=new FixedTimeProvider(start);Assert.Equal(start,time.GetUtcNow());time.Advance(TimeSpan.FromMinutes(2));Assert.Equal(start.AddMinutes(2),time.GetUtcNow());}
    [Fact]public void Isolated_workspaces_are_unique_and_cleaned(){string root;using(var workspace=new IsolatedTestWorkspace("fixture")){root=workspace.Root;Assert.True(Directory.Exists(root));Assert.Contains("WindowsAIDoctorTests",root,StringComparison.Ordinal);}Assert.False(Directory.Exists(root));}
    [Fact]public void Isolated_workspace_rejects_traversal(){using var workspace=new IsolatedTestWorkspace("fixture");Assert.Throws<ArgumentException>(()=>workspace.PathFor("..\\escape.db"));}
    [Fact]public async Task Async_gate_coordinates_without_sleep(){var gate=new AsyncTestGate();var worker=Task.Run(async()=>{gate.SignalEntered();await gate.WaitForReleaseAsync(CancellationToken.None);return 42;});await gate.WaitUntilEnteredAsync(CancellationToken.None);Assert.False(worker.IsCompleted);gate.Release();Assert.Equal(42,await worker);}
}

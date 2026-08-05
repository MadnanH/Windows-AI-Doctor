namespace WAID.Testing;

public static class WaidTestCategories
{
    public const string Unit="Unit";
    public const string Integration="Integration";
    public const string WindowsIntegration="WindowsIntegration";
    public const string Ui="UI";
    public const string Security="Security";
    public const string Performance="Performance";
    public const string Packaging="Packaging";
    public const string Architecture="Architecture";
    public const string CriticalPath="CriticalPath";
    public const string DestructiveVm="DestructiveVm";
}

public sealed class FixedTimeProvider(DateTimeOffset initialUtc) : TimeProvider
{
    private DateTimeOffset _utcNow=initialUtc.ToUniversalTime();
    public override DateTimeOffset GetUtcNow()=>_utcNow;
    public void Advance(TimeSpan duration)
    {
        if(duration<TimeSpan.Zero)throw new ArgumentOutOfRangeException(nameof(duration));
        _utcNow+=duration;
    }
}

public sealed class IsolatedTestWorkspace : IDisposable
{
    private readonly string _testRoot;
    private bool _disposed;
    public IsolatedTestWorkspace(string purpose)
    {
        if(string.IsNullOrWhiteSpace(purpose)||purpose.Any(ch=>!char.IsAsciiLetterOrDigit(ch)&&ch!='-'))throw new ArgumentException("Purpose must be a short safe identifier.",nameof(purpose));
        _testRoot=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"WindowsAIDoctorTests"));
        Root=Path.Combine(_testRoot,$"{purpose}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Root);
    }
    public string Root{get;}
    public string PathFor(string relativePath)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if(string.IsNullOrWhiteSpace(relativePath)||Path.IsPathFullyQualified(relativePath))throw new ArgumentException("A relative test path is required.",nameof(relativePath));
        var result=Path.GetFullPath(Path.Combine(Root,relativePath));
        if(!result.StartsWith(Root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("The test path must remain inside its isolated workspace.",nameof(relativePath));
        return result;
    }
    public string SqliteConnectionString(string name="waid.db")=>$"Data Source={PathFor(name)};Foreign Keys=True;Pooling=False";
    public void Dispose()
    {
        if(_disposed)return;
        _disposed=true;
        var root=Path.GetFullPath(Root);
        if(!root.StartsWith(_testRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Refusing to clean a path outside the test root.");
        if(Directory.Exists(root))Directory.Delete(root,true);
    }
}

public sealed class AsyncTestGate
{
    private readonly TaskCompletionSource _entered=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void SignalEntered()=>_entered.TrySetResult();
    public Task WaitUntilEnteredAsync(CancellationToken token)=>_entered.Task.WaitAsync(token);
    public Task WaitForReleaseAsync(CancellationToken token)=>_release.Task.WaitAsync(token);
    public void Release()=>_release.TrySetResult();
}

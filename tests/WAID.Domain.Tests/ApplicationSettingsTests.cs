using WAID.Domain.Settings;

namespace WAID.Domain.Tests;

public sealed class ApplicationSettingsTests
{
    [Theory]
    [InlineData(9)]
    [InlineData(3601)]
    public void Rejects_unsafe_timeout(int seconds) =>
        Assert.Throws<InvalidOperationException>(() => new ApplicationSettings { ScanTimeoutSeconds = seconds }.Validate());
}

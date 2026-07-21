using Microsoft.Extensions.DependencyInjection;
using WAID.Application.Abstractions;
using WAID.Domain.Diagnostics;
using WAID.Infrastructure.Plugins;

namespace WAID.Infrastructure.Tests;

public sealed class CompositionRootTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Supported_unsigned_and_signature_required_configurations_resolve(bool requireSignatures)
    {
        var root = CreateRoot();
        try
        {
            var options = Options(root, requireSignatures);
            var services = new ServiceCollection().AddWaidInfrastructure(options)
                .AddWaidPlugins(new PluginSecurityPolicy(options.AllowedPluginPublishers, options.RequireSignedPlugins), options.PluginDirectory, options.HostVersion);
            await using var provider = services.BuildValidatedWaidServiceProvider();
            Assert.Equal(options, provider.GetRequiredService<WaidHostOptions>());
            Assert.Equal(7, provider.GetRequiredService<WaidModuleCatalog>().Items.Count);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    [Fact]
    public void Missing_configuration_returns_an_actionable_typed_failure()
    {
        var failure = Assert.Throws<WaidStartupException>(() => new ServiceCollection().BuildValidatedWaidServiceProvider());
        Assert.Equal("WAID-DI-MISSING", failure.Code); Assert.False(string.IsNullOrWhiteSpace(failure.RecoveryAction));
    }

    [Fact]
    public void Unsupported_configuration_version_is_rejected_before_directories_are_created()
    {
        var root = Path.Combine(Path.GetTempPath(), $"waid-options-{Guid.NewGuid():N}");
        var options = Options(root) with { ConfigurationVersion = 99 };
        var failure = Assert.Throws<WaidStartupException>(() => new ServiceCollection().AddWaidInfrastructure(options));
        Assert.Equal("WAID-CONFIG-VERSION", failure.Code); Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void Duplicate_required_registration_is_rejected_deterministically()
    {
        var root = CreateRoot();
        try
        {
            var options = Options(root); var services = new ServiceCollection().AddWaidInfrastructure(options)
                .AddWaidPlugins(new PluginSecurityPolicy(options.AllowedPluginPublishers), options.PluginDirectory, options.HostVersion)
                .AddSingleton<IScanRepository, DuplicateScanRepository>();
            var failure = Assert.Throws<WaidStartupException>(() => services.BuildValidatedWaidServiceProvider());
            Assert.Equal("WAID-DI-DUPLICATE", failure.Code);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    [Fact]
    public void Singleton_capturing_scoped_service_is_rejected()
    {
        var root = CreateRoot();
        try
        {
            var options = Options(root); var services = new ServiceCollection().AddWaidInfrastructure(options)
                .AddWaidPlugins(new PluginSecurityPolicy(options.AllowedPluginPublishers), options.PluginDirectory, options.HostVersion)
                .AddScoped<ScopedDependency>().AddSingleton<InvalidSingleton>();
            var failure = Assert.Throws<WaidStartupException>(() => services.BuildValidatedWaidServiceProvider());
            Assert.Equal("WAID-DI-INVALID", failure.Code); Assert.NotNull(failure.InnerException);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    private static string CreateRoot() { var root = Path.Combine(Path.GetTempPath(), $"waid-composition-{Guid.NewGuid():N}"); Directory.CreateDirectory(root); return root; }
    private static WaidHostOptions Options(string root, bool requireSignatures = false) => new(WaidHostOptions.CurrentConfigurationVersion, Path.GetFullPath(root), Path.GetFullPath(Path.Combine(root, "Plugins")), Path.GetFullPath(Path.Combine(root, "WAID.Desktop.exe")), new Version(1, 0, 0), ["WAID Engineering"], requireSignatures);
    public sealed class ScopedDependency;
    public sealed class InvalidSingleton(ScopedDependency dependency) { public ScopedDependency Dependency { get; } = dependency; }
    private sealed class DuplicateScanRepository : IScanRepository
    {
        public Task SaveAsync(ScanSession session, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<ScanSession>> GetRecentAsync(int count, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ScanSession>>([]);
    }
}

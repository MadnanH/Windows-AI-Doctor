using WAID.Application.Abstractions;
using WAID.Domain.Diagnostics;
using WAID.Plugin.Sample;
using WAID.Infrastructure.Plugins;

namespace WAID.Infrastructure.Tests;

public sealed class PluginScannerTests
{
    [Fact] public void Malformed_plugin_is_quarantined_without_crashing_host()
    {
        var root=Path.Combine(Path.GetTempPath(),$"waid-plugin-{Guid.NewGuid():N}");Directory.CreateDirectory(root);
        try{File.WriteAllText(Path.Combine(root,"bad.waid-plugin.json"),"{not-json");var catalog=new PluginCatalog();var loaded=new PluginLoader().Load(root,new Version(1,0),new PluginSecurityPolicy(["WAID Engineering"]),catalog);Assert.Empty(loaded);Assert.Equal(PluginState.Quarantined,Assert.Single(catalog.Items).State);}finally{Directory.Delete(root,true);}
    }
    [Fact] public void Disabled_plugin_state_is_persisted_atomically()
    {
        var root=Path.Combine(Path.GetTempPath(),$"waid-plugin-{Guid.NewGuid():N}");
        try{PluginLoader.SetDisabled(root,"plugin.id",true);Assert.Contains("plugin.id",File.ReadAllText(Path.Combine(root,"plugin-state.json")),StringComparison.Ordinal);PluginLoader.SetDisabled(root,"plugin.id",false);Assert.DoesNotContain("plugin.id",File.ReadAllText(Path.Combine(root,"plugin-state.json")),StringComparison.Ordinal);}finally{Directory.Delete(root,true);}
    }
    [Fact]
    public async Task Sample_environment_scanner_performs_a_real_path_check()
    {
        var scanner = new EnvironmentScanner();

        var findings = await scanner.ScanAsync(new ScanContext(Guid.NewGuid(), false, DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.All(findings, finding =>
        {
            Assert.Equal(scanner.Id, finding.ScannerId);
            Assert.StartsWith("ENV_PATH_", finding.Code, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(finding.Description));
        });
    }
}

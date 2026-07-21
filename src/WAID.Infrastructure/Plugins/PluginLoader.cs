using System.Reflection;
using System.Runtime.Loader;
using WAID.Application.Plugins;
namespace WAID.Infrastructure.Plugins;
public sealed class PluginLoader
{
    public IReadOnlyList<IWaidPlugin> Load(string directory,Version hostVersion)
    {
        Directory.CreateDirectory(directory); var plugins=new List<IWaidPlugin>();
        foreach(var file in Directory.EnumerateFiles(directory,"*.dll",SearchOption.TopDirectoryOnly)) { var assembly=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(file)); foreach(var type in assembly.GetTypes().Where(t=>typeof(IWaidPlugin).IsAssignableFrom(t)&&!t.IsAbstract&&!t.IsInterface)) if(Activator.CreateInstance(type) is IWaidPlugin plugin && plugin.Metadata.MinimumHostVersion<=hostVersion) plugins.Add(plugin); }
        return plugins.AsReadOnly();
    }
}

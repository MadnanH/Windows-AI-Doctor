using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using WAID.Infrastructure.Plugins;
namespace WAID.Desktop.Views;
public sealed class PluginsPage:Page
{
    public PluginsPage(PluginCatalog catalog)
    {
        var panel=new StackPanel{Padding=new Thickness(32),Spacing=16,MaxWidth=900,HorizontalAlignment=HorizontalAlignment.Center};
        var title=new TextBlock{Text="Plugins",FontSize=28};AutomationProperties.SetAutomationId(title,"PluginsPageTitle");panel.Children.Add(title);
        panel.Children.Add(new TextBlock{Text="Plugins are validated, publisher allow-listed, dependency-isolated, and quarantined after a load failure. Enable or disable changes take effect after restart.",TextWrapping=TextWrapping.Wrap});
        if(catalog.Items.Count==0)panel.Children.Add(new TextBlock{Text="No plugins are installed."});
        foreach(var item in catalog.Items)panel.Children.Add(new TextBlock{Text=$"{item.Name} | {item.State} | {item.Detail}",TextWrapping=TextWrapping.Wrap});
        Content=new ScrollViewer{Content=panel};
    }
}

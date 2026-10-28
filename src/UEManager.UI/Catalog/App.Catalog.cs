using System.Windows;
using UEManager.Core;

namespace UEManager.UI;

/// <summary>The openmods page's part of the start-up. Only a build with the catalog compiles it in.</summary>
public partial class App
{
    /// <summary>--catalog &lt;url&gt; reads another openmods games list, such as a test copy of it.</summary>
    partial void ReadCatalogArgument(string[] args, ref string? catalogUrl)
    {
        catalogUrl = ArgumentAfter(args, "--catalog");
        if (catalogUrl is not null)
            Log.Info($"openmods list from the command line: '{catalogUrl}'.");
    }

    /// <summary>The navigation entry and the page, which MainWindow shows through DynamicResource.</summary>
    partial void AddCatalogResources() => Resources.MergedDictionaries.Add(new ResourceDictionary
    {
        Source = new Uri("pack://application:,,,/Catalog/CatalogResources.xaml", UriKind.Absolute),
    });
}

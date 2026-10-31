using System.Windows;
using System.Windows.Controls;

namespace UEManager.UI.Views;

public partial class ModsView : UserControl
{
    /// <summary>The room the mod's name keeps in the table; below it, the table drops what it can do without.</summary>
    private const double MinNameWidth = 200;

    /// <summary>The row's padding on both sides and the card's border, around the columns.</summary>
    private const double TableFrame = 2 * 22 + 2;

    /// <summary>
    /// The narrow table, as in a window snapped to half a 1080p screen: the switch without its words, the integrity
    /// icon without its word, and no install date (the mod's details show it).
    /// </summary>
    private static readonly Dictionary<string, object> Narrow = new()
    {
        ["Col.Status"] = new GridLength(56),
        ["Col.Integrity"] = new GridLength(36),
        ["Col.Date"] = new GridLength(0),
        ["Mods.WideOnly"] = Visibility.Collapsed,
    };

    private readonly Dictionary<string, object> wide;
    private readonly double wideColumns;
    private bool isNarrow;

    public ModsView()
    {
        InitializeComponent();
        wide = Narrow.Keys.ToDictionary(key => key, key => Resources[key]);
        wideColumns = new[] { "Col.Status", "Col.Type", "Col.Integrity", "Col.Date", "Col.Actions" }
            .Sum(key => ((GridLength)Resources[key]).Value);
    }

    /// <summary>Ctrl+F: the search box, with its text selected to type over.</summary>
    internal void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void OnTableSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var narrow = e.NewSize.Width - TableFrame - wideColumns < MinNameWidth;
        if (narrow == isNarrow)
            return;
        isNarrow = narrow;
        // The header and every row take their widths from these resources, so all of them follow at once.
        foreach (var (key, value) in narrow ? Narrow : wide)
            Resources[key] = value;
    }
}

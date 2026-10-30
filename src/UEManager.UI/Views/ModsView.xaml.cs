using System.Windows.Controls;

namespace UEManager.UI.Views;

public partial class ModsView : UserControl
{
    public ModsView() => InitializeComponent();

    /// <summary>Ctrl+F: the search box, with its text selected to type over.</summary>
    internal void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }
}

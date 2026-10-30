using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Threading;

namespace UEManager.UI.Views;

public partial class Ue4ssView : UserControl
{
    public Ue4ssView()
    {
        InitializeComponent();
        // The end of the log tells what went wrong, so a new log opens at its last line.
        DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty, typeof(ItemsControl))
            .AddValueChanged(LogLines, (_, _) => Dispatcher.BeginInvoke(ScrollToEnd, DispatcherPriority.Loaded));
    }

    private void ScrollToEnd() => (LogLines.Template.FindName("LogScroll", LogLines) as ScrollViewer)?.ScrollToEnd();

    /// <summary>Ctrl+F: the log's search box, with its text selected to type over.</summary>
    internal void FocusSearch()
    {
        SearchBox.BringIntoView();
        SearchBox.Focus();
        SearchBox.SelectAll();
    }
}

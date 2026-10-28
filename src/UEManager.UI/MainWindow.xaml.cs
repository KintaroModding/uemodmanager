using System.Windows;
using UEManager.UI.ViewModels;

namespace UEManager.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        StateChanged += (_, _) => OnWindowStateChanged();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    protected override void OnDragEnter(DragEventArgs e) => OnDragOver(e);

    protected override void OnDragOver(DragEventArgs e)
    {
        var files = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = files ? DragDropEffects.Copy : DragDropEffects.None;
        if (ViewModel is not null)
            ViewModel.IsDragOver = files;
        e.Handled = true;
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        if (ViewModel is not null)
            ViewModel.IsDragOver = false;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        if (ViewModel is null)
            return;
        ViewModel.IsDragOver = false;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            _ = ViewModel.InstallAsync(files);
        e.Handled = true;
    }

    private void OnWindowStateChanged()
    {
        // A maximized chromeless window reaches past the screen edge by the resize border.
        var maximized = WindowState == WindowState.Maximized;
        Root.Margin = maximized ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
        MaximizeButton.Content = maximized ? "" : "";
        MaximizeButton.ToolTip = maximized ? "Restore" : "Maximize";
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

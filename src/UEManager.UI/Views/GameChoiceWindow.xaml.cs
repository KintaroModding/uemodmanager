using System.Windows;
using System.Windows.Media;
using UEManager.Core.Storage;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;

namespace UEManager.UI.Views;

/// <summary>
/// The window at a start without a chosen game: the very first start, and once for everyone who used the app while
/// it knew only one game. It has the place and the size of the app's window, and holds only the game picker.
/// </summary>
public partial class GameChoiceWindow : Window
{
    /// <param name="backdrop">The picture behind the picker; without one, the window's own background shows.</param>
    /// <param name="placement">Where the app's window was when it last closed; null opens this one centred.</param>
    public GameChoiceWindow(GamePickerViewModel picker, ImageSource? backdrop, WindowPlacement? placement)
    {
        InitializeComponent();
        WindowFrame.Attach(this);
        Title = AppInfo.Name;
        AppName.Text = AppInfo.Name;
        DataContext = picker;
        Backdrop.Source = backdrop;
        // As the app's window: the start size is also the smallest.
        var smallest = MainWindow.Smallest(new Size(Width, Height), SystemParameters.WorkArea);
        MinWidth = Width = smallest.Width;
        MinHeight = Height = smallest.Height;
        if (placement is not null)
            MainWindow.Place(this, placement);
        OnWindowStateChanged();
        StateChanged += (_, _) => OnWindowStateChanged();
    }

    private void OnWindowStateChanged()
    {
        // A maximized chromeless window reaches past the screen edge by the resize border.
        var maximized = WindowState == WindowState.Maximized;
        Root.Margin = maximized ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
        MaximizeButton.Content = maximized ? "" : "";
        MaximizeButton.ToolTip = maximized ? L.T("Restore") : L.T("Maximize");
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

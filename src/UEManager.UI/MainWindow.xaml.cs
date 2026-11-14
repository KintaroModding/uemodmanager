using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using UEManager.Core.Storage;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;
using UEManager.UI.Views;

namespace UEManager.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        WindowFrame.Attach(this);
        // The start size is also the smallest, so there are fewer layouts to keep right.
        var smallest = Smallest(new Size(Width, Height), SystemParameters.WorkArea);
        MinWidth = Width = smallest.Width;
        MinHeight = Height = smallest.Height;
        OnWindowStateChanged();
        StateChanged += (_, _) => OnWindowStateChanged();
        // The tray icon follows its setting; minimize, and the X, can hide the window in it.
        StateChanged += (_, _) => HideInTray();
        Deactivated += (_, _) => deactivatedAt = DateTime.UtcNow;
        Loaded += (_, _) => FollowTraySetting();
        Closed += (_, _) => RemoveTray();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is MainViewModel old)
                old.PropertyChanged -= OnModelChanged;
            if (e.NewValue is MainViewModel model)
                model.PropertyChanged += OnModelChanged;
        };
        // Ctrl+F and F5, the keys of these commands, while no dialog covers the window.
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Find, (_, _) => FocusSearch(), CanUseKeys));
        CommandBindings.Add(new CommandBinding(NavigationCommands.Refresh, async (_, _) => await ViewModel!.ReloadAsync(), CanUseKeys));
        // The view model comes before the window shows, so it opens where it was last time.
        DataContextChanged += (_, e) =>
        {
            if (!IsLoaded && e.NewValue is MainViewModel { WindowPlacement: { } placement })
                PlaceAt(placement);
        };
        // The maximize button's tooltip is set here, so it follows a change of language too, on the window's thread.
        PropertyChangedEventManager.AddHandler(L.Current, (_, _) =>
        {
            if (Dispatcher.CheckAccess())
                OnWindowStateChanged();
            else
                Dispatcher.BeginInvoke(OnWindowStateChanged);
        }, nameof(L.Language));
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void CanUseKeys(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = ViewModel is { HasOverlay: false };

    /// <summary>The search of the UE4SS log on its page, the mod search from every other page.</summary>
    private void FocusSearch()
    {
        if (ViewModel!.SelectedPage == "Ue4ss")
        {
            Ue4ssPage.FocusSearch();
            return;
        }
        ViewModel.SelectedPage = "Mods";
        // The page shows only after the change of page is drawn.
        Dispatcher.BeginInvoke(ModsPage.FocusSearch, DispatcherPriority.Input);
    }

    /// <summary>
    /// The X hides the window in the tray when the player chose that; Exit in the tray menu, a switch of game and an update
    /// end the app. Unsaved settings in the editor: it asks first, and the window closes after the answer.
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!App.IsExiting && tray is not null && ViewModel is { ClosesToTray: true })
        {
            e.Cancel = true;
            Dispatcher.BeginInvoke(() => WindowState = WindowState.Minimized);
            return;
        }
        if (ViewModel?.SettingsEditor is { IsModified: true, IsReadOnly: false } editor)
        {
            e.Cancel = true;
            BringBack();
            editor.RequestClose(Close);
            return;
        }
        if (PlacementOf(this) is { } placement)
            ViewModel?.RememberWindow(placement);
        base.OnClosing(e);
    }

    private TrayIcon? tray;

    /// <summary>The state the window comes back in from the tray or the taskbar: normal or maximized.</summary>
    private WindowState shownState = WindowState.Normal;

    /// <summary>When the window last lost the focus: a click on the tray icon takes it away first.</summary>
    private DateTime deactivatedAt;

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnModelChanged(sender, e));
            return;
        }
        if (e.PropertyName is null or "" or nameof(MainViewModel.ShowTrayIcon))
            FollowTraySetting();
        if ((e.PropertyName is null or "" or nameof(MainViewModel.WindowTitle)) && ViewModel is { } model)
            tray?.SetTip(model.WindowTitle);
        // The launch options open with the cursor in the field, its text selected, so typing replaces it.
        if (e.PropertyName == nameof(MainViewModel.IsLaunchOptionsOpen) && ViewModel is { IsLaunchOptionsOpen: true })
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                LaunchArgumentsBox.Focus();
                LaunchArgumentsBox.SelectAll();
            });
    }

    /// <summary>Shows or removes the tray icon, as its setting says.</summary>
    private void FollowTraySetting()
    {
        if (!IsLoaded || ViewModel is not { } model)
            return;
        if (model.ShowTrayIcon && tray is null)
        {
            tray = new TrayIcon(TrayIcon.Draw(TrayIcon.Size, key => (Brush)FindResource(key)), model.WindowTitle);
            tray.LeftClick += ToggleFromTray;
            tray.RightClick += OpenTrayMenu;
        }
        else if (!model.ShowTrayIcon && tray is not null)
        {
            RemoveTray();
            // Without the icon, a window hidden in the tray could not come back.
            if (!IsVisible)
                BringBack();
        }
    }

    private void RemoveTray()
    {
        tray?.Dispose();
        tray = null;
    }

    /// <summary>Minimized, the window hides in the tray when the player chose that.</summary>
    private void HideInTray()
    {
        if (WindowState != WindowState.Minimized)
            shownState = WindowState;
        else if (tray is not null && ViewModel is { MinimizesToTray: true })
            Hide();
    }

    /// <summary>A left click on the tray icon: the window comes to the front; when it is in front already, it minimizes.</summary>
    private void ToggleFromTray()
    {
        var inFront = IsVisible && WindowState != WindowState.Minimized
            && (IsActive || DateTime.UtcNow - deactivatedAt < TimeSpan.FromMilliseconds(500));
        if (inFront)
            WindowState = WindowState.Minimized;
        else
            BringBack();
    }

    /// <summary>Shows the window in front, as it was before it was minimized or hidden in the tray.</summary>
    internal void BringBack()
    {
        if (!IsVisible)
            Show();
        if (WindowState == WindowState.Minimized)
            WindowState = shownState;
        Activate();
    }

    /// <summary>A right click on the tray icon: the menu with the games, in the app's look.</summary>
    private void OpenTrayMenu()
    {
        if (ViewModel is { } model)
            new TrayMenu(model.TrayMenu(open: BringBack, exit: App.Quit)).ShowAtCursor();
    }

    /// <summary>
    /// Where a window is: a maximized or minimized one by the place it returns to. Null for a window that never showed.
    /// </summary>
    internal static WindowPlacement? PlacementOf(Window window)
    {
        var bounds = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.Width, window.Height)
            : window.RestoreBounds;
        return !bounds.IsEmpty && double.IsFinite(bounds.Left) && double.IsFinite(bounds.Top)
            && double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height)
            ? new WindowPlacement(bounds.Left, bounds.Top, bounds.Width, bounds.Height, window.WindowState == WindowState.Maximized)
            : null;
    }

    /// <summary>The smallest window: the start size, or what fits a screen too small for it.</summary>
    internal static Size Smallest(Size start, Rect workArea) =>
        new(Math.Min(start.Width, workArea.Width), Math.Min(start.Height, workArea.Height));

    private void PlaceAt(WindowPlacement placement)
    {
        Place(this, placement);
        OnWindowStateChanged();
    }

    /// <summary>Opens a window at a saved place, when it is still on the screens; else it stays centred.</summary>
    internal static void Place(Window window, WindowPlacement placement)
    {
        var screens = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (Usable(placement, screens, new Size(window.MinWidth, window.MinHeight)) is not { } bounds)
            return;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = bounds.Left;
        window.Top = bounds.Top;
        window.Width = bounds.Width;
        window.Height = bounds.Height;
        if (placement.Maximized)
            window.WindowState = WindowState.Maximized;
    }

    /// <summary>
    /// The saved place, at least the minimum size, when enough of it and all of its title bar are on the screens; null
    /// when not, as after a screen was unplugged, so the window opens centred.
    /// </summary>
    internal static Rect? Usable(WindowPlacement placement, Rect screens, Size minimum)
    {
        if (!double.IsFinite(placement.Left) || !double.IsFinite(placement.Top)
            || !double.IsFinite(placement.Width) || !double.IsFinite(placement.Height))
            return null;
        var bounds = new Rect(placement.Left, placement.Top,
            Math.Clamp(placement.Width, minimum.Width, Math.Max(minimum.Width, screens.Width)),
            Math.Clamp(placement.Height, minimum.Height, Math.Max(minimum.Height, screens.Height)));
        var visible = Rect.Intersect(bounds, screens);
        var titleBarOnScreen = bounds.Top >= screens.Top && bounds.Top + TitleBarHeight <= screens.Bottom;
        return !visible.IsEmpty && visible.Width >= 200 && visible.Height >= 100 && titleBarOnScreen ? bounds : null;
    }

    /// <summary>The height of the title bar, as in the window's chrome.</summary>
    private const double TitleBarHeight = 34;

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
        MaximizeButton.ToolTip = maximized ? L.T("Restore") : L.T("Maximize");
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

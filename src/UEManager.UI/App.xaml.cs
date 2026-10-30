using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using UEManager.Core;
using UEManager.Core.Games;
using UEManager.Core.Storage;
using UEManager.Core.Updates;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;

namespace UEManager.UI;

public partial class App : Application
{
    /// <summary>Command-line options that take a value; never taken for downloads.</summary>
    private static readonly string[] ValueOptions = ["--data-dir", "--catalog", "--update-url"];

    /// <summary>
    /// Given to the copy a restart starts, as after an update: the old one may still be ending, so it waits
    /// for the data folder. The name stays for older versions, which start a new version with it.
    /// </summary>
    internal const string AfterUpdateOption = "--after-update";

    /// <summary>The start options, such as --data-dir, given again when the app restarts.</summary>
    internal static IReadOnlyList<string> Options { get; private set; } = [];

    /// <summary>Starts this exe again with the same options and ends this copy: after an update, or a reset of the settings.</summary>
    /// <returns>False when the new copy could not be started; this copy then keeps running.</returns>
    internal static bool Restart()
    {
        if (Environment.ProcessPath is not { } exe)
            return false;
        var start = new ProcessStartInfo(exe) { UseShellExecute = false };
        foreach (var option in Options)
            start.ArgumentList.Add(option);
        start.ArgumentList.Add(AfterUpdateOption);
        try
        {
            Process.Start(start);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Log.Error("The app could not be started again.", ex);
            return false;
        }
        Current.Shutdown();
        return true;
    }

    private SingleInstance? instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Options = OptionArguments(e.Args);

        // --data-dir <folder> keeps a test run away from the real data folder.
        var dataDir = ArgumentAfter(e.Args, "--data-dir");
        var paths = dataDir is null ? AppPaths.NextToApp() : new AppPaths(dataDir);
        L.Current.SetLanguage(AppSettings.Load(paths).Language);
        try
        {
            paths.EnsureWritable();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Nothing can be logged yet: the log lives in the folder that failed.
            MessageBox.Show(
                L.F($"{AppInfo.Name} cannot write to its data folder:\n\n{paths.Root}\n\nThe app keeps everything in a Data folder next to UEModManager.exe, so it needs write access to the folder it runs from. Windows protects folders such as C:\\Program Files.\n\nMove the whole app folder to a place you can write to, for example C:\\Games\\UEModManager or a folder on another drive, and start it again.\n\nDetails: {ex.Message}"),
                AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        Log.Initialize(paths.LogsDir);
        instance = SingleInstance.TryAcquire(paths, e.Args.Contains(AfterUpdateOption) ? TimeSpan.FromSeconds(30) : TimeSpan.Zero);
        if (instance is null)
        {
            HandOver(paths, DownloadArguments(e.Args));
            Shutdown();
            return;
        }
        Log.Info($"Starting {AppInfo.Version} from '{AppContext.BaseDirectory}'.");
        DispatcherUnhandledException += OnUnhandledException;
        CleanUpAfterUpdate();

        GameProfile profile;
        try
        {
            profile = GameProfile.BuiltIn().FirstOrDefault()
                ?? throw new InvalidDataException("This build contains no game profile.");
        }
        catch (InvalidDataException ex)
        {
            Log.Error("No usable game profile.", ex);
            MessageBox.Show(ex.Message, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        string? catalogUrl = null;
        ReadCatalogArgument(e.Args, ref catalogUrl);
        AddCatalogResources();

        // --update-url <url> reads another release list for the update check, such as a test copy of it.
        var updateUrl = ArgumentAfter(e.Args, "--update-url");
        if (updateUrl is not null)
            Log.Info($"Release list from the command line: '{updateUrl}'.");

        var viewModel = new MainViewModel(profile, paths, ArtSet.Load(), catalogUrl, updateUrl);
        MainWindow = new MainWindow { DataContext = viewModel };
        MainWindow.Show();
        instance.Listen(downloads => Dispatcher.BeginInvoke(() => Receive(viewModel, downloads)));
        _ = viewModel.InitializeAsync(DownloadArguments(e.Args));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        instance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>The app already runs with this data folder: it gets the downloads, and this start ends.</summary>
    private static void HandOver(AppPaths paths, IReadOnlyList<string> downloads)
    {
        if (SingleInstance.TryHandOver(paths, downloads))
        {
            Log.Info($"Another start handed {downloads.Count} download(s) to the app that is already running.");
            return;
        }
        MessageBox.Show(
            L.F($"{AppInfo.Name} is already running with the data folder\n\n{paths.Root}\n\nbut it does not answer. Close it, or end UEModManager.exe in the Task Manager, then start the app again."),
            AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>Another start, such as "Open with" in Explorer, handed over its downloads.</summary>
    private void Receive(MainViewModel viewModel, IReadOnlyList<string> downloads)
    {
        if (MainWindow.WindowState == WindowState.Minimized)
            MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
        if (downloads.Count > 0)
            _ = viewModel.InstallAsync(downloads);
    }

    // Hooks for the catalog, implemented in Catalog\App.Catalog.cs. They do nothing while the
    // catalog is switched off (AppInfo.IsCatalogOn).
    partial void ReadCatalogArgument(string[] args, ref string? catalogUrl);

    partial void AddCatalogResources();

    private static string? ArgumentAfter(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary>Files passed on the command line, as by "Open with" in Explorer, go to the install review.</summary>
    private static IReadOnlyList<string> DownloadArguments(string[] args)
    {
        var downloads = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (ValueOptions.Contains(args[i]))
                i++;
            else if (!args[i].StartsWith("--", StringComparison.Ordinal))
                downloads.Add(args[i]);
        }
        return downloads;
    }

    private static IReadOnlyList<string> OptionArguments(string[] args)
    {
        var options = new List<string>();
        for (var i = 0; i + 1 < args.Length; i++)
        {
            if (ValueOptions.Contains(args[i]))
            {
                options.Add(args[i]);
                options.Add(args[++i]);
            }
        }
        return options;
    }

    /// <summary>
    /// Deletes the old exe an update left behind. Right after the restart, the copy of the app that
    /// ran it may still be ending, so this tries a few times in the background.
    /// </summary>
    private static void CleanUpAfterUpdate()
    {
        if (Environment.ProcessPath is not { } exe)
            return;
        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 5 && !SelfUpdate.CleanUp(exe); attempt++)
                await Task.Delay(TimeSpan.FromSeconds(2));
        });
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled error.", e.Exception);
        MessageBox.Show(L.F($"Something went wrong: {L.Message(e.Exception)}\n\nThe details are in the log."),
            AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using UEManager.Cli;
using UEManager.Core;
using UEManager.Core.Games;
using UEManager.Core.Storage;
using UEManager.Core.Updates;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;
using UEManager.UI.Views;

namespace UEManager.UI;

public partial class App : Application
{
    /// <summary>Command-line options that take a value; never taken for downloads.</summary>
    private static readonly string[] ValueOptions = ["--data-dir", "--game", "--catalog", "--update-url", "--ue4ss-manifest-url"];

    /// <summary>
    /// Given to the copy a restart starts, as after an update: the old one may still be ending, so it waits
    /// for the data folder. The name stays for older versions, which start a new version with it.
    /// </summary>
    internal const string AfterUpdateOption = "--after-update";

    /// <summary>The start options, such as --data-dir, given again when the app restarts.</summary>
    internal static IReadOnlyList<string> Options { get; private set; } = [];

    /// <summary>
    /// Starts this exe again with the same options and ends this copy: after an update, a reset of the settings, or
    /// the choice of a game.
    /// </summary>
    /// <param name="game">
    /// The game the player just chose: the new copy starts with it (--game), and does not ask again, even when the
    /// player wants to choose at every start.
    /// </param>
    /// <param name="downloads">Downloads this copy got but did not install yet; the new copy takes them.</param>
    /// <returns>False when the new copy could not be started; this copy then keeps running.</returns>
    internal static bool Restart(string? game = null, IReadOnlyList<string>? downloads = null)
    {
        if (Environment.ProcessPath is not { } exe)
            return false;
        var start = new ProcessStartInfo(exe) { UseShellExecute = false };
        for (var i = 0; i + 1 < Options.Count; i += 2)
        {
            if (game is not null && Options[i] == "--game")
                continue;
            start.ArgumentList.Add(Options[i]);
            start.ArgumentList.Add(Options[i + 1]);
        }
        if (game is not null)
        {
            start.ArgumentList.Add("--game");
            start.ArgumentList.Add(game);
        }
        start.ArgumentList.Add(AfterUpdateOption);
        foreach (var download in downloads ?? [])
            start.ArgumentList.Add(download);
        try
        {
            Process.Start(start);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Log.Error("The app could not be started again.", ex);
            return false;
        }
        IsExiting = true;
        Current.Shutdown();
        return true;
    }

    /// <summary>
    /// The app ends for real: Exit in the tray menu, a start again, or the end of the Windows session. Until then, the X
    /// can hide the window in the tray, as the player chose.
    /// </summary>
    internal static bool IsExiting { get; private set; }

    /// <summary>Exit in the tray menu: the window closes for real, and with it the app.</summary>
    internal static void Quit()
    {
        IsExiting = true;
        Current.MainWindow?.Close();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        IsExiting = true;
        base.OnSessionEnding(e);
    }

    private SingleInstance? instance;

    /// <summary>Takes what a later start hands over: the downloads go to the open window.</summary>
    private Action<IReadOnlyList<string>>? receive;

    /// <summary>Why the game profiles could not be read; shown once the log is ready.</summary>
    private InvalidDataException? startError;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // "UEModManager.exe --cli <command>": one command of the command line tool, without a window and without the
        // hand-over to an open app; it refuses changes while the app holds the data folder (see UEManager.Cli).
        if (CommandLine.Wants(e.Args))
        {
            ParentConsole.Attach();
            Shutdown(CommandLine.Run(e.Args));
            return;
        }
        Options = OptionArguments(e.Args);

        // --data-dir <folder> keeps a test run away from the real data folder.
        var dataDir = ArgumentAfter(e.Args, "--data-dir");
        var paths = dataDir is null ? AppPaths.NextToApp() : new AppPaths(dataDir);
        var settings = AppSettings.Load(paths);
        L.Current.SetLanguage(settings.Language);
        IReadOnlyList<GameProfile> games;
        try
        {
            games = GameProfile.BuiltIn();
        }
        catch (InvalidDataException ex)
        {
            games = [];
            startError = ex;
        }
        // --game <id> starts with that game: the start after a choice, and test runs, which may name a hidden game too.
        // Else the game the player chose last, unless they want to choose at every start.
        var named = ArgumentAfter(e.Args, "--game");
        var profile = GameChoice.Resolve(GameChoice.Playable(games, named, AppInfo.CanChooseComingGames),
            named, settings.Game, settings.AskForGameAtStart);
        // A download made for one game names it in Data\serve.json: while no game is chosen, the picker shows it in the
        // middle of its highlights, and in the game's look.
        var served = profile is null ? GameChoice.Served(games, ServedGame.Read(paths), settings.Game, AppInfo.CanChooseComingGames) : null;
        GameTheme.Apply(Resources, profile?.Id ?? served?.Id);
        LanguageFonts.SetFont(settings.Font);
        LanguageFonts.SetUiScale(settings.UiScale);
        LanguageFonts.Follow(Resources);
        SmoothScroll.Allowed = settings.SmoothScrolling;
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
        NoteServedGame(paths, settings, profile, served);
        instance.Listen(downloads => Dispatcher.BeginInvoke(() => receive?.Invoke(downloads)));

        if (startError is not null || games.Count == 0)
        {
            var problem = startError ?? new InvalidDataException("This build contains no game profile.");
            Log.Error("No usable game profile.", problem);
            MessageBox.Show(problem.Message, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        if (profile is null)
        {
            ChooseGame(games.Where(game => !game.Hidden).ToList(), paths, settings, DownloadArguments(e.Args), served);
            return;
        }
        Log.Info($"Game: '{profile.Id}'.");

        string? catalogUrl = null;
        ReadCatalogArgument(e.Args, ref catalogUrl);
        AddCatalogResources();

        // --update-url <url> reads another release list for the update check, such as a test copy of it.
        var updateUrl = ArgumentAfter(e.Args, "--update-url");
        if (updateUrl is not null)
            Log.Info($"Release list from the command line: '{updateUrl}'.");

        // --ue4ss-manifest-url <url> reads another UE4SS manifest, such as a test copy of it.
        var ue4ssManifestUrl = ArgumentAfter(e.Args, "--ue4ss-manifest-url");
        if (ue4ssManifestUrl is not null)
            Log.Info($"UE4SS manifest from the command line: '{ue4ssManifestUrl}'.");

        var viewModel = new MainViewModel(profile, paths, ArtSet.Load(profile.Id), catalogUrl, updateUrl, ue4ssManifestUrl);
        MainWindow = new MainWindow { DataContext = viewModel };
        MainWindow.Show();
        receive = downloads => Receive(viewModel, downloads);
        _ = viewModel.InitializeAsync(DownloadArguments(e.Args));
    }

    /// <summary>
    /// Data\serve.json (see <see cref="ServedGame"/>) at a start: logged when it serves a game, and removed when a game is
    /// chosen already, so it never changes the picker of a player who has one.
    /// </summary>
    private static void NoteServedGame(AppPaths paths, AppSettings settings, GameProfile? profile, GameProfile? served)
    {
        if (!ServedGame.Exists(paths))
            return;
        if (served is not null)
            Log.Info($"Serving '{served.Id}' (serve.json): the picker shows it in the middle of the highlights, in its look.");
        else if (!string.IsNullOrWhiteSpace(settings.Game))
        {
            if (ServedGame.Remove(paths))
                Log.Info("serve.json removed: a game is chosen already.");
        }
        else if (profile is null)
            Log.Warn($"serve.json names no game the picker offers ('{ServedGame.Read(paths)}'); the picker shows as usual.");
    }

    /// <summary>
    /// A start without a chosen game, such as the very first: the game picker over a blurred picture. The choice is
    /// saved, and the app starts again with that game. Downloads handed over meanwhile go along to the new start.
    /// </summary>
    /// <param name="served">The game a download made for one game serves (see <see cref="ServedGame"/>); null for none.</param>
    private void ChooseGame(IReadOnlyList<GameProfile> games, AppPaths paths, AppSettings settings, IReadOnlyList<string> downloads,
        GameProfile? served)
    {
        Log.Info(settings.AskForGameAtStart && settings.Game is not null ? "The player chooses the game at every start; asking." : "No game chosen yet; asking.");
        var pending = downloads.ToList();
        GameChoiceWindow? window = null;
        var chosen = false;
        var servedArt = served is null ? null : ArtSet.Load(served.Id);
        // Taken here: the cards look their games up away from the window's thread.
        var saved = new Dictionary<string, string>(settings.GamePaths, StringComparer.OrdinalIgnoreCase);
        // The game chosen last has the accent frame, for a player who chooses at every start.
        var picker = new GamePickerViewModel(games, settings.Game, ArtSet.Card, game =>
        {
            // The app is starting again: a second click does nothing.
            if (chosen)
                return;
            settings.Game = game.Id;
            if (global::UEManager.UI.MainWindow.PlacementOf(window!) is { } placement)
                settings.Window = placement;
            try
            {
                settings.Save(paths);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                settings.Game = null;
                Log.Error("The chosen game could not be saved.", ex);
                MessageBox.Show(L.F($"Settings could not be saved: {ex.Message}"), AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            // Any choice ends the serving: from now on the picker shows the highlights as usual.
            if (ServedGame.Exists(paths) && ServedGame.Remove(paths))
                Log.Info("serve.json removed: the player chose a game.");
            Log.Info($"Chosen: '{game.Id}'; starting again.");
            chosen = Restart(game: game.Id, downloads: pending);
            if (!chosen)
                MessageBox.Show(L.F($"The app could not start again. Close it and start it again: it then opens with {L.T(game.DisplayName)}."),
                    AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
        }, close: null, chooseLanguage: code =>
        {
            settings.Language = code;
            L.Current.SetLanguage(code);
            try
            {
                settings.Save(paths);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The choice of a game saves the settings again.
                Log.Warn($"The language could not be saved: {ex.Message}");
            }
        }, lookUp: game => GameOnPc.Look(game, saved.GetValueOrDefault(game.Id), paths), servedId: served?.Id);
        // The developer's message opens by itself a single time, at the very first start; it is saved at once, so it stays
        // closed from then on, even when the player closes the window without a choice.
        if (!settings.DeveloperMessageShown)
        {
            picker.IsMessageOpen = true;
            settings.DeveloperMessageShown = true;
            try
            {
                settings.Save(paths);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The choice saves the settings again; at worst the message opens once more.
                Log.Warn($"The settings could not be saved: {ex.Message}");
            }
        }
        // A player who chose no language yet is asked first whether the app should show the language of Windows: the cards
        // and the developer's message then show in the language chosen.
        if (settings.Language is null && L.Current.Offer() is { } offered)
            _ = picker.OfferLanguageAsync(offered);
        // A served game: a plain background with its cover art top right, as on its pages, and its logo top left (the
        // user's choice, 2026-11-15).
        window = new GameChoiceWindow(picker, served is null ? ArtSet.Backdrop() : null, settings.Window, servedArt);
        MainWindow = window;
        receive = handed =>
        {
            pending.AddRange(handed);
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;
            window.Activate();
        };
        window.Show();
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
        // Also from the tray, where the window may be hidden.
        if (MainWindow is global::UEManager.UI.MainWindow main)
            main.BringBack();
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
    /// ran it may still be ending, so this tries a few times in the background. At the first start of a new
    /// version, also one an older version installed, Windows is told the exe changed, so it shows the new icon.
    /// </summary>
    private static void CleanUpAfterUpdate()
    {
        if (Environment.ProcessPath is not { } exe)
            return;
        _ = Task.Run(async () =>
        {
            if (SelfUpdate.IsJustUpdated(exe))
                ShellIcons.Refresh(exe);
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

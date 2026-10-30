namespace UEManager.Core.Storage;

/// <summary>User choices that survive a restart.</summary>
public sealed class AppSettings
{
    /// <summary>The chosen language's code, such as "zh-CN"; null follows the Windows display language.</summary>
    public string? Language { get; set; }

    /// <summary>Install folder per game profile id, set when the user locates the game by hand.</summary>
    public Dictionary<string, string> GamePaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The game's build (Steam build id or Epic version) per game profile id, as the player last confirmed it.</summary>
    public Dictionary<string, string> GameBuilds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The save folder per game profile id, when the player chose it.</summary>
    public Dictionary<string, string> SaveFolders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>"Launch game" zips the save folder first. Off until the player turns it on.</summary>
    public bool BackUpSavesOnLaunch { get; set; }

    /// <summary>The newest app version the banner at start announced; each new version is announced once.</summary>
    public string? AnnouncedUpdate { get; set; }

    /// <summary>The Mods page's sort column, such as "Name" or "Installed"; null for the default.</summary>
    public string? ModsSort { get; set; }

    public bool ModsSortDescending { get; set; }

    /// <summary>The openmods page's sort field, "Name" or "Updated"; null for the default, the newest release first.</summary>
    public string? CatalogSort { get; set; }

    public bool CatalogSortDescending { get; set; }

    /// <summary>Where the main window was when it last closed; null opens it centred at its default size.</summary>
    public WindowPlacement? Window { get; set; }

    public static AppSettings Load(AppPaths paths)
    {
        try
        {
            return JsonFile.Read<AppSettings>(paths.SettingsFile) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Log.Warn($"Settings could not be read, defaults are used: {ex.Message}");
            return new AppSettings();
        }
    }

    public void Save(AppPaths paths) => JsonFile.Write(paths.SettingsFile, this);
}

/// <summary>A window's place and size in device-independent pixels; when maximized, the size it returns to.</summary>
public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized);

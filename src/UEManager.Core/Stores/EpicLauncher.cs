using System.Text.Json;

namespace UEManager.Core.Stores;

/// <param name="DisplayName">The name the Epic Games Launcher shows for the app.</param>
public sealed record EpicApp(string DisplayName, string InstallPath, string LaunchUri, string? Version);

/// <summary>Reads the Epic Games Launcher's install manifests. Never writes to them.</summary>
public static class EpicLauncher
{
    public static string DefaultManifestsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");

    /// <summary>The installed app with this name, as the Epic Games Launcher shows it.</summary>
    public static EpicApp? FindApp(string displayName, string? manifestsFolder = null) =>
        Apps(manifestsFolder).FirstOrDefault(app => string.Equals(app.DisplayName, displayName, StringComparison.OrdinalIgnoreCase));

    /// <summary>The installed app in this folder, whatever its name.</summary>
    public static EpicApp? FindAppIn(string installFolder, string? manifestsFolder = null) =>
        Apps(manifestsFolder).FirstOrDefault(app => FileSystem.IsSameFolder(app.InstallPath, installFolder));

    /// <summary>The installed apps whose install folder exists. A manifest that cannot be read is skipped.</summary>
    private static IEnumerable<EpicApp> Apps(string? manifestsFolder)
    {
        manifestsFolder ??= DefaultManifestsFolder;
        if (!Directory.Exists(manifestsFolder))
            yield break;
        foreach (var file in Directory.EnumerateFiles(manifestsFolder, "*.item"))
        {
            EpicApp? app = null;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file));
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object && Text(root, "InstallLocation") is { } install && Directory.Exists(install))
                {
                    var launch = $"com.epicgames.launcher://apps/{Text(root, "CatalogNamespace")}%3A{Text(root, "CatalogItemId")}%3A{Text(root, "AppName")}?action=launch&silent=true";
                    app = new EpicApp(Text(root, "DisplayName") ?? "", install, launch, Text(root, "AppVersionString"));
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                Log.Warn($"Epic manifest '{file}' could not be read: {ex.Message}");
            }
            if (app is not null)
                yield return app;
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

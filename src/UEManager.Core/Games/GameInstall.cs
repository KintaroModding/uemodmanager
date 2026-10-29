using System.Diagnostics;

namespace UEManager.Core.Games;

public enum GameStore
{
    Manual,
    Steam,
    Epic,
}

/// <summary>A located installation of a game, with every profile path resolved.</summary>
/// <param name="BinariesPath">Folder of the shipping executable, where UE4SS is installed.</param>
public sealed record GameInstall(
    GameProfile Profile,
    string RootPath,
    string? ProjectFolder,
    string PaksPath,
    string ModsPath,
    string BinariesPath,
    GameStore Store,
    string? BuildId = null,
    string? LaunchUri = null)
{
    /// <summary>Helper programs Unreal games ship next to the real executable.</summary>
    private static readonly string[] HelperExecutables = ["UnrealCEFSubProcess", "CrashReportClient", "UEPrereqSetup"];

    /// <summary>Folder for Blueprint mods, which UE4SS's BPModLoaderMod loads.</summary>
    public string LogicModsPath => GameProfile.ResolvePath(RootPath, Profile.LogicModsPath, ProjectFolder);

    /// <summary>The game's real executable in <see cref="BinariesPath"/>, such as Game-Win64-Shipping.exe.</summary>
    public string? FindShippingExecutable() => FindShippingExecutable(Profile, BinariesPath);

    /// <summary>The profile's shipping executable in a folder; without a name in the profile, the usual Unreal one, else any.</summary>
    internal static string? FindShippingExecutable(GameProfile profile, string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
                return null;
            if (!string.IsNullOrWhiteSpace(profile.ShippingExecutable))
            {
                var named = Path.Combine(folder, profile.ShippingExecutable);
                return File.Exists(named) ? named : null;
            }
            return Directory.EnumerateFiles(folder, "*-Win64-Shipping.exe").FirstOrDefault()
                ?? Directory.EnumerateFiles(folder, "*.exe").FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The executable "Launch game" starts when no store launcher is known: the profile's
    /// choice, else the first executable in the install folder, else the shipping executable.
    /// </summary>
    public string? FindExecutable()
    {
        if (!string.IsNullOrWhiteSpace(Profile.Executable))
        {
            var configured = GameProfile.ResolvePath(RootPath, Profile.Executable, ProjectFolder);
            if (File.Exists(configured))
                return configured;
        }
        return Directory.EnumerateFiles(RootPath, "*.exe")
            .FirstOrDefault(path => !HelperExecutables.Any(helper =>
                Path.GetFileNameWithoutExtension(path).StartsWith(helper, StringComparison.OrdinalIgnoreCase)))
            ?? FindShippingExecutable();
    }

    /// <summary>Starts the game through its store when known, so DRM and overlays work as usual.</summary>
    public void Launch()
    {
        var target = LaunchUri ?? FindExecutable()
            ?? throw new InvalidOperationException("No game executable was found in the install folder.");
        Process.Start(new ProcessStartInfo(target)
        {
            UseShellExecute = true,
            WorkingDirectory = LaunchUri is null ? Path.GetDirectoryName(target) : null,
        });
    }
}

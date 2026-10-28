using System.Diagnostics;
using System.Text.RegularExpressions;
using UEManager.Core.Games;

namespace UEManager.Core.Mods;

/// <summary>The UE4SS script loader in a game install, and where its mods live.</summary>
public sealed partial record Ue4ssRuntime(bool Installed, string ModsPath, string? Version)
{
    /// <summary>The log header is near the top; the rest of the log can be large.</summary>
    private const int LogLinesToRead = 20;

    /// <summary>The folder holding UE4SS.dll: Binaries\Win64\ue4ss, or Binaries\Win64 for UE4SS 2.x.</summary>
    public string Folder => Path.GetDirectoryName(ModsPath)!;

    /// <summary>The log UE4SS writes at every game start.</summary>
    public string LogPath => Path.Combine(Folder, "UE4SS.log");

    /// <summary>The mod folders in <see cref="ModsPath"/>, also those installed by hand, without UE4SS's shared Lua code.</summary>
    public IReadOnlyList<string> ModFolders() =>
        Directory.Exists(ModsPath)
            ? Directory.EnumerateDirectories(ModsPath)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(name => !name.Equals("shared", StringComparison.OrdinalIgnoreCase))
                .ToList()
            : [];

    /// <summary>
    /// UE4SS 3.x keeps everything in Binaries\Win64\ue4ss, next to a proxy DLL such as dwmapi.dll.
    /// UE4SS 2.x put UE4SS.dll and its Mods folder straight into Binaries\Win64.
    /// Without UE4SS, the 3.x location is used, which is where a later install will look.
    /// </summary>
    public static Ue4ssRuntime Detect(GameInstall game)
    {
        foreach (var folder in new[] { Path.Combine(game.BinariesPath, "ue4ss"), game.BinariesPath })
        {
            if (File.Exists(Path.Combine(folder, "UE4SS.dll")))
                return new Ue4ssRuntime(true, Path.Combine(folder, "Mods"), VersionIn(folder));
        }
        return new Ue4ssRuntime(false, Path.Combine(game.BinariesPath, "ue4ss", "Mods"), null);
    }

    /// <summary>
    /// The DLL's version resource when it has one. Released UE4SS builds do not, so
    /// otherwise the banner UE4SS writes into its log at every game start, such as
    /// "UE4SS - v3.0.1 Beta #0 - Git SHA #d935b5b".
    /// </summary>
    private static string? VersionIn(string folder)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(Path.Combine(folder, "UE4SS.dll"));
            var resource = string.IsNullOrWhiteSpace(info.ProductVersion) ? info.FileVersion : info.ProductVersion;
            if (!string.IsNullOrWhiteSpace(resource) && resource.Any(c => char.IsDigit(c) && c != '0'))
                return resource.Trim();

            var log = Path.Combine(folder, "UE4SS.log");
            if (!File.Exists(log))
                return null;
            // The game may hold the log open while it runs.
            using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            for (var i = 0; i < LogLinesToRead && reader.ReadLine() is { } line; i++)
            {
                var banner = LogBanner().Match(line);
                if (banner.Success)
                    return banner.Groups["version"].Value + banner.Groups["stage"].Value;
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"UE4SS - v(?<version>\d+(?:\.\d+)+)(?<stage> (?:Alpha|Beta|RC|Experimental))?", RegexOptions.IgnoreCase)]
    private static partial Regex LogBanner();
}

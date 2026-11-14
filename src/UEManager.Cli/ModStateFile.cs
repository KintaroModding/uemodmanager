using System.Text.Json;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Cli;

/// <summary>Reads the game's mod list without constructing a ModManager, so read-only commands never write to it.</summary>
internal static class ModStateFile
{
    /// <exception cref="CliError">The file exists but cannot be read.</exception>
    public static ModState Read(CliContext context)
    {
        var path = PathOf(context);
        try
        {
            return JsonFile.Read<ModState>(path) ?? new ModState();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new CliError("state-unreadable", $"The mod list '{path}' could not be read: {error.Message}");
        }
    }

    public static string PathOf(CliContext context) => context.Paths.StateFile(context.GameId);
}

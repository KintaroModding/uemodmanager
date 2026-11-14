using System.Globalization;
using System.Text.Json;
using UEManager.Core.Storage;

namespace UEManager.Cli;

/// <summary>Where command output goes: plain text, or one JSON object with camelCase fields under --json.</summary>
internal static class Output
{
    public static void Json(object payload) => Console.Out.WriteLine(JsonSerializer.Serialize(payload, JsonFile.Options));

    public static void Line(string text = "") => Console.Out.WriteLine(text);

    /// <summary>An English message with its values kept apart, as the Core writes it.</summary>
    public static string Text(FormattableString message) => FormattableString.Invariant(message);

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.0} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.0} GB",
    };

    public static string Time(DateTimeOffset? time) =>
        time?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "never";
}

/// <summary>One JSON object for a failure on stderr.</summary>
internal sealed record ErrorPayload(int SchemaVersion, ErrorInfo Error);

internal sealed record ErrorInfo(string Code, string Message, IReadOnlyList<string>? Details);

/// <summary>The program that produced the output, and the source revision when the build knows it.</summary>
internal sealed record ToolInfo(string Name, string Version, string? Commit);

/// <summary>The UE4SS runtime found in the install, as <see cref="UEManager.Core.Mods.Ue4ssRuntime.Detect"/> reads it.</summary>
internal sealed record Ue4ssInfo(bool Installed, string? Version, string? Folder, string? ModsPath);

/// <summary>
/// The game and its install. Null paths when no install was found: status still reports the data folder then.
/// Shared by every command's JSON, so the field names stay stable.
/// </summary>
internal sealed record GameInfo(string Id, string DisplayName, bool Found, string? RootPath, string? Store, string? ProjectFolder,
    string? PaksPath, string? ModsPath, string? LogicModsPath, string? BinariesPath, Ue4ssInfo? Ue4ss, bool? Running);

/// <summary>The app executable next to the data folder, when it is there.</summary>
internal sealed record AppDto(string Executable, string? Version);

internal sealed record ModsCountDto(bool Readable, int Total, int Enabled, int Disabled, string StateFile, bool StateFileExists,
    string LibraryDir);

/// <summary>What the app wrote about mod page versions, if anything.</summary>
internal sealed record UpdateRecordsInfo(IReadOnlyList<string> Files, int Count, DateTimeOffset? CheckedAt);

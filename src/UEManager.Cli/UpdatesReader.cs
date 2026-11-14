using System.Globalization;
using System.Text.Json;
using UEManager.Core;

namespace UEManager.Cli;

/// <summary>A mod page version the app recorded for the game, before it is joined with the installed mods.</summary>
internal sealed record RawUpdateRecord(string RecordFile, string ModId, string Name, string? Version, DateTimeOffset? UpdatedAt,
    DateTimeOffset? CheckedAt);

/// <summary>
/// Reads what the app recorded about mod page versions in the game's data folder. The prototype accepts the two names
/// the app has used for it, "updates.json" and "nexus-updates.json", and every *.json in an "updates" folder; the record
/// shape is read tolerantly, because the file belongs to the app and may gain fields.
/// </summary>
internal static class UpdatesReader
{
    private static readonly string[] FileNames = ["updates.json", "nexus-updates.json"];

    private static readonly string[] ArrayNames = ["records", "mods", "updates", "items", "versions"];

    public static (IReadOnlyList<string> Files, IReadOnlyList<RawUpdateRecord> Records) Read(CliContext context)
    {
        var files = new List<string>();
        var gameDir = Path.Combine(context.GamesDir, context.GameId);
        if (Directory.Exists(gameDir))
        {
            foreach (var name in FileNames)
            {
                var path = Path.Combine(gameDir, name);
                if (File.Exists(path))
                    files.Add(path);
            }
            var folder = Path.Combine(gameDir, "updates");
            if (Directory.Exists(folder))
                files.AddRange(Directory.EnumerateFiles(folder, "*.json").Order(StringComparer.OrdinalIgnoreCase));
        }
        var records = new List<RawUpdateRecord>();
        foreach (var file in files)
            records.AddRange(ReadFile(file));
        return (files, records);
    }

    /// <summary>When the newest of the records was written.</summary>
    public static DateTimeOffset? LastChecked(IReadOnlyList<RawUpdateRecord> records)
    {
        var times = records.Where(record => record.CheckedAt is not null).Select(record => record.CheckedAt!.Value).ToList();
        return times.Count == 0 ? null : times.Max();
    }

    private static List<RawUpdateRecord> ReadFile(string file)
    {
        var found = new List<RawUpdateRecord>();
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
                MaxDepth = 16,
            });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Warn($"The update record '{file}' could not be read: {error.Message}");
            return found;
        }
        using (document)
        {
            if (!TryRecords(document.RootElement, out var records))
                return found;
            foreach (var element in records.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                    continue;
                var properties = Properties(element);
                var modId = Text(properties, "modId") ?? Text(properties, "mod_id") ?? Text(properties, "id");
                if (string.IsNullOrWhiteSpace(modId))
                    continue;
                found.Add(new RawUpdateRecord(file, modId,
                    Text(properties, "name") ?? Text(properties, "title") ?? $"Mod {modId}",
                    Text(properties, "version"), Time(properties, "updatedAt"), Time(properties, "checkedAt")));
            }
        }
        return found;
    }

    /// <summary>The array of records: the root itself, an object that holds it, or the only array of an object.</summary>
    private static bool TryRecords(JsonElement root, out JsonElement records)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            records = root;
            return true;
        }
        if (root.ValueKind == JsonValueKind.Object)
        {
            var properties = Properties(root);
            foreach (var name in ArrayNames)
            {
                if (properties.TryGetValue(name, out var array) && array.ValueKind == JsonValueKind.Array)
                {
                    records = array;
                    return true;
                }
            }
            foreach (var value in properties.Values)
            {
                if (value.ValueKind == JsonValueKind.Array)
                {
                    records = value;
                    return true;
                }
            }
        }
        records = default;
        return false;
    }

    private static Dictionary<string, JsonElement> Properties(JsonElement element)
    {
        var map = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
            map[property.Name] = property.Value;
        return map;
    }

    private static string? Text(Dictionary<string, JsonElement> properties, string name)
    {
        if (!properties.TryGetValue(name, out var value))
            return null;
        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>A timestamp as an ISO 8601 text, or as the seconds since 1970 that some tools write.</summary>
    private static DateTimeOffset? Time(Dictionary<string, JsonElement> properties, string name)
    {
        if (!properties.TryGetValue(name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            return parsed;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var seconds) && seconds > 1_000_000_000)
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        return null;
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace UEManager.Core.Storage;

/// <summary>
/// Reads and writes the manager's JSON files. Writes go through a temporary
/// file, so a crash never leaves a half-written state file behind.
/// </summary>
public static class JsonFile
{
    /// <summary>A virus scanner or a sync tool may hold a file for a moment, so replacing it is tried this often.</summary>
    private const int Attempts = 3;

    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(400);

    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static T? Read<T>(string path) where T : class
    {
        if (!File.Exists(path))
            return null;
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
    }

    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == Attempts)
                {
                    FileSystem.TryDeleteFile(temp);
                    throw;
                }
                Thread.Sleep(RetryDelay);
            }
        }
    }
}

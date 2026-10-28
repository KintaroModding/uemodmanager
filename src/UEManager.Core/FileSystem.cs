using System.Security.Cryptography;

namespace UEManager.Core;

internal static class FileSystem
{
    public static readonly string[] ArchiveExtensions = [".zip", ".7z", ".rar"];

    /// <summary>Files Unreal Engine mounts: a .pak, or an IoStore .utoc/.ucas pair.</summary>
    public static readonly string[] PackageExtensions = [".pak", ".utoc", ".ucas"];

    public static readonly string[] IoStoreExtensions = [".utoc", ".ucas"];

    public static bool HasExtension(string path, string[] extensions) =>
        extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether two paths name the same folder, whatever the case and a trailing separator.</summary>
    public static bool IsSameFolder(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            StringComparison.OrdinalIgnoreCase);

    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>A name usable as a file or folder name: characters Windows refuses are dropped.</summary>
    public static string SafeName(string name, string fallback)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim().TrimEnd('.');
        return safe.Length > 0 ? safe : fallback;
    }

    public static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Folder '{path}' could not be removed: {ex.Message}");
        }
    }

    public static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"File '{path}' could not be removed: {ex.Message}");
        }
    }
}

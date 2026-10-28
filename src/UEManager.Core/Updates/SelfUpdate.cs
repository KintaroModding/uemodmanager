using System.Diagnostics;
using UEManager.Core.Mods;

namespace UEManager.Core.Updates;

/// <summary>
/// Replaces the app's own exe with a new version. Windows refuses to delete or overwrite a running
/// exe, but lets it be renamed: the running exe becomes "&lt;name&gt;.old", the new exe takes its
/// name, and the next start deletes the old one.
/// </summary>
public static class SelfUpdate
{
    private const string OldSuffix = ".old";
    private const string NewSuffix = ".new";

    /// <summary>The app's exe in an unpacked release, checked to be <paramref name="version"/>.</summary>
    /// <exception cref="ModException">The release holds no exe, or one of another version.</exception>
    public static string FindExecutable(string folder, string version)
    {
        var exe = Directory.EnumerateFiles(folder, "*.exe", SearchOption.AllDirectories).ToList() is [var single]
            ? single
            : throw new ModException("The update does not hold exactly one program file, so it was not installed.");
        // The product version carries the build's commit after a "+".
        var found = FileVersionInfo.GetVersionInfo(exe).ProductVersion?.Split('+')[0].Trim();
        if (!string.Equals(found, version, StringComparison.OrdinalIgnoreCase))
            throw new ModException($"The update holds version {found ?? "unknown"} instead of {version}, so it was not installed.");
        return exe;
    }

    /// <summary>
    /// Puts <paramref name="newExe"/> in the place of <paramref name="currentExe"/>, which may be running.
    /// When a step fails, everything is put back and nothing has changed.
    /// </summary>
    /// <exception cref="ModException">The exe could not be replaced.</exception>
    public static void Replace(string currentExe, string newExe)
    {
        var old = currentExe + OldSuffix;
        if (File.Exists(old))
        {
            // Still held by a copy of the app that has not ended yet: take another name.
            try
            {
                File.Delete(old);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                old = $"{currentExe}.{Guid.NewGuid():N}{OldSuffix}";
            }
        }

        // The new exe goes next to the current one first, so the swap is two renames on one drive.
        var staged = currentExe + NewSuffix;
        try
        {
            File.Copy(newExe, staged, overwrite: true);
            File.Move(currentExe, old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            FileSystem.TryDeleteFile(staged);
            throw new ModException($"The app could not replace its own file ({ex.Message}). Download the update from the release page instead.");
        }
        try
        {
            File.Move(staged, currentExe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Move(old, currentExe);
            }
            catch (Exception undo) when (undo is IOException or UnauthorizedAccessException)
            {
                Log.Error($"'{currentExe}' could not be put back from '{old}'.", undo);
            }
            FileSystem.TryDeleteFile(staged);
            throw new ModException($"The app could not replace its own file ({ex.Message}). Download the update from the release page instead.");
        }
        Log.Info($"Replaced '{currentExe}' with the new version; the old one is '{old}' until the next start.");
    }

    /// <summary>
    /// Deletes what an update left next to the exe: the old version, and a new one that was not used.
    /// Only the names <see cref="Replace"/> gives are touched; a file such as "&lt;exe&gt;.backup.old" stays.
    /// </summary>
    /// <returns>Whether nothing is left; the old version stays while the copy of the app it ran is still ending.</returns>
    public static bool CleanUp(string currentExe)
    {
        var folder = Path.GetDirectoryName(currentExe);
        if (folder is null || !Directory.Exists(folder))
            return true;
        var name = Path.GetFileName(currentExe);
        var clean = true;
        var leftovers = Directory.EnumerateFiles(folder, name + "*" + OldSuffix)
            .Where(file => IsOldVersion(Path.GetFileName(file), name))
            .Append(currentExe + NewSuffix)
            .Where(File.Exists);
        foreach (var leftover in leftovers)
        {
            try
            {
                File.Delete(leftover);
                Log.Info($"Deleted '{leftover}', left by an update.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Info($"'{leftover}' could not be deleted yet: {ex.Message}");
                clean = false;
            }
        }
        return clean;
    }

    /// <summary>"&lt;exe&gt;.old", or "&lt;exe&gt;.&lt;32 hex digits&gt;.old" when that name was still taken.</summary>
    private static bool IsOldVersion(string fileName, string exeName)
    {
        if (fileName.Equals(exeName + OldSuffix, StringComparison.OrdinalIgnoreCase))
            return true;
        var middle = fileName.Length - exeName.Length - 1 - OldSuffix.Length;
        return middle == 32
            && fileName.StartsWith(exeName + ".", StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(OldSuffix, StringComparison.OrdinalIgnoreCase)
            && fileName.Substring(exeName.Length + 1, middle).All(Uri.IsHexDigit);
    }
}

using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace UEManager.Core.Mods;

/// <summary>
/// Puts a download into an empty staging folder: archives are extracted, loose
/// package files and folders are copied. Nothing in a download is ever run.
/// </summary>
public static class ArchiveStager
{
    public static bool IsSupported(string path) =>
        Directory.Exists(path)
        || FileSystem.HasExtension(path, FileSystem.ArchiveExtensions)
        || FileSystem.HasExtension(path, FileSystem.PackageExtensions);

    public static void Stage(string source, string stagingDir)
    {
        // A folder that holds the staging folder, such as the app's own folder, would copy its own copy again and again.
        if (Directory.Exists(source) && IsSameOrInside(stagingDir, source))
            throw new ModException(L.F($"'{Path.GetFileName(Path.TrimEndingDirectorySeparator(source))}' holds the manager's own Data folder, ")
                + L.T("so it cannot be installed. Choose the folder of the mod itself."));
        Directory.CreateDirectory(stagingDir);
        if (Directory.Exists(source))
            CopyTree(source, Path.Combine(stagingDir, Path.GetFileName(Path.TrimEndingDirectorySeparator(source))));
        else if (!File.Exists(source))
            throw new ModException(L.F($"'{source}' does not exist."));
        else if (FileSystem.HasExtension(source, FileSystem.PackageExtensions))
            CopyPackageSiblings(source, stagingDir);
        else if (FileSystem.HasExtension(source, FileSystem.ArchiveExtensions))
            Extract(source, stagingDir);
        else
            throw new ModException(L.F($"'{Path.GetFileName(source)}' is not a supported mod file. Use a .zip, .7z, .rar, .pak, .utoc or .ucas file, or a folder."));
    }

    /// <summary>
    /// Maps an archive entry name to a path inside the staging folder. Absolute
    /// paths, drive letters and ".." are refused, so no entry can land outside.
    /// </summary>
    internal static string SafeTarget(string stagingDir, string entryKey)
    {
        var parts = entryKey.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (Path.IsPathRooted(entryKey) || entryKey.Contains(':') || parts.Contains(".."))
            throw new ModException(L.F($"The archive contains an unsafe path and was not extracted: {entryKey}"));
        var root = Path.GetFullPath(stagingDir);
        var target = Path.GetFullPath(Path.Combine([root, .. parts.Where(p => p != ".")]));
        if (!target.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ModException(L.F($"The archive contains an unsafe path and was not extracted: {entryKey}"));
        return target;
    }

    private static void Extract(string archivePath, string stagingDir)
    {
        try
        {
            using var archive = ArchiveFactory.OpenArchive(archivePath, new ReaderOptions());
            // Solid archives decompress as one stream, so they are read front to back.
            if (archive.IsSolid || archive.Type == ArchiveType.SevenZip)
            {
                using var reader = archive.ExtractAllEntries();
                while (reader.MoveToNextEntry())
                    WriteEntry(stagingDir, reader.Entry, reader.WriteEntryTo);
            }
            else
            {
                foreach (var entry in archive.Entries)
                {
                    WriteEntry(stagingDir, entry, output =>
                    {
                        using var input = entry.OpenEntryStream();
                        input.CopyTo(output);
                    });
                }
            }
        }
        catch (Exception ex) when (ex is not ModException and not OutOfMemoryException)
        {
            throw new ModException(L.F($"'{Path.GetFileName(archivePath)}' could not be extracted: {ex.Message}"));
        }
    }

    private static void WriteEntry(string stagingDir, IEntry entry, Action<Stream> copyTo)
    {
        if (entry.IsDirectory || string.IsNullOrEmpty(entry.Key))
            return;
        if (!string.IsNullOrEmpty(entry.LinkTarget))
            throw new ModException(L.F($"The archive contains a link, which is not allowed: {entry.Key}"));
        var target = SafeTarget(stagingDir, entry.Key);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using var output = File.Create(target);
        copyTo(output);
    }

    /// <summary>A loose package file brings its companions: Example_P.pak picks up Example_P.utoc and Example_P.ucas.</summary>
    private static void CopyPackageSiblings(string file, string stagingDir)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(file))!;
        var stem = Path.GetFileNameWithoutExtension(file);
        foreach (var sibling in Directory.EnumerateFiles(folder, stem + ".*"))
        {
            if (string.Equals(Path.GetFileNameWithoutExtension(sibling), stem, StringComparison.OrdinalIgnoreCase)
                && FileSystem.HasExtension(sibling, FileSystem.PackageExtensions))
                File.Copy(sibling, Path.Combine(stagingDir, Path.GetFileName(sibling)));
        }
    }

    /// <summary>Whether <paramref name="path"/> is <paramref name="folder"/> or lies below it; "C:\Mods2" is not below "C:\Mods".</summary>
    private static bool IsSameOrInside(string path, string folder)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        return full.Equals(parent, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos())
        {
            // Links may point anywhere on the disk, so they are not followed.
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                continue;
            var target = Path.Combine(destination, entry.Name);
            if (entry is DirectoryInfo directory)
                CopyTree(directory.FullName, target);
            else
                ((FileInfo)entry).CopyTo(target);
        }
    }
}

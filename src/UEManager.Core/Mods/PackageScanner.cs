namespace UEManager.Core.Mods;

public enum PackageKind
{
    /// <summary>A single .pak file.</summary>
    Pak,

    /// <summary>An Unreal Engine 5 IoStore container: .utoc and .ucas, usually with a .pak companion.</summary>
    IoStore,

    /// <summary>A UE4SS mod folder: Lua scripts, C++ DLLs, or both.</summary>
    Ue4ss,
}

/// <summary>
/// One installable part of a download: a package as the game mounts it (files that
/// share a folder and a base name), or a whole UE4SS mod folder.
/// </summary>
public sealed class PackageGroup
{
    private PackageGroup(string name, string folder, IReadOnlyList<string> files, IReadOnlyList<string> relativePaths,
        PackageKind kind, DeployTarget target)
    {
        Name = name;
        Folder = folder;
        Files = files;
        RelativePaths = relativePaths;
        Kind = kind;
        Target = target;
        Size = files.Sum(f => new FileInfo(f).Length);

        var problems = new List<string>();
        var warnings = new List<string>();
        if (kind == PackageKind.IoStore)
        {
            bool Has(string extension) => files.Any(f => Path.GetExtension(f).Equals(extension, StringComparison.OrdinalIgnoreCase));
            if (Has(".utoc") && !Has(".ucas"))
                problems.Add(L.F($"{name}.utoc has no matching .ucas file."));
            if (Has(".ucas") && !Has(".utoc"))
                problems.Add(L.F($"{name}.ucas has no matching .utoc file."));
            if (!Has(".pak"))
                warnings.Add(L.F($"{name} has no .pak file. Unreal Engine 5 games usually need it next to the .utoc and .ucas files."));
        }
        Problems = problems;
        Warnings = warnings;
    }

    /// <summary>Base name shared by the files, such as "Example_P", or the UE4SS mod's folder name.</summary>
    public string Name { get; }

    /// <summary>Folder inside the download, or empty at the top level.</summary>
    public string Folder { get; }

    /// <summary>Full paths of the staged files.</summary>
    public IReadOnlyList<string> Files { get; }

    /// <summary>
    /// Where each file goes below the target folder, in the order of <see cref="Files"/>:
    /// the file name for a package, "ModName/Scripts/main.lua" for a UE4SS mod.
    /// </summary>
    public IReadOnlyList<string> RelativePaths { get; }

    public PackageKind Kind { get; }

    public DeployTarget Target { get; }

    public long Size { get; }

    /// <summary>Problems that stop this package from being installed.</summary>
    public IReadOnlyList<string> Problems { get; }

    public IReadOnlyList<string> Warnings { get; }

    public string Label => Folder.Length == 0 ? Name : $"{Folder}/{Name}";

    /// <summary>
    /// For one of several folders that hold the same files, such as "x1.5", "x2" and "x3": what
    /// tells this one apart. The player installs one variant of a mod. Null for other packages.
    /// </summary>
    public string? Variant { get; internal set; }

    /// <summary>Shared by the packages that are variants of each other; null for other packages.</summary>
    public string? VariantOf { get; internal set; }

    public string TypeLabel => ModTypes.Label(RelativePaths.Select(p => (Target, p)));

    internal static PackageGroup Packaged(string name, string folder, IReadOnlyList<string> files, DeployTarget target)
    {
        // The package files first, then a settings file that goes with them.
        var ordered = files
            .OrderBy(f => Array.IndexOf(FileSystem.PackageExtensions, Path.GetExtension(f).ToLowerInvariant()) is var index and >= 0
                ? index
                : int.MaxValue)
            .ToList();
        var kind = ordered.Any(f => FileSystem.HasExtension(f, FileSystem.IoStoreExtensions)) ? PackageKind.IoStore : PackageKind.Pak;
        return new PackageGroup(name, folder, ordered, ordered.Select(f => Path.GetFileName(f)).ToList(), kind, target);
    }

    internal static PackageGroup Ue4ssMod(string name, string folder, string root, IReadOnlyList<string> files)
    {
        var ordered = files.Order(StringComparer.OrdinalIgnoreCase).ToList();
        var relative = ordered.Select(f => name + "/" + Path.GetRelativePath(root, f).Replace('\\', '/')).ToList();
        return new PackageGroup(name, folder, ordered, relative, PackageKind.Ue4ss, DeployTarget.Ue4ssMods);
    }
}

public sealed record ScanResult(
    IReadOnlyList<PackageGroup> Packages,
    int IgnoredFileCount,
    int NestedArchiveCount,
    bool ContainsUe4ssRuntime);

/// <summary>
/// Finds the installable parts of a staged download. Readmes, images and the like are ignored, and so
/// are settings files, unless they sit next to a package in a folder named like a mods folder.
/// </summary>
public static class PackageScanner
{
    /// <param name="stagingDir">The unpacked download.</param>
    /// <param name="fallbackName">Folder name for a UE4SS mod whose files sit at the top of the download.</param>
    public static ScanResult Scan(string stagingDir, string fallbackName = "Mod")
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stagingDir));
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToList();
        var drafts = new List<Draft>();

        var ue4ssRoots = Ue4ssModRoots(files);
        foreach (var modRoot in ue4ssRoots)
        {
            var atTop = string.Equals(modRoot, root, StringComparison.OrdinalIgnoreCase);
            var name = atTop ? FileSystem.SafeName(fallbackName, "Mod") : Path.GetFileName(modRoot);
            var folder = atTop ? "" : RelativeFolder(root, Path.GetDirectoryName(modRoot)!);
            drafts.Add(new Draft(name, folder, files.Where(f => IsInside(f, modRoot)).ToList(), modRoot));
        }

        var packageFiles = files
            .Where(f => FileSystem.HasExtension(f, FileSystem.PackageExtensions) && !ue4ssRoots.Any(r => IsInside(f, r)))
            .ToList();
        foreach (var group in packageFiles.GroupBy(
                     f => Path.Combine(Path.GetDirectoryName(f)!, Path.GetFileNameWithoutExtension(f)), StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            drafts.Add(new Draft(Path.GetFileNameWithoutExtension(first), RelativeFolder(root, Path.GetDirectoryName(first)!), group.ToList(), null));
        }

        // A settings file in one of the game's mods folders, such as SpeakerHighlight.json next to its pak in
        // ~mods, goes with a package there: the mod reads it at that place. Readmes outside such a folder stay out.
        var packaged = drafts.Where(d => d.Ue4ssRoot is null).ToList();
        foreach (var file in files.Where(f => FileSystem.HasExtension(f, SettingsFiles.PakSettingsExtensions)
                     && IsModsFolder(Path.GetDirectoryName(f)!) && !ue4ssRoots.Any(r => IsInside(f, r))))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var owner = packaged
                .Where(d => string.Equals(Path.GetDirectoryName(d.Files[0]), Path.GetDirectoryName(file), StringComparison.OrdinalIgnoreCase))
                .OrderBy(d => d.Name.Contains(name, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            owner?.Files.Add(file);
        }

        // A folder every part sits in, such as the download's own top folder, says nothing.
        var common = CommonLeadingFolders(drafts.Select(d => d.Folder).ToList());
        var packages = drafts
            .Select(d =>
            {
                var folder = common.Length == 0 ? d.Folder
                    : d.Folder.Length > common.Length ? d.Folder[(common.Length + 1)..]
                    : "";
                return d.Ue4ssRoot is null
                    ? PackageGroup.Packaged(d.Name, folder, d.Files, IsLogicMod(d.Folder) ? DeployTarget.LogicMods : DeployTarget.Mods)
                    : PackageGroup.Ue4ssMod(d.Name, folder, d.Ue4ssRoot, d.Files);
            })
            .OrderBy(p => p.Label, NaturalOrder.Instance)
            .ToList();

        // Folders that hold the same files are variants of one mod, of which the player installs one.
        var variantGroups = packages
            .GroupBy(p => $"{p.Target}|{string.Join('|', p.RelativePaths.Order(StringComparer.OrdinalIgnoreCase))}", StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);
        foreach (var group in variantGroups)
        {
            foreach (var package in group)
            {
                package.VariantOf = group.Key;
                package.Variant = package.Folder.Length > 0 ? package.Folder : package.Name;
            }
        }

        var used = packages.Sum(p => p.Files.Count);
        var nestedArchives = files.Count(f => FileSystem.HasExtension(f, FileSystem.ArchiveExtensions));
        var runtime = files.Any(f => Path.GetFileName(f).Equals("UE4SS.dll", StringComparison.OrdinalIgnoreCase));
        return new ScanResult(packages, files.Count - used, nestedArchives, runtime);
    }

    /// <summary>
    /// UE4SS mod folders: the folder above Scripts\main.lua (a Lua mod) or above
    /// dlls\*.dll (a C++ mod). A candidate inside another candidate is part of it.
    /// </summary>
    private static List<string> Ue4ssModRoots(IEnumerable<string> files)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var parent = Path.GetDirectoryName(file)!;
            var parentName = Path.GetFileName(parent);
            var isLua = Path.GetFileName(file).Equals("main.lua", StringComparison.OrdinalIgnoreCase)
                && parentName.Equals("Scripts", StringComparison.OrdinalIgnoreCase);
            var isNative = Path.GetExtension(file).Equals(".dll", StringComparison.OrdinalIgnoreCase)
                && parentName.Equals("dlls", StringComparison.OrdinalIgnoreCase);
            if (!isLua && !isNative)
                continue;
            var modRoot = Path.GetDirectoryName(parent);
            if (modRoot is null)
                continue;
            // "shared" holds libraries UE4SS gives every mod; a mod named "Mods" would be a broken layout.
            var name = Path.GetFileName(modRoot);
            if (name.Equals("shared", StringComparison.OrdinalIgnoreCase) || name.Equals("Mods", StringComparison.OrdinalIgnoreCase))
                continue;
            candidates.Add(modRoot);
        }
        return candidates.Where(c => !candidates.Any(other => other != c && IsInside(c, other))).ToList();
    }

    private static bool IsInside(string path, string folder) =>
        path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>A folder named like one of the game's mods folders, where a mod's files are meant to land as they are.</summary>
    private static bool IsModsFolder(string folder) =>
        Path.GetFileName(folder) is var name
        && (name.Equals("~mods", StringComparison.OrdinalIgnoreCase) || name.Equals("LogicMods", StringComparison.OrdinalIgnoreCase));

    private static bool IsLogicMod(string folder) =>
        folder.Split('/').Any(part => part.Equals("LogicMods", StringComparison.OrdinalIgnoreCase));

    private static string RelativeFolder(string root, string folder)
    {
        var relative = Path.GetRelativePath(root, folder);
        return relative == "." ? "" : relative.Replace('\\', '/');
    }

    /// <summary>The leading folders shared by every path, such as "Mod/Data" for "Mod/Data/A" and "Mod/Data/B".</summary>
    private static string CommonLeadingFolders(IReadOnlyList<string> folders)
    {
        if (folders.Count == 0 || folders.Any(f => f.Length == 0))
            return "";
        var common = folders[0].Split('/');
        var length = common.Length;
        foreach (var folder in folders.Skip(1))
        {
            var parts = folder.Split('/');
            length = Math.Min(length, parts.Length);
            for (var i = 0; i < length; i++)
            {
                if (!string.Equals(parts[i], common[i], StringComparison.OrdinalIgnoreCase))
                {
                    length = i;
                    break;
                }
            }
        }
        return string.Join('/', common.Take(length));
    }

    private sealed record Draft(string Name, string Folder, List<string> Files, string? Ue4ssRoot);
}

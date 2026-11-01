namespace UEManager.Core.Mods;

public enum PackageKind
{
    /// <summary>A single .pak file.</summary>
    Pak,

    /// <summary>An Unreal Engine 5 IoStore container: .utoc and .ucas, usually with a .pak companion.</summary>
    IoStore,

    /// <summary>A UE4SS mod folder: Lua scripts, C++ DLLs, or both.</summary>
    Ue4ss,

    /// <summary>
    /// A folder for UE4SS's Mods folder without code, as the download places it (ue4ss/Mods/Name/...): a mod keeps
    /// files there that something else reads, such as the folder where a Blueprint mod writes the settings that
    /// ModConfigUI shows. UE4SS does not start it, so it gets no enabled.txt.
    /// </summary>
    Ue4ssFolder,

    /// <summary>
    /// A single file that goes below the install folder as the download places it, such as
    /// "Project/Saved/EnhancedInputUserSettings.sav": the game's own file, which the mod replaces.
    /// </summary>
    File,

    /// <summary>
    /// Files that share a folder in the download and go next to the game's exe as they are: a proxy DLL such as
    /// dxgi.dll, a ReShade add-on, an .asi loader, and the files that load with them.
    /// </summary>
    ExeFiles,
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

        var problems = new List<FormattableString>();
        var warnings = new List<FormattableString>();
        if (kind == PackageKind.IoStore)
        {
            bool Has(string extension) => files.Any(f => Path.GetExtension(f).Equals(extension, StringComparison.OrdinalIgnoreCase));
            if (Has(".utoc") && !Has(".ucas"))
                problems.Add($"{name}.utoc has no matching .ucas file.");
            if (Has(".ucas") && !Has(".utoc"))
                problems.Add($"{name}.ucas has no matching .utoc file.");
            if (!Has(".pak"))
                warnings.Add($"{name} has no .pak file. Unreal Engine 5 games usually need it next to the .utoc and .ucas files.");
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
    /// the file name for a package, "ModName/Scripts/main.lua" for a UE4SS mod, and "shared/json/json.lua"
    /// for a library the UE4SS mod brings for UE4SS's shared folder.
    /// </summary>
    public IReadOnlyList<string> RelativePaths { get; }

    public PackageKind Kind { get; }

    public DeployTarget Target { get; }

    public long Size { get; }

    /// <summary>Problems that stop this package from being installed.</summary>
    public IReadOnlyList<FormattableString> Problems { get; }

    public IReadOnlyList<FormattableString> Warnings { get; }

    public string Label => Folder.Length == 0 ? Name : $"{Folder}/{Name}";

    /// <summary>
    /// For one of several folders that hold the same files, such as "x1.5", "x2" and "x3": what
    /// tells this one apart. The player installs one variant of a mod. Null for other packages.
    /// </summary>
    public string? Variant { get; internal set; }

    /// <summary>Shared by the packages that are variants of each other; null for other packages.</summary>
    public string? VariantOf { get; internal set; }

    /// <summary>
    /// Files for the exe folder that do not look like a loader: a DLL with no known proxy name, no ReShade add-on and
    /// no .asi. Windows may load a DLL next to the game's exe when the game starts, so the review leaves the part
    /// unticked and the player decides.
    /// </summary>
    public bool IsUncertain { get; internal init; }

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

    /// <param name="sharedRoot">The shared folder next to the mod's folder, whose files go into UE4SS's shared folder.</param>
    internal static PackageGroup Ue4ssMod(string name, string folder, string root, IReadOnlyList<string> files, string? sharedRoot = null)
    {
        var placed = files
            .Select(f => (File: f, Place: sharedRoot is not null && f.StartsWith(sharedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? SharedFiles.Folder + "/" + Path.GetRelativePath(sharedRoot, f).Replace('\\', '/')
                : name + "/" + Path.GetRelativePath(root, f).Replace('\\', '/')))
            .OrderBy(f => f.Place, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new PackageGroup(name, folder, placed.Select(f => f.File).ToList(), placed.Select(f => f.Place).ToList(),
            PackageKind.Ue4ss, DeployTarget.Ue4ssMods);
    }

    internal static PackageGroup Ue4ssFolder(string name, string folder, string root, IReadOnlyList<string> files)
    {
        var ordered = files.Order(StringComparer.OrdinalIgnoreCase).ToList();
        var relative = ordered.Select(f => name + "/" + Path.GetRelativePath(root, f).Replace('\\', '/')).ToList();
        return new PackageGroup(name, folder, ordered, relative, PackageKind.Ue4ssFolder, DeployTarget.Ue4ssMods);
    }

    /// <summary>
    /// The files a download places in one folder of the game's own, as they are: each takes the place of the game's
    /// own file, which the manager saves and puts back when the mod leaves.
    /// </summary>
    /// <param name="files">Full paths of the staged files.</param>
    /// <param name="relativePaths">Each file's path below the install folder, in the order of <paramref name="files"/>.</param>
    internal static PackageGroup GameFiles(string name, string folder, IReadOnlyList<string> files, IReadOnlyList<string> relativePaths)
    {
        var placed = files
            .Select((file, i) => (File: file, Place: relativePaths[i].Replace('\\', '/')))
            .OrderBy(f => f.Place, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new PackageGroup(name, folder, placed.Select(f => f.File).ToList(), placed.Select(f => f.Place).ToList(),
            PackageKind.File, DeployTarget.InstallRoot);
    }

    /// <summary>
    /// A set of files that share a folder in the download and go next to the game's exe as they are, such as
    /// dxgi.dll with its ReShade add-on and ReShade.ini. The game's own file of the same name is saved first.
    /// </summary>
    /// <param name="folderInDownload">The folder the files share, relative to the top of the download.</param>
    /// <param name="files">Full paths of the staged files.</param>
    /// <param name="relativePaths">Each file's path below the folder, in the order of <paramref name="files"/>.</param>
    internal static PackageGroup ExeFiles(string name, string folderInDownload, IReadOnlyList<string> files,
        IReadOnlyList<string> relativePaths)
    {
        var placed = files
            .Select((file, i) => (File: file, Place: relativePaths[i].Replace('\\', '/')))
            .OrderBy(f => f.Place, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new PackageGroup(name, folderInDownload, placed.Select(f => f.File).ToList(),
            placed.Select(f => f.Place).ToList(), PackageKind.ExeFiles, DeployTarget.ExeFolder)
        {
            IsUncertain = !placed.Any(f => IsKnownLoader(f.Place)),
        };
    }

    /// <summary>
    /// Proxy DLLs that loader mods such as ReShade, OptiScaler and .asi loaders put next to a game's exe, so Windows
    /// loads them in place of its own.
    /// </summary>
    private static readonly string[] ProxyDllNames =
    [
        "dxgi", "d3d9", "d3d11", "d3d12", "dinput8", "version", "winmm", "winhttp", "dsound", "xinput1_3", "xinput1_4",
        "opengl32", "nvngx",
    ];

    /// <summary>A file in the exe folder that is clearly a loader: a known proxy DLL, a ReShade add-on, an .asi.</summary>
    private static bool IsKnownLoader(string place)
    {
        var name = Path.GetFileName(place);
        return FileSystem.HasExtension(name, [".addon64", ".addon32", ".asi"])
            || (FileSystem.HasExtension(name, [".dll"])
                && ProxyDllNames.Contains(Path.GetFileNameWithoutExtension(name), StringComparer.OrdinalIgnoreCase));
    }
}

/// <param name="IgnoredFiles">The download's other files, such as readmes and screenshots: their paths from its top, in order.</param>
public sealed record ScanResult(
    IReadOnlyList<PackageGroup> Packages,
    IReadOnlyList<string> IgnoredFiles,
    int NestedArchiveCount,
    bool ContainsUe4ssRuntime)
{
    public int IgnoredFileCount => IgnoredFiles.Count;
}

/// <summary>
/// Finds the installable parts of a staged download. Readmes, images and the like are ignored, and so
/// are settings files, unless they sit next to a package in the same folder.
/// </summary>
public static class PackageScanner
{
    /// <summary>The files that make a download a file mod next to the exe: a proxy DLL, a ReShade add-on, an .asi loader.</summary>
    private static readonly string[] LoaderExtensions = [".dll", ".addon64", ".asi"];

    /// <param name="stagingDir">The unpacked download.</param>
    /// <param name="fallbackName">Folder name for a UE4SS mod whose files sit at the top of the download.</param>
    /// <param name="projectFolder">
    /// The game's Unreal project folder, such as "ProjectPBZ". A file the download places in one of the game's own
    /// folders under it, such as Saved or Config, is installed too; without the name, those files are ignored.
    /// </param>
    /// <param name="exeFolder">
    /// The game's exe folder below the install folder, such as "platform/steam". A download whose paths name it keeps
    /// those paths; a download that keeps proxy DLLs loose next to the exe is installed from its own folder layout.
    /// </param>
    public static ScanResult Scan(string stagingDir, string fallbackName = "Mod", string? projectFolder = null,
        string? exeFolder = null)
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
            // Libraries for UE4SS's shared folder, in a "shared" folder next to the mod's folder as in UE4SS's Mods
            // folder: the mod loads them, so they go with it. Several mods in that folder each take them.
            var shared = atTop ? null : Path.Combine(Path.GetDirectoryName(modRoot)!, SharedFiles.Folder);
            var modFiles = files.Where(f => IsInside(f, modRoot) || (shared is not null && IsInside(f, shared))).ToList();
            drafts.Add(new Draft(name, folder, modFiles, modRoot, shared));
        }

        // A folder in UE4SS's Mods folder without code goes as the download places it, such as the folder where a
        // Blueprint mod writes the settings that ModConfigUI shows: the mod needs that folder to be there.
        var codeFree = files
            .Where(f => !ue4ssRoots.Any(r => IsInside(f, r)))
            .Select(f => (File: f, Folder: Ue4ssModsEntry(root, f)))
            .Where(f => f.Folder is not null)
            .GroupBy(f => f.Folder!, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var group in codeFree)
            drafts.Add(new Draft(Path.GetFileName(group.Key), RelativeFolder(root, Path.GetDirectoryName(group.Key)!),
                group.Select(f => f.File).ToList(), group.Key, CodeFree: true));

        var packageFiles = files
            .Where(f => FileSystem.HasExtension(f, FileSystem.PackageExtensions)
                && !ue4ssRoots.Any(r => IsInside(f, r)) && !codeFree.Any(g => IsInside(f, g.Key)))
            .ToList();
        foreach (var group in packageFiles.GroupBy(
                     f => Path.Combine(Path.GetDirectoryName(f)!, Path.GetFileNameWithoutExtension(f)), StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            drafts.Add(new Draft(Path.GetFileNameWithoutExtension(first), RelativeFolder(root, Path.GetDirectoryName(first)!), group.ToList(), null));
        }

        // A settings file next to a package, such as SpeakerHighlight.json or a .txt an asset mod reads, goes with
        // it into ~mods: the mod reads it next to its pak. Its name and the folder's do not matter, as many downloads
        // keep the files loose at the top. Readmes, licenses and change logs stay out.
        var packaged = drafts.Where(d => d.Ue4ssRoot is null).ToList();
        foreach (var file in files.Where(f => SettingsFiles.IsPakSettings(f) && !ue4ssRoots.Any(r => IsInside(f, r))))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var owner = packaged
                .Where(d => string.Equals(Path.GetDirectoryName(d.Files[0]), Path.GetDirectoryName(file), StringComparison.OrdinalIgnoreCase))
                .OrderBy(d => d.Name.Contains(name, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            owner?.Files.Add(file);
        }

        // A file the download places in one of the game's own folders, such as
        // "ProjectPBZ/Saved/EnhancedInputUserSettings.sav" or "platform/steam/dxgi.dll": a keybinding preset, a settings
        // file the game reads, or a mod file next to the exe. Each file is its own part and keeps its path below the
        // install folder.
        var claimed = drafts.SelectMany(d => d.Files).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var gameFiles = files
            .Where(f => !claimed.Contains(f) && !FileSystem.HasExtension(f, FileSystem.ArchiveExtensions) && !SettingsFiles.IsDocument(f))
            .Select(f => (File: f, Path: GameFilePath(RelativePath(root, f), projectFolder, exeFolder)))
            .Where(f => f.Path is not null)
            .Select(f => (f.File, Path: f.Path!))
            .ToList();

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
                    : d.CodeFree
                        ? PackageGroup.Ue4ssFolder(d.Name, folder, d.Ue4ssRoot, d.Files)
                        : PackageGroup.Ue4ssMod(d.Name, folder, d.Ue4ssRoot, d.Files, d.SharedRoot);
            })
            .ToList();
        // The game files of one folder are one part: a lone file is known by its name, several by the folder they
        // go to, such as the files a download places in the exe folder.
        foreach (var group in gameFiles.GroupBy(g => Path.GetDirectoryName(g.Path)?.Replace('\\', '/') ?? "", StringComparer.OrdinalIgnoreCase))
        {
            var folderPath = group.Key;
            var inFolder = group.OrderBy(g => g.Path, StringComparer.OrdinalIgnoreCase).ToList();
            var (folder, name) = inFolder.Count == 1
                ? (folderPath, Path.GetFileName(inFolder[0].Path))
                : (Path.GetDirectoryName(folderPath)?.Replace('\\', '/') ?? "", Path.GetFileName(folderPath));
            packages.Add(PackageGroup.GameFiles(name, folder, inFolder.Select(f => f.File).ToList(), inFolder.Select(f => f.Path).ToList()));
        }

        // A proxy DLL and the files that load with it, kept loose next to the game's exe: the folder that holds a
        // loader goes next to the exe as it is, which is where ReShade and the add-ons find their own files.
        var taken = claimed.Concat(gameFiles.Select(g => g.File)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var loaderFolders = files
            .Where(f => !taken.Contains(f) && !FileSystem.HasExtension(f, FileSystem.ArchiveExtensions)
                && !SettingsFiles.IsDocument(f) && FileSystem.HasExtension(f, LoaderExtensions))
            .Select(f => Path.GetDirectoryName(f)!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        // A loader folder inside another one is part of it. A folder that only holds a file the download already
        // places by path is an alternate copy of it, such as an older fallback build: it stays out.
        var named = gameFiles.Select(g => Path.GetFileName(g.File)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var loose = loaderFolders
            .Where(folder => !loaderFolders.Any(other => other != folder && IsInside(folder, other)))
            .Where(folder => !files.Any(f => IsInside(f, folder) && named.Contains(Path.GetFileName(f))))
            .ToList();
        foreach (var folder in loose)
        {
            var group = files
                .Where(f => IsInside(f, folder) && !taken.Contains(f)
                    && !FileSystem.HasExtension(f, FileSystem.ArchiveExtensions) && !SettingsFiles.IsDocument(f))
                .ToList();
            if (group.Count == 0)
                continue;
            var atTop = string.Equals(folder, root, StringComparison.OrdinalIgnoreCase);
            var name = atTop ? FileSystem.SafeName(fallbackName, "Mod") : Path.GetFileName(folder);
            packages.Add(PackageGroup.ExeFiles(name, atTop ? "" : RelativeFolder(root, Path.GetDirectoryName(folder)!), group,
                group.Select(f => Path.GetRelativePath(folder, f).Replace('\\', '/')).ToList()));
        }
        packages = packages.OrderBy(p => p.Label, NaturalOrder.Instance).ToList();

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

        // Shared libraries can go with several UE4SS mods; they count once.
        var used = packages.SelectMany(p => p.Files).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ignored = files.Where(f => !used.Contains(f)).Select(f => RelativePath(root, f)).Order(NaturalOrder.Instance).ToList();
        var nestedArchives = files.Count(f => FileSystem.HasExtension(f, FileSystem.ArchiveExtensions));
        var runtime = files.Any(f => Path.GetFileName(f).Equals("UE4SS.dll", StringComparison.OrdinalIgnoreCase));
        return new ScanResult(packages, ignored, nestedArchives, runtime);
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

    /// <summary>
    /// The folder in a UE4SS Mods folder, "ue4ss/Mods/Name", that holds a file of the download; null for other files,
    /// for files loose in the Mods folder (UE4SS's own, such as mods.txt), and for UE4SS's shared folder.
    /// </summary>
    private static string? Ue4ssModsEntry(string root, string file)
    {
        var parts = Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar);
        for (var i = 0; i + 3 < parts.Length; i++)
        {
            if (parts[i].Equals("ue4ss", StringComparison.OrdinalIgnoreCase) && parts[i + 1].Equals("Mods", StringComparison.OrdinalIgnoreCase))
                return parts[i + 2].Equals(SharedFiles.Folder, StringComparison.OrdinalIgnoreCase)
                    ? null
                    : Path.Combine(root, string.Join(Path.DirectorySeparatorChar, parts[..(i + 3)]));
        }
        return null;
    }

    private static bool IsInside(string path, string folder) =>
        path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool IsLogicMod(string folder) =>
        folder.Split('/').Any(part => part.Equals("LogicMods", StringComparison.OrdinalIgnoreCase));

    private static string RelativeFolder(string root, string folder)
    {
        var relative = Path.GetRelativePath(root, folder);
        return relative == "." ? "" : relative.Replace('\\', '/');
    }

    /// <summary>A file's path from the top of the download, with forward slashes.</summary>
    private static string RelativePath(string root, string file) => Path.GetRelativePath(root, file).Replace('\\', '/');

    /// <summary>The folders below Saved that hold the player's saves, the game's logs and crash reports.</summary>
    private static readonly string[] NotGameFiles = ["SaveGames", "Logs", "Crashes"];

    /// <summary>
    /// The path below the install folder of a file the download places in one of the game's own folders, such as
    /// "ProjectPBZ/Saved/Keys.sav" or "platform/steam/dxgi.dll"; null for any other file. The game reads the file
    /// there, and the mod replaces the one the game keeps, so only those folders count: nothing else of a download is
    /// installed over the game's own. A download may wrap its files in a folder of its own, so both are looked for at
    /// any depth.
    /// </summary>
    private static string? GameFilePath(string relativePath, string? projectFolder, string? exeFolder)
    {
        var parts = relativePath.Split('/');
        for (var i = 0; i + 2 < parts.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(projectFolder) && parts[i].Equals(projectFolder, StringComparison.OrdinalIgnoreCase)
                && (parts[i + 1].Equals("Saved", StringComparison.OrdinalIgnoreCase) || parts[i + 1].Equals("Config", StringComparison.OrdinalIgnoreCase)))
            {
                // Saves, logs and crash reports are the player's and the game's: a mod never replaces them, as its
                // copy would put back an old save when the mod leaves.
                if (parts[i + 1].Equals("Saved", StringComparison.OrdinalIgnoreCase) && i + 3 < parts.Length
                    && NotGameFiles.Contains(parts[i + 2], StringComparer.OrdinalIgnoreCase))
                    return null;
                return string.Join('/', parts[i..]);
            }
            if (!string.IsNullOrWhiteSpace(exeFolder) && StartsAt(parts, i, exeFolder!))
                return string.Join('/', parts[i..]);
        }
        return null;
    }

    /// <summary>Whether <paramref name="parts"/>, from <paramref name="start"/>, begins with the folders of <paramref name="folder"/>.</summary>
    private static bool StartsAt(string[] parts, int start, string folder)
    {
        var expected = folder.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (start + expected.Length >= parts.Length)
            return false;
        for (var i = 0; i < expected.Length; i++)
        {
            if (!parts[start + i].Equals(expected[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
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

    /// <param name="CodeFree">A folder for UE4SS's Mods folder without code; see <see cref="PackageKind.Ue4ssFolder"/>.</param>
    private sealed record Draft(string Name, string Folder, List<string> Files, string? Ue4ssRoot, string? SharedRoot = null,
        bool CodeFree = false);
}

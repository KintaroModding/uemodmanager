namespace UEManager.Core.Mods;

/// <summary>Short type names for the interface: PAK, IoStore, Blueprint, UE4SS Lua, UE4SS DLL, UE4SS files.</summary>
public static class ModTypes
{
    /// <summary>A folder for UE4SS's Mods folder without code (<see cref="PackageKind.Ue4ssFolder"/>), on its own.</summary>
    public const string Ue4ssFiles = "UE4SS files";

    /// <param name="files">Each file's deploy target and path below that target.</param>
    public static string Label(IEnumerable<(DeployTarget Target, string Path)> files)
    {
        var list = files.ToList();
        var ue4ss = list.Where(f => f.Target == DeployTarget.Ue4ssMods).Select(f => "/" + f.Path.Replace('\\', '/')).ToList();
        var lua = ue4ss.Any(p => p.Contains("/Scripts/", StringComparison.OrdinalIgnoreCase));
        var native = ue4ss.Any(p => p.Contains("/dlls/", StringComparison.OrdinalIgnoreCase));
        if (lua || native)
            return (lua, native) switch
            {
                (true, true) => "UE4SS Lua + DLL",
                (false, true) => "UE4SS DLL",
                _ => "UE4SS Lua",
            };
        // A UE4SS folder without code, such as the one a Blueprint mod writes its settings to, says nothing of the type.
        if (list.Any(f => f.Target == DeployTarget.LogicMods))
            return "Blueprint";
        if (list.All(f => f.Target == DeployTarget.Ue4ssMods) && list.Count > 0)
            return Ue4ssFiles;
        return list.Any(f => FileSystem.HasExtension(f.Path, FileSystem.IoStoreExtensions)) ? "IoStore" : "PAK";
    }
}

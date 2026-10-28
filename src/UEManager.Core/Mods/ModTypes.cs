namespace UEManager.Core.Mods;

/// <summary>Short type names for the interface: PAK, IoStore, Blueprint, UE4SS Lua, UE4SS DLL.</summary>
public static class ModTypes
{
    /// <param name="files">Each file's deploy target and path below that target.</param>
    public static string Label(IEnumerable<(DeployTarget Target, string Path)> files)
    {
        var list = files.ToList();
        var ue4ss = list.Where(f => f.Target == DeployTarget.Ue4ssMods).Select(f => "/" + f.Path.Replace('\\', '/')).ToList();
        if (ue4ss.Count > 0)
        {
            var lua = ue4ss.Any(p => p.Contains("/Scripts/", StringComparison.OrdinalIgnoreCase));
            var native = ue4ss.Any(p => p.Contains("/dlls/", StringComparison.OrdinalIgnoreCase));
            return (lua, native) switch
            {
                (true, true) => "UE4SS Lua + DLL",
                (false, true) => "UE4SS DLL",
                _ => "UE4SS Lua",
            };
        }
        if (list.Any(f => f.Target == DeployTarget.LogicMods))
            return "Blueprint";
        return list.Any(f => FileSystem.HasExtension(f.Path, FileSystem.IoStoreExtensions)) ? "IoStore" : "PAK";
    }
}

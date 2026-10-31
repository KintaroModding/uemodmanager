using System.Security.Cryptography;
using System.Text;
using UEManager.Core.Mods;

namespace UEManager.Core.Tests;

/// <summary>Telling an official UE4SS build from another one by the DLL fingerprints the UE4SS manifest lists.</summary>
public class Ue4ssBuildTests : IDisposable
{
    private readonly TestFolder folder = new();

    public void Dispose() => folder.Dispose();

    private static string Hash(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    /// <summary>The manifest entry of an official build with two releases that differ in BPModLoaderMod.</summary>
    private static Ue4ssRelease Official() => Ue4ssManifest.Parse($$"""
        { "ue4ss": { "phantom-blade-zero": {
            "version": "v3.0.1-1183-gcdbc0b66-r2", "url": "https://example.com/pbz.zip", "sha256": "AB",
            "files": {
              "dwmapi.dll": "{{Hash("proxy")}}",
              "ue4ss/UE4SS.dll": ["{{Hash("official runtime")}}"],
              "ue4ss/Mods/BPModLoaderMod/Scripts/main.lua": ["{{Hash("loader 1183")}}", "{{Hash("loader r2")}}"],
              "ue4ss/Mods/Keybinds/Scripts/main.lua": ["{{Hash("keybinds")}}"],
              "..\\outside.dll": ["{{Hash("x")}}"]
            } } } }
        """, "phantom-blade-zero")!;

    /// <summary>UE4SS 3.x next to the game's exe, as the official build places it.</summary>
    private string Install(string runtime = "official runtime", string loader = "loader 1183")
    {
        folder.File(@"Win64\dwmapi.dll", "proxy");
        folder.File(@"Win64\ue4ss\UE4SS.dll", runtime);
        folder.File(@"Win64\ue4ss\Mods\BPModLoaderMod\Scripts\main.lua", loader);
        folder.File(@"Win64\ue4ss\Mods\Keybinds\Scripts\main.lua", "keybinds");
        return folder.Combine("Win64");
    }

    [Fact]
    public void The_manifest_lists_each_file_with_its_official_fingerprints()
    {
        var files = Official().Files!;

        Assert.Equal(4, files.Count);
        Assert.Equal(2, files["ue4ss/Mods/BPModLoaderMod/Scripts/main.lua"].Count);
        Assert.Single(files["dwmapi.dll"]);
        Assert.Null(Ue4ssManifest.Parse("""{ "ue4ss": { "g": { "version": "v1", "url": "https://example.com/a.zip" } } }""", "g")!.Files);
    }

    [Fact]
    public void Either_official_release_is_official()
    {
        Assert.Empty(Ue4ssBuild.DifferingFiles(Install(loader: "loader 1183"), Official().Files!));
        Assert.Empty(Ue4ssBuild.DifferingFiles(Install(loader: "loader r2"), Official().Files!));
    }

    [Fact]
    public void Another_build_lists_the_DLLs_that_differ()
    {
        var exeFolder = Install(runtime: "another runtime");
        folder.File(@"Win64\dwmapi.dll", "another proxy");

        Assert.Equal(["dwmapi.dll", "ue4ss/UE4SS.dll"], Ue4ssBuild.DifferingFiles(exeFolder, Official().Files!));
    }

    [Fact]
    public void Changed_scripts_do_not_count()
    {
        var exeFolder = Install(loader: "another loader");
        folder.File(@"Win64\ue4ss\Mods\Keybinds\Scripts\main.lua", "the player's keys");

        Assert.Empty(Ue4ssBuild.DifferingFiles(exeFolder, Official().Files!));
    }

    [Fact]
    public void Settings_a_removed_proxy_and_the_players_own_mods_do_not_count()
    {
        var exeFolder = Install();
        var files = new Dictionary<string, IReadOnlyList<string>>(Official().Files!, StringComparer.OrdinalIgnoreCase)
        {
            ["ue4ss/UE4SS-settings.ini"] = [Hash("official settings")],
        };
        folder.File(@"Win64\ue4ss\UE4SS-settings.ini", "the player's settings");
        File.Delete(Path.Combine(exeFolder, "dwmapi.dll"));
        folder.File(@"Win64\ue4ss\Mods\MyMod\Scripts\main.lua", "my own mod");

        Assert.Empty(Ue4ssBuild.DifferingFiles(exeFolder, files));
    }
}

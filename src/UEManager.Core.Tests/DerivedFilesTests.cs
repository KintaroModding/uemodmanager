using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

/// <summary>
/// A file a mod derives from one of its own files in the mods folder, such as the SpeakerHighlight.txt that a mod
/// writes beside the SpeakerHighlight.json it shipped for its Mods Menu command. It stays out of the files that no mod
/// placed, while any other file still shows.
/// </summary>
public class DerivedFilesTests : IDisposable
{
    private const string Json = @"TestGame\Content\Paks\~mods\SpeakerHighlight.json";

    private readonly TestFolder folder = new();
    private readonly GameInstall game;

    public DerivedFilesTests()
    {
        game = GameLocator.Resolve(TestFolder.Profile(), folder.Game(), GameStore.Manual)!;
    }

    public void Dispose() => folder.Dispose();

    private ModRecord Install()
    {
        var manager = new ModManager(game, new AppPaths(folder.Combine("Data")));
        folder.File($@"Downloads\Speaker\{Json}", "{}");
        using var preview = manager.Inspect(folder.Combine("Downloads", "Speaker"));
        var parts = preview.Packages.ToList();
        var check = manager.Check(preview, parts);
        return manager.Install(preview, parts, "Speaker Highlight", "Speaker", check.Replaces is not null);
    }

    [Fact]
    public void A_file_beside_the_mods_own_json_is_not_unmanaged_but_another_file_still_is()
    {
        Install();
        File.WriteAllText(Path.Combine(game.ModsPath, "SpeakerHighlight.txt"), "color=FFE9A8");
        File.WriteAllText(Path.Combine(game.ModsPath, "SomethingElse.txt"), "data");

        var manager = new ModManager(game, new AppPaths(folder.Combine("Data")));

        var unmanaged = manager.UnmanagedFiles();
        Assert.Contains("SomethingElse.txt", unmanaged);
        Assert.DoesNotContain("SpeakerHighlight.txt", unmanaged);
    }

    [Fact]
    public void A_file_with_the_same_name_but_another_extension_in_another_folder_still_shows()
    {
        Install();
        Directory.CreateDirectory(Path.Combine(game.ModsPath, "Sub"));
        File.WriteAllText(Path.Combine(game.ModsPath, "Sub", "SpeakerHighlight.txt"), "color=FFE9A8");

        var manager = new ModManager(game, new AppPaths(folder.Combine("Data")));

        Assert.Contains(Path.Combine("Sub", "SpeakerHighlight.txt"), manager.UnmanagedFiles());
    }
}

using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

public class ModTagsTests : IDisposable
{
    private readonly TestFolder folder = new();
    private readonly ModManager manager;
    private readonly GameInstall game;

    public ModTagsTests()
    {
        game = GameLocator.Resolve(TestFolder.Profile(), folder.Game(), GameStore.Manual)!;
        manager = new ModManager(game, new AppPaths(folder.Combine("Data")));
    }

    public void Dispose() => folder.Dispose();

    private ModRecord Install(string name, string content = "pak", bool replace = false)
    {
        folder.File($@"Downloads\{name}\{name}_P.pak", content);
        using var preview = manager.Inspect(folder.Combine("Downloads", name));
        return manager.Install(preview, preview.Packages.ToList(), name, null, replace);
    }

    [Theory]
    [InlineData("Combat, UI", new[] { "Combat", "UI" })]
    [InlineData("  Combat ,, UI ;Maps ", new[] { "Combat", "UI", "Maps" })]
    [InlineData("战斗，界面、地图；任务", new[] { "战斗", "界面", "地图", "任务" })]
    [InlineData("Combat, combat, COMBAT", new[] { "Combat" })]
    [InlineData("Two words, UI", new[] { "Two words", "UI" })]
    [InlineData("  , ", new string[0])]
    [InlineData(null, new string[0])]
    public void What_the_player_types_becomes_tags(string? text, string[] tags) => Assert.Equal(tags, ModTags.Parse(text));

    [Fact]
    public void Tags_survive_a_restart_and_change_no_file()
    {
        var mod = Install("Sharp");
        var deployed = mod.Files.Single().DeployedPath!;

        manager.Rename(mod.Id, "Sharp", null, ["Combat", "UI"]);

        var restarted = new ModManager(game, new AppPaths(folder.Combine("Data")));
        var saved = Assert.Single(restarted.Mods);
        Assert.Equal(["Combat", "UI"], saved.Tags);
        Assert.Equal("Sharp", saved.Name);
        Assert.True(File.Exists(deployed));
        Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(saved));
    }

    [Fact]
    public void A_rename_without_tags_keeps_them_and_an_empty_list_removes_them()
    {
        var mod = Install("Sharp");
        manager.Rename(mod.Id, "Sharp", null, ["Combat"]);

        manager.Rename(mod.Id, "Sharp Blades", "1.0");
        Assert.Equal(["Combat"], manager.Mods.Single().Tags);

        manager.Rename(mod.Id, "Sharp Blades", "1.0", []);
        Assert.Null(manager.Mods.Single().Tags);
    }

    [Fact]
    public void A_mod_without_tags_writes_none_in_the_mod_list()
    {
        var mod = Install("Sharp");
        var list = Directory.GetFiles(folder.Combine("Data"), "mods.json", SearchOption.AllDirectories).Single();

        Assert.DoesNotContain("tags", File.ReadAllText(list), StringComparison.OrdinalIgnoreCase);

        manager.Rename(mod.Id, "Sharp", null, ["Combat"]);

        Assert.Contains("Combat", File.ReadAllText(list));
    }

    [Fact]
    public void An_update_keeps_the_tags()
    {
        var mod = Install("Sharp");
        manager.Rename(mod.Id, "Sharp", null, ["Combat"]);

        Install("Sharp", "a new version", replace: true);

        var updated = Assert.Single(manager.Mods);
        Assert.NotEqual(mod.Id, updated.Id);
        Assert.Equal(["Combat"], updated.Tags);
    }

    [Fact]
    public void An_install_takes_tags_and_an_update_can_change_or_remove_them()
    {
        folder.File(@"Downloads\Sharp\Sharp_P.pak", "pak");
        using var preview = manager.Inspect(folder.Combine("Downloads", "Sharp"));

        manager.Install(preview, preview.Packages.ToList(), "Sharp", null, tags: ["Combat", "UI"]);
        Assert.Equal(["Combat", "UI"], manager.Mods.Single().Tags);

        manager.Install(preview, preview.Packages.ToList(), "Sharp", null, replace: true, tags: ["Maps"]);
        Assert.Equal(["Maps"], manager.Mods.Single().Tags);

        manager.Install(preview, preview.Packages.ToList(), "Sharp", null, replace: true);
        Assert.Equal(["Maps"], manager.Mods.Single().Tags);

        manager.Install(preview, preview.Packages.ToList(), "Sharp", null, replace: true, tags: []);
        Assert.Null(manager.Mods.Single().Tags);
    }

    [Fact]
    public void An_uninstall_takes_its_tags_away()
    {
        var mod = Install("Sharp");
        manager.Rename(mod.Id, "Sharp", null, ["Combat"]);

        manager.Uninstall(mod.Id);

        Assert.Empty(ModTags.Count(manager.Mods));
    }

    [Fact]
    public void Tags_are_counted_by_name_whatever_their_case()
    {
        var mods = new[]
        {
            new ModRecord { Tags = ["UI", "Combat"] },
            new ModRecord { Tags = ["combat"] },
            new ModRecord(),
        };

        Assert.Equal([("Combat", 2), ("UI", 1)], ModTags.Count(mods));
    }

    [Theory]
    [InlineData("combat", true)]
    [InlineData("Combat", true)]
    [InlineData("UI", false)]
    [InlineData(null, true)]
    public void A_mod_has_a_tag_whatever_its_case_and_every_mod_counts_for_all(string? tag, bool has) =>
        Assert.Equal(has, ModTags.Has(new ModRecord { Tags = ["Combat"] }, tag));

    [Fact]
    public void A_mod_without_tags_has_only_the_filter_for_all()
    {
        Assert.True(ModTags.Has(new ModRecord(), null));
        Assert.False(ModTags.Has(new ModRecord(), "Combat"));
    }
}

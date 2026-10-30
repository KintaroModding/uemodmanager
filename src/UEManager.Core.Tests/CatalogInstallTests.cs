using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

/// <summary>Variants, and mods installed from the catalog: the catalog id tells which installed mod a download updates.</summary>
public class CatalogInstallTests : IDisposable
{
    private readonly TestFolder folder = new();
    private readonly GameInstall game;
    private readonly ModManager manager;

    public CatalogInstallTests()
    {
        game = GameLocator.Resolve(TestFolder.Profile(), folder.Game(), GameStore.Manual)!;
        manager = new ModManager(game, new AppPaths(folder.Combine("Data")));
    }

    public void Dispose() => folder.Dispose();

    /// <summary>A KintaroModding pak zip: one IoStore set at the top of the download.</summary>
    private string PakSet(string download, string baseName, string content = "v1")
    {
        foreach (var extension in new[] { ".pak", ".utoc", ".ucas" })
            folder.File($@"Downloads\{download}\{baseName}_P{extension}", content);
        return folder.Combine("Downloads", download);
    }

    private ModRecord Install(string source, string? catalogId = null, bool replace = false)
    {
        using var preview = manager.Inspect(source);
        return manager.Install(preview, preview.Packages.ToList(), preview.SuggestedName, preview.SuggestedVersion, replace, catalogId);
    }

    private string Deployed(string fileName) => Path.Combine(game.ModsPath, fileName);

    [Fact]
    public void Variant_folders_are_one_choice_in_natural_order()
    {
        foreach (var variant in new[] { "x10", "x2", "x1.5" })
            folder.File($@"Downloads\More_1.1\{variant}\More_P.pak", variant);

        using var preview = manager.Inspect(folder.Combine("Downloads", "More_1.1"));

        Assert.Equal(["x1.5", "x2", "x10"], preview.Packages.Select(p => p.Variant));
        Assert.Single(preview.Packages.Select(p => p.VariantOf).Distinct());
    }

    [Fact]
    public void The_chosen_variant_is_installed_and_recorded()
    {
        foreach (var variant in new[] { "x1.5", "x2", "x10" })
            folder.File($@"Downloads\More_1.1\{variant}\More_P.pak", variant);
        using var preview = manager.Inspect(folder.Combine("Downloads", "More_1.1"));

        var mod = manager.Install(preview, [preview.Packages[1]], "More", "1.1");

        Assert.Equal("x2", mod.Variant);
        Assert.Equal("x2", File.ReadAllText(Deployed("More_P.pak")));
    }

    [Fact]
    public void Folders_with_different_files_are_not_variants()
    {
        folder.File(@"Downloads\Two\A\One_P.pak");
        folder.File(@"Downloads\Two\B\Two_P.pak");

        using var preview = manager.Inspect(folder.Combine("Downloads", "Two"));

        Assert.All(preview.Packages, p => Assert.Null(p.Variant));
    }

    [Fact]
    public void A_catalog_update_replaces_the_mod_with_the_same_id_even_with_new_file_names()
    {
        var first = Install(PakSet("Dash_1.0", "Dash"), "kintaromodding/dash");
        using var preview = manager.Inspect(PakSet("Dash_1.1", "DashRenamed"));

        var check = manager.Check(preview, preview.Packages.ToList(), "kintaromodding/dash");
        Assert.Equal(first.Id, check.Replaces?.Id);
        manager.Install(preview, preview.Packages.ToList(), "Dash", "1.1", replace: true, catalogId: "kintaromodding/dash");

        var mod = Assert.Single(manager.Mods);
        Assert.Equal("kintaromodding/dash", mod.CatalogId);
        Assert.Equal("1.1", mod.Version);
        Assert.False(File.Exists(Deployed("Dash_P.pak")));
        Assert.True(File.Exists(Deployed("DashRenamed_P.pak")));
    }

    [Fact]
    public void A_catalog_mod_never_takes_over_the_files_of_another_enabled_mod()
    {
        var dash = Install(PakSet("Dash_1.0", "Dash"), "kintaromodding/dash");
        using var preview = manager.Inspect(PakSet("Variants_1.0", "Dash", "other"));

        var check = manager.Check(preview, preview.Packages.ToList(), "kintaromodding/variants");
        Assert.False(check.CanInstall);
        Assert.Null(check.Replaces);
        Assert.Contains(check.Problems, p => p.ToString().Contains("\"Dash\""));

        manager.SetEnabled(dash.Id, false);
        Assert.True(manager.Check(preview, preview.Packages.ToList(), "kintaromodding/variants").CanInstall);
    }

    [Fact]
    public void A_catalog_install_takes_over_the_same_mod_installed_by_hand()
    {
        var hand = Install(PakSet("Dash_1.0", "Dash"));
        using var preview = manager.Inspect(PakSet("Dash_1.1", "Dash", "v2"));

        var check = manager.Check(preview, preview.Packages.ToList(), "kintaromodding/dash");
        Assert.Equal(hand.Id, check.Replaces?.Id);
        manager.Install(preview, preview.Packages.ToList(), "Dash", "1.1", replace: true, catalogId: "kintaromodding/dash");

        Assert.Equal("kintaromodding/dash", Assert.Single(manager.Mods).CatalogId);
        Assert.Equal("v2", File.ReadAllText(Deployed("Dash_P.pak")));
    }

    [Fact]
    public void An_update_installed_by_hand_keeps_the_catalog_link()
    {
        Install(PakSet("Dash_1.0", "Dash"), "kintaromodding/dash");

        Install(PakSet("Dash_1.1", "Dash", "v2"), replace: true);

        Assert.Equal("kintaromodding/dash", Assert.Single(manager.Mods).CatalogId);
    }
}

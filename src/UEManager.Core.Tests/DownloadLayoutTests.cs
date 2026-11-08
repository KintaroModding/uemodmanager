using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

/// <summary>Where the files of a download go, and which of those that stay out the review warns about.</summary>
public class DownloadLayoutTests : IDisposable
{
    private readonly TestFolder folder = new();
    private readonly ModManager manager;
    private readonly GameInstall game;

    public DownloadLayoutTests()
    {
        game = GameLocator.Resolve(TestFolder.Profile(), folder.Game(), GameStore.Manual)!;
        manager = new ModManager(game, new AppPaths(folder.Combine("Data")));
    }

    public void Dispose() => folder.Dispose();

    private InstallPreview Inspect(params string[] files)
    {
        foreach (var file in files)
            folder.File($@"Downloads\Mod\{file}", "content");
        return manager.Inspect(folder.Combine("Downloads", "Mod"));
    }

    private static IEnumerable<string> Placed(InstallPreview preview) => preview.Packages.SelectMany(p => p.RelativePaths);

    private IEnumerable<string> WarningsFor(InstallPreview preview) =>
        manager.Check(preview, preview.Packages.ToList()).Warnings.Select(w => w.ToString());

    [Theory]
    [InlineData(@"TestGame\Plugins\Foo\Foo.uplugin", "TestGame/Plugins/Foo/Foo.uplugin")]
    [InlineData(@"TestGame\Mods\Foo\main.js", "TestGame/Mods/Foo/main.js")]
    [InlineData(@"TestGame\PBZModLoader\extra\data.bin", "TestGame/PBZModLoader/extra/data.bin")]
    [InlineData(@"TestGame\foo.ini", "TestGame/foo.ini")]
    [InlineData(@"TestGame\Config\notice.ini", "TestGame/Config/notice.ini")]
    [InlineData(@"TestGame\Saved\Foo\credits.json", "TestGame/Saved/Foo/credits.json")]
    [InlineData(@"Wrap\TestGame\Plugins\Foo\Foo.uplugin", "TestGame/Plugins/Foo/Foo.uplugin")]
    public void A_file_below_the_project_folder_keeps_its_path(string file, string expected)
    {
        using var preview = Inspect(file);

        Assert.Equal(expected, Assert.Single(Placed(preview)));
        Assert.Empty(preview.IgnoredFiles);
    }

    [Theory]
    [InlineData(@"TestGame\Content\Paks\Foo\icon.png")]
    [InlineData(@"TestGame\Content\Paks\~mods\Foo\data.bin")]
    [InlineData(@"TestGame\Binaries\Other\helper.bin")]
    [InlineData(@"TestGame\Saved\SaveGames\slot.sav")]
    [InlineData(@"TestGame\Saved\Logs\game.log")]
    [InlineData(@"TestGame\Saved\Crashes\crash.dmp")]
    [InlineData(@"TestGame\PBZModLoader\mods.ini")]
    public void Some_files_below_the_project_folder_stay_out(string file)
    {
        using var preview = Inspect(file);

        Assert.Empty(preview.Packages);
        Assert.Single(preview.IgnoredFiles);
    }

    [Theory]
    [InlineData(@"PBZModLoader\mods\Foo\main.js", "TestGame/PBZModLoader/mods/Foo/main.js")]
    [InlineData(@"Wrap\PBZModLoader\mods\Foo\main.js", "TestGame/PBZModLoader/mods/Foo/main.js")]
    [InlineData(@"One\Two\PBZModLoader\mods\Foo\main.js", "TestGame/PBZModLoader/mods/Foo/main.js")]
    [InlineData(@"Content\Mods\Foo\icon.png", "TestGame/Content/Mods/Foo/icon.png")]
    [InlineData(@"Content\JavaScript\Foo\main.js", "TestGame/Content/JavaScript/Foo/main.js")]
    public void A_download_may_leave_out_the_project_folder(string file, string expected)
    {
        using var preview = Inspect(file);

        Assert.Equal(expected, Assert.Single(Placed(preview)));
    }

    [Fact]
    public void A_script_mod_without_the_project_folder_waits_for_its_loader_too()
    {
        using var preview = Inspect(@"PBZModLoader\mods\Foo\main.js");

        var part = Assert.Single(preview.Packages);
        Assert.Equal(PackageScanner.ForModLoader, part.NeedsLoader);
        Assert.Contains(manager.Check(preview, [part]).Problems, p => p.ToString().Contains("runs in PBZModLoader"));
    }

    [Theory]
    [InlineData(@"One\Two\Content\Mods\Foo\icon.png")]
    [InlineData(@"Content\Paks\Foo\data.bin")]
    [InlineData(@"PBZModLoader\mods.ini")]
    [InlineData(@"mods\Foo\main.js")]
    [InlineData(@"Foo\main.js")]
    public void Without_the_project_folder_other_layouts_stay_out(string file)
    {
        using var preview = Inspect(file);

        Assert.Empty(preview.Packages);
    }

    [Fact]
    public void A_loader_with_a_content_folder_still_goes_next_to_the_exe_as_a_whole()
    {
        using var preview = Inspect("dxgi.dll", @"Content\tex.png");

        var part = Assert.Single(preview.Packages);
        Assert.Equal(DeployTarget.ExeFolder, part.Target);
        Assert.Equal(["Content/tex.png", "dxgi.dll"], part.RelativePaths);
    }

    [Fact]
    public void A_dll_below_the_project_folder_still_goes_next_to_the_exe()
    {
        using var preview = Inspect(@"TestGame\Bin\helper.dll", @"TestGame\Bin\helper.ini");

        var part = Assert.Single(preview.Packages);
        Assert.Equal(DeployTarget.ExeFolder, part.Target);
        Assert.Equal(["helper.dll", "helper.ini"], part.RelativePaths);
    }

    [Fact]
    public void A_dll_named_like_a_document_does_not_move_the_loader()
    {
        using var preview = Inspect(@"A\install.dll", @"A\B\dxgi.dll");

        var part = Assert.Single(preview.Packages);
        Assert.Equal(DeployTarget.ExeFolder, part.Target);
        Assert.Equal(["dxgi.dll"], part.RelativePaths);
        Assert.Equal(["Mod/A/install.dll"], preview.IgnoredFiles);
    }

    [Fact]
    public void A_config_named_like_a_document_does_not_turn_a_loader_into_a_copy()
    {
        using var preview = Inspect(@"TestGame\Config\notice.ini", @"Loader\dxgi.dll", @"Loader\notice.ini");

        Assert.Contains(preview.Packages, p => p.Target == DeployTarget.ExeFolder && p.RelativePaths.SequenceEqual(["dxgi.dll"]));
        Assert.Contains("TestGame/Config/notice.ini", Placed(preview));
    }

    [Theory]
    [InlineData("platform/steam/reshade-shaders/Shaders/foo.fx")]
    [InlineData("TestGame/Saved/Foo/Deep/x.ini")]
    [InlineData("TestGame/Config/Foo/Deep/x.ini")]
    [InlineData("TestGame/Binaries/Win64/Foo/Deep/x.ini")]
    [InlineData("TestGame/Content/Paks/Foo/Deep/x.ini")]
    public void The_folders_next_to_the_exe_and_of_the_game_stay_when_a_mod_leaves(string path) =>
        Assert.Null(PackageScanner.BelowModFolder(path, "TestGame"));

    [Theory]
    [InlineData("TestGame/Content/JavaScript/Menu/main.js", "Menu/main.js")]
    [InlineData("TestGame/PBZModLoader/mods/Foo/main.js", "Foo/main.js")]
    [InlineData("TestGame/Plugins/Foo/Data/x.bin", "Data/x.bin")]
    public void The_folders_of_a_mod_go_when_it_leaves(string path, string below) =>
        Assert.Equal(below, PackageScanner.BelowModFolder(path, "TestGame"));

    [Fact]
    public void A_mods_files_in_a_new_folder_of_the_project_leave_with_it()
    {
        folder.File(@"Downloads\Plugin\TestGame\Plugins\Foo\Data\Deep\table.bin", "a table");
        using var preview = manager.Inspect(folder.Combine("Downloads", "Plugin"));

        var mod = manager.Install(preview, preview.Packages.ToList(), "Plugin", null);
        var place = Path.Combine(game.RootPath, "TestGame", "Plugins", "Foo", "Data");
        Assert.True(File.Exists(Path.Combine(place, "Deep", "table.bin")));

        manager.Uninstall(mod.Id);

        Assert.False(Directory.Exists(place));
    }

    [Fact]
    public void A_signature_goes_with_its_package_by_name()
    {
        using var preview = Inspect("Foo_P.pak", "Foo_P.sig", "Other.sig");

        Assert.Equal(["Foo_P.pak", "Foo_P.sig"], Assert.Single(preview.Packages).RelativePaths);
        Assert.Equal(["Mod/Other.sig"], preview.LeftOutFiles);
    }

    [Fact]
    public void A_signature_is_no_settings_file()
    {
        folder.File(@"Downloads\Mod\Foo_P.pak", "pak");
        folder.File(@"Downloads\Mod\Foo_P.sig", "signature");
        using var preview = manager.Inspect(folder.Combine("Downloads", "Mod"));

        var mod = manager.Install(preview, preview.Packages.ToList(), "Foo", null);

        Assert.True(File.Exists(Path.Combine(game.ModsPath, "Foo_P.sig")));
        Assert.DoesNotContain(mod.Files, f => f.IsSettingsFile);
    }

    [Fact]
    public void A_loose_package_brings_its_signature()
    {
        folder.File(@"Downloads\Foo_P.pak", "pak");
        folder.File(@"Downloads\Foo_P.sig", "signature");

        using var preview = manager.Inspect(folder.Combine("Downloads", "Foo_P.pak"));

        Assert.Equal(["Foo_P.pak", "Foo_P.sig"], Assert.Single(preview.Packages).RelativePaths);
    }

    [Theory]
    [InlineData("license.json", true)]
    [InlineData("notice.ini", true)]
    [InlineData("install.js", false)]
    [InlineData("credits.png", false)]
    public void A_file_named_like_a_document_is_no_document_in_another_file_type(string name, bool settings)
    {
        Assert.False(SettingsFiles.IsDocument(name));
        Assert.Equal(settings, SettingsFiles.IsPakSettings(name));
    }

    [Theory]
    [InlineData("LICENSE")]
    [InlineData("license.txt")]
    [InlineData("README.md")]
    [InlineData("Credits.pdf")]
    [InlineData("changelog.html")]
    public void A_document_is_told_by_its_name_and_type(string name) => Assert.True(SettingsFiles.IsDocument(name));

    [Fact]
    public void A_file_beside_a_pak_named_like_a_document_goes_with_it_in_a_data_type()
    {
        using var preview = Inspect("Foo_P.pak", "license.json", "license.txt");

        Assert.Equal(["Foo_P.pak", "license.json"], Assert.Single(preview.Packages).RelativePaths);
        Assert.Empty(preview.LeftOutFiles);
    }

    [Theory]
    [InlineData("tables.dat")]
    [InlineData("extras.zip")]
    [InlineData(@"Foo\main.js")]
    [InlineData(@"TestGame\Content\Paks\~mods\Foo\icon.png")]
    [InlineData(@"TestGame\Binaries\Other\icon.png")]
    public void The_review_warns_about_a_file_that_stays_out_and_may_matter(string file)
    {
        using var preview = Inspect("Foo_P.pak", file);

        var warning = Assert.Single(WarningsFor(preview));
        Assert.Contains("Files left out (1)", warning);
        Assert.Contains(Path.GetFileName(file), warning);
    }

    [Theory]
    [InlineData("README.txt")]
    [InlineData("LICENSE")]
    [InlineData("Tool-LICENSE.txt")]
    [InlineData("Mod-Install-Guide.txt")]
    [InlineData("CONTRIBUTORS.md")]
    [InlineData("CONTRIBUTORS")]
    [InlineData("preview.png")]
    [InlineData(@"Pictures\screen.jpg")]
    [InlineData("Mod page.url")]
    [InlineData("install.bat")]
    [InlineData(@"TestGame\Saved\SaveGames\slot.sav")]
    [InlineData(@"TestGame\PBZModLoader\mods.ini")]
    [InlineData(@"PBZModLoader\mods.ini")]
    [InlineData(@"ue4ss\Mods\mods.txt")]
    public void The_review_says_nothing_of_a_file_that_is_meant_to_stay_out(string file)
    {
        using var preview = Inspect(@"TestGame\Content\Paks\~mods\Foo_P.pak", file);

        Assert.Empty(WarningsFor(preview));
        Assert.Single(preview.IgnoredFiles);
    }

    /// <summary>As ModConfigUI 1.0 ships it: a name and a version at the top of the zip, for mod tools.</summary>
    [Fact]
    public void The_review_says_nothing_of_a_modinfo_file_at_the_top()
    {
        using var preview = Inspect("modinfo.ini", @"TestGame\Content\Paks\~mods\Foo_P.pak");

        Assert.Empty(WarningsFor(preview));
        Assert.Single(preview.IgnoredFiles);
    }

    [Fact]
    public void A_copy_of_a_placed_file_for_another_store_is_no_cause_for_a_warning()
    {
        using var preview = Inspect(@"TestGame\Binaries\Win64\version.dll", @"platform\other\version.dll");

        Assert.Empty(preview.LeftOutFiles);
    }

    [Fact]
    public void The_warning_names_eight_files_and_counts_all()
    {
        using var preview = Inspect(["Foo_P.pak", .. Enumerable.Range(1, 10).Select(i => $"file{i}.dat")]);

        var warning = Assert.Single(WarningsFor(preview));
        Assert.Contains("Files left out (10)", warning);
        Assert.Contains("file8.dat, …", warning);
        Assert.DoesNotContain("file9.dat", warning);
    }

    [Fact]
    public void The_review_warns_about_a_mod_with_only_a_main_ts()
    {
        folder.File(@"Game\TestGame\Binaries\Win64\version.dll", "[PBZMOD] by hand");
        using var preview = Inspect(@"TestGame\PBZModLoader\mods\Source\main.ts", @"TestGame\PBZModLoader\mods\Source\data.json");

        var check = manager.Check(preview, preview.Packages.ToList());

        Assert.True(check.CanInstall);
        var warning = Assert.Single(check.Warnings).ToString();
        Assert.Contains("PBZModLoader 2.0 cannot use", warning);
        Assert.Contains("Source", warning);
    }

    [Theory]
    [InlineData(@"TestGame\PBZModLoader\mods\Both\main.ts", @"TestGame\PBZModLoader\mods\Both\main.js")]
    [InlineData(@"TestGame\PBZModLoader\mods\Deep\src\main.ts", @"TestGame\PBZModLoader\mods\Deep\main.js")]
    [InlineData(@"TestGame\Content\JavaScript\PBZModsMenu\Mods\Menu\main.ts", @"TestGame\Content\JavaScript\PBZModsMenu\bootstrap.js")]
    public void A_main_ts_with_something_to_run_or_for_another_loader_is_no_cause_for_a_warning(string first, string second)
    {
        folder.File(@"Game\TestGame\Binaries\Win64\version.dll", "[PBZMOD] by hand");
        using var preview = Inspect(first, second);

        Assert.DoesNotContain(WarningsFor(preview), w => w.Contains("main.ts"));
    }

    [Fact]
    public void The_message_for_a_download_with_nothing_names_the_games_folders()
    {
        using var preview = Inspect(@"mods\Foo\main.js");

        var problem = Assert.Single(manager.Check(preview, []).Problems).ToString();
        Assert.Contains("No mod files were found", problem);
        Assert.Contains("(TestGame, TestGame/Binaries/Win64)", problem);
    }

    [Fact]
    public void A_copy_for_ue4ss_and_one_for_the_mod_loader_are_one_choice()
    {
        using var preview = Inspect(
            @"TestGame\Content\JavaScript\UE4SSQuestGuide\standalone.js",
            @"TestGame\PBZModLoader\mods\pbz-quest-guide\main.js");

        var ue4ss = preview.Packages.Single(p => p.RelativePaths[0].Contains("UE4SSQuestGuide"));
        var loader = preview.Packages.Single(p => p.RelativePaths[0].Contains("pbz-quest-guide"));
        Assert.True(ue4ss.IsAlternate);
        Assert.True(loader.IsAlternate);
        Assert.Equal(PackageScanner.ForUe4ss, ue4ss.Variant);
        Assert.Equal(PackageScanner.ForModLoader, loader.Variant);
    }

    [Fact]
    public void The_ue4ss_copy_is_chosen_when_ue4ss_is_the_only_loader()
    {
        folder.File(@"Game\TestGame\Binaries\Win64\UE4SS.dll", "");
        using var preview = Inspect(
            @"TestGame\Content\JavaScript\UE4SSQuestGuide\standalone.js",
            @"TestGame\PBZModLoader\mods\pbz-quest-guide\main.js");

        var chosen = manager.PreferredChoice(preview);

        Assert.NotNull(chosen);
        Assert.Contains("UE4SSQuestGuide", chosen!.Label);
    }
}

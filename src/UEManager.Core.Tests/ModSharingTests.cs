using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

/// <summary>
/// Mods of one author that bring the same framework, byte for byte, and files of their own, as Mo Yuan and Play as Bai
/// Chilian for Phantom Blade Zero (2026-11-21): a pak bridge in ~mods and a UE4SS core with one profile per character.
/// The player says at install that they work together, and they share the framework's files.
/// </summary>
public class ModSharingTests : IDisposable
{
    private readonly TestFolder folder = new();
    private readonly GameInstall game;
    private readonly AppPaths paths;
    private ModManager manager;

    public ModSharingTests()
    {
        game = GameLocator.Resolve(TestFolder.Profile(), folder.Game(), GameStore.Manual)!;
        paths = new AppPaths(folder.Combine("Data"));
        manager = new ModManager(game, paths, _ => false);
        File.WriteAllText(Path.Combine(game.BinariesPath, "dwmapi.dll"), "proxy");
        Directory.CreateDirectory(Path.Combine(game.BinariesPath, "ue4ss", "Mods"));
        File.WriteAllText(Path.Combine(game.BinariesPath, "ue4ss", "UE4SS.dll"), "runtime");
    }

    public void Dispose() => folder.Dispose();

    private string Ue4ssMods => Ue4ssRuntime.Detect(game).ModsPath;

    private string CoreMain => Path.Combine(Ue4ssMods, "Core", "Scripts", "main.lua");

    private string Bridge => Path.Combine(game.ModsPath, "zz_Bridge_P.pak");

    private string Profile(string name) => Path.Combine(Ue4ssMods, "Core", "Scripts", $"Profile_{name}.lua");

    private string Character(string name) => Path.Combine(game.ModsPath, $"{name}_P.pak");

    /// <summary>A character mod: the shared framework (a bridge pak and the UE4SS core) and its own pak and profile.</summary>
    private string Download(string name, string framework = "core 1", string version = "1", string? settings = null, string? folderName = null)
    {
        var top = $@"Downloads\{folderName ?? name + " " + version}";
        folder.File($@"{top}\TestGame\Content\Paks\~mods\zz_Bridge_P.pak", "bridge " + framework);
        folder.File($@"{top}\TestGame\Content\Paks\~mods\{name}_P.pak", $"{name} pak {version}");
        folder.File($@"{top}\platform\steam\ue4ss\Mods\Core\Scripts\main.lua", framework);
        folder.File($@"{top}\platform\steam\ue4ss\Mods\Core\Scripts\Profile_{name}.lua", $"{name} {version}");
        folder.File($@"{top}\platform\steam\ue4ss\Mods\Core\enabled.txt", "");
        if (settings is not null)
            folder.File($@"{top}\platform\steam\ue4ss\Mods\Core\Scripts\config.lua", settings);
        return folder.Combine("Downloads", folderName ?? name + " " + version);
    }

    private InstallCheck Check(string download, bool shareFiles = false)
    {
        using var preview = manager.Inspect(download);
        return manager.Check(preview, preview.Packages.ToList(), shareFiles: shareFiles);
    }

    private ModRecord Install(string download, string name, bool shareFiles = false, bool replace = false)
    {
        using var preview = manager.Inspect(download);
        return manager.Install(preview, preview.Packages.ToList(), name, null, replace: replace, shareFiles: shareFiles);
    }

    private ModRecord Mod(string id) => manager.Mods.Single(m => m.Id == id);

    /// <summary>Mo Yuan, then Bai Chilian with the box ticked.</summary>
    private (ModRecord Mo, ModRecord Bai) BothShared()
    {
        var mo = Install(Download("Mo"), "Mo");
        var bai = Install(Download("Bai"), "Bai", shareFiles: true);
        return (Mod(mo.Id), Mod(bai.Id));
    }

    [Fact]
    public void Without_the_box_the_second_mod_replaces_the_first_as_before_and_the_box_is_offered()
    {
        var mo = Install(Download("Mo"), "Mo");

        var check = Check(Download("Bai"));
        Assert.Equal(mo.Id, check.Replaces?.Id);
        Assert.Null(check.Sharing);
        var offer = Assert.IsType<FileSharing>(check.ShareOffer);
        Assert.Equal(mo.Id, Assert.Single(offer.Mods).Id);
        Assert.Equal(3, offer.Files);

        var bai = Install(Download("Bai"), "Bai", replace: true);
        Assert.Equal(bai.Id, Assert.Single(manager.Mods).Id);
        Assert.False(File.Exists(Character("Mo")));
        Assert.DoesNotContain(manager.Mods.SelectMany(m => m.Files), f => f.SharedWithMods);
    }

    [Fact]
    public void With_the_box_both_mods_are_in_the_game_and_only_the_same_files_are_shared()
    {
        var check = Check(Download("Bai"), shareFiles: true);
        Assert.Contains(check.Problems, p => p.Format.Contains("nothing to share"));

        var mo = Install(Download("Mo"), "Mo");
        check = Check(Download("Bai"), shareFiles: true);
        Assert.Empty(check.Problems);
        Assert.Null(check.Replaces);
        Assert.Equal(3, check.Sharing?.Files);

        var bai = Install(Download("Bai"), "Bai", shareFiles: true);
        var moNow = Mod(mo.Id);
        bai = Mod(bai.Id);
        Assert.Equal(2, manager.Mods.Count);
        Assert.All(manager.Mods, m => Assert.True(m.Enabled));
        Assert.All(manager.Mods, m => Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(m)));
        Assert.True(File.Exists(Character("Mo")) && File.Exists(Character("Bai")));
        Assert.True(File.Exists(Profile("Mo")) && File.Exists(Profile("Bai")));
        Assert.Equal("core 1", File.ReadAllText(CoreMain));
        // The framework's files are marked in both mods, their own files in neither.
        foreach (var mod in new[] { moNow, bai })
        {
            Assert.Equal(["Core/Scripts/main.lua", "Core/enabled.txt", "zz_Bridge_P.pak"],
                mod.Files.Where(f => f.SharedWithMods).Select(f => f.FileName).Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public void Turning_one_off_keeps_the_shared_files_for_the_other_and_the_last_takes_them_out()
    {
        var (mo, bai) = BothShared();

        manager.SetEnabled(mo.Id, false);
        Assert.False(File.Exists(Character("Mo")));
        Assert.False(File.Exists(Profile("Mo")));
        Assert.True(File.Exists(CoreMain) && File.Exists(Bridge) && File.Exists(Profile("Bai")));
        Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(Mod(bai.Id)));

        manager.SetEnabled(mo.Id, true);
        Assert.All(manager.Mods, m => Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(m)));

        manager.SetEnabled(bai.Id, false);
        manager.SetEnabled(mo.Id, false);
        Assert.False(File.Exists(CoreMain));
        Assert.False(File.Exists(Bridge));
        Assert.False(Directory.Exists(Path.Combine(Ue4ssMods, "Core")));

        // Either one alone brings the framework back.
        manager.SetEnabled(bai.Id, true);
        Assert.Equal("core 1", File.ReadAllText(CoreMain));
        Assert.False(File.Exists(Character("Mo")));
    }

    [Fact]
    public void Uninstalling_one_keeps_the_shared_files_and_the_other_runs_on()
    {
        var (mo, bai) = BothShared();

        var result = manager.Uninstall(mo.Id);
        Assert.Empty(result.Leftovers);
        Assert.Equal(bai.Id, Assert.Single(manager.Mods).Id);
        Assert.True(File.Exists(CoreMain) && File.Exists(Bridge));
        Assert.False(File.Exists(Character("Mo")));
        Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(Mod(bai.Id)));

        manager.Uninstall(bai.Id);
        Assert.False(File.Exists(CoreMain));
        Assert.False(File.Exists(Bridge));
    }

    [Fact]
    public void A_file_a_mod_left_in_the_shared_folder_is_no_leftover_while_the_other_uses_the_folder()
    {
        var (mo, _) = BothShared();
        File.WriteAllText(Path.Combine(Ue4ssMods, "Core", "core.log"), "log");

        Assert.Empty(manager.LeftoversOf(mo.Id));
    }

    [Fact]
    public void A_test_without_mods_and_mod_sets_turn_them_off_and_on_together()
    {
        var (mo, bai) = BothShared();

        Assert.Equal(2, manager.TurnOffForTest());
        Assert.False(File.Exists(CoreMain));
        Assert.Equal(2, manager.TurnBackOn());
        Assert.All(manager.Mods, m => Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(m)));

        manager.SetEnabled(mo.Id, false);
        manager.SaveSet("Bai only");
        manager.SetEnabled(mo.Id, true);
        Assert.Equal((0, 1), manager.ApplySet("Bai only"));
        Assert.True(File.Exists(CoreMain));
        Assert.False(File.Exists(Character("Mo")));
        Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(Mod(bai.Id)));
    }

    [Fact]
    public void Files_that_are_not_the_same_cannot_be_shared()
    {
        var mo = Install(Download("Mo", framework: "core 1"), "Mo");

        var check = Check(Download("Bai", framework: "core 2"));
        Assert.Null(check.ShareOffer);
        check = Check(Download("Bai", framework: "core 2"), shareFiles: true);
        Assert.Contains(check.Problems, p => p.Format.Contains("are not the same as those of"));
        Assert.Throws<ModException>(() => Install(Download("Bai", framework: "core 2"), "Bai", shareFiles: true));

        Assert.Equal(mo.Id, Assert.Single(manager.Mods).Id);
        Assert.Equal("core 1", File.ReadAllText(CoreMain));
        Assert.DoesNotContain(Mod(mo.Id).Files, f => f.SharedWithMods);
    }

    [Fact]
    public void A_settings_file_is_never_shared()
    {
        Install(Download("Mo", settings: "speed = 1"), "Mo");

        Assert.Null(Check(Download("Bai", settings: "speed = 1")).ShareOffer);
        var check = Check(Download("Bai", settings: "speed = 1"), shareFiles: true);
        Assert.Contains(check.Problems, p => p.Format.Contains("or are settings"));
    }

    [Fact]
    public void A_download_of_the_mod_itself_is_an_update_and_offers_no_sharing()
    {
        var mo = Install(Download("Mo"), "Mo");

        var check = Check(Download("Mo", version: "2"));
        Assert.Equal(mo.Id, check.Replaces?.Id);
        Assert.Null(check.ShareOffer);
        Assert.Contains(Check(Download("Mo", version: "2"), shareFiles: true).Problems, p => p.Format.Contains("adds nothing"));
    }

    [Fact]
    public void An_update_of_one_keeps_sharing_with_the_other()
    {
        var (mo, bai) = BothShared();

        var check = Check(Download("Mo", version: "2"));
        Assert.Empty(check.Problems);
        Assert.Equal(mo.Id, check.Replaces?.Id);
        Assert.Equal(bai.Id, Assert.Single(check.Sharing!.Mods).Id);
        Assert.Null(check.ShareOffer);

        var mo2 = Install(Download("Mo", version: "2"), "Mo", replace: true);
        Assert.Equal(2, manager.Mods.Count);
        Assert.Equal("Mo pak 2", File.ReadAllText(Character("Mo")));
        Assert.All(manager.Mods, m => Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(m)));
        Assert.Equal(3, Mod(mo2.Id).Files.Count(f => f.SharedWithMods));

        // The shared files still serve whichever of them is on.
        manager.SetEnabled(mo2.Id, false);
        Assert.True(File.Exists(CoreMain));
        Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(Mod(bai.Id)));
    }

    [Fact]
    public void An_update_with_another_framework_waits_until_the_other_mod_is_off_and_then_that_one_waits()
    {
        var (mo, bai) = BothShared();

        var check = Check(Download("Mo", framework: "core 2", version: "2"));
        Assert.Contains(check.Problems, p => p.Format.Contains("shares {1} file(s) with this mod in another version"));

        manager.SetEnabled(bai.Id, false);
        Assert.Empty(Check(Download("Mo", framework: "core 2", version: "2")).Problems);
        Install(Download("Mo", framework: "core 2", version: "2"), "Mo", replace: true);
        Assert.Equal("core 2", File.ReadAllText(CoreMain));

        var error = Assert.Throws<ModException>(() => manager.SetEnabled(bai.Id, true));
        Assert.Contains("in another version", error.Message);
        Assert.False(Mod(bai.Id).Enabled);
        Assert.Equal("core 2", File.ReadAllText(CoreMain));
        Assert.False(File.Exists(Profile("Bai")));
        Assert.All(manager.Mods.Where(m => m.Enabled), m => Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(m)));
    }

    [Fact]
    public void A_third_mod_with_the_same_framework_shares_it_without_the_box()
    {
        var (mo, bai) = BothShared();

        var check = Check(Download("Yu"));
        Assert.Empty(check.Problems);
        Assert.Null(check.Replaces);
        Assert.Equal(new[] { mo.Id, bai.Id }.Order(StringComparer.Ordinal), check.Sharing!.Mods.Select(m => m.Id).Order(StringComparer.Ordinal));
        var yu = Install(Download("Yu"), "Yu");

        manager.SetEnabled(mo.Id, false);
        manager.SetEnabled(bai.Id, false);
        Assert.True(File.Exists(CoreMain) && File.Exists(Character("Yu")));
        Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(Mod(yu.Id)));
    }

    [Fact]
    public void The_sharing_is_kept_in_the_mod_list()
    {
        var (mo, bai) = BothShared();

        manager = new ModManager(game, paths, _ => false);
        Assert.Equal(3, Mod(mo.Id).Files.Count(f => f.SharedWithMods));
        manager.SetEnabled(mo.Id, false);
        Assert.True(File.Exists(CoreMain));
        Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(Mod(bai.Id)));
    }

    [Fact]
    public void Reset_modding_takes_everything_out()
    {
        BothShared();

        manager.ResetModding(deleteMods: true);

        Assert.Empty(manager.Mods);
        Assert.False(File.Exists(CoreMain));
        Assert.False(File.Exists(Bridge));
    }
}

using System.IO.Compression;
using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

public class SaveBackupTests : IDisposable
{
    private readonly TestFolder folder = new();

    public void Dispose() => folder.Dispose();

    private string Saves => folder.Combine("Saves");

    private string Backups => folder.Combine("Backups");

    [Fact]
    public void Zips_the_save_folder()
    {
        folder.File(@"Saves\Slot1.sav", "slot one");
        folder.File(@"Saves\Profiles\Profile.sav", "profile");

        var backup = SaveBackups.Create(Saves, Backups, new DateTime(2026, 10, 27, 21, 0, 0));

        Assert.Equal(Path.Combine(Backups, "saves 2026-10-27 21-00-00.zip"), backup);
        using var zip = ZipFile.OpenRead(backup);
        Assert.Equal(["Profiles/Profile.sav", "Slot1.sav"], zip.Entries.Select(e => e.FullName.Replace('\\', '/')).Order());
    }

    [Fact]
    public void Keeps_only_the_newest_five()
    {
        folder.File(@"Saves\Slot1.sav", "slot one");
        var start = new DateTime(2026, 10, 27, 21, 0, 0);
        var made = new List<string>();
        for (var i = 0; i < SaveBackups.Keep + 2; i++)
        {
            made.Add(SaveBackups.Create(Saves, Backups, start.AddMinutes(i)));
            File.SetLastWriteTimeUtc(made[^1], start.AddMinutes(i).ToUniversalTime());
        }

        var kept = SaveBackups.List(Backups);

        Assert.Equal(SaveBackups.Keep, kept.Count);
        Assert.Equal(made[^1], kept[0]);
        Assert.DoesNotContain(made[0], kept);
        Assert.DoesNotContain(made[1], kept);
    }

    [Fact]
    public void Two_backups_in_one_second_both_stay()
    {
        folder.File(@"Saves\Slot1.sav", "slot one");
        var now = new DateTime(2026, 10, 27, 21, 0, 0);

        var first = SaveBackups.Create(Saves, Backups, now);
        var second = SaveBackups.Create(Saves, Backups, now);

        Assert.NotEqual(first, second);
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
    }

    [Fact]
    public void A_restore_puts_back_exactly_the_backups_saves_and_backs_up_the_ones_of_now()
    {
        folder.File(@"Saves\Slot1.sav", "slot one");
        var backup = SaveBackups.Create(Saves, Backups, new DateTime(2026, 10, 27, 21, 0, 0));
        folder.File(@"Saves\Slot1.sav", "slot one, broken by a mod");
        folder.File(@"Saves\Slot2.sav", "slot two, made later");

        var before = SaveBackups.Restore(backup, Saves, Backups, new DateTime(2026, 10, 28, 9, 0, 0));

        Assert.Equal("slot one", File.ReadAllText(Path.Combine(Saves, "Slot1.sav")));
        Assert.False(File.Exists(Path.Combine(Saves, "Slot2.sav")));
        using (var zip = ZipFile.OpenRead(before!))
            Assert.Equal(["Slot1.sav", "Slot2.sav"], zip.Entries.Select(e => e.FullName).Order());
        Assert.Equal([Saves], Directory.GetDirectories(folder.Combine()).Where(d => d.StartsWith(Saves)));
    }

    [Fact]
    public void The_oldest_of_five_backups_can_be_restored()
    {
        folder.File(@"Saves\Slot1.sav", "oldest");
        var start = new DateTime(2026, 10, 27, 21, 0, 0);
        var oldest = SaveBackups.Create(Saves, Backups, start);
        File.SetLastWriteTimeUtc(oldest, start.ToUniversalTime());
        for (var i = 1; i < SaveBackups.Keep; i++)
        {
            folder.File(@"Saves\Slot1.sav", $"newer {i}");
            File.SetLastWriteTimeUtc(SaveBackups.Create(Saves, Backups, start.AddMinutes(i)), start.AddMinutes(i).ToUniversalTime());
        }

        SaveBackups.Restore(oldest, Saves, Backups, start.AddHours(1));

        Assert.Equal("oldest", File.ReadAllText(Path.Combine(Saves, "Slot1.sav")));
    }

    [Fact]
    public void A_restore_without_saves_yet_makes_the_save_folder()
    {
        folder.File(@"Other\Slot1.sav", "slot one");
        var backup = SaveBackups.Create(folder.Combine("Other"), Backups, new DateTime(2026, 10, 27, 21, 0, 0));

        Assert.Null(SaveBackups.Restore(backup, Saves, Backups, new DateTime(2026, 10, 28, 9, 0, 0)));

        Assert.Equal("slot one", File.ReadAllText(Path.Combine(Saves, "Slot1.sav")));
    }

    [Fact]
    public void A_save_in_use_stops_the_restore_and_the_saves_stay()
    {
        folder.File(@"Saves\Slot1.sav", "slot one");
        var backup = SaveBackups.Create(Saves, Backups, new DateTime(2026, 10, 27, 21, 0, 0));
        folder.File(@"Saves\Slot1.sav", "slot one, now");

        using (File.Open(Path.Combine(Saves, "Slot1.sav"), FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Throws<ModException>(() => SaveBackups.Restore(backup, Saves, Backups, new DateTime(2026, 10, 28, 9, 0, 0)));

        Assert.Equal("slot one, now", File.ReadAllText(Path.Combine(Saves, "Slot1.sav")));
        Assert.Equal([Saves], Directory.GetDirectories(folder.Combine()).Where(d => d.StartsWith(Saves)));
    }

    [Fact]
    public void A_missing_or_empty_save_folder_makes_no_backup()
    {
        Assert.False(SaveBackups.HasSaves(Saves));
        Assert.Throws<DirectoryNotFoundException>(() => SaveBackups.Create(Saves, Backups, DateTime.Now));
        Directory.CreateDirectory(Path.Combine(Saves, "Profiles"));
        Assert.False(SaveBackups.HasSaves(Saves));
        Assert.Throws<IOException>(() => SaveBackups.Create(Saves, Backups, DateTime.Now));
        Assert.Empty(SaveBackups.List(Backups));

        folder.File(@"Saves\Profiles\Profile.sav", "profile");
        Assert.True(SaveBackups.HasSaves(Saves));
    }

    [Fact]
    public void The_chosen_folder_wins_over_the_profile()
    {
        var profile = new GameProfile { Id = "g", DisplayName = "G", SaveFolder = @"%LOCALAPPDATA%\Game\Saved\SaveGames" };

        Assert.Equal(@"D:\Saves", SaveBackups.Folder(profile, null, @"D:\Saves"));
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Game", "Saved", "SaveGames"),
            SaveBackups.Folder(profile, null, null));
    }

    [Fact]
    public void A_save_folder_in_the_install_folder_is_found_once_the_game_is()
    {
        var profile = new GameProfile { Id = "g", DisplayName = "G", ProjectFolder = "TestGame", SaveFolder = "{Project}/Saved/SaveGames" };
        var game = GameLocator.Resolve(profile, folder.Game(), GameStore.Manual)!;

        Assert.True(SaveBackups.IsAvailable(profile));
        Assert.Null(SaveBackups.Folder(profile, null, null));
        Assert.Equal(Path.Combine(game.RootPath, "TestGame", "Saved", "SaveGames"), SaveBackups.Folder(profile, game, null));
        Assert.Equal(@"D:\Saves", SaveBackups.Folder(profile, game, @"D:\Saves"));
    }

    [Fact]
    public void Without_a_known_save_folder_there_are_no_save_backups()
    {
        var profile = new GameProfile { Id = "g", DisplayName = "G" };

        Assert.False(SaveBackups.IsAvailable(profile));
        Assert.Null(SaveBackups.Folder(profile, null, null));
        Assert.Null(SaveBackups.Folder(profile, null, @"D:\Saves"));
        Assert.False(new AppSettings().BackUpSavesOnLaunch);
    }

    [Fact]
    public void Hides_the_user_name_in_shared_text()
    {
        var text = Privacy.HideUser(@"Game at C:\Users\Kintaro\Games\X; log D:\Backup\kintaro\a.txt; word Kintaro stays",
            @"C:\Users\Kintaro", "Kintaro");

        Assert.Equal(@"Game at %USERPROFILE%\Games\X; log D:\Backup\<user>\a.txt; word Kintaro stays", text);
    }
}

using System.IO.Compression;
using UEManager.Core.Games;
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

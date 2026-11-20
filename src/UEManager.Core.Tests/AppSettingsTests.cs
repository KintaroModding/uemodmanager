using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

public class AppSettingsTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"saveBackupKeep\":0}")]
    [InlineData("{\"saveBackupKeep\":-1}")]
    public void Old_or_invalid_retention_settings_keep_six(string json)
    {
        using var folder = new TestFolder();
        var paths = new AppPaths(folder.Combine("Data"));
        Directory.CreateDirectory(paths.Root);
        File.WriteAllText(paths.SettingsFile, json);

        Assert.Equal(6, AppSettings.Load(paths).SaveBackupKeep);
    }

    /// <summary>The Settings slider has 2 to 20 in steps of 2: an odd number rounds up, so no backup is lost by it.</summary>
    [Theory]
    [InlineData(1, 2)]
    [InlineData(5, 6)]
    [InlineData(7, 8)]
    [InlineData(20, 20)]
    [InlineData(25, 20)]
    [InlineData(1000, 20)]
    public void A_retention_count_snaps_to_the_sliders_steps(int set, int kept)
    {
        Assert.Equal(kept, new AppSettings { SaveBackupKeep = set }.SaveBackupKeep);
    }

    [Fact]
    public void The_save_backup_retention_count_survives_a_restart()
    {
        using var folder = new TestFolder();
        var paths = new AppPaths(folder.Combine("Data"));
        new AppSettings { SaveBackupKeep = 20 }.Save(paths);

        Assert.Equal(20, AppSettings.Load(paths).SaveBackupKeep);
    }

    [Fact]
    public void The_language_choice_survives_a_restart_and_old_settings_still_load()
    {
        using var folder = new TestFolder();
        var paths = new AppPaths(folder.Combine("Data"));
        Directory.CreateDirectory(paths.Root);
        File.WriteAllText(paths.SettingsFile, "{\"backUpSavesOnLaunch\":true}");

        var settings = AppSettings.Load(paths);
        Assert.Null(settings.Language);
        Assert.True(settings.BackUpSavesOnLaunch);

        settings.Language = "zh-CN";
        settings.Save(paths);

        Assert.Equal("zh-CN", AppSettings.Load(paths).Language);
        Assert.True(AppSettings.Load(paths).BackUpSavesOnLaunch);
        Assert.Null(AppSettings.Load(paths).Window);
    }

    [Fact]
    public void The_removed_donate_button_stays_removed_and_old_settings_keep_it()
    {
        using var folder = new TestFolder();
        var paths = new AppPaths(folder.Combine("Data"));
        Directory.CreateDirectory(paths.Root);
        File.WriteAllText(paths.SettingsFile, "{\"backUpSavesOnLaunch\":true}");

        var settings = AppSettings.Load(paths);
        Assert.False(settings.HideDonateButton);

        settings.HideDonateButton = true;
        settings.Save(paths);

        Assert.True(AppSettings.Load(paths).HideDonateButton);
    }

    [Fact]
    public void Smooth_scrolling_is_on_until_the_player_turns_it_off()
    {
        using var folder = new TestFolder();
        var paths = new AppPaths(folder.Combine("Data"));
        Directory.CreateDirectory(paths.Root);
        File.WriteAllText(paths.SettingsFile, "{\"backUpSavesOnLaunch\":true}");

        var settings = AppSettings.Load(paths);
        Assert.True(settings.SmoothScrolling);

        settings.SmoothScrolling = false;
        settings.Save(paths);

        Assert.False(AppSettings.Load(paths).SmoothScrolling);
    }

    [Fact]
    public void The_window_place_survives_a_restart()
    {
        using var folder = new TestFolder();
        var paths = new AppPaths(folder.Combine("Data"));
        var settings = new AppSettings { Window = new WindowPlacement(120.5, 40, 1400, 900, true) };

        settings.Save(paths);

        Assert.Equal(new WindowPlacement(120.5, 40, 1400, 900, true), AppSettings.Load(paths).Window);
    }

    [Theory]
    [InlineData("{}", 100)]
    [InlineData("{\"uiScale\":0}", 100)]
    [InlineData("{\"uiScale\":-5}", 100)]
    [InlineData("{\"uiScale\":50}", 85)]
    [InlineData("{\"uiScale\":102}", 100)]
    [InlineData("{\"uiScale\":103}", 105)]
    [InlineData("{\"uiScale\":125}", 125)]
    [InlineData("{\"uiScale\":200}", 140)]
    public void Old_or_out_of_range_ui_scale_is_normalized(string json, int expected)
    {
        using var folder = new TestFolder();
        var paths = new AppPaths(folder.Combine("Data"));
        Directory.CreateDirectory(paths.Root);
        File.WriteAllText(paths.SettingsFile, json);

        Assert.Equal(expected, AppSettings.Load(paths).UiScale);
    }

    [Fact]
    public void The_font_and_ui_scale_choices_survive_a_restart()
    {
        using var folder = new TestFolder();
        var paths = new AppPaths(folder.Combine("Data"));
        var settings = new AppSettings { Font = "$system", UiScale = 125 };

        settings.Save(paths);

        var loaded = AppSettings.Load(paths);
        Assert.Equal("$system", loaded.Font);
        Assert.Equal(125, loaded.UiScale);
    }
}

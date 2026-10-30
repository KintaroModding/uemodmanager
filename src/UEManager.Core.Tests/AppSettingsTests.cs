using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

public class AppSettingsTests
{
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
    }
}

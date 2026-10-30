using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UEManager.Core.Localization;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

[CollectionDefinition("Language", DisableParallelization = true)]
public sealed class LanguageCollection;

[Collection("Language")]
public class LocalizationTests
{
    [Fact]
    public void Every_translation_preserves_its_format_arguments_and_has_an_English_fallback()
    {
        var assembly = typeof(Localizer).Assembly;
        var resources = assembly.GetManifestResourceNames().Where(n => n.EndsWith(".Localization.Strings.json")).ToList();
        Assert.NotEmpty(resources);
        var english = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in resources)
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var document = JsonDocument.Parse(stream);
            foreach (var entry in document.RootElement.EnumerateObject())
            {
                var en = entry.Value.GetProperty("en").GetString()!;
                var zh = entry.Value.GetProperty("zh-CN").GetString()!;
                Assert.True(english.Add(en), $"Duplicate English resource: {en}");
                Assert.False(string.IsNullOrWhiteSpace(zh));
                Assert.Equal(Placeholders(en), Placeholders(zh));
                Assert.Equal(CompositeFormat.Parse(en).MinimumArgumentCount, CompositeFormat.Parse(zh).MinimumArgumentCount);
            }
        }

        static string[] Placeholders(string value) => Regex.Matches(value, @"\{\d+(?:,-?\d+)?(?::[^{}]+)?\}")
            .Select(m => m.Value).Order(StringComparer.Ordinal).ToArray();
    }

    [Fact]
    public void Switching_both_ways_updates_keys_formats_culture_and_notifications()
    {
        var originalLanguage = Localizer.Current.Language;
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        var originalDefault = CultureInfo.DefaultThreadCurrentCulture;
        var originalDefaultUi = CultureInfo.DefaultThreadCurrentUICulture;
        var notifications = new List<string?>();
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => notifications.Add(e.PropertyName);
        Localizer.Current.PropertyChanged += Changed;
        try
        {
            Localizer.Current.SetLanguage("en");
            Assert.Equal("Settings", Localizer.T("Settings"));
            notifications.Clear();
            Localizer.Current.SetLanguage("zh-CN");
            Assert.Equal("设置", Localizer.T("Settings"));
            Assert.Equal("设置", Localizer.Current["Text.74a883a037bc"]);
            Assert.Equal("zh-CN", CultureInfo.CurrentCulture.Name);
            Assert.Contains("Language", notifications);
            Assert.Contains("Item[]", notifications);
            const string userName = @"D:\Mods\English {name} 中文";
            Assert.Equal($"正在安装 {userName}…", Localizer.F($"Installing {userName}…"));
            Assert.Equal("User supplied text {0}", Localizer.T("User supplied text {0}"));
            Assert.Equal("1.25", Localizer.F(FormattableStringFactory.Create("{0:0.00}", 1.25)));
            var failure = ModException.Localized($"The newest release could not be read: {userName}");
            var missing = new ModException("No release is published yet.");
            Assert.Equal($"无法读取最新发布版本：{userName}", failure.Message);
            Assert.Equal("尚无发布版本。", missing.Message);
            Localizer.Current.SetLanguage("en");
            Assert.Equal("Settings", Localizer.T("Settings"));
            Assert.Equal($"Installing {userName}…", Localizer.F($"Installing {userName}…"));
            Assert.Equal($"The newest release could not be read: {userName}", failure.Message);
            Assert.Equal("No release is published yet.", missing.Message);
        }
        finally
        {
            Localizer.Current.PropertyChanged -= Changed;
            Localizer.Current.SetLanguage(originalLanguage);
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
            CultureInfo.DefaultThreadCurrentCulture = originalDefault;
            CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUi;
        }
    }

    [Theory]
    [InlineData("zh-CN", "zh-CN")]
    [InlineData("zh-Hans", "zh-CN")]
    [InlineData("en-US", "en")]
    [InlineData("unknown-language", "en")]
    public void Resolves_supported_and_unknown_languages(string language, string expected) =>
        Assert.Equal(expected, Localizer.ResolveLanguage(language));

    [Fact]
    public void Language_choice_survives_a_restart_and_old_settings_still_load()
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
        settings.Language = "en";
        settings.Save(paths);
        Assert.Equal("en", AppSettings.Load(paths).Language);
    }
}

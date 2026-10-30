using System.ComponentModel;
using System.Globalization;
using UEManager.Core.Mods;
using Xunit;
using L = UEManager.UI.Localizer;

namespace UEManager.UI.Tests;

/// <summary>The tests that switch the app's one language run one after another.</summary>
[CollectionDefinition("Language", DisableParallelization = true)]
public sealed class LanguageCollection;

[Collection("Language")]
public class LocalizerTests
{
    [Fact]
    public void The_languages_are_English_and_one_per_file()
    {
        Assert.Equal(L.English, L.Current.Languages[0].Code);
        Assert.Contains(L.Current.Languages, language => language is { Code: "zh-CN", Name: "简体中文" });
    }

    [Theory]
    [InlineData("zh-CN", "zh-CN")]
    [InlineData("zh-SG", "zh-CN")]
    [InlineData("zh-TW", "zh-CN")]
    [InlineData("en-GB", "en")]
    [InlineData("de-DE", "en")]
    public void Windows_gets_the_closest_language_the_app_has(string windows, string expected) =>
        Assert.Equal(expected, L.Current.Closest(CultureInfo.GetCultureInfo(windows)));

    [Fact]
    public void A_saved_language_wins_while_the_app_has_it_else_English()
    {
        Assert.Equal("zh-CN", L.Current.Resolve("zh-CN"));
        Assert.Equal("en", L.Current.Resolve("en"));
        Assert.Equal("en", L.Current.Resolve(null));
        Assert.Equal("en", L.Current.Resolve("xx-unknown"));
    }

    [Theory]
    [InlineData("zh-CN", "zh-CN")]
    [InlineData("en-US,zh-TW", "zh-CN")]
    [InlineData("en-US,de-DE,en-GB", null)]
    public void Offers_the_language_of_Windows_when_the_app_has_one_besides_English(string windows, string? offered) =>
        Assert.Equal(offered, L.Current.Offer(windows.Split(',').Select(CultureInfo.GetCultureInfo))?.Code);

    [Fact]
    public void The_offer_is_in_that_language_and_in_English()
    {
        var chinese = L.Current.Languages.Single(language => language.Code == "zh-CN");

        Assert.Equal("Simplified Chinese", chinese.EnglishName);
        Assert.Equal("语言", L.In("zh-CN", "Language"));
        Assert.Equal("Language", L.In("xx-unknown", "Language"));
        Assert.Equal("是否将程序切换为简体中文？\nSwitch the app to Simplified Chinese?", L.Both(chinese, "Switch the app to {0}?"));
    }

    [Fact]
    public void Texts_follow_the_language_while_the_log_stays_English()
    {
        var original = L.Current.Language;
        var changes = 0;
        PropertyChangedEventHandler counted = (_, e) => changes += e.PropertyName == nameof(L.Language) ? 1 : 0;
        L.Current.PropertyChanged += counted;
        try
        {
            L.Current.SetLanguage(L.English);
            changes = 0;
            L.Current.SetLanguage("zh-CN");

            Assert.Equal(1, changes);
            Assert.Equal("设置", L.T("Settings"));
            Assert.Equal("Not a text of the app", L.T("Not a text of the app"));
            const string name = @"D:\Mods\{name} 中文";
            Assert.Equal($"正在启动 {name}…", L.F($"Starting {name}…"));
            Assert.Equal("已禁用 3 个模组", L.Plural(3, "{0} mod turned off", "{0} mods turned off"));
            Assert.Equal("尚无可备份的存档。启动一次游戏后即可显示版本。",
                L.Join(L.T("There are no saves to back up yet."), null, L.T("Start the game once to show its version.")));
            var failure = new ModException($"The newest release could not be read: {name}");
            Assert.Equal($"无法读取最新发布版本：{name}", L.Message(failure));
            Assert.Equal($"The newest release could not be read: {name}", failure.Message);

            L.Current.SetLanguage(L.English);

            Assert.Equal(2, changes);
            Assert.Equal("Settings", L.T("Settings"));
            Assert.Equal("1 mod turned off", L.Plural(1, "{0} mod turned off", "{0} mods turned off"));
            Assert.Equal("There are no saves to back up yet. Start the game once to show its version.",
                L.Join(L.T("There are no saves to back up yet."), "", L.T("Start the game once to show its version.")));
            Assert.Equal(failure.Message, L.Message(failure));
        }
        finally
        {
            L.Current.PropertyChanged -= counted;
            L.Current.SetLanguage(original);
        }
    }
}

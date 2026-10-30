using System.IO;
using UEManager.Core.Games;
using UEManager.Core.Storage;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;
using Xunit;

namespace UEManager.UI.Tests;

/// <summary>The question that offers the language of Windows to a player who chose none yet.</summary>
[Collection("Language")]
public class LanguageOfferTests
{
    [Fact]
    public void Yes_switches_to_the_offered_language_and_saves_it() => OnUiThread(model =>
    {
        var answer = model.OfferLanguageAsync(Chinese);

        Assert.Equal("语言  ·  Language", model.Confirm!.Title);
        Assert.Equal("是否将程序切换为简体中文？\nSwitch the app to Simplified Chinese?\n\n"
            + "以后可以在“设置 ▸ 语言”中更改。\nYou can change this later in Settings ▸ Language.", model.Confirm.Message);
        Assert.Equal("Yes", model.Confirm.ConfirmText);
        Assert.Equal("No", model.Confirm.CancelText);
        model.Confirm.AcceptCommand.Execute(null);

        Assert.True(answer.Wait(TimeSpan.FromSeconds(5)));
        Assert.Null(model.Confirm);
        Assert.Equal("zh-CN", Localizer.Current.Language);
        return "zh-CN";
    });

    [Fact]
    public void No_keeps_English_and_saves_it_so_the_question_comes_once() => OnUiThread(model =>
    {
        var answer = model.OfferLanguageAsync(Chinese);
        model.Confirm!.CancelCommand.Execute(null);

        Assert.True(answer.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(Localizer.English, Localizer.Current.Language);
        return Localizer.English;
    });

    private static LanguageOption Chinese => Localizer.Current.Languages.Single(language => language.Code == "zh-CN");

    /// <summary>Runs a check on a WPF thread with a fresh data folder; the check returns the language it expects saved.</summary>
    private static void OnUiThread(Func<MainViewModel, string> check) => UiThread.Run(() =>
    {
        var original = Localizer.Current.Language;
        var folder = Path.Combine(Path.GetTempPath(), "uemm-offer-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Localizer.Current.SetLanguage(Localizer.English);
            var paths = new AppPaths(folder);
            paths.EnsureWritable();
            var model = new MainViewModel(new GameProfile { Id = "test", DisplayName = "Phantom Blade Zero" }, paths, new ArtSet(null, null));
            var saved = check(model);
            Assert.Equal(saved, AppSettings.Load(paths).Language);
        }
        finally
        {
            Localizer.Current.SetLanguage(original);
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    });
}

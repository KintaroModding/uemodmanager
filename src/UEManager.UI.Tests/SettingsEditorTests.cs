using System.IO;
using System.Text;
using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;
using Xunit;

namespace UEManager.UI.Tests;

/// <summary>The app's own editor for a mod's settings files.</summary>
[Collection("Language")]
public class SettingsEditorTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "uemm-editor-test-" + Guid.NewGuid().ToString("N"));

    public SettingsEditorTests() => Directory.CreateDirectory(folder);

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private string Config => Path.Combine(folder, "config.lua");

    [Theory]
    [InlineData("UTF-8")]
    [InlineData("UTF-8 with its mark")]
    [InlineData("UTF-16")]
    [InlineData("an old code page")]
    public void A_save_keeps_the_files_encoding_and_line_breaks(string kind)
    {
        Encoding encoding = kind switch
        {
            "UTF-8" => new UTF8Encoding(false),
            "UTF-8 with its mark" => new UTF8Encoding(true),
            "UTF-16" => Encoding.Unicode,
            _ => Encoding.Latin1,
        };
        var path = Path.Combine(folder, "settings.txt");
        File.WriteAllText(path, "; Größe\r\nMultiplier=2\n", encoding);
        var before = File.ReadAllBytes(path);

        var read = TextFile.Read(path);
        TextFile.Write(path, read.Text, read.Encoding);

        Assert.Equal("; Größe\r\nMultiplier=2\n", read.Text);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(folder));
    }

    [Theory]
    [InlineData("config.lua", "Lua")]
    [InlineData("Highlight.json", "JSON")]
    [InlineData("PBZBuffDuration.txt", "Settings")]
    [InlineData("settings.ini", "Settings")]
    public void Every_file_type_gets_its_colours(string file, string colours) =>
        UiThread.Run(() => Assert.Equal(colours, Syntax.For(file).Name));

    [Fact]
    public void The_settings_button_opens_the_mods_file_in_the_game() => WithModel(model =>
    {
        model.EditSettings(Row(model));

        var editor = Assert.IsType<SettingsEditorViewModel>(model.SettingsEditor);
        Assert.Equal("Witch", editor.ModName);
        Assert.Equal("Scripts/config.lua", editor.SelectedFile.Name);
        Assert.Equal("speed = 1\n", editor.Document.Text);
        Assert.False(editor.IsModified);
        Assert.False(editor.SaveCommand.CanExecute(null));
    });

    [Fact]
    public void Save_writes_the_file_and_the_editor_stays_open() => WithModel(model =>
    {
        model.EditSettings(Row(model));
        var editor = model.SettingsEditor!;

        editor.Document.Text = "speed = 5\n";
        Assert.True(editor.IsModified);
        editor.SaveCommand.Execute(null);

        Assert.Equal("speed = 5\n", File.ReadAllText(Config));
        Assert.False(editor.IsModified);
        Assert.NotNull(editor.SavedText);
        Assert.Same(editor, model.SettingsEditor);
    });

    [Fact]
    public void Close_with_changes_asks_and_each_answer_does_what_it_says() => WithModel(model =>
    {
        model.EditSettings(Row(model));
        var editor = model.SettingsEditor!;
        editor.Document.Text = "speed = 5\n";

        editor.CloseCommand.Execute(null);
        Assert.True(editor.IsAsking);
        editor.KeepEditingCommand.Execute(null);
        Assert.False(editor.IsAsking);
        Assert.Same(editor, model.SettingsEditor);

        editor.CloseCommand.Execute(null);
        editor.DiscardCommand.Execute(null);
        Assert.Null(model.SettingsEditor);
        Assert.Equal("speed = 1\n", File.ReadAllText(Config));

        model.EditSettings(Row(model));
        editor = model.SettingsEditor!;
        editor.Document.Text = "speed = 9\n";
        var windowClosed = false;
        editor.RequestClose(() => windowClosed = true);
        editor.SaveAndCloseCommand.Execute(null);
        Assert.Null(model.SettingsEditor);
        Assert.Equal("speed = 9\n", File.ReadAllText(Config));
        Assert.True(windowClosed);
    });

    [Fact]
    public void A_file_in_use_keeps_the_changes_and_says_why() => WithModel(model =>
    {
        model.EditSettings(Row(model));
        var editor = model.SettingsEditor!;
        editor.Document.Text = "speed = 5\n";

        using (File.Open(Config, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            editor.CloseCommand.Execute(null);
            editor.SaveAndCloseCommand.Execute(null);
        }

        Assert.Same(editor, model.SettingsEditor);
        Assert.False(editor.IsAsking);
        Assert.True(editor.IsModified);
        Assert.NotNull(editor.Error);
        Assert.Equal("speed = 1\n", File.ReadAllText(Config));
    });

    /// <summary>A UE4SS mod's row in the Mods list, enabled, its config.lua in the game.</summary>
    private ModItemViewModel Row(MainViewModel model)
    {
        if (!File.Exists(Config))
            File.WriteAllText(Config, "speed = 1\n");
        return new ModItemViewModel(model, new ModRecord
        {
            Id = "witch",
            Name = "Witch",
            Enabled = true,
            Files =
            [
                new ModFileRecord { Target = DeployTarget.Ue4ssMods, FileName = "Witch/Scripts/main.lua", DeployedPath = Path.Combine(folder, "main.lua") },
                new ModFileRecord { Target = DeployTarget.Ue4ssMods, FileName = "Witch/Scripts/config.lua", DeployedPath = Config },
            ],
        }, Path.Combine(folder, "library"));
    }

    private void WithModel(Action<MainViewModel> check) => UiThread.Run(() =>
    {
        var paths = new AppPaths(Path.Combine(folder, "Data"));
        paths.EnsureWritable();
        check(new MainViewModel(new GameProfile { Id = "test", DisplayName = "Phantom Blade Zero" }, paths, new ArtSet(null, null)));
    });
}

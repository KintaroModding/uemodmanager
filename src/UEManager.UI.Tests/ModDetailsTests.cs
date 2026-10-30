using System.IO;
using UEManager.Core.Mods;
using UEManager.UI.ViewModels;
using Xunit;

namespace UEManager.UI.Tests;

/// <summary>The state of each file in a mod's details, with the same tones as the Mods list.</summary>
[Collection("Language")]
public class ModDetailsTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "uemm-details-test-" + Guid.NewGuid().ToString("N"));

    public ModDetailsTests() => Directory.CreateDirectory(folder);

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private ModFileRecord Deployed(string name, string content, long recordedSize)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, content);
        return new ModFileRecord { FileName = name, Target = DeployTarget.Mods, Size = recordedSize, DeployedPath = path };
    }

    [Fact]
    public void Each_file_shows_whether_it_is_in_the_game_as_installed()
    {
        var mod = new ModRecord { Name = "Speaker Highlight", Enabled = true };

        Assert.Equal(("In the game", "good"), ModDetailsViewModel.StateOf(mod, Deployed("a_P.pak", "pak", 3)));
        Assert.Equal(("Changed outside the manager", "bad"), ModDetailsViewModel.StateOf(mod, Deployed("b_P.pak", "pak", 99)));
        Assert.Equal(("In the game: settings you may change", "good"), ModDetailsViewModel.StateOf(mod, Deployed("Speaker.json", "{ }", 99)));
        var gone = Deployed("c_P.pak", "pak", 3);
        File.Delete(gone.DeployedPath!);
        Assert.Equal(("Missing", "warn"), ModDetailsViewModel.StateOf(mod, gone));
    }

    [Fact]
    public void A_disabled_mod_has_no_files_in_the_game()
    {
        var mod = new ModRecord { Name = "Speaker Highlight", Enabled = false };
        var file = new ModFileRecord { FileName = "a_P.pak", Target = DeployTarget.Mods, Size = 3 };

        Assert.Equal(("Not in the game", ""), ModDetailsViewModel.StateOf(mod, file));
    }
}

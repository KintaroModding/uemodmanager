using UEManager.Core.Games;

namespace UEManager.Core.Tests;

/// <summary>A temporary folder that is deleted after the test.</summary>
internal sealed class TestFolder : IDisposable
{
    public TestFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "uemm-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public string File(string relative, string content = "data")
    {
        var path = Combine(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    public static GameProfile Profile(string? projectFolder = "TestGame") => new()
    {
        Id = "test-game",
        DisplayName = "Test Game",
        SteamAppId = "999999",
        ProjectFolder = projectFolder,
    };

    /// <summary>
    /// Creates a fake Unreal install: Root/Engine, Root/Project/Content/Paks, the shipping
    /// executable in Root/Project/Binaries/Win64, and a launcher executable.
    /// </summary>
    /// <param name="name">The install folder below the test folder; a second name makes a second install.</param>
    public string Game(string project = "TestGame", string name = "Game")
    {
        var root = Combine(name);
        Directory.CreateDirectory(System.IO.Path.Combine(root, "Engine", "Binaries", "Win64"));
        Directory.CreateDirectory(System.IO.Path.Combine(root, project, "Content", "Paks"));
        Directory.CreateDirectory(System.IO.Path.Combine(root, project, "Binaries", "Win64"));
        System.IO.File.WriteAllText(System.IO.Path.Combine(root, project, "Binaries", "Win64", project + "-Win64-Shipping.exe"), "");
        System.IO.File.WriteAllText(System.IO.Path.Combine(root, project + ".exe"), "");
        return root;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

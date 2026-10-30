using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

/// <summary>Clear on the UE4SS page deletes UE4SS's log, so the next game start begins a fresh one.</summary>
public class Ue4ssLogClearTests : IDisposable
{
    private readonly TestFolder folder = new();
    private readonly GameInstall game;
    private readonly ModManager manager;
    private bool gameRunning;

    public Ue4ssLogClearTests()
    {
        game = GameLocator.Resolve(TestFolder.Profile(), folder.Game(), GameStore.Manual)!;
        manager = new ModManager(game, new AppPaths(folder.Combine("Data")), _ => gameRunning);
    }

    public void Dispose() => folder.Dispose();

    /// <summary>UE4SS 3.x next to the game's exe, with a log of the last game start.</summary>
    private string WriteLog()
    {
        var log = Ue4ssRuntime.Detect(game).LogPath;
        Directory.CreateDirectory(Path.GetDirectoryName(log)!);
        File.WriteAllText(Path.Combine(game.BinariesPath, "dwmapi.dll"), "proxy");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(log)!, "UE4SS.dll"), "runtime");
        File.WriteAllText(log, "[12:00:00.001] UE4SS - v3.0.1\n[12:00:01.002] [Lua] Error: [Witch] main.lua:12\n");
        return log;
    }

    [Fact]
    public void Deletes_the_log_and_the_page_shows_none()
    {
        var log = WriteLog();
        Assert.True(Ue4ssRuntime.Detect(game).Installed);

        Assert.True(manager.ClearUe4ssLog());

        Assert.False(File.Exists(log));
        Assert.Null(Ue4ssLog.Read(log));
        Assert.True(File.Exists(Path.Combine(game.BinariesPath, "dwmapi.dll")));
        Assert.False(manager.ClearUe4ssLog());
    }

    [Fact]
    public void Waits_while_the_game_runs()
    {
        var log = WriteLog();
        gameRunning = true;

        Assert.Throws<ModException>(() => manager.ClearUe4ssLog());

        Assert.True(File.Exists(log));
    }
}

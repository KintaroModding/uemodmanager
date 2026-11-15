using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

/// <summary>
/// The files a mod leaves in the game after an uninstall, such as logs it wrote while the game ran: the uninstall
/// reports them, keeps them, and can move them into the backups.
/// </summary>
public class LeftoversTests : IDisposable
{
    private readonly TestFolder folder = new();
    private readonly GameInstall game;
    private readonly ModManager manager;

    public LeftoversTests()
    {
        game = GameLocator.Resolve(TestFolder.Profile(), folder.Game(), GameStore.Manual)!;
        manager = new ModManager(game, new AppPaths(folder.Combine("Data")), _ => false);
    }

    public void Dispose() => folder.Dispose();

    private ModRecord InstallUe4ssMod(string name)
    {
        folder.File($@"Downloads\{name}\ue4ss\Mods\{name}\enabled.txt", "");
        folder.File($@"Downloads\{name}\ue4ss\Mods\{name}\dlls\main.dll", "dll");
        using var preview = manager.Inspect(folder.Combine("Downloads", name));
        return manager.Install(preview, preview.Packages.ToList(), name, "1.0");
    }

    /// <summary>A log the mod writes while the game runs: not part of its download, so its record does not own it.</summary>
    private string WriteRuntimeLog(ModRecord mod, string name = "compatibility.log", string text = "Verified UE4SS ...")
    {
        var deployed = Path.GetDirectoryName(mod.Files.First(file => file.DeployedPath is not null).DeployedPath!)!;
        var path = Path.Combine(deployed, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void A_log_the_mod_wrote_is_left_behind_and_reported()
    {
        var mod = InstallUe4ssMod("Bypass");
        var log = WriteRuntimeLog(mod);

        var leftovers = manager.LeftoversOf(mod.Id);

        var leftover = Assert.Single(leftovers);
        Assert.Equal(log, leftover.Path);
        Assert.True(leftover.IsLog);
        Assert.Contains("compatibility.log", leftover.RelativePath);
    }

    [Fact]
    public void Uninstalling_keeps_the_file_and_returns_it()
    {
        var mod = InstallUe4ssMod("Bypass");
        var log = WriteRuntimeLog(mod);

        var result = manager.Uninstall(mod.Id);

        Assert.True(File.Exists(log));
        Assert.Null(result.LeftoverBackup);
        Assert.Single(result.Leftovers);
        Assert.Empty(manager.Mods);
    }

    [Fact]
    public void The_files_can_be_moved_into_the_backups()
    {
        var mod = InstallUe4ssMod("Bypass");
        var log = WriteRuntimeLog(mod);
        var data = WriteRuntimeLog(mod, "dlls/state.bin", "data");

        var result = manager.Uninstall(mod.Id, moveLeftovers: true);

        Assert.NotNull(result.LeftoverBackup);
        Assert.False(File.Exists(log));
        Assert.False(File.Exists(data));
        Assert.False(Directory.Exists(Path.GetDirectoryName(log)!));
        var moved = Directory.GetFiles(result.LeftoverBackup!, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetFileName(file)!)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.Equal(["compatibility.log", "state.bin"], moved);
    }

    [Fact]
    public void Files_in_a_shared_folder_are_the_players()
    {
        folder.File(@"Downloads\Sharp\Sharp_P.pak", "pak");
        using var preview = manager.Inspect(folder.Combine("Downloads", "Sharp"));
        var mod = manager.Install(preview, preview.Packages.ToList(), "Sharp", null);
        var shared = Path.Combine(game.ModsPath, "SomethingElse_P.pak");
        File.WriteAllText(shared, "not ours");

        Assert.Empty(manager.LeftoversOf(mod.Id));
    }

    [Fact]
    public void Another_mods_files_are_not_left_behind()
    {
        var first = InstallUe4ssMod("First");
        InstallUe4ssMod("Second");

        Assert.Empty(manager.LeftoversOf(first.Id));
    }
}
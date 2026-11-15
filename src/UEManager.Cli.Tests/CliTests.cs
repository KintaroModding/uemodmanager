using System.IO.Compression;
using System.Text.Json;
using UEManager.Cli;
using UEManager.Core.Storage;

namespace UEManager.Cli.Tests;

/// <summary>
/// The command line tool, run in this process against a fake Phantom Blade Zero install and a data folder of its own.
/// The tests take over the console's output, so every class of them is in one collection (<see cref="CliConsole"/>)
/// and they run one after the other.
/// </summary>
[Collection(CliConsole.Name)]
public sealed class CliTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "uemm-cli-tests", Guid.NewGuid().ToString("N"));

    private string Data => Path.Combine(root, "Data");

    private string GameRoot => Path.Combine(root, "Game");

    private string ModsFolder => Path.Combine(GameRoot, "ProjectPBZ", "Content", "Paks", "~mods");

    public CliTests()
    {
        // What the game's profile needs to know an install: the Engine folder, the project folder and the exe.
        Directory.CreateDirectory(Path.Combine(GameRoot, "Engine"));
        Directory.CreateDirectory(Path.Combine(GameRoot, "ProjectPBZ", "Content", "Paks"));
        Write(Path.Combine(GameRoot, "platform", "steam", "PBZ.exe"), "the game");
        Directory.CreateDirectory(Data);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // The log file may still be open; the temp folder is cleaned by Windows.
        }
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    /// <summary>Runs one command with its output caught, as a script would see it.</summary>
    private static (int Code, string Out, string Error) Raw(params string[] args)
    {
        var (output, error) = (new StringWriter(), new StringWriter());
        var (oldOutput, oldError) = (Console.Out, Console.Error);
        Console.SetOut(output);
        Console.SetError(error);
        try
        {
            return (Program.Main(args), output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetOut(oldOutput);
            Console.SetError(oldError);
        }
    }

    private (int Code, string Out, string Error) Run(params string[] args) =>
        Raw([.. args, "--data-dir", Data, "--game", "phantom-blade-zero", "--game-root", GameRoot]);

    private string Download()
    {
        var folder = Path.Combine(root, "Downloads", "MyMod");
        Write(Path.Combine(folder, "MyMod_P.pak"), "a pak");
        return folder;
    }

    /// <summary>A zip download at <paramref name="fileName"/>, with the given files below its top.</summary>
    private string Zip(string fileName, params (string Path, string Text)[] files)
    {
        var path = Path.Combine(root, "Downloads", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, text) in files)
        {
            var entry = archive.CreateEntry(name.Replace('\\', '/'));
            using var writer = new StreamWriter(entry.Open());
            writer.Write(text);
        }
        return path;
    }

    [Fact]
    public void A_change_waits_while_the_app_holds_the_data_folder()
    {
        // The app holds this lock for as long as it runs.
        using (var app = DataFolderLock.TryTake(new AppPaths(Data)))
        {
            Assert.NotNull(app);
            var (code, _, error) = Run("install", Download(), "--yes");

            Assert.Equal(1, code);
            Assert.Contains("UE MM is open", error);
            Assert.False(Directory.Exists(ModsFolder));
        }

        // Once the app is closed, the same command works, and gives the folder free again at its end.
        Assert.Equal(0, Run("install", Download(), "--yes").Code);
        using var again = DataFolderLock.TryTake(new AppPaths(Data));
        Assert.NotNull(again);
    }

    [Fact]
    public void Without_a_data_folder_it_says_where_one_belongs()
    {
        var (code, _, error) = Raw("list");

        Assert.Equal(1, code);
        Assert.Contains("--data-dir", error);
        Assert.Contains("UEModManager.exe", error);
        Assert.DoesNotContain(@"C:\Games", error);
    }

    [Fact]
    public void Updates_is_no_command_any_more()
    {
        // It read files the app never writes: the app keeps the mod pages' versions in memory only.
        var (code, _, error) = Run("updates");

        Assert.Equal(1, code);
        Assert.Contains("Unknown command", error);
    }

    [Fact]
    public void The_cli_version_is_still_printed_for_version_alone()
    {
        var (code, output, _) = Raw("--version");

        Assert.Equal(0, code);
        Assert.Contains("UEModManager --cli", output);
        // After a command too: --version has one meaning (ue4ss install takes --ue4ss-version).
        var (afterCode, afterOutput, _) = Raw("list", "--version");
        Assert.Equal(0, afterCode);
        Assert.Contains("UEModManager --cli", afterOutput);
    }

    [Fact]
    public void A_mod_installs_after_its_plan_and_turns_off_on_and_goes()
    {
        var pak = Path.Combine(ModsFolder, "MyMod_P.pak");

        // Without --yes the plan only: exit code 2, nothing in the game.
        var plan = Run("install", Download());
        Assert.Equal(2, plan.Code);
        Assert.Contains("\"canInstall\": true", plan.Out);
        Assert.False(File.Exists(pak));

        Assert.Equal(0, Run("install", Download(), "--yes").Code);
        Assert.True(File.Exists(pak));
        Assert.Contains("MyMod", Run("list", "--json").Out);

        Assert.Equal(0, Run("disable", "MyMod").Code);
        Assert.False(File.Exists(pak));
        Assert.Equal(0, Run("enable", "MyMod").Code);
        Assert.True(File.Exists(pak));

        Assert.Equal(2, Run("uninstall", "MyMod").Code);
        Assert.True(File.Exists(pak));
        Assert.Equal(0, Run("uninstall", "MyMod", "--yes").Code);
        Assert.False(File.Exists(pak));
        Assert.Contains("0 mod(s)", Run("list").Out);
    }

    [Fact]
    public void A_download_from_the_same_page_installs_beside_the_mod_with_keep_both()
    {
        // The same mod page, twice: both downloads hold MyMod_P.pak, the second is a newer file of that page.
        var first = Zip("MyMod-77-1-0-1730000000.zip", ("MyMod_P.pak", "the first pak"));
        var second = Zip("MyMod-77-2-0-1730000001.zip", ("MyMod_P.pak", "the second pak"));
        var firstPak = Path.Combine(ModsFolder, "MyMod_P.pak");

        Assert.Equal(0, Run("install", first, "--yes").Code);
        Assert.Equal("the first pak", File.ReadAllText(firstPak));

        // Without --keep-both the plan would replace the installed mod.
        var replace = Run("install", second);
        Assert.Equal(2, replace.Code);
        Assert.Contains("\"replaces\": {", replace.Out);

        // With --keep-both the plan replaces nothing and gives the new mod a folder of its own below ~mods.
        var plan = Run("install", second, "--keep-both");
        Assert.Equal(2, plan.Code);
        Assert.Contains("\"keepBoth\": true", plan.Out);
        Assert.Contains("\"ownFolder\": \"MyMod\"", plan.Out);
        Assert.Contains("\"replaces\": null", plan.Out);

        Assert.Equal(0, Run("install", second, "--keep-both", "--yes").Code);

        // Two mods, the first file untouched, the second in its own folder.
        var secondPak = Path.Combine(ModsFolder, "MyMod", "MyMod_P.pak");
        Assert.Equal("the first pak", File.ReadAllText(firstPak));
        Assert.Equal("the second pak", File.ReadAllText(secondPak));
        using var list = JsonDocument.Parse(Run("list", "--json").Out);
        Assert.Equal(2, list.RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public void Another_file_of_the_same_page_is_kept_beside_the_mod_as_the_app_does()
    {
        // The same mod page, but no file in common: the download may be an optional file of that page. The app ticks
        // Keep both first then, and so does the tool, so a script never removes the mod the player has.
        var first = Zip("MyMod-77-1-0-1730000000.zip", ("MyMod_P.pak", "the main pak"));
        var optional = Zip("MyMod-77-1-0-1730000005.zip", ("MyMod_Extra_P.pak", "an optional pak"));
        Assert.Equal(0, Run("install", first, "--yes").Code);

        using (var plan = JsonDocument.Parse(Run("plan", optional).Out))
        {
            Assert.True(plan.RootElement.GetProperty("keepBoth").GetBoolean());
            Assert.Equal(JsonValueKind.Null, plan.RootElement.GetProperty("replaces").ValueKind);
        }

        var (code, output, _) = Run("install", optional, "--yes", "--json");

        Assert.Equal(0, code);
        using var installed = JsonDocument.Parse(output);
        var folder = installed.RootElement.GetProperty("ownFolder").GetString()!;
        Assert.True(installed.RootElement.GetProperty("keepBoth").GetBoolean());
        Assert.True(File.Exists(Path.Combine(ModsFolder, "MyMod_P.pak")));
        Assert.True(File.Exists(Path.Combine(ModsFolder, folder, "MyMod_Extra_P.pak")));
        using var list = JsonDocument.Parse(Run("list", "--json").Out);
        Assert.Equal(2, list.RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public void Keep_both_says_so_when_the_download_must_take_the_mods_place()
    {
        // A pak and a Lua mod: a folder of its own keeps the pak apart, but the Lua mod has one place in the game.
        (string, string)[] Files(string version) =>
        [
            ("Mixed_P.pak", $"pak {version}"),
            ("Mixed/Scripts/main.lua", $"-- main {version}"),
            ("Mixed/Scripts/a.lua", $"-- a {version}"),
            ("Mixed/Scripts/b.lua", $"-- b {version}"),
        ];
        var first = Zip("Mixed-91-1-0-1730000006.zip", Files("1"));
        var second = Zip("Mixed-91-2-0-1730000007.zip", Files("2"));
        Assert.Equal(0, Run("install", first, "--yes").Code);

        var (code, _, error) = Run("install", second, "--keep-both", "--yes");

        Assert.Equal(1, code);
        Assert.Contains("cannot keep", error);
        Assert.Equal("pak 1", File.ReadAllText(Path.Combine(ModsFolder, "Mixed_P.pak")));
        // Without --keep-both it is the mod's update.
        Assert.Equal(0, Run("install", second, "--yes").Code);
        Assert.Equal("pak 2", File.ReadAllText(Path.Combine(ModsFolder, "Mixed_P.pak")));
    }

    [Fact]
    public void A_part_name_that_several_parts_share_asks_for_more()
    {
        var download = Zip("Speed-92-1-0-1730000008.zip", ("x2/Speed_P.pak", "x2"), ("x3/Speed_P.pak", "x3"));

        var (code, _, error) = Run("plan", download, "--part", "Speed_P");

        Assert.Equal(1, code);
        Assert.Contains("More than one part matches 'Speed_P'", error);
        // The variant names one, and so does its folder/name.
        foreach (var name in new[] { "x3", "x3/Speed_P" })
        {
            using var plan = JsonDocument.Parse(Run("plan", download, "--part", name).Out);
            var parts = plan.RootElement.GetProperty("parts");
            Assert.False(parts[0].GetProperty("selected").GetBoolean());
            Assert.True(parts[1].GetProperty("selected").GetBoolean());
        }
    }

    [Fact]
    public void Part_picks_the_named_parts_of_a_download()
    {
        var download = Zip("TwoParts-88-1-0-1730000002.zip", ("A_P.pak", "a pak"), ("B_P.pak", "b pak"));
        var a = Path.Combine(ModsFolder, "A_P.pak");
        var b = Path.Combine(ModsFolder, "B_P.pak");

        // The default selection takes both parts, and the plan numbers them from 1.
        using (var plan = JsonDocument.Parse(Run("plan", download).Out))
        {
            var parts = plan.RootElement.GetProperty("parts");
            Assert.Equal(2, parts.GetArrayLength());
            Assert.Equal(new[] { 1, 2 }, Enumerable.Range(0, 2).Select(i => parts[i].GetProperty("index").GetInt32()));
            Assert.All(Enumerable.Range(0, 2), i => Assert.True(parts[i].GetProperty("selected").GetBoolean()));
        }

        // --part 2 takes only the second one.
        using (var plan = JsonDocument.Parse(Run("plan", download, "--part", "2").Out))
        {
            var parts = plan.RootElement.GetProperty("parts");
            Assert.False(parts[0].GetProperty("selected").GetBoolean());
            Assert.True(parts[1].GetProperty("selected").GetBoolean());
        }

        // A part's name works instead of its index.
        Assert.Equal(0, Run("install", download, "--part", "B_P", "--yes").Code);
        Assert.False(File.Exists(a));
        Assert.True(File.Exists(b));
    }

    [Fact]
    public void A_part_that_is_not_there_lists_the_ones_that_are()
    {
        var download = Zip("TwoParts-88-1-0-1730000002.zip", ("A_P.pak", "a pak"), ("B_P.pak", "b pak"));

        var (code, _, error) = Run("plan", download, "--part", "9");

        Assert.Equal(1, code);
        Assert.Contains("No part matches '9'", error);
        Assert.Contains("Available parts:", error);
        Assert.Contains("A_P", error);
        Assert.Contains("B_P", error);
    }

    [Fact]
    public void Part_and_all_parts_together_are_an_argument_error()
    {
        var download = Zip("TwoParts-88-1-0-1730000002.zip", ("A_P.pak", "a pak"), ("B_P.pak", "b pak"));

        var (code, _, error) = Run("plan", download, "--part", "1", "--all-parts");

        Assert.Equal(1, code);
        Assert.Contains("--part", error);
        Assert.Contains("--all-parts", error);
        // It stops at the arguments, before the download is unpacked.
        Assert.False(Directory.Exists(Path.Combine(Data, "staging")));
    }

    [Fact]
    public void A_part_that_needs_a_missing_loader_is_a_problem()
    {
        var download = Zip("MenuMod-90-1-0-1730000003.zip",
            ("MyMod_P.pak", "a pak"),
            ("ProjectPBZ/Content/JavaScript/PBZModsMenu/Mods/MenuThing/main.js", "// a Mods Menu script mod"));

        // The pak alone installs; the script mod needs Mods Menu, which this install does not have.
        using (var plan = JsonDocument.Parse(Run("plan", download, "--part", "1").Out))
            Assert.True(plan.RootElement.GetProperty("canInstall").GetBoolean());
        using (var plan = JsonDocument.Parse(Run("plan", download, "--part", "2").Out))
        {
            Assert.False(plan.RootElement.GetProperty("canInstall").GetBoolean());
            var problems = string.Join(" | ", plan.RootElement.GetProperty("problems").EnumerateArray().Select(p => p.GetString()));
            Assert.Contains("Mods Menu", problems);
        }
    }

    [Fact]
    public void Staging_keeps_the_unpacked_download_out_of_the_data_folder()
    {
        var download = Download();
        var staging = Path.Combine(root, "SharedStaging");

        var (code, output, _) = Run("plan", download, "--staging", staging);

        Assert.Equal(0, code);
        Assert.Contains("\"canInstall\": true", output);
        // The download was unpacked below the given folder and cleaned up again; the data folder kept no staging.
        Assert.True(Directory.Exists(staging));
        Assert.Empty(Directory.EnumerateFileSystemEntries(staging));
        Assert.False(Directory.Exists(Path.Combine(Data, "staging")));
    }

    [Fact]
    public void Staging_inside_the_game_is_refused()
    {
        var (code, _, error) = Run("plan", Download(), "--staging", Path.Combine(ModsFolder, "unpacked"));

        Assert.Equal(1, code);
        Assert.Contains("outside the game", error);
        Assert.False(Directory.Exists(Path.Combine(ModsFolder, "unpacked")));
    }

    /// <summary>A UE4SS mod in a folder of its own, so it can leave a log behind like the real ones do.</summary>
    private string Ue4ssModDownload()
    {
        Write(Path.Combine(GameRoot, "platform", "steam", "ue4ss", "UE4SS.dll"), "ue4ss");
        return Zip("MyMod.zip", ("ue4ss/Mods/MyMod/enabled.txt", ""), ("ue4ss/Mods/MyMod/Scripts/main.lua", "main"));
    }

    private string Ue4ssModFolder => Path.Combine(GameRoot, "platform", "steam", "ue4ss", "Mods", "MyMod");

    [Fact]
    public void An_uninstall_reports_a_log_the_mod_left_behind()
    {
        Assert.Equal(0, Run("install", Ue4ssModDownload(), "--yes").Code);
        Write(Path.Combine(Ue4ssModFolder, "compatibility.log"), "Verified UE4SS ...");

        var preview = Run("uninstall", "MyMod");
        Assert.Equal(2, preview.Code);
        Assert.Contains("compatibility.log", preview.Out);
        Assert.Contains("--remove-leftovers", preview.Out);

        var kept = Run("uninstall", "MyMod", "--yes", "--json");
        Assert.Equal(0, kept.Code);
        using var json = JsonDocument.Parse(kept.Out);
        var leftovers = json.RootElement.GetProperty("leftovers");
        var leftover = leftovers.EnumerateArray().Single();
        Assert.EndsWith("compatibility.log", leftover.GetProperty("relativePath").GetString());
        Assert.True(leftover.GetProperty("isLog").GetBoolean());
        Assert.True(File.Exists(Path.Combine(Ue4ssModFolder, "compatibility.log")));
    }

    [Fact]
    public void Remove_leftovers_moves_them_into_the_backups()
    {
        Assert.Equal(0, Run("install", Ue4ssModDownload(), "--yes").Code);
        var log = Path.Combine(Ue4ssModFolder, "compatibility.log");
        Write(log, "Verified UE4SS ...");

        var moved = Run("uninstall", "MyMod", "--yes", "--remove-leftovers", "--json");
        Assert.Equal(0, moved.Code);
        Assert.False(File.Exists(log));
        using var json = JsonDocument.Parse(moved.Out);
        var backup = json.RootElement.GetProperty("leftoverBackup").GetString()!;
        Assert.True(Directory.Exists(backup));
        Assert.Contains(Directory.GetFiles(backup, "*", SearchOption.AllDirectories), file => file.EndsWith("compatibility.log"));
    }
}
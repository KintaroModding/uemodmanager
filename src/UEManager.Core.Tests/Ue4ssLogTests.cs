using UEManager.Core.Mods;

namespace UEManager.Core.Tests;

public class Ue4ssLogTests : IDisposable
{
    private readonly TestFolder folder = new();

    public void Dispose() => folder.Dispose();

    [Fact]
    public void Marks_errors_and_warnings_and_names_the_mods_in_errors()
    {
        var path = folder.File("UE4SS.log", string.Join(Environment.NewLine,
            "[12:00:00.001] UE4SS - v3.0.1 Beta #0 - Git SHA #d935b5b",
            "[12:00:01.002] Starting Lua mod 'Witch'",
            "[12:00:01.003] [Lua] Error: [Witch] main.lua:12: attempt to index a nil value",
            "[12:00:01.004] Warning: setting 'GuiConsoleEnabled' is deprecated",
            "[12:00:01.005] Mod 'DashEnhanced-x2' failed to start",
            "[12:00:01.006] Starting Lua mod 'Witchcraft'"));

        var log = Ue4ssLog.Read(path)!;

        Assert.Equal(6, log.Lines.Count);
        Assert.Equal(2, log.ErrorCount);
        Assert.Equal(1, log.WarningCount);
        Assert.Equal(Ue4ssLogLevel.Info, log.Lines[1].Level);
        Assert.Equal(["DashEnhanced-x2", "Witch"], log.ModsNamedInErrors(["Witch", "Witchcraft", "DashEnhanced-x2", "DashEnhanced"]));
    }

    [Fact]
    public void Keeps_the_end_of_a_long_log_and_reads_it_while_the_game_writes()
    {
        var path = folder.Combine("UE4SS.log");
        using (var game = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
        using (var writer = new StreamWriter(game))
        {
            for (var i = 1; i <= Ue4ssLog.MaxLines + 50; i++)
                writer.WriteLine($"line {i}");
            writer.Flush();

            var log = Ue4ssLog.Read(path)!;

            Assert.True(log.Truncated);
            Assert.Equal(Ue4ssLog.MaxLines, log.Lines.Count);
            Assert.Equal($"line {Ue4ssLog.MaxLines + 50}", log.Lines[^1].Text);
        }
    }

    [Fact]
    public void After_a_mark_only_the_new_lines_are_read()
    {
        var path = folder.File("UE4SS.log", "[12:00:00.001] UE4SS - v3.0.1\n[12:00:01.002] Starting Lua mod 'Witch'\n");
        var mark = Ue4ssLog.Read(path)!.End;
        File.AppendAllText(path, "[12:05:00.000] [Lua] Error: [Witch] main.lua:12\n");

        var log = Ue4ssLog.Read(path, mark)!;

        Assert.True(log.AfterMark);
        Assert.Equal(["[12:05:00.000] [Lua] Error: [Witch] main.lua:12"], log.Lines.Select(l => l.Text));
        Assert.Equal(1, log.ErrorCount);
        Assert.Equal(3, log.End.LineCount);
    }

    [Fact]
    public void A_line_still_being_written_shows_whole_after_the_mark()
    {
        var path = folder.File("UE4SS.log", "[12:00:00.001] UE4SS - v3.0.1\n[12:00:01.002] Starting Lua");
        var mark = Ue4ssLog.Read(path)!.End;
        File.AppendAllText(path, " mod 'Witch'\n");

        var log = Ue4ssLog.Read(path, mark)!;

        Assert.Equal(["[12:00:01.002] Starting Lua mod 'Witch'"], log.Lines.Select(l => l.Text));
    }

    [Fact]
    public void A_new_game_start_reads_the_whole_log_again()
    {
        var path = folder.File("UE4SS.log", "[12:00:00.001] UE4SS - v3.0.1\nline 2\nline 3\n");
        var mark = Ue4ssLog.Read(path)!.End;
        File.WriteAllText(path, "[13:30:00.001] UE4SS - v3.0.1\nline 2\nline 3\nline 4\n");

        var log = Ue4ssLog.Read(path, mark)!;

        Assert.False(log.AfterMark);
        Assert.Equal(4, log.Lines.Count);
    }

    [Fact]
    public void No_log_yet_is_null() => Assert.Null(Ue4ssLog.Read(folder.Combine("UE4SS.log")));
}

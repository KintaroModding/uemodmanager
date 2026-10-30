using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using UEManager.UI.Services;
using Xunit;

namespace UEManager.UI.Tests;

/// <summary>Release notes in the update window, read as GitHub shows them.</summary>
public class NotesTextTests
{
    [Fact]
    public void Lists_bold_code_and_links_are_read_from_real_notes()
    {
        var lines = NotesText.Parse(
            "**What changed**\r\n" +
            "- **Mod details:** click a mod's name.\r\n" +
            "- **UE4SS errors:**\r\n" +
            "  - The log has **Clear** and a search box.\r\n" +
            "\r\n" +
            "**Install:** unzip `uemm_phantom-blade-zero_1.2.0.zip`. It needs the [.NET 8 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/8.0).");

        Assert.Equal([0, 1, 1, 2, 0], lines.Select(l => l.Level));
        Assert.Equal([false, false, false, false, true], lines.Select(l => l.Gap));
        Assert.Equal(new NotesSpan("What changed", Bold: true), Assert.Single(lines[0].Spans));
        Assert.Equal([new NotesSpan("Mod details:", Bold: true), new NotesSpan(" click a mod's name.")], lines[1].Spans);
        Assert.Equal([new NotesSpan("The log has "), new NotesSpan("Clear", Bold: true), new NotesSpan(" and a search box.")], lines[3].Spans);
        Assert.Equal(
            [
                new NotesSpan("Install:", Bold: true),
                new NotesSpan(" unzip "),
                new NotesSpan("uemm_phantom-blade-zero_1.2.0.zip", Code: true),
                new NotesSpan(". It needs the "),
                new NotesSpan(".NET 8 Desktop Runtime (x64)", Url: "https://dotnet.microsoft.com/download/dotnet/8.0"),
                new NotesSpan("."),
            ],
            lines[4].Spans);
    }

    [Fact]
    public void Code_and_links_inside_bold_stay_bold()
    {
        var spans = NotesText.Parse("**Open `Data` or the [page](https://example.com/a)**").Single().Spans;

        Assert.Equal(
            [
                new NotesSpan("Open ", Bold: true),
                new NotesSpan("Data", Bold: true, Code: true),
                new NotesSpan(" or the ", Bold: true),
                new NotesSpan("page", Bold: true, Url: "https://example.com/a"),
            ],
            spans);
    }

    [Theory]
    [InlineData("[run](javascript:alert(1))")]
    [InlineData("[open](file:///C:/Windows/System32/calc.exe)")]
    [InlineData("[here](ue4ss/Mods)")]
    public void Only_web_links_open(string text) => Assert.Null(NotesText.Parse(text).Single().Spans[0].Url);

    [Fact]
    public void Unfinished_marks_and_headings_show_plainly()
    {
        var lines = NotesText.Parse("## Changes\n2 ** 3 and a `tick\n* star item\n-not a list");

        Assert.Equal(new NotesSpan("Changes", Bold: true), Assert.Single(lines[0].Spans));
        Assert.Equal(new NotesSpan("2 ** 3 and a `tick"), Assert.Single(lines[1].Spans));
        Assert.Equal((1, "star item"), (lines[2].Level, lines[2].Spans.Single().Text));
        Assert.Equal((0, "-not a list"), (lines[3].Level, lines[3].Spans.Single().Text));
    }

    [Fact]
    public void A_click_on_a_link_in_the_window_opens_its_address()
    {
        UiThread.Run(() =>
        {
            string? opened = null;
            var panel = new StackPanel();
            panel.Resources["Brush.Text"] = Brushes.White;
            ReleaseNotes.SetLinkCommand(panel, new RelayCommand<string>(url => opened = url));
            ReleaseNotes.SetText(panel, "- Open an [issue](https://github.com/KintaroModding/uemodmanager/issues) with **`Data`**.");

            var line = Assert.IsType<DockPanel>(Assert.Single(panel.Children));
            var text = Assert.IsType<TextBlock>(line.Children[1]);
            Assert.Equal("Open an issue with Data.", new TextRange(text.ContentStart, text.ContentEnd).Text);
            Assert.Single(text.Inlines.OfType<Hyperlink>()).RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));

            Assert.Equal("https://github.com/KintaroModding/uemodmanager/issues", opened);
        });
    }

    [Fact]
    public void No_notes_are_no_lines()
    {
        Assert.Empty(NotesText.Parse(null));
        Assert.Empty(NotesText.Parse("\n\n"));
    }
}

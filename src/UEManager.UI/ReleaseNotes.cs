using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using UEManager.UI.Services;

namespace UEManager.UI;

/// <summary>
/// Shows release notes in a panel as GitHub shows them (see <see cref="NotesText"/>): a line per paragraph or list
/// item, with bold text, code and links. A link runs <see cref="LinkCommandProperty"/> with its address.
/// </summary>
public static class ReleaseNotes
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(ReleaseNotes), new FrameworkPropertyMetadata(null, OnTextChanged));

    public static readonly DependencyProperty LinkCommandProperty = DependencyProperty.RegisterAttached(
        "LinkCommand", typeof(ICommand), typeof(ReleaseNotes), new FrameworkPropertyMetadata(null));

    public static string? GetText(DependencyObject element) => (string?)element.GetValue(TextProperty);

    public static void SetText(DependencyObject element, string? value) => element.SetValue(TextProperty, value);

    public static ICommand? GetLinkCommand(DependencyObject element) => (ICommand?)element.GetValue(LinkCommandProperty);

    public static void SetLinkCommand(DependencyObject element, ICommand? value) => element.SetValue(LinkCommandProperty, value);

    private static void OnTextChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not Panel panel)
            return;
        panel.Children.Clear();
        foreach (var line in NotesText.Parse(e.NewValue as string))
            panel.Children.Add(Line(panel, line));
    }

    private static FrameworkElement Line(Panel panel, NotesLine line)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 18 };
        foreach (var span in line.Spans)
            text.Inlines.Add(Piece(panel, span));
        var margin = new Thickness(0, line.Gap ? 10 : 2, 0, 0);
        if (line.Level == 0)
        {
            text.Margin = margin;
            return text;
        }
        var bullet = new TextBlock { Width = 14, LineHeight = 18, Text = line.Level == 1 ? "•" : "◦" };
        DockPanel.SetDock(bullet, Dock.Left);
        return new DockPanel { Margin = margin with { Left = (line.Level - 1) * 16 }, Children = { bullet, text } };
    }

    private static Inline Piece(Panel panel, NotesSpan span)
    {
        var run = new Run(span.Text);
        if (span.Code)
        {
            run.FontFamily = new FontFamily("Cascadia Mono, Consolas");
            run.FontSize = 11.5;
        }
        Inline piece = run;
        if (span.Url is { } url)
        {
            var link = new Hyperlink(run) { ToolTip = url, Foreground = (Brush)panel.FindResource("Brush.Text") };
            link.Click += (_, _) =>
            {
                if (GetLinkCommand(panel) is { } command && command.CanExecute(url))
                    command.Execute(url);
            };
            piece = link;
        }
        if (span.Bold)
        {
            piece.FontWeight = FontWeights.SemiBold;
            piece.Foreground = (Brush)panel.FindResource("Brush.Text");
        }
        return piece;
    }
}

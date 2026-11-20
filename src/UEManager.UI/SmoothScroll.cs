using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace UEManager.UI;

/// <summary>
/// Smooth wheel scrolling, as apps and browsers do it (the user's wish, 2026-11-16): a wheel notch moves as far as
/// before (Windows' wheel setting, 16 px a line), but glides there in about a fifth of a second instead of jumping.
/// Every ScrollViewer has it through its style in Theme\Controls.xaml.
/// </summary>
/// <remarks>
/// The wheel goes to the innermost scroll area under the mouse that can still move that way, so a list inside a page
/// scrolls first and the page takes over at the list's end. A scroll area that moves by items (CanContentScroll, such as
/// a drop-down list or the UE4SS log) keeps Windows' own scrolling, as does any wheel with Ctrl, Shift or Alt held, and
/// everything when Windows' animations are off.
/// </remarks>
public static class SmoothScroll
{
    /// <summary>How fast a glide closes in on its goal: about 95% of the way after three of these.</summary>
    private const double TimeConstantSeconds = 0.06;

    /// <summary>The pixels of a scroll line, as WPF's own wheel scrolling uses them.</summary>
    private const double LinePixels = 16;

    /// <summary>The player's setting (Settings ▸ Smooth scrolling); off, every scroll area keeps Windows' own scrolling.</summary>
    public static bool Allowed { get; set; } = true;

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SmoothScroll), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static readonly DependencyProperty GlideProperty =
        DependencyProperty.RegisterAttached("Glide", typeof(Glide), typeof(SmoothScroll));

    private static void OnIsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not ScrollViewer viewer)
            return;
        viewer.PreviewMouseWheel -= OnWheel;
        if (e.NewValue is true)
            viewer.PreviewMouseWheel += OnWheel;
    }

    private static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        var viewer = (ScrollViewer)sender;
        if (!Allowed || e.Handled || e.Delta == 0 || Keyboard.Modifiers != ModifierKeys.None || !SystemParameters.ClientAreaAnimation)
            return;
        // The preview reaches the outer scroll areas first: each lets it pass unless it is the one the wheel belongs to.
        if (!ReferenceEquals(Target(e.OriginalSource as DependencyObject, e.Delta), viewer) || viewer.CanContentScroll)
            return;
        var lines = SystemParameters.WheelScrollLines;
        var notch = lines < 0 ? viewer.ViewportHeight : lines * LinePixels;
        var glide = (Glide?)viewer.GetValue(GlideProperty);
        if (glide is null)
        {
            glide = new Glide(viewer);
            viewer.SetValue(GlideProperty, glide);
        }
        glide.By(-e.Delta / 120.0 * notch);
        e.Handled = true;
    }

    /// <summary>The innermost scroll area from <paramref name="source"/> up that can still move the wheel's way; null for none.</summary>
    private static ScrollViewer? Target(DependencyObject? source, int delta)
    {
        for (var element = source; element is not null; element = Parent(element))
        {
            if (element is ScrollViewer viewer && viewer.ScrollableHeight > 0
                && viewer.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled)
            {
                var offset = ((Glide?)viewer.GetValue(GlideProperty))?.Goal ?? viewer.VerticalOffset;
                if (delta > 0 ? offset > 0.5 : offset < viewer.ScrollableHeight - 0.5)
                    return viewer;
            }
        }
        return null;
    }

    private static DependencyObject? Parent(DependencyObject element) =>
        element is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(element) ?? LogicalTreeHelper.GetParent(element)
            : LogicalTreeHelper.GetParent(element);

    /// <summary>One scroll area's glide toward its goal, a step at every frame the screen draws.</summary>
    private sealed class Glide(ScrollViewer viewer)
    {
        private double last;
        private TimeSpan lastFrame;

        /// <summary>Where the glide ends; null while it rests.</summary>
        public double? Goal { get; private set; }

        public void By(double pixels)
        {
            var from = Goal ?? viewer.VerticalOffset;
            var goal = Math.Round(Math.Clamp(from + pixels, 0, viewer.ScrollableHeight));
            if (Goal is null)
            {
                last = viewer.VerticalOffset;
                lastFrame = TimeSpan.Zero;
                CompositionTarget.Rendering += OnFrame;
            }
            Goal = goal;
        }

        private void OnFrame(object? sender, EventArgs e)
        {
            if (Goal is not { } goal)
                return;
            var now = e is RenderingEventArgs rendering ? rendering.RenderingTime : TimeSpan.Zero;
            var seconds = lastFrame == TimeSpan.Zero ? 1 / 60.0 : (now - lastFrame).TotalSeconds;
            if (seconds <= 0)
                return; // The same frame again.
            lastFrame = now;
            // Something else moved it, such as the scroll bar's thumb, a key or a jump to a card: it stays there.
            if (!viewer.IsLoaded || Math.Abs(viewer.VerticalOffset - last) > 1)
            {
                Stop();
                return;
            }
            goal = Math.Min(goal, viewer.ScrollableHeight);
            var current = viewer.VerticalOffset;
            var next = current + (goal - current) * (1 - Math.Exp(-seconds / TimeConstantSeconds));
            if (Math.Abs(goal - next) < 0.5)
                next = goal;
            viewer.ScrollToVerticalOffset(next);
            last = next;
            if (next == goal)
                Stop();
        }

        private void Stop()
        {
            CompositionTarget.Rendering -= OnFrame;
            Goal = null;
        }
    }
}

using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using UEManager.UI.Services;

namespace UEManager.UI;

/// <summary>true shows the element; Invert flips that.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is true) ^ Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A value shows the element, null hides it; Invert flips that.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is not null) ^ Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Compares the selected page with the parameter. Gives a Visibility for page
/// hosts and a bool for navigation buttons, and selects the page when checked.
/// </summary>
public sealed class PageConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var match = Equals(value, parameter);
        return targetType == typeof(Visibility) ? (match ? Visibility.Visible : Visibility.Collapsed) : match;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? parameter : Binding.DoNothing;
}

/// <summary>
/// Maps a tone name (good, warn, bad, accent) to the matching theme brush; with the parameter
/// "wash", to the faint fill in that tone.
/// </summary>
public sealed class ToneBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value switch
        {
            "good" or true => "Brush.Success",
            "warn" => "Brush.Warning",
            "bad" or false => "Brush.Danger",
            "accent" => "Brush.Accent",
            _ => "Brush.TextMuted",
        };
        if (parameter is "wash")
            key += "Wash";
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Letter-spaced capitals for a bound label.</summary>
public sealed class TrackedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        Format.Tracked(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Letter-spaced capitals for a fixed label: Text="{ui:Tracked 'Collection'}".</summary>
public sealed class TrackedExtension(string text) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => Format.Tracked(text);
}

/// <summary>An icon glyph (Segoe Fluent Icons) shown by the button and navigation templates.</summary>
public static class Icon
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.RegisterAttached(
        "Glyph", typeof(string), typeof(Icon), new FrameworkPropertyMetadata(null));

    public static string? GetGlyph(DependencyObject element) => (string?)element.GetValue(GlyphProperty);

    public static void SetGlyph(DependencyObject element, string? value) => element.SetValue(GlyphProperty, value);
}

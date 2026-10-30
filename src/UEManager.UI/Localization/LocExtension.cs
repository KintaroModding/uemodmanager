using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using UEManager.UI.Services;

namespace UEManager.UI;

/// <summary>
/// A fixed text in the chosen language, such as Text="{ui:Loc 'Save backups'}". It follows a change of
/// language at once. With Tracked=True it is set in letter-spaced capitals, as a label.
/// </summary>
public sealed class LocExtension(string text) : MarkupExtension
{
    public bool Tracked { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding(nameof(Localizer.Language))
        {
            Source = Localizer.Current,
            Mode = BindingMode.OneWay,
            Converter = new TextConverter(text, Tracked),
        }.ProvideValue(serviceProvider);

    private sealed class TextConverter(string text, bool tracked) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            tracked ? Format.Tracked(L.T(text)) : L.T(text);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }
}

/// <summary>
/// A view model whose texts follow a change of language. The subscription is weak, so short-lived rows
/// and dialogs are still collected after they are gone.
/// </summary>
public abstract class LocalizedObservableObject : ObservableObject
{
    /// <summary>The thread the view model lives on, which its views and collections belong to.</summary>
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;

    protected LocalizedObservableObject() =>
        PropertyChangedEventManager.AddHandler(Localizer.Current, OnLanguageChanged, nameof(Localizer.Language));

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (dispatcher.CheckAccess())
            RefreshLanguage();
        else
            dispatcher.BeginInvoke(RefreshLanguage);
    }

    /// <summary>Tells the views that every text may have changed.</summary>
    protected virtual void RefreshLanguage() => OnPropertyChanged(string.Empty);
}

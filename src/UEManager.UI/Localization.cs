using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Markup;
using CommunityToolkit.Mvvm.ComponentModel;

namespace UEManager.UI;

/// <summary>A one-way binding to the language dictionary, updated without rebuilding the view.</summary>
public sealed class LocExtension(string key) : MarkupExtension
{
    public bool Tracked { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{key}]") { Source = L.Current, Mode = BindingMode.OneWay };
        if (Tracked)
            binding.Converter = new TrackedConverter();
        return binding.ProvideValue(serviceProvider);
    }
}

/// <summary>Weak subscription keeps transient mod rows and dialogs collectable after a refresh.</summary>
public abstract class LocalizedObservableObject : ObservableObject
{
    protected LocalizedObservableObject() =>
        PropertyChangedEventManager.AddHandler(L.Current, OnLanguageChanged, nameof(L.Language));

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) => RefreshLanguage();

    protected virtual void RefreshLanguage() => OnPropertyChanged(string.Empty);
}

public sealed record LanguageOption(string Code, string Name)
{
    public override string ToString() => Name;
}

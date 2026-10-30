using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using UEManager.UI.ViewModels;

namespace UEManager.UI.Views;

public partial class ModSetsDialog : UserControl
{
    public ModSetsDialog()
    {
        InitializeComponent();
        // Ready to type a name as soon as it opens.
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
                FocusLater(NameBox);
        };
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ModSetsViewModel old)
                old.PropertyChanged -= OnSetsChanged;
            if (e.NewValue is ModSetsViewModel sets)
                sets.PropertyChanged += OnSetsChanged;
        };
    }

    /// <summary>The question about deleting a set takes the keys (Enter deletes); back from it, the name box has them again.</summary>
    private void OnSetsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ModSetsViewModel.IsAsking) && sender is ModSetsViewModel sets)
            FocusLater(sets.IsAsking ? AskDelete : NameBox);
    }

    /// <summary>Focus once the control shows, which a change of visibility only makes happen afterwards.</summary>
    private void FocusLater(IInputElement element) =>
        Dispatcher.BeginInvoke(() => element.Focus(), DispatcherPriority.Input);
}

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using UEManager.UI.ViewModels;

namespace UEManager.UI.Views;

public partial class SettingsEditor : UserControl
{
    public SettingsEditor()
    {
        InitializeComponent();
        // The editor's own parts in the app's colours: a red-tinted selection, a light caret, no links.
        var area = Editor.TextArea;
        area.SelectionBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xC8, 0x10, 0x2E));
        area.SelectionBorder = null;
        area.SelectionForeground = null;
        area.Caret.CaretBrush = (Brush)FindResource("Brush.Text");
        Editor.Options.EnableHyperlinks = false;
        Editor.Options.EnableEmailHyperlinks = false;
        // Ready to type as soon as it opens.
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
                FocusLater(Editor.TextArea);
        };
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is SettingsEditorViewModel old)
                old.PropertyChanged -= OnEditorChanged;
            if (e.NewValue is SettingsEditorViewModel editor)
                editor.PropertyChanged += OnEditorChanged;
        };
    }

    /// <summary>The question about unsaved changes takes the keys (Enter saves); back from it, the text has them again.</summary>
    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsEditorViewModel.IsAsking) && sender is SettingsEditorViewModel editor)
            FocusLater(editor.IsAsking ? AskSave : Editor.TextArea);
    }

    /// <summary>Focus once the control shows, which a change of visibility only makes happen afterwards.</summary>
    private void FocusLater(IInputElement element) =>
        Dispatcher.BeginInvoke(() => element.Focus(), DispatcherPriority.Input);
}

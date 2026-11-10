using System.Windows;
using System.Windows.Controls;
using UEManager.UI.ViewModels;

namespace UEManager.UI.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        Unloaded += (_, _) => NexusApiKeyInput.Clear();
    }

    private async void SaveNexusApiKeyClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel model || !model.CanEditNexusApiKey)
            return;
        // Clear the visible control immediately; the saved key is never loaded into it.
        var key = NexusApiKeyInput.Password;
        NexusApiKeyInput.Clear();
        await model.SaveNexusApiKeyAsync(key);
    }

    private void ClearNexusApiKeyClick(object sender, RoutedEventArgs e)
    {
        NexusApiKeyInput.Clear();
        if (DataContext is MainViewModel model)
            model.ClearNexusApiKeyCommand.Execute(null);
    }
}

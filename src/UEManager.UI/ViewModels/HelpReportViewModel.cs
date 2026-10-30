using System.Runtime.InteropServices;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UEManager.UI.Services;

namespace UEManager.UI.ViewModels;

/// <summary>
/// The "Report a problem" window: the player says in a few words what happened, the manager adds the
/// facts. Nothing is sent anywhere; the player copies the report into the help channel on Discord.
/// </summary>
public sealed partial class HelpReportViewModel(MainViewModel owner, string facts) : LocalizedObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReportText))]
    private string problem = "";

    [ObservableProperty]
    private string? copiedText;

    public string ReportText => HelpReport.Compose(Problem, facts);

    [RelayCommand]
    private void Copy()
    {
        try
        {
            Clipboard.SetText(ReportText);
            CopiedText = L.T("Copied. Paste it into the help channel on Discord.");
        }
        catch (COMException ex)
        {
            CopiedText = L.F($"The clipboard is in use by another app. Try again. ({ex.Message})");
        }
    }

    [RelayCommand]
    private void OpenDiscord() => owner.OpenDiscordCommand.Execute(null);

    [RelayCommand]
    private void Close() => owner.CloseHelpDialog();
}

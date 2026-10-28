using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UEManager.Core;
using UEManager.Core.Mods;
using UEManager.UI.Services;

namespace UEManager.UI.ViewModels;

/// <summary>A log line with the colour of its level: "bad" for an error, "warn" for a warning.</summary>
public sealed record Ue4ssLogLineItem(string Text, string Tone);

/// <summary>The log part of the UE4SS page: the end of UE4SS.log from the last game start, errors and warnings marked.</summary>
public sealed partial class Ue4ssLogViewModel(MainViewModel owner) : ObservableObject
{
    private Ue4ssRuntime? runtime;
    private Ue4ssLog? log;
    private IReadOnlyList<string> errorMods = [];
    private string? readError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownLines))]
    private bool onlyProblems;

    public bool HasLog => log is not null;

    public IReadOnlyList<Ue4ssLogLineItem> ShownLines => log is null
        ? []
        : log.Lines
            .Where(l => !OnlyProblems || l.Level != Ue4ssLogLevel.Info)
            .Select(l => new Ue4ssLogLineItem(l.Text, l.Level switch
            {
                Ue4ssLogLevel.Error => "bad",
                Ue4ssLogLevel.Warning => "warn",
                _ => "",
            }))
            .ToList();

    public string SummaryText => (runtime, log) switch
    {
        ({ Installed: false } or null, _) => "UE4SS is not installed, so there is no log.",
        _ when readError is not null => $"The log could not be read: {readError}",
        (_, null) => "No log yet. UE4SS writes it each time the game starts.",
        _ => $"Game start of {log.WrittenAt:g}:  {Format.Count(log.ErrorCount, "error", "errors")},  "
            + $"{Format.Count(log.WarningCount, "warning", "warnings")}."
            + (log.Truncated ? $"  Showing the last {Ue4ssLog.MaxLines} lines." : ""),
    };

    public string SummaryTone => log is null ? "" : log.ErrorCount > 0 ? "bad" : log.WarningCount > 0 ? "warn" : "good";

    public bool HasErrorMods => errorMods.Count > 0;

    public string ErrorModsText => $"Errors name these mods: {string.Join(", ", errorMods)}";

    /// <summary>Reads the log of the installed UE4SS again.</summary>
    public async Task LoadAsync(Ue4ssRuntime? current)
    {
        runtime = current;
        log = null;
        errorMods = [];
        readError = null;
        if (current is { Installed: true })
        {
            try
            {
                (log, errorMods) = await Task.Run(() =>
                {
                    var read = Ue4ssLog.Read(current.LogPath);
                    return (read, read?.ModsNamedInErrors(current.ModFolders()) ?? []);
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"UE4SS.log could not be read: {ex.Message}");
                readError = ex.Message;
            }
        }
        OnPropertyChanged(string.Empty);
    }

    [RelayCommand]
    private Task Refresh() => owner.RefreshUe4ssLogAsync();

    [RelayCommand]
    private void OpenLog()
    {
        if (log is not null)
            owner.OpenPath(log.Path);
    }

    [RelayCommand]
    private void Copy()
    {
        if (log is null)
            return;
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, log.Lines.Select(l => l.Text)));
            owner.ShowToast("The UE4SS log is on the clipboard.");
        }
        catch (COMException ex)
        {
            owner.ShowToast($"The clipboard is in use by another app: {ex.Message}", isError: true);
        }
    }
}

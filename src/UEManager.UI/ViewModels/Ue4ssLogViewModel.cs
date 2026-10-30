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
public sealed partial class Ue4ssLogViewModel(MainViewModel owner) : LocalizedObservableObject
{
    private Ue4ssRuntime? runtime;
    private Ue4ssLog? log;
    private IReadOnlyList<string> errorMods = [];
    private string? readError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownLines), nameof(FoundText))]
    private bool onlyProblems;

    /// <summary>Text the shown lines contain, such as a mod's name; it stays when the log is read again.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownLines), nameof(IsSearching), nameof(FoundText))]
    private string searchText = "";

    public bool HasLog => log is not null;

    public bool IsSearching => !string.IsNullOrWhiteSpace(SearchText);

    public string FoundText => L.F($"Lines found: {ShownLines.Count}");

    public IReadOnlyList<Ue4ssLogLineItem> ShownLines => log is null
        ? []
        : log.Lines
            .Where(l => Shows(l, OnlyProblems, SearchText))
            .Select(l => new Ue4ssLogLineItem(l.Text, l.Level switch
            {
                Ue4ssLogLevel.Error => "bad",
                Ue4ssLogLevel.Warning => "warn",
                _ => "",
            }))
            .ToList();

    /// <summary>Whether a line shows: an error or warning when only those are wanted, and containing the search text, in any case.</summary>
    internal static bool Shows(Ue4ssLogLine line, bool onlyProblems, string search) =>
        (!onlyProblems || line.Level != Ue4ssLogLevel.Info)
        && (string.IsNullOrWhiteSpace(search) || line.Text.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase));

    public string SummaryText => (runtime, log) switch
    {
        ({ Installed: false } or null, _) => L.T("UE4SS is not installed, so there is no log."),
        _ when readError is not null => L.F($"The log could not be read: {readError}"),
        (_, null) => L.T("No log yet. UE4SS writes it each time the game starts."),
        _ => L.F($"Game start of {log.WrittenAt:g}:  {Errors(log)},  {Warnings(log)}.") + Shortened(log),
    };

    private static string Errors(Ue4ssLog log) => L.Plural(log.ErrorCount, "{0} error", "{0} errors");

    private static string Warnings(Ue4ssLog log) => L.Plural(log.WarningCount, "{0} warning", "{0} warnings");

    private static string Shortened(Ue4ssLog log) => log.Truncated ? "  " + L.F($"Showing the last {Ue4ssLog.MaxLines} lines.") : "";

    public string SummaryTone => log is null ? "" : log.ErrorCount > 0 ? "bad" : log.WarningCount > 0 ? "warn" : "good";

    public bool HasErrorMods => errorMods.Count > 0;

    public string ErrorModsText => L.F($"Errors name these mods: {string.Join(", ", errorMods)}");

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

    /// <summary>Deletes the log, so its old lines are gone; UE4SS writes a new one at the next game start.</summary>
    [RelayCommand]
    private Task Clear() => owner.ClearUe4ssLogAsync();

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
            owner.ShowToast(L.T("The UE4SS log is on the clipboard."));
        }
        catch (COMException ex)
        {
            owner.ShowToast(L.F($"The clipboard is in use by another app: {ex.Message}"), isError: true);
        }
    }
}

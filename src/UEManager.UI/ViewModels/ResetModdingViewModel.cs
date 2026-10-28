using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UEManager.Core.Mods;
using UEManager.UI.Services;

namespace UEManager.UI.ViewModels;

/// <summary>The question before "Reset modding": what it changes in the game, and two choices that go further.</summary>
public sealed partial class ResetModdingViewModel(string gameName, ResetPreview preview) : ObservableObject
{
    private readonly TaskCompletionSource<bool> result = new();

    /// <summary>The mods also leave the manager, as when each is uninstalled.</summary>
    [ObservableProperty]
    private bool deleteMods;

    /// <summary>The app's own settings go back to their defaults; the app then starts again.</summary>
    [ObservableProperty]
    private bool resetSettings;

    public string Intro => $"This takes {gameName} back to its original files, so you can start clean. Close the game first.";

    /// <summary>What the reset changes in the game, one line each.</summary>
    public IReadOnlyList<string> Changes { get; } = DescribeChanges(preview);

    public bool HasMods => preview.InstalledMods > 0;

    public string DeleteModsText => HasMods
        ? $"Also delete the {Format.Count(preview.InstalledMods, "mod", "mods")} from the manager. "
            + "Without this, they stay in the Mods list, turned off, ready to turn on again."
        : "Also delete the mods from the manager. There are none.";

    public Task<bool> Result => result.Task;

    [RelayCommand]
    private void Accept() => result.TrySetResult(true);

    [RelayCommand]
    private void Cancel() => result.TrySetResult(false);

    private static List<string> DescribeChanges(ResetPreview preview)
    {
        var changes = new List<string>();
        if (preview.EnabledMods > 0)
            changes.Add($"{Format.Count(preview.EnabledMods, "enabled mod is", "enabled mods are")} removed from the game.");
        if (preview.Ue4ssInstalled)
            changes.Add("UE4SS is removed, with its settings and every mod in its folder, also mods put there by hand.");
        if (preview.OtherFiles > 0)
            changes.Add($"{Format.Count(preview.OtherFiles, "file", "files")} in the mods folders that the manager did not install "
                + $"{(preview.OtherFiles == 1 ? "moves" : "move")} to a backup folder.");
        if (changes.Count == 0)
            changes.Add("The game has no mods and no UE4SS to remove.");
        return changes;
    }
}

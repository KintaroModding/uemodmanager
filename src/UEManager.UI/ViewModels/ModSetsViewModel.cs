using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UEManager.Core.Mods;

namespace UEManager.UI.ViewModels;

/// <summary>A mod set in its list: its name, its mods, and whether exactly those are on now.</summary>
public sealed record ModSetItem(string Name, string CountText, string ModsText, bool IsInUse);

/// <summary>
/// The player's mod sets: each turns on exactly its mods with one click, and the mods that are on now can be saved
/// as a new set.
/// </summary>
public sealed partial class ModSetsViewModel(MainViewModel owner) : LocalizedObservableObject
{
    [ObservableProperty]
    private IReadOnlyList<ModSetItem> sets = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string newName = "";

    /// <summary>The set the player chose to delete, while the dialog asks whether to.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAsking), nameof(CanChange), nameof(AskText))]
    private ModSetItem? deleting;

    public bool HasSets => Sets.Count > 0;

    public bool IsAsking => Deleting is not null;

    /// <summary>The sets and the name can be used while no question is open.</summary>
    public bool CanChange => !IsAsking;

    public string AskText => Deleting is null ? "" : L.F($"Delete the set \"{Deleting.Name}\"? The mods stay as they are.");

    partial void OnSetsChanged(IReadOnlyList<ModSetItem> value) => OnPropertyChanged(nameof(HasSets));

    /// <summary>Shows the sets as they are now, with the names of their mods.</summary>
    internal void Load(IReadOnlyList<ModSet> saved, IReadOnlyList<ModRecord> mods)
    {
        var names = mods.ToDictionary(m => m.Id, m => m.Name);
        var enabled = mods.Where(m => m.Enabled).Select(m => m.Id).ToHashSet();
        Sets = saved.Select(set =>
        {
            var installed = set.ModIds.Where(names.ContainsKey).ToList();
            return new ModSetItem(
                set.Name,
                L.Plural(installed.Count, "{0} mod", "{0} mods"),
                installed.Count == 0 ? L.T("No mods: using it turns every mod off.") : string.Join(", ", installed.Select(id => names[id])),
                enabled.SetEquals(installed));
        }).ToList();
    }

    private bool CanSave() => !string.IsNullOrWhiteSpace(NewName);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        if (await owner.SaveModSetAsync(NewName))
            NewName = "";
    }

    [RelayCommand]
    private Task Apply(ModSetItem set) => owner.ApplyModSetAsync(set.Name);

    [RelayCommand]
    private void Delete(ModSetItem set) => Deleting = set;

    [RelayCommand]
    private void KeepSet() => Deleting = null;

    [RelayCommand]
    private async Task ConfirmDelete()
    {
        if (Deleting is not { } set)
            return;
        Deleting = null;
        await owner.DeleteModSetAsync(set.Name);
    }

    [RelayCommand]
    private void Close() => owner.CloseModSets();
}

using System.Collections;

namespace UEManager.UI.ViewModels;

public enum ModSortField
{
    Name,
    Installed,
}

/// <summary>Sorts the mod list by name or installation date; equal entries fall back to the name.</summary>
public sealed class ModOrder(ModSortField field, bool descending) : IComparer
{
    private static readonly StringComparer Names = StringComparer.CurrentCultureIgnoreCase;

    public int Compare(object? x, object? y)
    {
        if (x is not ModItemViewModel a || y is not ModItemViewModel b)
            return 0;
        var result = field == ModSortField.Installed ? a.InstalledAt.CompareTo(b.InstalledAt) : Names.Compare(a.Name, b.Name);
        if (result == 0)
            result = Names.Compare(a.Name, b.Name);
        return descending ? -result : result;
    }
}

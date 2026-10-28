using System.Collections;

namespace UEManager.UI.Catalog;

public enum CatalogSortField
{
    Name,
    Updated,
}

/// <summary>Sorts the openmods page by name or by the date of the latest release; equal entries fall back to the name.</summary>
public sealed class CatalogOrder(CatalogSortField field, bool descending) : IComparer
{
    private static readonly StringComparer Names = StringComparer.CurrentCultureIgnoreCase;

    public int Compare(object? x, object? y)
    {
        if (x is not CatalogItemViewModel a || y is not CatalogItemViewModel b)
            return 0;
        var result = field == CatalogSortField.Updated
            ? Nullable.Compare(a.Mod.Updated, b.Mod.Updated)
            : Names.Compare(a.Name, b.Name);
        if (result == 0)
            result = Names.Compare(a.Name, b.Name);
        return descending ? -result : result;
    }
}

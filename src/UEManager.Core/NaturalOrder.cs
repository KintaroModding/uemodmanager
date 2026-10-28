using System.Globalization;

namespace UEManager.Core;

/// <summary>
/// Compares names with the numbers in them by value, so "x2" comes before "x10" and "x1.5"
/// before "x2". Case is ignored.
/// </summary>
public sealed class NaturalOrder : IComparer<string?>
{
    public static readonly NaturalOrder Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
            return 0;
        if (x is null)
            return -1;
        if (y is null)
            return 1;
        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            int result;
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                result = Number(x, ref i).CompareTo(Number(y, ref j));
            }
            else
            {
                result = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                i++;
                j++;
            }
            if (result != 0)
                return result;
        }
        return (x.Length - i).CompareTo(y.Length - j);
    }

    /// <summary>Reads a number such as "10" or "1.5" and moves past it.</summary>
    private static double Number(string text, ref int index)
    {
        var start = index;
        while (index < text.Length && char.IsAsciiDigit(text[index]))
            index++;
        if (index + 1 < text.Length && text[index] == '.' && char.IsAsciiDigit(text[index + 1]))
        {
            index++;
            while (index < text.Length && char.IsAsciiDigit(text[index]))
                index++;
        }
        return double.Parse(text.AsSpan(start, index - start), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    }
}

using System.IO;
using System.Reflection;

namespace UEManager.UI.Services;

/// <summary>A license text built into the exe.</summary>
public sealed record LicenseNotice(string Name, string Text);

/// <summary>
/// The license texts built into the exe: the app's own (GPL-3.0 with additional terms, and its
/// contributors), and those of the open-source libraries compiled into it. The licenses require
/// the texts to ship with the app, and the app is one file, so they are shown on the About page.
/// </summary>
internal static class LicenseNotices
{
    private const string LibraryPrefix = "UEManager.UI.Licenses.";
    private const string LegalPrefix = "UEManager.UI.Legal.";

    public static IReadOnlyList<LicenseNotice> Load() =>
    [
        Read("SharpCompress", LibraryPrefix + "SharpCompress.txt"),
        Read("CommunityToolkit.Mvvm", LibraryPrefix + "CommunityToolkit.Mvvm.txt"),
        Read("CommunityToolkit.Mvvm: notices for code it includes", LibraryPrefix + "CommunityToolkit.Mvvm.ThirdPartyNotices.txt"),
    ];

    /// <summary>The app's own license and the additional terms that come with it.</summary>
    public static IReadOnlyList<LicenseNotice> LoadOwn() =>
    [
        Read("GNU General Public License, version 3", LegalPrefix + "LICENSE"),
        Read("Copyright and additional terms", LegalPrefix + "NOTICE.md"),
    ];

    /// <summary>The entries of CONTRIBUTORS.md: its "- " lines, such as "KintaroModding: creator and maintainer".</summary>
    public static IReadOnlyList<string> Contributors() =>
        Read("Contributors", LegalPrefix + "CONTRIBUTORS.md").Text
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("- ", StringComparison.Ordinal))
            .Select(line => line[2..].Trim())
            .ToList();

    private static LicenseNotice Read(string name, string resource)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"'{resource}' is not built into this exe.");
        using var reader = new StreamReader(stream);
        return new LicenseNotice(name, reader.ReadToEnd().Trim());
    }
}

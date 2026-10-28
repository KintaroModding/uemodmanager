using System.Text.RegularExpressions;

namespace UEManager.Core;

/// <summary>Takes the Windows user name out of text that is meant to be shared, such as a help report.</summary>
public static class Privacy
{
    /// <summary>Shorter names would also match ordinary folder names.</summary>
    private const int MinNameLength = 3;

    public static string HideUser(string text) =>
        HideUser(text, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.UserName);

    /// <summary>The profile folder becomes %USERPROFILE%; the user name as a folder anywhere else becomes &lt;user&gt;.</summary>
    public static string HideUser(string text, string userProfile, string userName)
    {
        if (!string.IsNullOrEmpty(userProfile))
            text = text.Replace(Path.TrimEndingDirectorySeparator(userProfile), "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        if (userName.Length >= MinNameLength)
            text = Regex.Replace(text, $@"(?<=[\\/]){Regex.Escape(userName)}(?=[\\/]|$)", "<user>", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        return text;
    }
}

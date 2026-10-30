namespace UEManager.Core.Mods;

/// <summary>
/// A problem the user can act on. <see cref="Exception.Message"/> is the English text, as the log keeps it;
/// the app shows <see cref="Text"/> in the player's language.
/// </summary>
public sealed class ModException(FormattableString message) : Exception(FormattableString.Invariant(message))
{
    /// <summary>The message with its values kept apart, so the text around them can be translated.</summary>
    public FormattableString Text { get; } = message;
}

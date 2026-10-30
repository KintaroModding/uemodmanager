namespace UEManager.Core.Mods;

/// <summary>A problem the user can act on. The message is shown to the user as it is.</summary>
public sealed class ModException : Exception
{
    private readonly FormattableString? formatted;

    public ModException(string message) : base(message) { }

    private ModException(FormattableString message) : base(message.ToString()) => formatted = message;

    /// <summary>Keep the format and its values so a cached failure follows the current UI language.</summary>
    public static ModException Localized(FormattableString message) => new(message);

    public override string Message => formatted is null ? L.T(base.Message) : L.F(formatted);
}

namespace UEManager.Core.Mods;

/// <summary>A problem the user can act on. The message is shown to the user as it is.</summary>
public sealed class ModException(string message) : Exception(message);

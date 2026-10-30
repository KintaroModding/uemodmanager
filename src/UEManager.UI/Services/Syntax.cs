using System.IO;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace UEManager.UI.Services;

/// <summary>
/// The settings editor's colours for a file, by its type: Lua for config.lua, JSON, and Name=Value lines for the
/// rest (.ini, .cfg, .txt, .toml, .yaml). The rules are built into the exe, in Theme\Syntax.
/// </summary>
internal static class Syntax
{
    private static readonly Dictionary<string, IHighlightingDefinition> Loaded = [];

    public static IHighlightingDefinition For(string path) => Load(Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".lua" => "Lua",
        ".json" => "Json",
        _ => "Ini",
    });

    private static IHighlightingDefinition Load(string name)
    {
        if (!Loaded.TryGetValue(name, out var definition))
        {
            using var stream = typeof(Syntax).Assembly.GetManifestResourceStream($"UEManager.UI.Syntax.{name}.xshd")
                ?? throw new InvalidOperationException($"The colours for {name} are not built into this exe.");
            using var reader = XmlReader.Create(stream);
            Loaded[name] = definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        }
        return definition;
    }
}

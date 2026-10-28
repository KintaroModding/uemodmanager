using System.Text;

namespace UEManager.Core.Stores;

/// <summary>
/// Valve's KeyValues text format, as used by Steam's .vdf and .acf files:
/// quoted (or bare) keys, each followed by a string value or a { } block.
/// A node is either a value or a list of children; keys may repeat.
/// </summary>
internal sealed class VdfNode
{
    private readonly List<KeyValuePair<string, VdfNode>> children = [];

    private VdfNode(string? value)
    {
        Value = value;
    }

    /// <summary>The string value, or null for a block.</summary>
    public string? Value { get; }

    public IReadOnlyList<KeyValuePair<string, VdfNode>> Children => children;

    /// <summary>The first child with this key, compared without case.</summary>
    public VdfNode? Child(string key) =>
        children.FirstOrDefault(c => c.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;

    public string? Text(string key) => Child(key)?.Value;

    /// <exception cref="FormatException">The text is not well-formed KeyValues.</exception>
    public static VdfNode Parse(string text)
    {
        var root = new VdfNode(null);
        ReadChildren(new Tokenizer(text), root, isRoot: true);
        return root;
    }

    private static void ReadChildren(Tokenizer tokens, VdfNode parent, bool isRoot)
    {
        while (tokens.Next() is { } key)
        {
            if (key.Kind == TokenKind.Close)
            {
                if (isRoot)
                    throw new FormatException("Unexpected '}'.");
                return;
            }
            if (key.Kind == TokenKind.Open)
                throw new FormatException("A '{' has no key in front of it.");

            var value = tokens.Next() ?? throw new FormatException($"Key \"{key.Text}\" has no value.");
            VdfNode child;
            if (value.Kind == TokenKind.Open)
            {
                child = new VdfNode(null);
                ReadChildren(tokens, child, isRoot: false);
            }
            else if (value.Kind == TokenKind.String)
            {
                child = new VdfNode(value.Text);
            }
            else
            {
                throw new FormatException($"Key \"{key.Text}\" has no value.");
            }
            parent.children.Add(new KeyValuePair<string, VdfNode>(key.Text, child));
        }
        if (!isRoot)
            throw new FormatException("A '{' is never closed.");
    }

    private enum TokenKind
    {
        String,
        Open,
        Close,
    }

    private readonly record struct Token(TokenKind Kind, string Text);

    private sealed class Tokenizer(string text)
    {
        private int position;

        public Token? Next()
        {
            SkipSpaceAndComments();
            if (position >= text.Length)
                return null;
            switch (text[position])
            {
                case '{':
                    position++;
                    return new Token(TokenKind.Open, "{");
                case '}':
                    position++;
                    return new Token(TokenKind.Close, "}");
                case '"':
                    return Quoted();
                default:
                    return Bare();
            }
        }

        private void SkipSpaceAndComments()
        {
            while (position < text.Length)
            {
                if (char.IsWhiteSpace(text[position]))
                {
                    position++;
                }
                else if (text[position] == '/' && position + 1 < text.Length && text[position + 1] == '/')
                {
                    while (position < text.Length && text[position] != '\n')
                        position++;
                }
                else
                {
                    return;
                }
            }
        }

        /// <summary>A quoted string. Steam escapes backslashes in paths, as in "D:\\SteamLibrary".</summary>
        private Token Quoted()
        {
            var builder = new StringBuilder();
            position++;
            while (position < text.Length)
            {
                var c = text[position++];
                if (c == '"')
                    return new Token(TokenKind.String, builder.ToString());
                if (c == '\\' && position < text.Length)
                {
                    var escaped = text[position++];
                    builder.Append(escaped switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        _ => escaped,
                    });
                }
                else
                {
                    builder.Append(c);
                }
            }
            throw new FormatException("A quoted string is never closed.");
        }

        private Token Bare()
        {
            var start = position;
            while (position < text.Length && !char.IsWhiteSpace(text[position]) && text[position] is not ('{' or '}' or '"'))
                position++;
            return new Token(TokenKind.String, text[start..position]);
        }
    }
}

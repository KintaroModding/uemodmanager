using System.IO;
using System.Text;
using UEManager.Core;

namespace UEManager.UI.Services;

/// <summary>
/// A text file as the settings editor reads and saves it: in its own encoding, so a save changes only what the
/// player changed. UTF-8 with or without its mark, UTF-16, or else an old code page, kept byte for byte.
/// </summary>
internal sealed record TextFile(string Text, Encoding Encoding)
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static TextFile Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes is [0xEF, 0xBB, 0xBF, ..])
            return new TextFile(Utf8.GetString(bytes, 3, bytes.Length - 3), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        if (bytes is [0xFF, 0xFE, ..])
            return new TextFile(Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), Encoding.Unicode);
        if (bytes is [0xFE, 0xFF, ..])
            return new TextFile(Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), Encoding.BigEndianUnicode);
        try
        {
            return new TextFile(Utf8.GetString(bytes), Utf8);
        }
        catch (DecoderFallbackException)
        {
            // Not UTF-8: Latin-1 maps every byte to one character and back, so an old code page survives a save.
            return new TextFile(Encoding.Latin1.GetString(bytes), Encoding.Latin1);
        }
    }

    /// <summary>Writes the text in the file's encoding: to a temporary file first, which then takes the old one's place.</summary>
    public static void Write(string path, string text, Encoding encoding)
    {
        var temp = path + ".uemm-tmp";
        try
        {
            File.WriteAllText(temp, text, encoding);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                TryDelete(temp);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"'{path}' could not be removed: {ex.Message}");
        }
    }
}

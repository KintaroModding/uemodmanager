using System.IO;
using System.Security.Cryptography;
using System.Text;
using UEManager.Core.Storage;

namespace UEManager.UI.Services;

/// <summary>
/// A local Windows CurrentUser DPAPI credential, not a portable password: copying Data to
/// another user or machine does not make this key usable there. No plaintext is written to disk.
/// </summary>
internal sealed class NexusApiKeyStore(AppPaths paths)
{
    private const int MaxKeyLength = 4096;
    private const int MaxFileLength = 64 * 1024;
    private const string InvalidKeyMessage = "The Nexus API key is invalid.";
    private const string InvalidFileMessage = "The stored Nexus API key cannot be read.";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly string file = Path.Combine(paths.Root, "nexus-api-key.dat");

    public string? Load()
    {
        byte[] encrypted;
        try
        {
            // Check the opened file's size before allocating; sharing also prevents a Windows
            // writer from replacing or growing it while it is being read.
            using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length is <= 0 or > MaxFileLength)
                throw new CryptographicException(InvalidFileMessage);
            encrypted = new byte[(int)input.Length];
            input.ReadExactly(encrypted);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }

        byte[]? plaintext = null;
        try
        {
            plaintext = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Normalize(Utf8.GetString(plaintext));
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            // Do not expose decrypted input or an encoding exception's offending characters.
            throw new CryptographicException(InvalidFileMessage);
        }
        finally
        {
            if (plaintext is not null)
                CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public void Save(string apiKey)
    {
        var normalized = Normalize(apiKey);
        byte[] plaintext;
        try
        {
            plaintext = Utf8.GetBytes(normalized);
        }
        catch (EncoderFallbackException)
        {
            throw new ArgumentException(InvalidKeyMessage, nameof(apiKey));
        }

        try
        {
            var encrypted = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(paths.Root);
            var temp = Path.Combine(paths.Root, "nexus-api-key." + Guid.NewGuid().ToString("N") + ".tmp");
            var created = false;
            try
            {
                using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    created = true;
                    output.Write(encrypted);
                    output.Flush(flushToDisk: true);
                }
                // Same-directory rename only after encryption, writing and flushing succeeded.
                // Until this succeeds, any existing credential remains untouched.
                File.Move(temp, file, overwrite: true);
            }
            finally
            {
                if (created)
                {
                    try
                    {
                        File.Delete(temp);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // Best-effort cleanup; do not mask the save failure or log credentials.
                    }
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public void Clear()
    {
        try
        {
            File.Delete(file);
        }
        catch (DirectoryNotFoundException)
        {
            // The data folder need not exist when no credential has ever been saved.
        }
    }

    private static string Normalize(string? apiKey)
    {
        // Reject controls before trimming: a pasted newline or tab is never a valid key.
        if (apiKey is null || apiKey.Length > MaxKeyLength || apiKey.Any(char.IsControl))
            throw new ArgumentException(InvalidKeyMessage, nameof(apiKey));
        var normalized = apiKey.Trim();
        if (normalized.Length == 0)
            throw new ArgumentException(InvalidKeyMessage, nameof(apiKey));
        return normalized;
    }
}

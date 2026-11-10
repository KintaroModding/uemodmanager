using System.IO;
using System.Security.Cryptography;
using System.Text;
using UEManager.Core.Storage;
using UEManager.UI.Services;
using Xunit;

namespace UEManager.UI.Tests;

/// <summary>Windows-only credentials, kept outside portable plaintext settings.</summary>
public class NexusApiKeyStoreTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "uemm-nexus-key-test-" + Guid.NewGuid().ToString("N"));
    private AppPaths Paths => new(Path.Combine(folder, "Data"));
    private string KeyFile => Path.Combine(Paths.Root, "nexus-api-key.dat");
    private NexusApiKeyStore Store => new(Paths);

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void Missing_key_and_clear_do_not_create_the_data_folder()
    {
        Assert.Null(Store.Load());
        Store.Clear();
        Store.Clear();
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void Save_load_replace_and_clear_round_trip()
    {
        var store = Store;
        store.Save("  first-test-key  ");
        Assert.Equal("first-test-key", store.Load());
        Assert.Single(Directory.GetFiles(Paths.Root));

        store.Save("replacement-test-key");
        Assert.Equal("replacement-test-key", store.Load());
        Assert.Single(Directory.GetFiles(Paths.Root));

        store.Clear();
        Assert.Null(store.Load());
        store.Clear();
        Assert.Empty(Directory.GetFileSystemEntries(Paths.Root));
    }

    [Fact]
    public void Another_instance_with_the_same_data_folder_can_read_the_key()
    {
        Store.Save("persisted-test-key");
        Assert.Equal("persisted-test-key", new NexusApiKeyStore(new AppPaths(Paths.Root)).Load());
    }

    [Fact]
    public void The_only_disk_file_is_current_user_dpapi_ciphertext_not_plaintext_settings()
    {
        const string key = "unique-nexus-secret-not-for-plaintext-0123456789";
        Store.Save(key);

        var encrypted = File.ReadAllBytes(KeyFile);
        Assert.False(encrypted.AsSpan().IndexOf(Encoding.UTF8.GetBytes(key)) >= 0);
        Assert.False(encrypted.AsSpan().IndexOf(Encoding.Unicode.GetBytes(key)) >= 0);
        Assert.Equal(KeyFile, Assert.Single(Directory.GetFiles(Paths.Root)));
        Assert.False(File.Exists(Paths.SettingsFile));
        var plaintext = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        try
        {
            Assert.Equal(key, Encoding.UTF8.GetString(plaintext));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\tsecret")]
    [InlineData("secret\r")]
    [InlineData("secret\n")]
    [InlineData("sec\0ret")]
    [InlineData("secret\u007f")]
    [InlineData("secret\u0085")]
    public void Invalid_keys_have_a_fixed_exception_and_never_create_files(string? key)
    {
        var error = Assert.Throws<ArgumentException>(() => Store.Save(key!));
        Assert.StartsWith("The Nexus API key is invalid.", error.Message);
        Assert.DoesNotContain("secret", error.ToString());
        Assert.Null(error.InnerException);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void Overlong_keys_are_rejected_without_writing()
    {
        var key = new string('x', 4097);
        var error = Assert.Throws<ArgumentException>(() => Store.Save(key));
        Assert.DoesNotContain(key, error.ToString());
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void The_maximum_key_length_is_supported()
    {
        var key = new string('x', 4096);
        Store.Save(key);
        Assert.Equal(key, Store.Load());
    }

    [Fact]
    public void Invalid_utf16_is_rejected_without_disclosing_the_input_or_writing()
    {
        const string key = "secret-\ud800";
        var error = Assert.Throws<ArgumentException>(() => Store.Save(key));
        Assert.DoesNotContain("secret", error.ToString());
        Assert.Null(error.InnerException);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void Invalid_replacement_keeps_the_old_credential_unchanged()
    {
        Store.Save("old-test-key");
        var before = File.ReadAllBytes(KeyFile);
        Assert.Throws<ArgumentException>(() => Store.Save("new-secret\n"));
        Assert.Equal(before, File.ReadAllBytes(KeyFile));
        Assert.Equal("old-test-key", Store.Load());
        Assert.Single(Directory.GetFiles(Paths.Root));
    }

    [Fact]
    public void Failed_atomic_replacement_keeps_the_old_key_and_removes_the_encrypted_temp_file()
    {
        Store.Save("old-test-key");
        var before = File.ReadAllBytes(KeyFile);
        using (var locked = new FileStream(KeyFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            // Windows denies rename/delete while an existing reader does not share deletion.
            var error = Record.Exception(() => Store.Save("replacement-secret"));
            Assert.True(error is IOException or UnauthorizedAccessException, "The locked credential must prevent replacement.");
            Assert.DoesNotContain("replacement-secret", error!.ToString());
            Assert.Equal(before, File.ReadAllBytes(KeyFile));
            Assert.Single(Directory.GetFiles(Paths.Root));
        }
        Assert.Equal("old-test-key", Store.Load());
    }

    [Fact]
    public void A_blocked_data_folder_does_not_write_any_key_or_disclose_it()
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Paths.Root, "not a directory");
        var error = Assert.ThrowsAny<IOException>(() => Store.Save("cannot-save-secret"));
        Assert.DoesNotContain("cannot-save-secret", error.ToString());
        Assert.Equal("not a directory", File.ReadAllText(Paths.Root));
        Assert.Single(Directory.GetFileSystemEntries(folder));
    }

    [Fact]
    public void Damaged_ciphertext_throws_a_cryptographic_error_without_disclosing_the_key()
    {
        const string key = "secret-before-corruption";
        Store.Save(key);
        var encrypted = File.ReadAllBytes(KeyFile);
        encrypted[^1] ^= 0xff;
        File.WriteAllBytes(KeyFile, encrypted);

        var error = Assert.Throws<CryptographicException>(() => Store.Load());
        Assert.Equal("The stored Nexus API key cannot be read.", error.Message);
        Assert.DoesNotContain(key, error.ToString());
        Assert.Null(error.InnerException);
        Assert.Single(Directory.GetFiles(Paths.Root));
    }

    [Fact]
    public void Plaintext_is_not_accepted_as_a_stored_credential()
    {
        Directory.CreateDirectory(Paths.Root);
        const string key = "plaintext-secret";
        File.WriteAllText(KeyFile, key);
        var error = Assert.Throws<CryptographicException>(() => Store.Load());
        Assert.DoesNotContain(key, error.ToString());
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65537)]
    public void Empty_or_oversized_files_are_rejected_before_decryption(int length)
    {
        Directory.CreateDirectory(Paths.Root);
        using (var output = File.Create(KeyFile))
            output.SetLength(length);
        var error = Assert.Throws<CryptographicException>(() => Store.Load());
        Assert.Equal("The stored Nexus API key cannot be read.", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("secret\n")]
    [InlineData("\tsecret")]
    [InlineData("sec\0ret")]
    public void Decrypted_content_is_validated_without_leaking_it(string invalidKey)
    {
        WriteProtected(Encoding.UTF8.GetBytes(invalidKey));
        var error = Assert.Throws<CryptographicException>(() => Store.Load());
        Assert.Equal("The stored Nexus API key cannot be read.", error.Message);
        Assert.DoesNotContain("secret", error.ToString());
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void Overlong_decrypted_content_is_rejected()
    {
        WriteProtected(Encoding.UTF8.GetBytes(new string('x', 4097)));
        Assert.Throws<CryptographicException>(() => Store.Load());
    }

    [Fact]
    public void Invalid_decrypted_utf8_is_rejected_without_leaking_decoder_details()
    {
        WriteProtected([0x73, 0x65, 0x63, 0x72, 0x65, 0x74, 0xff]);
        var error = Assert.Throws<CryptographicException>(() => Store.Load());
        Assert.Equal("The stored Nexus API key cannot be read.", error.Message);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void Valid_decrypted_keys_are_trimmed_on_load()
    {
        WriteProtected(Encoding.UTF8.GetBytes("  test-key  "));
        Assert.Equal("test-key", Store.Load());
    }

    private void WriteProtected(byte[] plaintext)
    {
        try
        {
            Directory.CreateDirectory(Paths.Root);
            File.WriteAllBytes(KeyFile, ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}

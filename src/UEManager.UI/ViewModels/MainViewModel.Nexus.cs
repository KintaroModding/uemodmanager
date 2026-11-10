using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.Input;
using UEManager.Core.Updates;
using UEManager.UI.Services;

namespace UEManager.UI.ViewModels;

public sealed partial class MainViewModel
{
    private enum NexusKeyState { NotConfigured, Saved, Checking, Valid, Invalid, AccessDenied, RateLimited, Unavailable, StorageError, InvalidInput }

    private NexusApiKeyStore nexusApiKeyStore = null!;
    private HttpClient nexusUpdatesHttp = ModPageHttp;
    private NexusKeyState nexusKeyState;
    private bool nexusApiKeyBusy;

    public bool HasNexusApiKey { get; private set; }
    public bool CanEditNexusApiKey => !nexusApiKeyBusy;

    public string NexusApiKeyStatus => nexusKeyState switch
    {
        NexusKeyState.Saved => L.T("API key saved. Test it to verify access."),
        NexusKeyState.Checking => L.T("Checking Nexus Mods API key…"),
        NexusKeyState.Valid => L.T("Nexus Mods API key is valid. Mod update checks use the official API."),
        NexusKeyState.Invalid => L.T("Nexus Mods API key is invalid or expired. Replace it in Settings."),
        NexusKeyState.AccessDenied => L.T("Nexus Mods API access was denied. Check your account permissions."),
        NexusKeyState.RateLimited => L.T("Nexus Mods API rate limit reached. Try again later."),
        NexusKeyState.Unavailable => L.T("Could not reach or read the Nexus Mods API. The saved key has not been removed."),
        NexusKeyState.StorageError => L.T("Could not read or save the protected API key. Re-enter it, or clear the saved key."),
        NexusKeyState.InvalidInput => L.T("Enter a valid API key without spaces, line breaks or control characters."),
        _ => L.T("No API key saved. Mod updates use public pages, which may be blocked by site protection."),
    };

    private void InitializeNexusApiKey(HttpClient? http)
    {
        nexusUpdatesHttp = http ?? ModPageHttp;
        nexusApiKeyStore = new NexusApiKeyStore(paths);
        try
        {
            var key = nexusApiKeyStore.Load();
            ReplaceNexusChecker(key);
            nexusKeyState = key is null ? NexusKeyState.NotConfigured : NexusKeyState.Saved;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException or NotSupportedException)
        {
            // Do not put decrypted keys, response bodies or exception text in logs/help reports.
            nexusKeyState = NexusKeyState.StorageError;
        }
    }

    private void ReplaceNexusChecker(string? key)
    {
        CancelModUpdateChecks();
        modPageUpdates.Deactivate();
        modPageUpdates = new ModPageUpdates(nexusUpdatesHttp, key);
        HasNexusApiKey = !string.IsNullOrEmpty(key);
    }

    /// <summary>The password box supplies a new key only on Save; no plaintext key is data-bound or displayed.</summary>
    internal async Task<bool> SaveNexusApiKeyAsync(string apiKey)
    {
        if (nexusApiKeyBusy)
            return false;
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length > 4096
            || apiKey.Any(c => char.IsControl(c) || c > 126)
            || apiKey.Trim().Any(char.IsWhiteSpace))
        {
            nexusKeyState = NexusKeyState.InvalidInput;
            OnPropertyChanged(string.Empty);
            return false;
        }
        nexusApiKeyBusy = true;
        OnPropertyChanged(string.Empty);
        try
        {
            nexusApiKeyStore.Save(apiKey.Trim());
            ReplaceNexusChecker(apiKey.Trim());
            RefreshMods();
            await ValidateNexusKeyAsync();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException or NotSupportedException)
        {
            nexusKeyState = NexusKeyState.StorageError;
            return false;
        }
        finally
        {
            nexusApiKeyBusy = false;
            OnPropertyChanged(string.Empty);
        }
    }

    [RelayCommand]
    private async Task TestNexusApiKey()
    {
        if (nexusApiKeyBusy || !HasNexusApiKey)
            return;
        nexusApiKeyBusy = true;
        OnPropertyChanged(string.Empty);
        try
        {
            await ValidateNexusKeyAsync();
        }
        finally
        {
            nexusApiKeyBusy = false;
            OnPropertyChanged(string.Empty);
        }
    }

    private async Task ValidateNexusKeyAsync()
    {
        nexusKeyState = NexusKeyState.Checking;
        OnPropertyChanged(string.Empty);
        var checker = modPageUpdates;
        var result = await checker.ValidateApiKeyAsync();
        if (!ReferenceEquals(checker, modPageUpdates))
            return;
        nexusKeyState = result switch
        {
            ModPageUpdateFailure.None => NexusKeyState.Valid,
            ModPageUpdateFailure.InvalidApiKey => NexusKeyState.Invalid,
            ModPageUpdateFailure.AccessDenied => NexusKeyState.AccessDenied,
            ModPageUpdateFailure.RateLimited => NexusKeyState.RateLimited,
            _ => NexusKeyState.Unavailable,
        };
    }

    [RelayCommand]
    private void ClearNexusApiKey()
    {
        if (nexusApiKeyBusy)
            return;
        try
        {
            ClearStoredNexusApiKey();
            RefreshMods();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            nexusKeyState = NexusKeyState.StorageError;
        }
        OnPropertyChanged(string.Empty);
    }

    internal void ClearStoredNexusApiKey()
    {
        nexusApiKeyStore.Clear();
        ReplaceNexusChecker(null);
        nexusKeyState = NexusKeyState.NotConfigured;
        OnPropertyChanged(string.Empty);
    }

    [RelayCommand]
    private void OpenNexusApiAccess() => OpenPath("https://www.nexusmods.com/users/myaccount?tab=api%20access");
}

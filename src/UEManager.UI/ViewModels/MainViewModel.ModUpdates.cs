using System.Net.Http;
using UEManager.Core.Updates;
using UEManager.UI.Services;

namespace UEManager.UI.ViewModels;

public sealed partial class MainViewModel
{
    // Unlike downloads, page checks must not follow a redirect to another host or a login page.
    private static readonly HttpClient ModPageHttp = CreateModPageHttpClient();
    private ModPageUpdates modPageUpdates = new(ModPageHttp);
    private readonly SemaphoreSlim modPageChecks = new(3);
    private CancellationTokenSource? modPageCheckCancellation;

    private static HttpClient CreateModPageHttpClient()
    {
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"UEModManager/{AppInfo.Version}");
        return http;
    }

    /// <summary>Checks only rows with a mod page, without blocking the list or changing installed files.</summary>
    internal async Task CheckModUpdatesAsync()
    {
        CancelModUpdateChecks();
        if (SelectedPage != "Mods")
            return;
        modPageCheckCancellation = new CancellationTokenSource();
        var cancellationToken = modPageCheckCancellation.Token;
        var updates = modPageUpdates;
        var rows = Mods.Where(row => row.ModPageUrl is not null).ToArray();
        await Task.WhenAll(rows.Select(CheckRowAsync));

        async Task CheckRowAsync(ModItemViewModel row)
        {
            try
            {
                await modPageChecks.WaitAsync(cancellationToken);
                try
                {
                    await row.CheckForUpdateAsync(updates, cancellationToken);
                }
                finally
                {
                    modPageChecks.Release();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // A refreshed list or a different page owns the next check.
            }
        }
    }

    private void CancelModUpdateChecks()
    {
        modPageCheckCancellation?.Cancel();
        modPageCheckCancellation?.Dispose();
        modPageCheckCancellation = null;
    }
}

using System.IO;
using UEManager.Core;
using UEManager.Core.Catalog;
using UEManager.Core.Mods;
using UEManager.UI.Catalog;
using UEManager.UI.Services;

namespace UEManager.UI.ViewModels;

/// <summary>
/// The openmods page's part of the main view model. While the catalog is switched off
/// (<see cref="AppInfo.IsCatalogOn"/>), it makes no catalog, so the page never goes online.
/// </summary>
public sealed partial class MainViewModel
{
    public CatalogViewModel? Catalog { get; private set; }

    partial void CreateCatalog(string? catalogUrl)
    {
        if (AppInfo.IsCatalogOn)
            Catalog = new CatalogViewModel(this, Http, catalogUrl ?? OpenMods.GamesListUrl, Profile.SteamAppId, isTest: catalogUrl is not null,
                Enum.TryParse<CatalogSortField>(settings.CatalogSort, out var sort) ? sort : CatalogSortField.Updated,
                settings.CatalogSort is null || settings.CatalogSortDescending);
    }

    partial void OnModsChanged() => Catalog?.UpdateInstalled(manager?.Mods ?? []);

    partial void OnSelectedPageChanged(string value)
    {
        if (value == "Catalog")
            _ = Catalog?.LoadAsync();
    }

    internal void SaveCatalogSort(CatalogSortField field, bool descending)
    {
        settings.CatalogSort = field.ToString();
        settings.CatalogSortDescending = descending;
        SaveSettings();
    }

    /// <summary>
    /// One click on the openmods page: download the release file, check its size and checksum, and
    /// install it. A download that needs a choice or a look first opens the install review instead:
    /// variants, warnings, a mod installed by hand that it would replace, or settings the player changed.
    /// </summary>
    internal async Task InstallFromCatalogAsync(CatalogMod mod)
    {
        if (IsBusy || InstallDialog is not null || !EnsureGame() || mod.Release is not { } release || mod.InstallFile is not { } file)
            return;
        var folder = Path.Combine(paths.StagingDir, "download-" + Guid.NewGuid().ToString("N"));
        try
        {
            string? download = null;
            await RunAsync(L.F($"Downloading {mod.Name}…"), async () =>
            {
                var progress = new Progress<double>(share => BusyText = L.F($"Downloading {mod.Name}…  {share:P0}"));
                download = await FileDownloader.DownloadAsync(Http, file.Url, file.Name, file.Size, file.Sha256, folder, mod.Name, progress);
            });
            if (download is null)
                return;
            InstallPreview? preview = null;
            await RunAsync(L.F($"Reading {file.Name}…"), async () => preview = await Task.Run(() => manager!.Inspect(download)));
            if (preview is null)
                return;

            using (preview)
            {
                var all = preview.Packages.ToList();
                var check = manager!.Check(preview, all, mod.Id);
                var direct = all.Count == 1 && check.CanInstall && check.Warnings.Count == 0 && !check.HasEditedSettings
                    && (check.Replaces is null || string.Equals(check.Replaces.CatalogId, mod.Id, StringComparison.OrdinalIgnoreCase));
                var choice = direct
                    ? new InstallChoice(all, mod.Name, release.Version, check.Replaces is not null, mod.Id)
                    : await ReviewAsync(preview, new CatalogSource(mod.Id, mod.Name, release.Version));
                if (choice is not null)
                    await InstallChoiceAsync(preview, choice, showMods: false);
            }
        }
        finally
        {
            TryDeleteFolder(folder);
        }
    }
}

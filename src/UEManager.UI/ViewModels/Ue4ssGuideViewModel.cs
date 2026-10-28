using CommunityToolkit.Mvvm.Input;
using UEManager.Core.Games;
using UEManager.Core.Mods;

namespace UEManager.UI.ViewModels;

/// <summary>
/// The "Install UE4SS" popup: one-click download and install when a release is known (from
/// the online manifest or the game profile); installing from a downloaded file always works.
/// </summary>
public sealed partial class Ue4ssGuideViewModel(MainViewModel owner, GameInstall game, Ue4ssRelease? release)
{
    public const string OfficialReleases = "https://github.com/UE4SS-RE/RE-UE4SS/releases";

    public string DownloadUrl => game.Profile.Ue4ssDownloadUrl ?? OfficialReleases;

    public string? Note => game.Profile.Ue4ssNote;

    public string DownloadText => $"Download the UE4SS release for {game.Profile.DisplayName}.";

    /// <summary>A release file is known, so the manager can fetch it itself.</summary>
    public bool CanDownload => release is not null;

    public bool CannotDownload => !CanDownload;

    public string DownloadAndInstallText => release is null
        ? ""
        : $"The manager downloads {release.FileName}, "
          + (string.IsNullOrWhiteSpace(release.Sha256) ? "" : "checks that it is the expected file, ")
          + "and installs it into the game.";

    public string Win64Path => game.BinariesPath;

    [RelayCommand]
    private Task DownloadAndInstall() => owner.DownloadAndInstallUe4ssAsync();

    [RelayCommand]
    private Task InstallFromFile() => owner.InstallUe4ssFromFileAsync();

    [RelayCommand]
    private void OpenDownload() => owner.OpenPath(DownloadUrl);

    [RelayCommand]
    private void OpenWin64() => owner.OpenPath(Win64Path);

    [RelayCommand]
    private void CheckAgain() => owner.CheckUe4ss();

    [RelayCommand]
    private void Close() => owner.CloseUe4ssGuide();
}

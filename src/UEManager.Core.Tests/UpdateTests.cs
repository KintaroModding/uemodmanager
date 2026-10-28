using System.Diagnostics;
using UEManager.Core.Mods;
using UEManager.Core.Updates;

namespace UEManager.Core.Tests;

public class UpdateTests
{
    private const string Release = """
        {
          "tag_name": "v1.1.0",
          "html_url": "https://github.com/KintaroModding/uemodmanager/releases/tag/v1.1.0",
          "published_at": "2026-11-02T18:00:00Z",
          "body": "Faster installs.",
          "assets": [
            { "name": "uemm-full_phantom-blade-zero_1.1.0.zip", "size": 200,
              "browser_download_url": "https://github.com/KintaroModding/uemodmanager/releases/download/v1.1.0/uemm-full_phantom-blade-zero_1.1.0.zip",
              "digest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
            { "name": "uemm_mortal-shell-2_1.1.0.zip", "size": 100,
              "browser_download_url": "https://github.com/KintaroModding/uemodmanager/releases/download/v1.1.0/uemm_mortal-shell-2_1.1.0.zip" },
            { "name": "uemm_phantom-blade-zero_1.1.0.zip", "size": 150,
              "browser_download_url": "https://github.com/KintaroModding/uemodmanager/releases/download/v1.1.0/uemm_phantom-blade-zero_1.1.0.zip",
              "digest": "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" }
          ]
        }
        """;

    [Fact]
    public void Picks_the_zip_of_this_build_and_game()
    {
        var release = AppReleases.Parse(Release, "uemm_phantom-blade-zero_");

        Assert.Equal("1.1.0", release.Version);
        Assert.Equal("Faster installs.", release.Notes);
        Assert.Equal("https://github.com/KintaroModding/uemodmanager/releases/tag/v1.1.0", release.PageUrl);
        Assert.Equal("uemm_phantom-blade-zero_1.1.0.zip", release.File?.Name);
        Assert.Equal(150, release.File?.Size);
        Assert.Equal(new string('b', 64), release.File?.Sha256);
    }

    [Fact]
    public void A_build_without_a_zip_gets_none()
    {
        var release = AppReleases.Parse(Release, "uemm_wukong_");

        Assert.Equal("1.1.0", release.Version);
        Assert.Null(release.File);
    }

    [Fact]
    public void Http_links_count_only_for_a_test_list()
    {
        var json = Release.Replace("https://github.com/KintaroModding/uemodmanager/releases/download", "http://localhost:8765");

        Assert.Null(AppReleases.Parse(json, "uemm_phantom-blade-zero_").File);
        Assert.Equal("http://localhost:8765/v1.1.0/uemm_phantom-blade-zero_1.1.0.zip",
            AppReleases.Parse(json, "uemm_phantom-blade-zero_", allowHttp: true).File?.Url);
    }

    [Fact]
    public void A_release_without_a_version_is_refused()
    {
        Assert.Throws<ModException>(() => AppReleases.Parse("""{ "assets": [] }""", "uemm_"));
    }

    [Fact]
    public void Replaces_the_exe_and_cleans_up_on_the_next_start()
    {
        using var folder = new TestFolder();
        var current = folder.File("App.exe", "old version");
        var update = folder.File(@"update\App.exe", "new version");

        SelfUpdate.Replace(current, update);

        Assert.Equal("new version", File.ReadAllText(current));
        Assert.Equal("old version", File.ReadAllText(current + ".old"));
        SelfUpdate.CleanUp(current);
        Assert.Equal(["App.exe"], Directory.GetFiles(folder.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void The_clean_up_deletes_only_what_an_update_left()
    {
        using var folder = new TestFolder();
        var current = folder.File("App.exe", "app");
        folder.File("App.exe.old", "old");
        folder.File($"App.exe.{new string('a', 32)}.old", "old, name taken");
        folder.File("App.exe.new", "not used");
        folder.File("App.exe.personal-backup.old", "the player's own copy");
        folder.File("App.exe.12345.old", "the player's own copy");

        SelfUpdate.CleanUp(current);

        Assert.Equal(["App.exe", "App.exe.12345.old", "App.exe.personal-backup.old"],
            Directory.GetFiles(folder.Path).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void An_exe_that_cannot_be_moved_stays_as_it_was()
    {
        using var folder = new TestFolder();
        var current = folder.File("App.exe", "old version");
        var update = folder.File(@"update\App.exe", "new version");

        // Unlike a running exe, a file held this way cannot even be renamed.
        using (File.Open(current, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Throws<ModException>(() => SelfUpdate.Replace(current, update));

        Assert.Equal("old version", File.ReadAllText(current));
        Assert.Equal(["App.exe"], Directory.GetFiles(folder.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void The_new_exe_must_be_the_announced_version()
    {
        using var folder = new TestFolder();
        // Any program file with version information will do: the test assembly carries the app's version.
        var assembly = typeof(UpdateTests).Assembly.Location;
        var version = FileVersionInfo.GetVersionInfo(assembly).ProductVersion!.Split('+')[0];
        File.Copy(assembly, folder.Combine("UEModManager.exe"));

        Assert.Equal(folder.Combine("UEModManager.exe"), SelfUpdate.FindExecutable(folder.Path, version));
        Assert.Throws<ModException>(() => SelfUpdate.FindExecutable(folder.Path, "99.0.0"));
    }
}

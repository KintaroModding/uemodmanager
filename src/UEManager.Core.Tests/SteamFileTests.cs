using UEManager.Core.Stores;

namespace UEManager.Core.Tests;

public class SteamFileTests
{
    [Fact]
    public void Reads_current_library_folders()
    {
        const string vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "path"      "C:\\Program Files (x86)\\Steam"
                    "label"     ""
                    "apps"
                    {
                        "228980"    "419758425"
                    }
                }
                "1"
                {
                    "path"      "D:\\SteamLibrary"
                }
            }
            """;

        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], SteamLibrary.ParseLibraryFolders(vdf));
    }

    [Fact]
    public void Reads_old_library_folders()
    {
        const string vdf = """
            "LibraryFolders"
            {
                "TimeNextStatsReport"   "1700000000"
                "ContentStatsID"        "-123"
                "1"                     "E:\\Games\\Steam"
            }
            """;

        Assert.Equal([@"E:\Games\Steam"], SteamLibrary.ParseLibraryFolders(vdf));
    }

    [Fact]
    public void Reads_an_app_manifest()
    {
        const string acf = """
            // written by Steam
            "AppState"
            {
                "appid"         "4115450"
                "name"          "Test \"Quoted\" Game"
                "StateFlags"    "4"
                "installdir"    "Phantom Blade Zero"
                "buildid"       "20123456"
            }
            """;

        Assert.Equal(("Phantom Blade Zero", "20123456"), SteamLibrary.ParseAppManifest(acf));
    }

    [Fact]
    public void A_manifest_without_install_folder_is_ignored()
    {
        Assert.Null(SteamLibrary.ParseAppManifest("""  "AppState" { "appid" "1" }  """));
    }

    [Theory]
    [InlineData("""  "AppState" { "installdir" "x"  """)]
    [InlineData("""  "AppState" { "installdir" "x" } }  """)]
    [InlineData("""  "AppState" { "installdir" "unclosed }  """)]
    public void Broken_files_are_refused(string text)
    {
        Assert.Throws<FormatException>(() => SteamLibrary.ParseAppManifest(text));
    }

    [Fact]
    public void A_broken_library_list_does_not_stop_discovery()
    {
        using var folder = new TestFolder();
        folder.File(@"Steam\steamapps\libraryfolders.vdf", "\"libraryfolders\" { \"0\" {");
        folder.File(@"Steam\steamapps\appmanifest_999999.acf", "\"AppState\" { \"installdir\" \"Test Game\" }");
        Directory.CreateDirectory(folder.Combine("Steam", "steamapps", "common", "Test Game"));

        var app = SteamLibrary.FindApp("999999", [folder.Combine("Steam")]);

        Assert.NotNull(app);
    }
}

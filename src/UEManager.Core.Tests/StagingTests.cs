using System.IO.Compression;
using UEManager.Core.Mods;

namespace UEManager.Core.Tests;

public class StagingTests
{
    private static string Zip(TestFolder folder, string name, params string[] entries)
    {
        var path = folder.Combine(name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var entry in entries)
        {
            using var writer = new StreamWriter(zip.CreateEntry(entry).Open());
            writer.Write("content of " + entry);
        }
        return path;
    }

    [Fact]
    public void Extracts_a_zip_and_finds_packages()
    {
        using var folder = new TestFolder();
        var zip = Zip(folder, "mod.zip", "Mod/Sharp_P.pak", "Mod/Sharp_P.utoc", "Mod/Sharp_P.ucas", "readme.txt");
        var staging = folder.Combine("staging");

        ArchiveStager.Stage(zip, staging);
        var scan = PackageScanner.Scan(staging);

        var package = Assert.Single(scan.Packages);
        Assert.Equal("Sharp_P", package.Name);
        Assert.Equal("", package.Folder);
        Assert.Equal(PackageKind.IoStore, package.Kind);
        Assert.Empty(package.Problems);
        Assert.Equal(1, scan.IgnoredFileCount);
    }

    [Fact]
    public void A_folder_that_holds_the_staging_folder_is_refused()
    {
        using var folder = new TestFolder();
        folder.File(@"App\Mod\Sharp_P.pak", "pak");
        var staging = folder.Combine("App", "Data", "staging", "1");

        var error = Assert.Throws<ModException>(() => ArchiveStager.Stage(folder.Combine("App"), staging));

        Assert.Contains("Data folder", error.Message);
        Assert.False(Directory.Exists(staging));

        // A staging folder below a folder whose name only starts the same is not inside.
        folder.File(@"Mod\Sharp_P.pak", "pak");
        ArchiveStager.Stage(folder.Combine("Mod"), folder.Combine("Mod2", "staging"));
        Assert.True(File.Exists(folder.Combine("Mod2", "staging", "Mod", "Sharp_P.pak")));
    }

    [Theory]
    [InlineData("../evil.pak")]
    [InlineData("Mod/../../evil.pak")]
    [InlineData("C:/evil.pak")]
    public void Refuses_entries_that_leave_the_staging_folder(string entry)
    {
        using var folder = new TestFolder();
        var zip = Zip(folder, "bad.zip", entry);

        var error = Assert.Throws<ModException>(() => ArchiveStager.Stage(zip, folder.Combine("staging")));
        Assert.Contains("unsafe path", error.Message);
        Assert.False(File.Exists(folder.Combine("evil.pak")));
    }

    [Fact]
    public void A_loose_package_file_brings_its_companions()
    {
        using var folder = new TestFolder();
        var pak = folder.File(@"Downloads\Sharp_P.pak");
        folder.File(@"Downloads\Sharp_P.utoc");
        folder.File(@"Downloads\Sharp_P.ucas");
        folder.File(@"Downloads\Other_P.pak");
        var staging = folder.Combine("staging");

        ArchiveStager.Stage(pak, staging);

        Assert.Equal(["Sharp_P.pak", "Sharp_P.ucas", "Sharp_P.utoc"],
            Directory.GetFiles(staging).Select(f => Path.GetFileName(f)).Order().ToArray());
    }

    [Fact]
    public void An_incomplete_iostore_pair_is_a_problem()
    {
        using var folder = new TestFolder();
        folder.File(@"staging\Broken_P.utoc");

        var package = Assert.Single(PackageScanner.Scan(folder.Combine("staging")).Packages);

        Assert.NotEmpty(package.Problems);
    }

    [Fact]
    public void Variant_folders_become_separate_packages()
    {
        using var folder = new TestFolder();
        folder.File(@"staging\Download\Options\Red\Blade_P.pak");
        folder.File(@"staging\Download\Options\Blue\Blade_P.pak");

        var scan = PackageScanner.Scan(folder.Combine("staging"));

        Assert.Equal(["Blue/Blade_P", "Red/Blade_P"], scan.Packages.Select(p => p.Label).ToArray());
    }

    [Fact]
    public void Folders_at_the_top_of_a_zip_keep_their_full_names()
    {
        using var folder = new TestFolder();
        var zip = Zip(folder, "options.zip", "x1.5/More_P.pak", "x2/More_P.pak");
        var staging = folder.Combine("staging");

        ArchiveStager.Stage(zip, staging);

        Assert.Equal(["x1.5/More_P", "x2/More_P"], PackageScanner.Scan(staging).Packages.Select(p => p.Label));
    }

    [Fact]
    public void Natural_order_compares_numbers_by_value()
    {
        string[] names = ["x10", "x2", "x1.5", "X3", "b", "a"];

        Assert.Equal(["a", "b", "x1.5", "x2", "X3", "x10"], names.Order(NaturalOrder.Instance));
    }

    [Theory]
    [InlineData(@"C:\Downloads\Sharp Blades-12-1-2-1730000000.zip", "Sharp Blades", "1.2")]
    [InlineData(@"C:\Downloads\Sharp_Blades_P.pak", "Sharp Blades", null)]
    [InlineData(@"C:\Downloads\Plain.7z", "Plain", null)]
    [InlineData(@"C:\Downloads\KintaroCdoDashTest_0.2.zip", "KintaroCdoDashTest", "0.2")]
    [InlineData(@"C:\Downloads\Sharp Blades v1.3.0.zip", "Sharp Blades", "1.3.0")]
    [InlineData(@"C:\Downloads\Mod 2.zip", "Mod 2", null)]
    public void Names_come_from_the_download(string source, string name, string? version)
    {
        Assert.Equal((name, version), ModNaming.FromSource(source));
    }
}

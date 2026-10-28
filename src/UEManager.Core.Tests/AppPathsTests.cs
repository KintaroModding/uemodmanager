using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

public class AppPathsTests
{
    [Fact]
    public void Data_lives_next_to_the_app()
    {
        var paths = AppPaths.NextToApp();

        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "Data"), paths.Root);
        Assert.StartsWith(paths.Root, paths.StateFile("some-game"));
    }

    [Fact]
    public void A_writable_folder_passes_the_check_and_keeps_no_probe_file()
    {
        using var folder = new TestFolder();
        var paths = new AppPaths(folder.Combine("Data"));

        paths.EnsureWritable();

        Assert.True(Directory.Exists(paths.Root));
        Assert.Empty(Directory.GetFileSystemEntries(paths.Root));
    }
}

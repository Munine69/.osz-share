using System.IO.Compression;
using PuushShare.Client.Core.Services;

namespace PuushShare.Client.Tests;

public sealed class OszPackagerTests
{
    [Fact]
    public async Task PackageAsync_CreatesArchiveWithBeatmapFiles()
    {
        var packager = new OszPackager();
        var root = Path.Combine(Path.GetTempPath(), "PuushShare.Client.Tests", Guid.NewGuid().ToString("N"));
        var beatmapFolder = Path.Combine(root, "123 Artist - Title");
        Directory.CreateDirectory(beatmapFolder);

        var osuFile = Path.Combine(beatmapFolder, "map.osu");
        var mp3File = Path.Combine(beatmapFolder, "audio.mp3");
        await File.WriteAllTextAsync(osuFile, "osu file");
        await File.WriteAllTextAsync(mp3File, "audio");

        string? archivePath = null;
        try
        {
            archivePath = await packager.PackageAsync(beatmapFolder, CancellationToken.None);

            Assert.True(File.Exists(archivePath));
            using var archive = ZipFile.OpenRead(archivePath);
            Assert.Contains(archive.Entries, entry => entry.FullName == "map.osu");
            Assert.Contains(archive.Entries, entry => entry.FullName == "audio.mp3");
        }
        finally
        {
            if (archivePath is not null && File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}

using System.IO.Compression;
using PuushShare.Client.Core.Abstractions;

namespace PuushShare.Client.Core.Services;

public sealed class OszPackager : IOszPackager
{
    public Task<string> PackageAsync(string beatmapSetPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(beatmapSetPath))
        {
            throw new ArgumentException("Beatmap path is required.", nameof(beatmapSetPath));
        }

        if (!Directory.Exists(beatmapSetPath))
        {
            throw new DirectoryNotFoundException($"Beatmap path not found: {beatmapSetPath}");
        }

        var outputPath = Path.Combine(
            Path.GetTempPath(),
            $"osz-share-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.osz");

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ZipFile.CreateFromDirectory(beatmapSetPath, outputPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            return outputPath;
        }, cancellationToken);
    }
}

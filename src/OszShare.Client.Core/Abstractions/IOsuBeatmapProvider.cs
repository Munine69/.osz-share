using PuushShare.Client.Core.Models;

namespace PuushShare.Client.Core.Abstractions;

public interface IOsuBeatmapProvider
{
    Task<DetectedBeatmapInfo?> GetCurrentBeatmapInfoAsync(CancellationToken cancellationToken);

    Task<string?> GetCurrentBeatmapSetPathAsync(CancellationToken cancellationToken);
}

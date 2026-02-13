using PuushShare.Client.Core.Models;

namespace PuushShare.Client.Core.Abstractions;

public interface IShareApiClient
{
    Task<ShareResult> UploadAsync(string oszFilePath, int expiryMinutes, CancellationToken cancellationToken);
}

using PuushShare.Client.Core.Models;

namespace PuushShare.Client.Core.Abstractions;

public interface IClientSettingsStore
{
    Task<ClientSettings> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(ClientSettings settings, CancellationToken cancellationToken);
}

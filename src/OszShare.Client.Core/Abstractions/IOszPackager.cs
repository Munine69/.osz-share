namespace PuushShare.Client.Core.Abstractions;

public interface IOszPackager
{
    Task<string> PackageAsync(string beatmapSetPath, CancellationToken cancellationToken);
}

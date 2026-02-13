namespace PuushShare.Client.Core.Models;

public sealed class ClientSettings
{
    public const string DefaultServerBaseUrl = "https://168.107.57.128.sslip.io";
    public const string DefaultUploadApiKey = "osz-share-upload-v1";

    public string ServerBaseUrl { get; set; } = DefaultServerBaseUrl;

    public string UploadApiKey { get; set; } = DefaultUploadApiKey;

    public int DefaultExpiryMinutes { get; set; } = 5;

    public int MinExpiryMinutes { get; set; } = 1;

    public int MaxExpiryMinutes { get; set; } = 60;

    public ClientSettings Normalize()
    {
        if (!Uri.TryCreate(ServerBaseUrl, UriKind.Absolute, out _))
        {
            ServerBaseUrl = DefaultServerBaseUrl;
        }

        UploadApiKey = (UploadApiKey ?? DefaultUploadApiKey).Trim();

        if (MinExpiryMinutes < 1)
        {
            MinExpiryMinutes = 1;
        }

        if (MaxExpiryMinutes < MinExpiryMinutes)
        {
            MaxExpiryMinutes = MinExpiryMinutes;
        }

        DefaultExpiryMinutes = Math.Clamp(DefaultExpiryMinutes, MinExpiryMinutes, MaxExpiryMinutes);
        return this;
    }
}

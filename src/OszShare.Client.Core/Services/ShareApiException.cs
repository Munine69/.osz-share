namespace PuushShare.Client.Core.Services;

public sealed class ShareApiException : Exception
{
    public ShareApiException(string message)
        : base(message)
    {
    }

    public ShareApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

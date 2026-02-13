namespace PuushShare.Client.Services;

public interface IClientLogger
{
    void Info(string eventName, string message);

    void Warn(string eventName, string message);

    void Error(string eventName, string message, Exception? exception = null);
}

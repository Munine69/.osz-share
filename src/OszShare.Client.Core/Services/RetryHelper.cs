namespace PuushShare.Client.Core.Services;

public static class RetryHelper
{
    public static readonly TimeSpan[] DefaultRetryDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4)
    ];

    public static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken,
        IReadOnlyList<TimeSpan>? retryDelays = null)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var delays = retryDelays ?? DefaultRetryDelays;
        Exception? lastException = null;

        for (var attempt = 0; attempt <= delays.Count; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await operation(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastException = exception;
                if (attempt >= delays.Count)
                {
                    break;
                }

                await Task.Delay(delays[attempt], cancellationToken);
            }
        }

        throw new ShareApiException("Operation failed after retries.", lastException!);
    }
}

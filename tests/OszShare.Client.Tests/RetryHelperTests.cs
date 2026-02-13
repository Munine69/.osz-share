using PuushShare.Client.Core.Services;

namespace PuushShare.Client.Tests;

public sealed class RetryHelperTests
{
    [Fact]
    public async Task ExecuteAsync_RetriesUntilSuccess()
    {
        var attempts = 0;

        var result = await RetryHelper.ExecuteAsync(
            _ =>
            {
                attempts++;
                if (attempts < 3)
                {
                    throw new InvalidOperationException("fail");
                }

                return Task.FromResult("ok");
            },
            CancellationToken.None,
            [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero]);

        Assert.Equal("ok", result);
        Assert.Equal(3, attempts);
    }
}

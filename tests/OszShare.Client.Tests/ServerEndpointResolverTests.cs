using System.Net;
using System.Net.Http.Json;
using PuushShare.Client.Core.Services;

namespace PuushShare.Client.Tests;

public sealed class ServerEndpointResolverTests
{
    [Fact]
    public async Task ResolveAsync_UsesFallback5088_WhenConfiguredEndpointFails()
    {
        var handler = new StubHealthHttpHandler(new Dictionary<string, HttpStatusCode>
        {
            ["http://127.0.0.1:5000/api/v1/health"] = HttpStatusCode.ServiceUnavailable,
            ["http://localhost:5088/api/v1/health"] = HttpStatusCode.OK
        });

        using var client = new HttpClient(handler);
        var resolver = new ServerEndpointResolver(client);

        var result = await resolver.ResolveAsync("http://127.0.0.1:5000", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.WasAutoDetected);
        Assert.Equal("http://localhost:5088", result.BaseUrl);
    }

    [Fact]
    public async Task ResolveAsync_UsesConfiguredEndpoint_WhenItIsHealthy()
    {
        var handler = new StubHealthHttpHandler(new Dictionary<string, HttpStatusCode>
        {
            ["http://127.0.0.1:5000/api/v1/health"] = HttpStatusCode.OK
        });

        using var client = new HttpClient(handler);
        var resolver = new ServerEndpointResolver(client);

        var result = await resolver.ResolveAsync("http://127.0.0.1:5000", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.WasAutoDetected);
        Assert.Equal("http://127.0.0.1:5000", result.BaseUrl);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsFailure_WhenNoCandidatesAreHealthy()
    {
        var handler = new StubHealthHttpHandler(new Dictionary<string, HttpStatusCode>());
        using var client = new HttpClient(handler);
        var resolver = new ServerEndpointResolver(client);

        var result = await resolver.ResolveAsync("http://127.0.0.1:5000", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.ProbeResults);
        Assert.All(result.ProbeResults, probe => Assert.False(probe.IsHealthy));
    }

    private sealed class StubHealthHttpHandler : HttpMessageHandler
    {
        private readonly IReadOnlyDictionary<string, HttpStatusCode> _statusByUrl;

        public StubHealthHttpHandler(IReadOnlyDictionary<string, HttpStatusCode> statusByUrl)
        {
            _statusByUrl = statusByUrl;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.GetLeftPart(UriPartial.Path) ?? string.Empty;
            if (!_statusByUrl.TryGetValue(url, out var statusCode))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            if (statusCode == HttpStatusCode.OK)
            {
                return Task.FromResult(new HttpResponseMessage(statusCode)
                {
                    Content = JsonContent.Create(new { status = "ok" })
                });
            }

            return Task.FromResult(new HttpResponseMessage(statusCode));
        }
    }
}

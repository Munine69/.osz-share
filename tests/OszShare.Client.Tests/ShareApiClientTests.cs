using System.Net;
using System.Net.Http.Json;
using System.Text;
using PuushShare.Client.Core.Services;

namespace PuushShare.Client.Tests;

public sealed class ShareApiClientTests
{
    [Fact]
    public async Task UploadAsync_ParsesResponsePayload()
    {
        var root = Path.Combine(Path.GetTempPath(), "PuushShare.Client.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var oszPath = Path.Combine(root, "test.osz");
        await File.WriteAllTextAsync(oszPath, "dummy");

        var handler = new FakeHttpMessageHandler();
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.test")
        };

        var apiClient = new ShareApiClient(httpClient, "test-upload-key");

        try
        {
            var result = await apiClient.UploadAsync(oszPath, 5, CancellationToken.None);

            Assert.Equal("abc123", result.Id);
            Assert.Equal("https://example.test/d/abc123", result.Url);
            Assert.Equal(12345, result.SizeBytes);
            Assert.Equal("test-upload-key", handler.LastUploadKeyHeader);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        public string? LastUploadKeyHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.TryGetValues("X-Upload-Key", out var values))
            {
                LastUploadKeyHeader = values.FirstOrDefault();
            }

            var response = new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new
                {
                    id = "abc123",
                    url = "https://example.test/d/abc123",
                    expires_at = DateTimeOffset.UtcNow.AddMinutes(5),
                    size_bytes = 12345L
                })
            };

            return Task.FromResult(response);
        }
    }
}

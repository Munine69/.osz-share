using System.Net.Http.Json;
using System.Text.Json.Serialization;
using PuushShare.Client.Core.Abstractions;
using PuushShare.Client.Core.Models;

namespace PuushShare.Client.Core.Services;

public sealed class ShareApiClient : IShareApiClient
{
    private readonly HttpClient _httpClient;

    public ShareApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ShareResult> UploadAsync(string oszFilePath, int expiryMinutes, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(oszFilePath))
        {
            throw new ArgumentException("File path is required.", nameof(oszFilePath));
        }

        if (!File.Exists(oszFilePath))
        {
            throw new FileNotFoundException("OSZ file does not exist.", oszFilePath);
        }

        await using var fileStream = new FileStream(
            oszFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);

        using var multipart = new MultipartFormDataContent();
        using var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new("application/octet-stream");
        multipart.Add(fileContent, "file", Path.GetFileName(oszFilePath));
        multipart.Add(new StringContent(expiryMinutes.ToString()), "expiry_minutes");

        using var response = await _httpClient.PostAsync("/api/v1/shares", multipart, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new ShareApiException($"Upload failed ({(int)response.StatusCode}): {body}");
        }

        var payload = await response.Content.ReadFromJsonAsync<CreateSharePayload>(cancellationToken: cancellationToken);
        if (payload is null)
        {
            throw new ShareApiException("Upload response could not be parsed.");
        }

        return new ShareResult(payload.Id, payload.Url, payload.ExpiresAt, payload.SizeBytes);
    }

    private sealed record CreateSharePayload(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt,
        [property: JsonPropertyName("size_bytes")] long SizeBytes);
}

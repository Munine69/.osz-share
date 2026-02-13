using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace PuushShare.Client.Core.Services;

public sealed class ServerEndpointResolver
{
    private static readonly string[] FallbackCandidates =
    [
        "http://localhost:5088",
        "http://127.0.0.1:5088",
        "http://localhost:5000",
        "http://127.0.0.1:5000"
    ];

    private readonly HttpClient _httpClient;

    public ServerEndpointResolver(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ResolvedServerEndpoint> ResolveAsync(
        string configuredBaseUrl,
        CancellationToken cancellationToken)
    {
        var candidates = BuildCandidates(configuredBaseUrl);
        var probeResults = new List<ServerProbeResult>(candidates.Count);

        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            var probe = await ProbeAsync(candidate, cancellationToken);
            probeResults.Add(probe);
            if (probe.IsHealthy)
            {
                return new ResolvedServerEndpoint(
                    IsSuccess: true,
                    BaseUrl: candidate,
                    WasAutoDetected: index > 0,
                    ProbeResults: probeResults);
            }
        }

        return new ResolvedServerEndpoint(
            IsSuccess: false,
            BaseUrl: candidates[0],
            WasAutoDetected: false,
            ProbeResults: probeResults);
    }

    private async Task<ServerProbeResult> ProbeAsync(string baseUrl, CancellationToken cancellationToken)
    {
        var endpoint = $"{baseUrl.TrimEnd('/')}/api/v1/health";
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(1500));

        try
        {
            using var response = await _httpClient.GetAsync(endpoint, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                return new ServerProbeResult(baseUrl, false, $"HTTP {(int)response.StatusCode}");
            }

            var payload = await response.Content.ReadFromJsonAsync<HealthPayload>(cancellationToken: timeoutCts.Token);
            if (payload?.Status == "ok")
            {
                return new ServerProbeResult(baseUrl, true);
            }

            return new ServerProbeResult(baseUrl, false, "Unexpected health payload");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ServerProbeResult(baseUrl, false, "Timeout");
        }
        catch (Exception exception)
        {
            return new ServerProbeResult(baseUrl, false, exception.GetType().Name);
        }
    }

    private static IReadOnlyList<string> BuildCandidates(string configuredBaseUrl)
    {
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();

        void Add(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed))
            {
                return;
            }

            var normalized = parsed.GetLeftPart(UriPartial.Authority).TrimEnd('/');
            if (unique.Add(normalized))
            {
                ordered.Add(normalized);
            }
        }

        Add(configuredBaseUrl);
        foreach (var candidate in FallbackCandidates)
        {
            Add(candidate);
        }

        if (ordered.Count == 0)
        {
            ordered.Add("http://localhost:5088");
        }

        return ordered;
    }

    private sealed record HealthPayload([property: JsonPropertyName("status")] string Status);
}

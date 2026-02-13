namespace PuushShare.Client.Core.Services;

public sealed record ServerProbeResult(string BaseUrl, bool IsHealthy, string? FailureReason = null);

public sealed record ResolvedServerEndpoint(
    bool IsSuccess,
    string BaseUrl,
    bool WasAutoDetected,
    IReadOnlyList<ServerProbeResult> ProbeResults);

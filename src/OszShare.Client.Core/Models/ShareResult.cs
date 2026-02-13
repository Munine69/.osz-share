namespace PuushShare.Client.Core.Models;

public sealed record ShareResult(
    string Id,
    string Url,
    DateTimeOffset ExpiresAt,
    long SizeBytes);

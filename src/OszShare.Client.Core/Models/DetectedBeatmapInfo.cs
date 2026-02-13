namespace PuushShare.Client.Core.Models;

public sealed class DetectedBeatmapInfo
{
    public required string BeatmapSetPath { get; init; }

    public required string BeatmapFilePath { get; init; }

    public required string Artist { get; init; }

    public required string Title { get; init; }

    public required string DifficultyName { get; init; }

    public string? BackgroundImagePath { get; init; }

    public float Hp { get; init; }

    public float Od { get; init; }

    public float Ar { get; init; }

    public float Cs { get; init; }

    public double? StarRating { get; init; }
}

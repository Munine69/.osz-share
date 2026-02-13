using System.Diagnostics;
using System.Text;
using OppaiSharp;
using OsuMemoryDataProvider;
using ProcessMemoryDataFinder.API;
using PuushShare.Client.Core.Abstractions;
using PuushShare.Client.Core.Models;

namespace PuushShare.Client.Core.Services;

public sealed class OsuStableBeatmapProvider : IOsuBeatmapProvider
{
    private static readonly string[] KnownProcessNames = ["osu!", "osu"];
    private static readonly string[] SupportedBackgroundExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp"];

    private readonly object _sync = new();
    private readonly object _metadataSync = new();
    private readonly StructuredOsuMemoryReader _reader = new();

    private string? _songsDirectory;
    private int? _songsDirectoryProcessId;
    private string? _cachedBeatmapFilePath;
    private BeatmapFileMetadata? _cachedBeatmapMetadata;

    public async Task<string?> GetCurrentBeatmapSetPathAsync(CancellationToken cancellationToken)
    {
        var info = await GetCurrentBeatmapInfoAsync(cancellationToken);
        return info?.BeatmapSetPath;
    }

    public Task<DetectedBeatmapInfo?> GetCurrentBeatmapInfoAsync(CancellationToken cancellationToken)
    {
        return Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var detected = TryReadCurrentBeatmapInfo(cancellationToken);
                if (detected is not null)
                {
                    return detected;
                }

                if (attempt < 2)
                {
                    await Task.Delay(40, cancellationToken);
                }
            }

            return null;
        }, cancellationToken);
    }

    private DetectedBeatmapInfo? TryReadCurrentBeatmapInfo(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var osuProcess = FindOsuProcess();
        if (osuProcess is null)
        {
            return null;
        }

        var songsDirectory = ResolveSongsDirectory(osuProcess);
        if (string.IsNullOrWhiteSpace(songsDirectory) || !Directory.Exists(songsDirectory))
        {
            return null;
        }

        lock (_sync)
        {
            try
            {
                _reader.TryRead(_reader.OsuMemoryAddresses.Beatmap);
            }
            catch
            {
                return null;
            }
        }

        string beatmapFilename;
        string beatmapFolder;
        float hp;
        float od;
        float ar;
        float cs;

        lock (_sync)
        {
            beatmapFilename = _reader.OsuMemoryAddresses.Beatmap.OsuFileName ?? string.Empty;
            beatmapFolder = _reader.OsuMemoryAddresses.Beatmap.FolderName ?? string.Empty;
            hp = _reader.OsuMemoryAddresses.Beatmap.Hp;
            od = _reader.OsuMemoryAddresses.Beatmap.Od;
            ar = _reader.OsuMemoryAddresses.Beatmap.Ar;
            cs = _reader.OsuMemoryAddresses.Beatmap.Cs;
        }

        if (!IsValidRelativeComponent(beatmapFilename) || !IsValidRelativeComponent(beatmapFolder))
        {
            return null;
        }

        var relativeFolder = beatmapFolder.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(relativeFolder))
        {
            return null;
        }

        var absoluteBeatmapPath = Path.Combine(songsDirectory, relativeFolder, beatmapFilename.Trim());
        if (!File.Exists(absoluteBeatmapPath))
        {
            var refreshedSongsDirectory = ForceRefreshSongsDirectory(osuProcess);
            if (string.IsNullOrWhiteSpace(refreshedSongsDirectory))
            {
                return null;
            }

            absoluteBeatmapPath = Path.Combine(refreshedSongsDirectory, relativeFolder, beatmapFilename.Trim());
            if (!File.Exists(absoluteBeatmapPath))
            {
                return null;
            }
        }

        var beatmapSetPath = Path.GetDirectoryName(absoluteBeatmapPath);
        if (string.IsNullOrWhiteSpace(beatmapSetPath))
        {
            return null;
        }

        var metadata = ResolveBeatmapFileMetadata(absoluteBeatmapPath, beatmapSetPath);
        return new DetectedBeatmapInfo
        {
            BeatmapSetPath = beatmapSetPath,
            BeatmapFilePath = absoluteBeatmapPath,
            Artist = metadata.Artist,
            Title = metadata.Title,
            DifficultyName = metadata.DifficultyName,
            BackgroundImagePath = ResolveBackgroundImagePath(beatmapSetPath, metadata.BackgroundRelativePath),
            Hp = hp,
            Od = od,
            Ar = ar,
            Cs = cs,
            StarRating = metadata.StarRating
        };
    }

    private BeatmapFileMetadata ResolveBeatmapFileMetadata(string beatmapFilePath, string beatmapSetPath)
    {
        lock (_metadataSync)
        {
            if (!string.IsNullOrWhiteSpace(_cachedBeatmapFilePath)
                && string.Equals(_cachedBeatmapFilePath, beatmapFilePath, StringComparison.OrdinalIgnoreCase)
                && _cachedBeatmapMetadata is not null)
            {
                return _cachedBeatmapMetadata;
            }
        }

        var parsed = ParseBeatmapFileMetadata(beatmapFilePath, beatmapSetPath);

        lock (_metadataSync)
        {
            _cachedBeatmapFilePath = beatmapFilePath;
            _cachedBeatmapMetadata = parsed;
        }

        return parsed;
    }

    private static BeatmapFileMetadata ParseBeatmapFileMetadata(string beatmapFilePath, string beatmapSetPath)
    {
        var folderName = Path.GetFileName(beatmapSetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var inferred = InferArtistAndTitleFromFolderName(folderName);
        var title = inferred.title;
        var artist = inferred.artist;
        var difficultyName = Path.GetFileNameWithoutExtension(beatmapFilePath);
        double? starRating = null;

        try
        {
            using var reader = new StreamReader(beatmapFilePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var parsedBeatmap = Beatmap.Read(reader);

            title = PreferUnicode(parsedBeatmap.TitleUnicode, parsedBeatmap.Title, title);
            artist = PreferUnicode(parsedBeatmap.ArtistUnicode, parsedBeatmap.Artist, artist);
            difficultyName = string.IsNullOrWhiteSpace(parsedBeatmap.Version)
                ? difficultyName
                : parsedBeatmap.Version.Trim();

            var starValue = new DiffCalc().Calc(parsedBeatmap, Mods.NoMod).Total;
            if (!double.IsNaN(starValue) && !double.IsInfinity(starValue))
            {
                starRating = Math.Round(starValue, 2, MidpointRounding.AwayFromZero);
            }
        }
        catch
        {
        }

        return new BeatmapFileMetadata
        {
            Artist = string.IsNullOrWhiteSpace(artist) ? "Unknown Artist" : artist,
            Title = string.IsNullOrWhiteSpace(title) ? difficultyName : title,
            DifficultyName = string.IsNullOrWhiteSpace(difficultyName) ? "Unknown Difficulty" : difficultyName,
            BackgroundRelativePath = TryExtractBackgroundRelativePath(beatmapFilePath),
            StarRating = starRating
        };
    }

    private static (string artist, string title) InferArtistAndTitleFromFolderName(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return ("Unknown Artist", "Unknown Title");
        }

        var normalized = folderName.Trim();
        var firstSpace = normalized.IndexOf(' ');
        if (firstSpace > 0 && int.TryParse(normalized[..firstSpace], out _))
        {
            normalized = normalized[(firstSpace + 1)..].Trim();
        }

        var separatorIndex = normalized.IndexOf(" - ", StringComparison.Ordinal);
        if (separatorIndex > 0 && separatorIndex < normalized.Length - 3)
        {
            return (normalized[..separatorIndex].Trim(), normalized[(separatorIndex + 3)..].Trim());
        }

        return ("Unknown Artist", normalized);
    }

    private static string PreferUnicode(string? unicodeValue, string? fallbackValue, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(unicodeValue))
        {
            return unicodeValue.Trim();
        }

        if (!string.IsNullOrWhiteSpace(fallbackValue))
        {
            return fallbackValue.Trim();
        }

        return fallback;
    }

    private static string? TryExtractBackgroundRelativePath(string beatmapFilePath)
    {
        try
        {
            var inEventsSection = false;
            foreach (var rawLine in File.ReadLines(beatmapFilePath, Encoding.UTF8))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    inEventsSection = line.Equals("[Events]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!inEventsSection)
                {
                    continue;
                }

                var typeToken = line.Split(',', 2)[0].Trim();
                if (!typeToken.Equals("0", StringComparison.Ordinal) && !typeToken.Equals("Background", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var candidate = ExtractPathToken(line);
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                candidate = candidate.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).Trim();
                if (!IsSafeRelativePath(candidate))
                {
                    continue;
                }

                var extension = Path.GetExtension(candidate);
                if (!SupportedBackgroundExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                return candidate;
            }
        }
        catch
        {
        }

        return null;
    }

    private static string? ExtractPathToken(string line)
    {
        var firstQuote = line.IndexOf('"');
        if (firstQuote >= 0)
        {
            var secondQuote = line.IndexOf('"', firstQuote + 1);
            if (secondQuote > firstQuote + 1)
            {
                return line[(firstQuote + 1)..secondQuote];
            }
        }

        var segments = line.Split(',');
        if (segments.Length >= 3)
        {
            return segments[2].Trim().Trim('"');
        }

        return null;
    }

    private static string? ResolveBackgroundImagePath(string beatmapSetPath, string? relativeBackgroundPath)
    {
        if (string.IsNullOrWhiteSpace(relativeBackgroundPath))
        {
            return null;
        }

        try
        {
            var normalizedSetPath = Path.GetFullPath(beatmapSetPath);
            var candidate = Path.GetFullPath(Path.Combine(normalizedSetPath, relativeBackgroundPath));
            if (!candidate.StartsWith(normalizedSetPath, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return File.Exists(candidate) ? candidate : null;
        }
        catch
        {
            return null;
        }
    }

    private string? ResolveSongsDirectory(Process osuProcess)
    {
        if (_songsDirectoryProcessId == osuProcess.Id
            && !string.IsNullOrWhiteSpace(_songsDirectory)
            && Directory.Exists(_songsDirectory))
        {
            return _songsDirectory;
        }

        return ForceRefreshSongsDirectory(osuProcess);
    }

    private string? ForceRefreshSongsDirectory(Process osuProcess)
    {
        var candidates = new List<string>();

        try
        {
            var executablePath = osuProcess.MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                var installDirectory = Path.GetDirectoryName(executablePath);
                if (!string.IsNullOrWhiteSpace(installDirectory))
                {
                    candidates.Add(Path.Combine(installDirectory, "Songs"));
                }
            }
        }
        catch
        {
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            candidates.Add(Path.Combine(localAppData, "osu!", "Songs"));
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            candidates.Add(Path.Combine(programFiles, "osu!", "Songs"));
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            candidates.Add(Path.Combine(programFilesX86, "osu!", "Songs"));
        }

        var resolved = candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path =>
            {
                try
                {
                    return Path.GetFullPath(path);
                }
                catch
                {
                    return null;
                }
            })
            .FirstOrDefault(path => path is not null && Directory.Exists(path));

        _songsDirectory = resolved;
        _songsDirectoryProcessId = osuProcess.Id;
        return resolved;
    }

    private static bool IsValidRelativeComponent(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (Path.IsPathRooted(value))
        {
            return false;
        }

        if (value.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        var invalidPathChars = Path.GetInvalidPathChars();
        if (value.Any(ch => invalidPathChars.Contains(ch)))
        {
            return false;
        }

        return true;
    }

    private static bool IsSafeRelativePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (Path.IsPathRooted(value))
        {
            return false;
        }

        if (value.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        var invalidPathChars = Path.GetInvalidPathChars();
        if (value.Any(ch => invalidPathChars.Contains(ch)))
        {
            return false;
        }

        return true;
    }

    private static Process? FindOsuProcess()
    {
        foreach (var processName in KnownProcessNames)
        {
            var processes = Process.GetProcessesByName(processName);
            var selected = processes.FirstOrDefault(process => process.MainWindowHandle != IntPtr.Zero);

            foreach (var process in processes)
            {
                if (selected is not null && process.Id == selected.Id)
                {
                    continue;
                }

                process.Dispose();
            }

            if (selected is not null)
            {
                return selected;
            }
        }

        return null;
    }

    private sealed class BeatmapFileMetadata
    {
        public required string Artist { get; init; }

        public required string Title { get; init; }

        public required string DifficultyName { get; init; }

        public string? BackgroundRelativePath { get; init; }

        public double? StarRating { get; init; }
    }
}

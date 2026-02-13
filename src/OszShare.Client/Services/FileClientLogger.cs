using System.Globalization;
using System.IO;
using System.Text;

namespace PuushShare.Client.Services;

public sealed class FileClientLogger : IClientLogger
{
    private readonly string _logDirectory;
    private readonly Func<DateTimeOffset> _nowProvider;
    private readonly int _retentionDays;
    private readonly object _sync = new();

    public FileClientLogger()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PuushShare",
                "logs"),
            () => DateTimeOffset.UtcNow,
            retentionDays: 7)
    {
    }

    public FileClientLogger(string logDirectory, Func<DateTimeOffset> nowProvider, int retentionDays)
    {
        _logDirectory = logDirectory;
        _nowProvider = nowProvider;
        _retentionDays = Math.Max(1, retentionDays);
    }

    public void Info(string eventName, string message)
    {
        Write("INFO", eventName, message);
    }

    public void Warn(string eventName, string message)
    {
        Write("WARN", eventName, message);
    }

    public void Error(string eventName, string message, Exception? exception = null)
    {
        var combinedMessage = exception is null
            ? message
            : $"{message} | {FormatExceptionSummary(exception)}";

        Write("ERROR", eventName, combinedMessage);
    }

    private void Write(string level, string eventName, string message)
    {
        try
        {
            var now = _nowProvider();
            Directory.CreateDirectory(_logDirectory);

            var path = Path.Combine(_logDirectory, $"client-{now:yyyyMMdd}.log");
            var line = $"{now:O} | {level} | {eventName} | {message}{Environment.NewLine}";

            lock (_sync)
            {
                File.AppendAllText(path, line, Encoding.UTF8);
            }

            CleanupOldLogs(now);
        }
        catch
        {
            // Avoid crashing the tray app when logging fails.
        }
    }

    private void CleanupOldLogs(DateTimeOffset now)
    {
        var cutoffDate = DateOnly.FromDateTime(now.AddDays(-_retentionDays).UtcDateTime);

        foreach (var path in Directory.EnumerateFiles(_logDirectory, "client-*.log"))
        {
            try
            {
                var fileName = Path.GetFileNameWithoutExtension(path);
                var datePart = fileName.Split('-', 2).LastOrDefault();
                if (!DateOnly.TryParseExact(
                    datePart,
                    "yyyyMMdd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var fileDate))
                {
                    continue;
                }

                if (fileDate < cutoffDate)
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }

    private static string FormatExceptionSummary(Exception exception)
    {
        var parts = new List<string>();
        Exception? current = exception;
        var depth = 0;

        while (current is not null && depth < 4)
        {
            parts.Add($"{current.GetType().Name}: {current.Message}");
            current = current.InnerException;
            depth++;
        }

        return $"exception_chain={string.Join(" => ", parts)}";
    }
}

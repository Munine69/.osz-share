using PuushShare.Client.Services;

namespace PuushShare.Client.Tests;

public sealed class FileClientLoggerTests
{
    [Fact]
    public void Info_WritesStructuredLogLine()
    {
        var root = Path.Combine(Path.GetTempPath(), "PuushShare.Client.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var now = new DateTimeOffset(2026, 2, 13, 10, 0, 0, TimeSpan.Zero);

        try
        {
            var logger = new FileClientLogger(root, () => now, retentionDays: 7);
            logger.Info("upload_started", "Preparing archive");

            var path = Path.Combine(root, "client-20260213.log");
            Assert.True(File.Exists(path));

            var line = File.ReadAllText(path);
            Assert.Contains("INFO", line);
            Assert.Contains("upload_started", line);
            Assert.Contains("Preparing archive", line);
            Assert.Contains("2026-02-13T10:00:00.0000000+00:00", line);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Info_DeletesLogsOlderThanRetention()
    {
        var root = Path.Combine(Path.GetTempPath(), "PuushShare.Client.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var now = new DateTimeOffset(2026, 2, 13, 10, 0, 0, TimeSpan.Zero);

        try
        {
            File.WriteAllText(Path.Combine(root, "client-20260201.log"), "old");
            File.WriteAllText(Path.Combine(root, "client-20260208.log"), "keep");

            var logger = new FileClientLogger(root, () => now, retentionDays: 7);
            logger.Info("tick", "cleanup");

            Assert.False(File.Exists(Path.Combine(root, "client-20260201.log")));
            Assert.True(File.Exists(Path.Combine(root, "client-20260208.log")));
            Assert.True(File.Exists(Path.Combine(root, "client-20260213.log")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}

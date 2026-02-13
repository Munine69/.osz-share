using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PuushShare.Client.Core.Abstractions;
using PuushShare.Client.Core.Models;
using PuushShare.Client.Core.Services;
using PuushShare.Client.Services;
using FormsClipboard = System.Windows.Forms.Clipboard;

namespace PuushShare.Client;

public partial class MainWindow : Window
{
    private static readonly int[] PreferredExpiryMinutes = [1, 3, 5, 10, 15, 30, 45, 60];

    private static readonly TimeSpan[] ClipboardRetryDelays =
    [
        TimeSpan.FromMilliseconds(25),
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(200)
    ];

    private readonly ClientSettings _settings;
    private readonly IOsuBeatmapProvider _beatmapProvider;
    private readonly IOszPackager _packager;
    private readonly IShareApiClient _shareApiClient;
    private readonly IClientLogger _logger;
    private readonly SemaphoreSlim _uploadLock = new(1, 1);
    private readonly DispatcherTimer _beatmapRefreshTimer;

    private bool _isBeatmapRefreshRunning;
    private DetectedBeatmapInfo? _detectedBeatmapInfo;
    private string? _renderedBeatmapFilePath;
    private string? _latestUrl;

    public MainWindow(
        ClientSettings settings,
        IOsuBeatmapProvider beatmapProvider,
        IOszPackager packager,
        IShareApiClient shareApiClient,
        IClientLogger logger)
    {
        _settings = settings;
        _beatmapProvider = beatmapProvider;
        _packager = packager;
        _shareApiClient = shareApiClient;
        _logger = logger;

        InitializeComponent();

        InitializeExpiryOptions();
        LinkTextBox.Text = "Share link will appear here after upload.";
        StatusTextBlock.Text = "Ready. Click 'Upload Current Beatmap'.";
        SetBeatmapUnavailableUi();

        _beatmapRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };

        Loaded += MainWindow_Loaded;
        _beatmapRefreshTimer.Tick += BeatmapRefreshTimer_Tick;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshDetectedBeatmapAsync();
        _beatmapRefreshTimer.Start();
    }

    private async void BeatmapRefreshTimer_Tick(object? sender, EventArgs e)
    {
        await RefreshDetectedBeatmapAsync();
    }

    private async Task RefreshDetectedBeatmapAsync()
    {
        if (_isBeatmapRefreshRunning)
        {
            return;
        }

        _isBeatmapRefreshRunning = true;
        try
        {
            var beatmapInfo = await _beatmapProvider.GetCurrentBeatmapInfoAsync(CancellationToken.None);
            if (IsValidBeatmapInfo(beatmapInfo))
            {
                _detectedBeatmapInfo = beatmapInfo;
                SetBeatmapDetectedUi(beatmapInfo!, DateTime.Now);
            }
            else
            {
                _detectedBeatmapInfo = null;
                SetBeatmapUnavailableUi();
            }
        }
        catch (Exception exception)
        {
            _detectedBeatmapInfo = null;
            SetBeatmapUnavailableUi();
            _logger.Warn("beatmap_refresh_failed", $"Beatmap detection refresh failed: {exception.Message}");
        }
        finally
        {
            _isBeatmapRefreshRunning = false;
        }
    }

    private async void UploadButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await _uploadLock.WaitAsync(0))
        {
            SetStatus("Upload is already in progress.");
            return;
        }

        string? archivePath = null;
        try
        {
            SetUiBusy(true);
            SetStatus("Detecting current beatmap...");

            var beatmapInfo = await ResolveCurrentBeatmapInfoAsync();
            if (!IsValidBeatmapInfo(beatmapInfo))
            {
                _logger.Warn("upload_no_beatmap", "Upload cancelled because no beatmap set could be detected.");
                SetStatus("Could not detect current beatmap. Open Song Select in osu and try again.");
                return;
            }

            var activeBeatmapInfo = beatmapInfo!;
            var expiryMinutes = Math.Clamp(GetSelectedExpiryMinutes(), 1, 60);
            SetBeatmapDetectedUi(activeBeatmapInfo, DateTime.Now);
            _logger.Info("upload_started", $"Packaging beatmap set path={activeBeatmapInfo.BeatmapSetPath} expiry_minutes={expiryMinutes}");

            SetStatus("Packaging .osz file...");
            archivePath = await _packager.PackageAsync(activeBeatmapInfo.BeatmapSetPath, CancellationToken.None);

            SetStatus($"Uploading (expires in {expiryMinutes} min)...");
            var result = await RetryHelper.ExecuteAsync(
                token => _shareApiClient.UploadAsync(archivePath, expiryMinutes, token),
                CancellationToken.None);

            _latestUrl = result.Url;
            LinkTextBox.Text = result.Url;
            _logger.Info("upload_succeeded", $"Upload completed id={result.Id} url={result.Url}");

            if (TrySetClipboardTextWithRetry(result.Url, out var clipboardError))
            {
                SetStatus($"Upload complete. Link copied to clipboard. Expires at {result.ExpiresAt.LocalDateTime:HH:mm:ss}.");
            }
            else
            {
                _logger.Warn("clipboard_set_failed", $"Upload succeeded but clipboard update failed: {clipboardError}");
                SetStatus("Upload complete. Clipboard is busy; click 'Copy Link'.");
            }
        }
        catch (Exception exception)
        {
            _logger.Error("upload_failed", $"Upload failed. server={_settings.ServerBaseUrl}", exception);
            SetStatus($"Upload failed: {exception.Message}");
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(archivePath) && File.Exists(archivePath))
            {
                try
                {
                    File.Delete(archivePath);
                }
                catch
                {
                }
            }

            SetUiBusy(false);
            _uploadLock.Release();
        }
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_latestUrl))
        {
            SetStatus("No link available to copy.");
            return;
        }

        if (TrySetClipboardTextWithRetry(_latestUrl, out var clipboardError))
        {
            _logger.Info("copy_link", $"Copied URL to clipboard: {_latestUrl}");
            SetStatus("Link copied to clipboard.");
        }
        else
        {
            _logger.Warn("copy_link_failed", $"Clipboard update failed: {clipboardError}");
            SetStatus("Clipboard is busy. Try again.");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _beatmapRefreshTimer.Stop();
        _beatmapRefreshTimer.Tick -= BeatmapRefreshTimer_Tick;
        Loaded -= MainWindow_Loaded;
        _uploadLock.Dispose();
        base.OnClosed(e);
    }

    private async Task<DetectedBeatmapInfo?> ResolveCurrentBeatmapInfoAsync()
    {
        var liveBeatmapInfo = await _beatmapProvider.GetCurrentBeatmapInfoAsync(CancellationToken.None);
        if (IsValidBeatmapInfo(liveBeatmapInfo))
        {
            _detectedBeatmapInfo = liveBeatmapInfo;
            return liveBeatmapInfo;
        }

        if (IsValidBeatmapInfo(_detectedBeatmapInfo))
        {
            return _detectedBeatmapInfo;
        }

        return null;
    }

    private void SetBeatmapDetectedUi(DetectedBeatmapInfo beatmapInfo, DateTime updatedAt)
    {
        if (!string.Equals(_renderedBeatmapFilePath, beatmapInfo.BeatmapFilePath, StringComparison.OrdinalIgnoreCase))
        {
            _renderedBeatmapFilePath = beatmapInfo.BeatmapFilePath;
            _logger.Info("beatmap_detected", $"Detected beatmap file={beatmapInfo.BeatmapFilePath}");
        }

        DetectedArtistTextBlock.Text = string.IsNullOrWhiteSpace(beatmapInfo.Artist) ? "Unknown Artist" : beatmapInfo.Artist;
        DetectedTitleTextBlock.Text = string.IsNullOrWhiteSpace(beatmapInfo.Title) ? "Unknown Title" : beatmapInfo.Title;
        DetectedDifficultyTextBlock.Text = string.IsNullOrWhiteSpace(beatmapInfo.DifficultyName)
            ? "[Unknown Difficulty]"
            : $"[{beatmapInfo.DifficultyName}]";
        HpValueTextBlock.Text = beatmapInfo.Hp.ToString("0.0");
        OdValueTextBlock.Text = beatmapInfo.Od.ToString("0.0");
        SrValueTextBlock.Text = beatmapInfo.StarRating?.ToString("0.00") ?? "-";
        DetectedBeatmapUpdatedAtTextBlock.Text = $"Last checked: {updatedAt:HH:mm:ss}";

        var backgroundImage = TryLoadBackgroundImage(beatmapInfo.BackgroundImagePath);
        BeatmapBackgroundImage.Source = backgroundImage;
        BeatmapBackgroundFallbackTextBlock.Visibility = backgroundImage is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetBeatmapUnavailableUi()
    {
        _renderedBeatmapFilePath = null;
        DetectedArtistTextBlock.Text = "Not detected";
        DetectedTitleTextBlock.Text = "Open osu! song select to detect beatmap automatically.";
        DetectedDifficultyTextBlock.Text = "[Difficulty unavailable]";
        HpValueTextBlock.Text = "-";
        OdValueTextBlock.Text = "-";
        SrValueTextBlock.Text = "-";
        DetectedBeatmapUpdatedAtTextBlock.Text = $"Last checked: {DateTime.Now:HH:mm:ss}";
        BeatmapBackgroundImage.Source = null;
        BeatmapBackgroundFallbackTextBlock.Visibility = Visibility.Visible;
    }

    private static BitmapImage? TryLoadBackgroundImage(string? backgroundImagePath)
    {
        if (string.IsNullOrWhiteSpace(backgroundImagePath) || !File.Exists(backgroundImagePath))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(backgroundImagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsValidBeatmapInfo(DetectedBeatmapInfo? beatmapInfo)
    {
        return beatmapInfo is not null
            && !string.IsNullOrWhiteSpace(beatmapInfo.BeatmapSetPath)
            && Directory.Exists(beatmapInfo.BeatmapSetPath)
            && !string.IsNullOrWhiteSpace(beatmapInfo.BeatmapFilePath)
            && File.Exists(beatmapInfo.BeatmapFilePath);
    }

    private void SetUiBusy(bool busy)
    {
        UploadButton.IsEnabled = !busy;
        UploadButton.Content = busy ? "Uploading..." : "Upload Current Beatmap";
        ExpiryComboBox.IsEnabled = !busy;
        CopyButton.IsEnabled = !busy && !string.IsNullOrWhiteSpace(_latestUrl);
    }

    private void SetStatus(string text)
    {
        StatusTextBlock.Text = text;
    }

    private static bool TrySetClipboardTextWithRetry(string text, out string? error)
    {
        error = null;
        for (var attempt = 0; attempt <= ClipboardRetryDelays.Length; attempt++)
        {
            var setError = TrySetClipboardOnStaThread(text);
            if (setError is null)
            {
                return true;
            }

            error = setError;
            if (attempt == ClipboardRetryDelays.Length)
            {
                return false;
            }

            Thread.Sleep(ClipboardRetryDelays[attempt]);
        }

        return false;
    }

    private static string? TrySetClipboardOnStaThread(string text)
    {
        string? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                FormsClipboard.SetDataObject(text, copy: true, retryTimes: 40, retryDelay: 100);
            }
            catch (Exception exception) when (exception is COMException or ExternalException)
            {
                error = exception.Message;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        return error;
    }

    private void InitializeExpiryOptions()
    {
        var minExpiry = Math.Max(1, _settings.MinExpiryMinutes);
        var maxExpiry = Math.Min(60, Math.Max(minExpiry, _settings.MaxExpiryMinutes));
        var defaultExpiry = Math.Clamp(_settings.DefaultExpiryMinutes, minExpiry, maxExpiry);

        var options = PreferredExpiryMinutes
            .Where(minutes => minutes >= minExpiry && minutes <= maxExpiry)
            .Select(minutes => new ExpiryOption(minutes))
            .ToList();

        if (!options.Any(option => option.Minutes == defaultExpiry))
        {
            options.Add(new ExpiryOption(defaultExpiry));
            options = options.OrderBy(option => option.Minutes).ToList();
        }

        if (options.Count == 0)
        {
            options.Add(new ExpiryOption(defaultExpiry));
        }

        ExpiryComboBox.ItemsSource = options;
        ExpiryComboBox.SelectedItem = options.FirstOrDefault(option => option.Minutes == defaultExpiry) ?? options[0];
    }

    private int GetSelectedExpiryMinutes()
    {
        if (ExpiryComboBox.SelectedItem is ExpiryOption selected)
        {
            return selected.Minutes;
        }

        return Math.Clamp(_settings.DefaultExpiryMinutes, 1, 60);
    }

    private sealed class ExpiryOption
    {
        public ExpiryOption(int minutes)
        {
            Minutes = minutes;
        }

        public int Minutes { get; }

        public override string ToString()
        {
            return $"{Minutes} min";
        }
    }
}

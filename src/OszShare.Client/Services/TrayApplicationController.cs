using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Threading;
using PuushShare.Client.Core.Abstractions;
using PuushShare.Client.Core.Models;
using PuushShare.Client.Core.Services;
using FormsClipboard = System.Windows.Forms.Clipboard;
using WpfClipboard = System.Windows.Clipboard;

namespace PuushShare.Client.Services;

public sealed class TrayApplicationController : IDisposable
{
    private static readonly TimeSpan[] ClipboardRetryDelays =
    [
        TimeSpan.FromMilliseconds(25),
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(200)
    ];

    private readonly IClientSettingsStore _settingsStore;
    private readonly ClientSettings _settings;
    private readonly IOsuBeatmapProvider _beatmapProvider;
    private readonly IOszPackager _packager;
    private readonly IShareApiClient _shareApiClient;
    private readonly IClientLogger _logger;
    private readonly Action _shutdownAction;

    private readonly NotifyIcon _notifyIcon;
    private readonly DispatcherTimer _detectTimer;
    private readonly SemaphoreSlim _uploadLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();

    private GlobalHotkeyManager? _hotkeyManager;
    private string? _currentBeatmapSetPath;
    private string? _lastShareUrl;
    private string? _lastLoggedBeatmapSetPath;
    private bool _detectFailureLogged;
    private bool _detectInProgress;
    private bool _disposed;

    public TrayApplicationController(
        IClientSettingsStore settingsStore,
        ClientSettings settings,
        IOsuBeatmapProvider beatmapProvider,
        IOszPackager packager,
        IShareApiClient shareApiClient,
        IClientLogger logger,
        Action shutdownAction)
    {
        _settingsStore = settingsStore;
        _settings = settings;
        _beatmapProvider = beatmapProvider;
        _packager = packager;
        _shareApiClient = shareApiClient;
        _logger = logger;
        _shutdownAction = shutdownAction;

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "PuushShare",
            Visible = false
        };

        _detectTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _detectTimer.Tick += OnDetectTimerTick;
    }

    public void Start()
    {
        var menu = BuildContextMenu();
        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.Visible = true;
        _notifyIcon.ShowBalloonTip(
            2500,
            "PuushShare",
            $"Ready. Press Ctrl+Shift+U to upload selected map.\nServer: {_settings.ServerBaseUrl}",
            ToolTipIcon.Info);
        _logger.Info("tray_started", $"Tray controller started. server={_settings.ServerBaseUrl}");

        _detectTimer.Start();
        TryRegisterHotkey();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _detectTimer.Stop();
        _detectTimer.Tick -= OnDetectTimerTick;

        _cts.Cancel();
        _cts.Dispose();

        if (_hotkeyManager is not null)
        {
            _hotkeyManager.HotkeyPressed -= OnHotkeyPressed;
            _hotkeyManager.Dispose();
        }
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _uploadLock.Dispose();

        _logger.Info("tray_disposed", "Tray controller disposed.");
        _disposed = true;
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        var uploadItem = new ToolStripMenuItem("Upload current map");
        uploadItem.Click += async (_, _) => await UploadCurrentBeatmapAsync();
        menu.Items.Add(uploadItem);

        var openLastUrlItem = new ToolStripMenuItem("Open last URL");
        openLastUrlItem.Click += (_, _) => OpenLastUrl();
        menu.Items.Add(openLastUrlItem);

        var copyLastUrlItem = new ToolStripMenuItem("Copy last URL");
        copyLastUrlItem.Click += (_, _) => CopyLastUrl();
        menu.Items.Add(copyLastUrlItem);

        var openSettingsItem = new ToolStripMenuItem("Open settings folder");
        openSettingsItem.Click += (_, _) => OpenSettingsFolder();
        menu.Items.Add(openSettingsItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => _shutdownAction();
        menu.Items.Add(exitItem);

        return menu;
    }

    private void TryRegisterHotkey()
    {
        try
        {
            _hotkeyManager = new GlobalHotkeyManager(Key.U, ModifierKeys.Control | ModifierKeys.Shift);
            _hotkeyManager.HotkeyPressed += OnHotkeyPressed;
            _logger.Info("hotkey_registered", "Global hotkey Ctrl+Shift+U registered.");
        }
        catch (Exception exception)
        {
            _logger.Error("hotkey_register_failed", "Global hotkey registration failed.", exception);
            _notifyIcon.ShowBalloonTip(3000, "PuushShare", $"Hotkey registration failed: {exception.Message}", ToolTipIcon.Warning);
        }
    }

    private async Task DetectCurrentBeatmapAsync()
    {
        if (_detectInProgress || _disposed)
        {
            return;
        }

        _detectInProgress = true;
        try
        {
            var detectedPath = await _beatmapProvider.GetCurrentBeatmapSetPathAsync(_cts.Token);
            if (!string.IsNullOrWhiteSpace(detectedPath) && Directory.Exists(detectedPath))
            {
                _currentBeatmapSetPath = detectedPath;
                if (!string.Equals(_lastLoggedBeatmapSetPath, detectedPath, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Info("beatmap_detected", $"Detected beatmap set: {detectedPath}");
                    _lastLoggedBeatmapSetPath = detectedPath;
                }

                _detectFailureLogged = false;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            if (!_detectFailureLogged)
            {
                _logger.Warn("beatmap_detect_failed", "Beatmap detection pass failed.");
                _detectFailureLogged = true;
            }
        }
        finally
        {
            _detectInProgress = false;
        }
    }

    private async void OnDetectTimerTick(object? sender, EventArgs e)
    {
        await DetectCurrentBeatmapAsync();
    }

    private async void OnHotkeyPressed(object? sender, EventArgs e)
    {
        _logger.Info("hotkey_pressed", "Hotkey Ctrl+Shift+U pressed.");
        _notifyIcon.ShowBalloonTip(1000, "PuushShare", "Hotkey received. Starting upload...", ToolTipIcon.Info);
        await UploadCurrentBeatmapAsync();
    }

    private async Task UploadCurrentBeatmapAsync()
    {
        if (!await _uploadLock.WaitAsync(0))
        {
            _logger.Warn("upload_skipped", "Upload request skipped because another upload is in progress.");
            _notifyIcon.ShowBalloonTip(2000, "PuushShare", "Upload is already in progress.", ToolTipIcon.Info);
            return;
        }

        string? archivePath = null;
        try
        {
            var beatmapSetPath = _currentBeatmapSetPath;
            if (string.IsNullOrWhiteSpace(beatmapSetPath) || !Directory.Exists(beatmapSetPath))
            {
                beatmapSetPath = await _beatmapProvider.GetCurrentBeatmapSetPathAsync(_cts.Token);
            }

            if (string.IsNullOrWhiteSpace(beatmapSetPath) || !Directory.Exists(beatmapSetPath))
            {
                _logger.Warn("upload_no_beatmap", "Upload cancelled because no beatmap set could be detected.");
                _notifyIcon.ShowBalloonTip(
                    5000,
                    "PuushShare",
                    "Could not detect current osu beatmap set. Check client logs in %AppData%\\PuushShare\\logs.",
                    ToolTipIcon.Warning);
                return;
            }

            var validatedPath = beatmapSetPath;
            _logger.Info("upload_started", $"Packaging beatmap set path={validatedPath}");
            archivePath = await _packager.PackageAsync(validatedPath, _cts.Token);

            var result = await RetryHelper.ExecuteAsync(
                token => _shareApiClient.UploadAsync(archivePath, _settings.DefaultExpiryMinutes, token),
                _cts.Token);

            _lastShareUrl = result.Url;
            _logger.Info("upload_succeeded", $"Upload completed id={result.Id} url={result.Url}");

            if (TrySetClipboardTextWithRetry(result.Url, out var clipboardError))
            {
                _notifyIcon.ShowBalloonTip(
                    4000,
                    "PuushShare",
                    $"Upload complete. URL copied to clipboard.\nServer: {_settings.ServerBaseUrl}\nExpires: {result.ExpiresAt.LocalDateTime:HH:mm:ss}",
                    ToolTipIcon.Info);
            }
            else
            {
                _logger.Warn("clipboard_set_failed", $"Upload succeeded but clipboard update failed: {clipboardError}");
                _notifyIcon.ShowBalloonTip(
                    5000,
                    "PuushShare",
                    $"Upload complete but clipboard was busy.\nUse tray menu: Copy last URL.\nServer: {_settings.ServerBaseUrl}",
                    ToolTipIcon.Warning);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Warn("upload_cancelled", "Upload cancelled.");
        }
        catch (Exception exception)
        {
            _logger.Error("upload_failed", $"Upload failed. server={_settings.ServerBaseUrl}", exception);
            _notifyIcon.ShowBalloonTip(
                5000,
                "PuushShare",
                $"Upload failed on {_settings.ServerBaseUrl}: {exception.Message}",
                ToolTipIcon.Error);
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

            _uploadLock.Release();
        }
    }

    private void OpenLastUrl()
    {
        if (string.IsNullOrWhiteSpace(_lastShareUrl))
        {
            _logger.Warn("open_last_url_empty", "No last URL to open.");
            _notifyIcon.ShowBalloonTip(2000, "PuushShare", "No URL available yet.", ToolTipIcon.Info);
            return;
        }

        _logger.Info("open_last_url", $"Opening URL: {_lastShareUrl}");
        Process.Start(new ProcessStartInfo
        {
            FileName = _lastShareUrl,
            UseShellExecute = true
        });
    }

    private void CopyLastUrl()
    {
        if (string.IsNullOrWhiteSpace(_lastShareUrl))
        {
            _logger.Warn("copy_last_url_empty", "No last URL to copy.");
            _notifyIcon.ShowBalloonTip(2000, "PuushShare", "No URL available yet.", ToolTipIcon.Info);
            return;
        }

        if (TrySetClipboardTextWithRetry(_lastShareUrl, out var clipboardError))
        {
            _logger.Info("copy_last_url", $"Copied URL to clipboard: {_lastShareUrl}");
            _notifyIcon.ShowBalloonTip(2000, "PuushShare", "Last URL copied to clipboard.", ToolTipIcon.Info);
        }
        else
        {
            _logger.Warn("copy_last_url_failed", $"Clipboard update failed: {clipboardError}");
            _notifyIcon.ShowBalloonTip(3000, "PuushShare", "Clipboard is busy. Try again in a moment.", ToolTipIcon.Warning);
        }
    }

    private void OpenSettingsFolder()
    {
        if (_settingsStore is not JsonClientSettingsStore jsonStore)
        {
            _logger.Warn("open_settings_folder_skipped", "Settings store is not JsonClientSettingsStore.");
            return;
        }

        var folderPath = Path.GetDirectoryName(jsonStore.SettingsPath);
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            _logger.Warn("open_settings_folder_failed", "Settings folder path is empty.");
            return;
        }

        Directory.CreateDirectory(folderPath);
        _logger.Info("open_settings_folder", $"Opening settings folder: {folderPath}");
        Process.Start(new ProcessStartInfo
        {
            FileName = folderPath,
            UseShellExecute = true
        });
    }

    private static bool TrySetClipboardTextWithRetry(string text, out string? error)
    {
        error = null;

        // Primary path: dedicated STA thread + WinForms clipboard API with built-in retries.
        var staError = TrySetClipboardOnStaThread(text);
        if (staError is null)
        {
            return true;
        }

        // Secondary fallback: current thread retries.
        error = staError;
        for (var attempt = 0; attempt <= ClipboardRetryDelays.Length; attempt++)
        {
            try
            {
                WpfClipboard.SetText(text);
                return true;
            }
            catch (COMException exception)
            {
                error = exception.Message;
            }
            catch (ExternalException exception)
            {
                error = exception.Message;
            }

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
}

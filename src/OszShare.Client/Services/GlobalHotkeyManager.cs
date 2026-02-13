using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace PuushShare.Client.Services;

public sealed class GlobalHotkeyManager : IDisposable
{
    private const int WmHotKey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    private readonly int _id;
    private readonly HwndSource _source;
    private bool _disposed;
    private bool _registered;

    public GlobalHotkeyManager(Key key, ModifierKeys modifiers)
    {
        _id = Random.Shared.Next(1, int.MaxValue);

        var parameters = new HwndSourceParameters("PuushShareHotkeyWindow")
        {
            PositionX = 0,
            PositionY = 0,
            Width = 0,
            Height = 0,
            WindowStyle = unchecked((int)0x80000000)
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        var modifierFlags = ConvertModifierFlags(modifiers);

        _registered = RegisterHotKey(_source.Handle, _id, modifierFlags, virtualKey);
        if (!_registered)
        {
            _source.RemoveHook(WndProc);
            _source.Dispose();
            throw new InvalidOperationException("Failed to register global hotkey.");
        }
    }

    public event EventHandler? HotkeyPressed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_registered)
        {
            UnregisterHotKey(_source.Handle, _id);
            _registered = false;
        }

        _source.RemoveHook(WndProc);
        _source.Dispose();
        _disposed = true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotKey || wParam.ToInt32() != _id)
        {
            return IntPtr.Zero;
        }

        HotkeyPressed?.Invoke(this, EventArgs.Empty);
        handled = true;
        return IntPtr.Zero;
    }

    private static uint ConvertModifierFlags(ModifierKeys modifiers)
    {
        uint flags = 0;

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            flags |= ModAlt;
        }

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            flags |= ModControl;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            flags |= ModShift;
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            flags |= ModWin;
        }

        return flags;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

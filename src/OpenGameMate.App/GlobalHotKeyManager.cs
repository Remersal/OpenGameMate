using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using OpenGameMate.Configuration;

namespace OpenGameMate.App;

internal sealed class GlobalHotKeyManager : IDisposable
{
    private const int WmHotKey = 0x0312;
    private const int FirstHotKeyId = 0x4F47;
    private const int SecondHotKeyId = 0x4F48;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    private readonly nint _windowHandle;
    private readonly HwndSource _source;
    private int? _activeId;
    private ManualCaptureHotKey? _activeHotKey;
    private bool _disposed;

    public GlobalHotKeyManager(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _windowHandle = new WindowInteropHelper(window).Handle;
        if (_windowHandle == nint.Zero)
        {
            throw new InvalidOperationException("The window handle is not available.");
        }

        _source = HwndSource.FromHwnd(_windowHandle)
            ?? throw new InvalidOperationException("The WPF window source is not available.");
        _source.AddHook(WindowProcedure);
    }

    public event EventHandler? Pressed;

    public ManualCaptureHotKey? ActiveHotKey => _activeHotKey;

    public bool TrySet(ManualCaptureHotKey hotKey, out int errorCode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(hotKey);
        errorCode = 0;
        if (_activeHotKey == hotKey)
        {
            return true;
        }

        var candidateId = _activeId == FirstHotKeyId ? SecondHotKeyId : FirstHotKeyId;
        var modifiers = ToNativeModifiers(hotKey.Modifiers) | ModNoRepeat;
        var virtualKey = ToVirtualKey(hotKey.KeyName);
        if (!RegisterHotKey(_windowHandle, candidateId, modifiers, virtualKey))
        {
            errorCode = Marshal.GetLastWin32Error();
            return false;
        }

        if (_activeId is int previousId)
        {
            _ = UnregisterHotKey(_windowHandle, previousId);
        }

        _activeId = candidateId;
        _activeHotKey = hotKey;
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_activeId is int activeId)
        {
            _ = UnregisterHotKey(_windowHandle, activeId);
        }

        _source.RemoveHook(WindowProcedure);
        Pressed = null;
    }

    private nint WindowProcedure(
        nint hwnd,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (message == WmHotKey && _activeId is int activeId && wParam.ToInt32() == activeId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        return nint.Zero;
    }

    private static uint ToNativeModifiers(HotKeyModifiers modifiers)
    {
        var result = 0u;
        if (modifiers.HasFlag(HotKeyModifiers.Alt))
        {
            result |= ModAlt;
        }

        if (modifiers.HasFlag(HotKeyModifiers.Control))
        {
            result |= ModControl;
        }

        if (modifiers.HasFlag(HotKeyModifiers.Shift))
        {
            result |= ModShift;
        }

        if (modifiers.HasFlag(HotKeyModifiers.Windows))
        {
            result |= ModWin;
        }

        return result;
    }

    private static uint ToVirtualKey(string keyName)
    {
        if (keyName.Length == 1)
        {
            return keyName[0];
        }

        if (keyName[0] == 'F' && int.TryParse(keyName.AsSpan(1), out var functionKey))
        {
            return (uint)(0x70 + functionKey - 1);
        }

        throw new ArgumentOutOfRangeException(nameof(keyName), "Unsupported hotkey key name.");
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint windowHandle, int id);
}

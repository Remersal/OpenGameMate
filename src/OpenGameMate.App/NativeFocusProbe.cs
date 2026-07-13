using System.Runtime.InteropServices;
using OpenGameMate.Core;

namespace OpenGameMate.App;

internal static class NativeFocusProbe
{
    public static FocusSnapshot Capture()
    {
        var foregroundWindow = GetForegroundWindow();
        _ = GetWindowThreadProcessId(foregroundWindow, out var processId);
        if (!GetCursorPos(out var point))
        {
            throw new InvalidOperationException("Unable to read the cursor position.");
        }

        return new(foregroundWindow.ToInt64(), processId, point.X, point.Y);
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }
}

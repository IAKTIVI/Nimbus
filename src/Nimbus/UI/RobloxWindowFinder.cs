using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Nimbus.UI;

/// <summary>
/// Roblox's main window, remembered between lookups. Process.MainWindowHandle walks every window on the desktop,
/// and the overlays ask several times a second while you play; this only looks again once the window is gone or hidden.
/// </summary>
internal sealed class RobloxWindowFinder(Process roblox)
{
    private nint _window;

    /// <summary>The window, or 0 if Roblox has exited or has none yet.</summary>
    public nint Find()
    {
        if (_window != 0 && IsWindow(_window) && IsWindowVisible(_window))
            return _window;

        roblox.Refresh();
        _window = roblox.HasExited ? 0 : roblox.MainWindowHandle;
        return _window;
    }

    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
}

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Nimbus.UI;

/// <summary>Mica window backgrounds (like Lumen) and rounded corners via DWM.</summary>
internal static partial class Glass
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmwcpRound = 2;
    private const int DwmsbtMainWindow = 2; // Mica
    private const int GwlStyle = -16;
    private const nint WsSysMenu = 0x00080000;

    /// <summary>System backdrops need Windows 11 22H2 or newer; older systems get solid colors.</summary>
    public static bool IsSupported { get; } = Environment.OSVersion.Version.Build >= 22621;

    public static void Attach(Window window) => window.SourceInitialized += (_, _) => Apply(window);

    public static void Apply(Window window)
    {
        nint hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0)
            return;

        int dark = ThemeManager.IsDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));

        int corner = DwmwcpRound;
        DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref corner, sizeof(int));

        // With the frame extended into the window, DWM paints its own caption buttons on top of ours
        // unless the window has no system menu.
        nint style = GetWindowLongPtr(hwnd, GwlStyle);
        SetWindowLongPtr(hwnd, GwlStyle, style & ~WsSysMenu);

        if (!IsSupported)
            return;

        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
            target.BackgroundColor = Colors.Transparent;

        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);

        int backdrop = DwmsbtMainWindow;
        DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left, Right, Top, Bottom;
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmExtendFrameIntoClientArea(nint hwnd, ref Margins margins);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr(nint hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial nint SetWindowLongPtr(nint hwnd, int index, nint value);
}

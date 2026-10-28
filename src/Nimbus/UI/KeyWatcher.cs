using System.Runtime.InteropServices;

namespace Nimbus.UI;

/// <summary>
/// Notices one key anywhere in Windows (the widget menu key) without taking it: every key still reaches Roblox and
/// other apps, so nothing clashes. Runs its own small message loop on a background thread, so a busy Nimbus window
/// can never make typing lag.
/// </summary>
internal sealed class KeyWatcher : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100, WmKeyUp = 0x0101, WmSysKeyDown = 0x0104, WmSysKeyUp = 0x0105;
    private const uint WmQuit = 0x0012;

    private readonly Thread _thread;
    private readonly HookProc _proc; // kept alive: Windows calls it for as long as the hook is set
    private uint _threadId;
    private volatile int _key;
    private bool _down;

    /// <summary>The key was pressed (once per press, not while held). Raised on the watcher thread.</summary>
    public event Action? Pressed;

    public KeyWatcher(int virtualKey)
    {
        _key = virtualKey;
        _proc = HookCallback;
        _thread = new Thread(Run) { IsBackground = true, Name = "Nimbus key watcher" };
        _thread.Start();
    }

    public int Key
    {
        get => _key;
        set => _key = value;
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        nint hook = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(null), 0);
        if (hook == 0)
            return;

        while (GetMessage(out _, 0, 0, 0) > 0)
        {
        }

        UnhookWindowsHookEx(hook);
    }

    private nint HookCallback(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            int vk = Marshal.ReadInt32(lParam);
            if (vk == _key)
            {
                int message = (int)wParam;
                if (message is WmKeyDown or WmSysKeyDown && !_down)
                {
                    _down = true;
                    Pressed?.Invoke();
                }
                else if (message is WmKeyUp or WmSysKeyUp)
                {
                    _down = false;
                }
            }
        }

        return CallNextHookEx(0, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_threadId != 0)
            PostThreadMessage(_threadId, WmQuit, 0, 0);
    }

    private delegate nint HookProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint Hwnd;
        public uint Message;
        public nint WParam, LParam;
        public uint Time;
        public int X, Y;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, HookProc proc, nint module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern int GetMessage(out Msg msg, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint threadId, uint msg, nint wParam, nint lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
}

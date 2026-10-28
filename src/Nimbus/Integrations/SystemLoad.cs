using System.Runtime.InteropServices;

namespace Nimbus.Integrations;

/// <summary>
/// How busy the PC is, for the CPU and GPU widgets: CPU from Windows' system times, GPU from the same
/// "GPU Engine" performance counters Task Manager uses. Nothing here looks at Roblox itself.
/// </summary>
internal sealed partial class SystemLoad : IDisposable
{
    private const uint PdhFmtDouble = 0x00000200;
    private const uint PdhFmtNoCap100 = 0x00008000;
    private const uint PdhMoreData = 0x800007D2;

    private long _idle, _kernel, _user;
    private nint _query;
    private nint _gpuCounter;

    public SystemLoad()
    {
        Cpu(); // the first reading only sets the starting point

        if (PdhOpenQuery(null, 0, out _query) == 0 &&
            PdhAddEnglishCounter(_query, @"\GPU Engine(*engtype_3D)\Utilization Percentage", 0, out _gpuCounter) == 0)
        {
            PdhCollectQueryData(_query);
        }
        else
        {
            _gpuCounter = 0;
        }
    }

    /// <summary>Percent of all CPU cores in use since the last call.</summary>
    public int? Cpu()
    {
        if (!GetSystemTimes(out long idle, out long kernel, out long user))
            return null;

        long idleDelta = idle - _idle, total = kernel - _kernel + (user - _user); // kernel time includes idle time
        (_idle, _kernel, _user) = (idle, kernel, user);
        return total <= 0 ? null : (int)Math.Round(Math.Clamp(100.0 * (total - idleDelta) / total, 0, 100));
    }

    /// <summary>Percent of the graphics card's 3D engine in use (all apps together), or null if Windows doesn't report it.</summary>
    public int? Gpu()
    {
        if (_gpuCounter == 0 || PdhCollectQueryData(_query) != 0)
            return null;

        uint size = 0;
        if (PdhGetFormattedCounterArray(_gpuCounter, PdhFmtDouble | PdhFmtNoCap100, ref size, out _, 0) != PdhMoreData || size == 0)
            return null;

        nint buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArray(_gpuCounter, PdhFmtDouble | PdhFmtNoCap100, ref size, out uint count, buffer) != 0)
                return null;

            // PDH_FMT_COUNTERVALUE_ITEM: name pointer, then { status, (padding), double }.
            int itemSize = IntPtr.Size + 16;
            double sum = 0;
            for (int i = 0; i < count; i++)
            {
                nint item = buffer + i * itemSize;
                if (Marshal.ReadInt32(item + IntPtr.Size) == 0)
                    sum += BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item + IntPtr.Size + 8));
            }
            return (int)Math.Round(Math.Clamp(sum, 0, 100));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        if (_query != 0)
            PdhCloseQuery(_query);
        _query = 0;
        _gpuCounter = 0;
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemTimes(out long idle, out long kernel, out long user);

    [LibraryImport("pdh.dll", EntryPoint = "PdhOpenQueryW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint PdhOpenQuery(string? dataSource, nint userData, out nint query);

    [LibraryImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint PdhAddEnglishCounter(nint query, string path, nint userData, out nint counter);

    [LibraryImport("pdh.dll")]
    private static partial uint PdhCollectQueryData(nint query);

    [LibraryImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW")]
    private static partial uint PdhGetFormattedCounterArray(nint counter, uint format, ref uint bufferSize, out uint itemCount, nint buffer);

    [LibraryImport("pdh.dll")]
    private static partial uint PdhCloseQuery(nint query);
}

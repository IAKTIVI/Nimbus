using System.Runtime.InteropServices;
using Nimbus.Core;

namespace Nimbus.Integrations;

public enum RepeatMode
{
    None = 0,
    Track = 1,
    List = 2,
}

/// <summary>What's playing on this PC, as Windows reports it (the same info as the volume-key media flyout).</summary>
public sealed record MediaState(
    string Title,
    string Artist,
    string App,
    byte[]? Cover,
    bool IsPlaying,
    TimeSpan Position,
    DateTimeOffset PositionAt,
    TimeSpan Duration,
    bool? Shuffle,
    RepeatMode? Repeat,
    bool CanPrevious,
    bool CanNext,
    bool CanShuffle,
    bool CanRepeat,
    bool CanSeek = false)
{
    /// <summary>Where playback is right now, moving the last reported position forward while playing.</summary>
    public TimeSpan PositionNow(DateTimeOffset now)
    {
        var position = Position + (IsPlaying ? now - PositionAt : TimeSpan.Zero);
        if (position < TimeSpan.Zero)
            return TimeSpan.Zero;
        return Duration > TimeSpan.Zero && position > Duration ? Duration : position;
    }
}

/// <summary>
/// Reads and controls the PC's current media session (Spotify app, YouTube or any site in a browser, other players)
/// through Windows' "Global System Media Transport Controls". No account or sign-in needed.
///
/// Called through the WinRT ABI directly instead of Microsoft's 25 MB projection package, which keeps Nimbus small.
/// Interface IDs and vtable slots were read from that package (Microsoft.Windows.SDK.NET 10.0.19041).
/// Everything runs on thread-pool (MTA) threads, so the UI never waits on it.
/// </summary>
internal static unsafe partial class PcMedia
{
    private const string LogIdent = "PcMedia";

    private static readonly Guid IidManagerStatics = new("2050C4EE-11A0-57DE-AED7-C97C70338245");
    private static readonly Guid IidAsyncInfo = new("00000036-0000-0000-C000-000000000046");
    private static readonly Guid IidStream = new("0000000C-0000-0000-C000-000000000046");

    // Vtable slots (0-5 are IUnknown + IInspectable)
    private const int ManagerStaticsRequestAsync = 6;
    private const int ManagerGetCurrentSession = 6;
    private const int SessionSourceAppUserModelId = 6;
    private const int SessionTryGetMediaPropertiesAsync = 7;
    private const int SessionGetTimelineProperties = 8;
    private const int SessionGetPlaybackInfo = 9;
    private const int SessionTrySkipNextAsync = 16;
    private const int SessionTrySkipPreviousAsync = 17;
    private const int SessionTryTogglePlayPauseAsync = 20;
    private const int SessionTryChangeAutoRepeatModeAsync = 21;
    private const int SessionTryChangeShuffleActiveAsync = 23;
    private const int SessionTryChangePlaybackPositionAsync = 24;
    private const int PropsTitle = 6;
    private const int PropsArtist = 9;
    private const int PropsThumbnail = 15;
    private const int TimelineEndTime = 7;
    private const int TimelinePosition = 10;
    private const int TimelineLastUpdatedTime = 11;
    private const int InfoControls = 6;
    private const int InfoPlaybackStatus = 7;
    private const int InfoAutoRepeatMode = 9;
    private const int InfoIsShuffleActive = 11;
    private const int ControlsIsNextEnabled = 12;
    private const int ControlsIsPreviousEnabled = 13;
    private const int ControlsIsShuffleEnabled = 17;
    private const int ControlsIsRepeatEnabled = 18;
    private const int ControlsIsPlaybackPositionEnabled = 20;
    private const int StreamReferenceOpenReadAsync = 6;
    private const int ReferenceValue = 6;          // IReference<T>.get_Value
    private const int AsyncInfoStatus = 7;         // IAsyncInfo.get_Status
    private const int AsyncOperationGetResults = 8;

    private const int PlaybackStatusPlaying = 4;
    private static readonly TimeSpan AsyncTimeout = TimeSpan.FromSeconds(3);

    private static readonly Lock Gate = new();
    private static nint _manager;
    private static string? _coverKey;
    private static byte[]? _cover;

    /// <summary>The current media session, or null when nothing is playing (or Windows has no media sessions).</summary>
    public static Task<MediaState?> GetAsync() => Task.Run(() => Safe(Read));

    public static Task<bool> TogglePlayPauseAsync() => Command(SessionTryTogglePlayPauseAsync);

    public static Task<bool> NextAsync() => Command(SessionTrySkipNextAsync);

    public static Task<bool> PreviousAsync() => Command(SessionTrySkipPreviousAsync);

    public static Task<bool> SetShuffleAsync(bool on) => Command(SessionTryChangeShuffleActiveAsync, on ? 1 : 0);

    public static Task<bool> SetRepeatAsync(RepeatMode mode) => Command(SessionTryChangeAutoRepeatModeAsync, (int)mode);

    /// <summary>Jumps to a position in the current song or video.</summary>
    public static Task<bool> SeekAsync(TimeSpan position) => Task.Run(() => Safe(() =>
    {
        nint session = CurrentSession();
        if (session == 0)
            return false;

        try
        {
            nint op;
            Check(((delegate* unmanaged[Stdcall]<nint, long, nint*, int>)Slot(session, SessionTryChangePlaybackPositionAsync))(session, Math.Max(0, position.Ticks), &op));
            return AwaitBool(op);
        }
        finally
        {
            Release(session);
        }
    }));

    /// <summary>None → List (repeat all) → Track (repeat one) → None, like Spotify's button.</summary>
    public static RepeatMode NextRepeat(RepeatMode mode) => mode switch
    {
        RepeatMode.None => RepeatMode.List,
        RepeatMode.List => RepeatMode.Track,
        _ => RepeatMode.None,
    };

    private static Task<bool> Command(int slot, int? argument = null) => Task.Run(() => Safe(() =>
    {
        nint session = CurrentSession();
        if (session == 0)
            return false;

        try
        {
            nint op;
            int hr = argument is { } value
                ? ((delegate* unmanaged[Stdcall]<nint, int, nint*, int>)Slot(session, slot))(session, value, &op)
                : ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(session, slot))(session, &op);
            Check(hr);
            return AwaitBool(op);
        }
        finally
        {
            Release(session);
        }
    }));

    private static MediaState? Read()
    {
        nint session = CurrentSession();
        if (session == 0)
            return null;

        nint props = 0, timeline = 0, info = 0, controls = 0;
        try
        {
            string app = GetString(session, SessionSourceAppUserModelId);
            props = Await(GetPointer(session, SessionTryGetMediaPropertiesAsync));
            string title = GetString(props, PropsTitle);
            string artist = GetString(props, PropsArtist);

            timeline = GetPointer(session, SessionGetTimelineProperties);
            var duration = TimeSpan.FromTicks(GetInt64(timeline, TimelineEndTime));
            var position = TimeSpan.FromTicks(GetInt64(timeline, TimelinePosition));
            long updated = GetInt64(timeline, TimelineLastUpdatedTime); // 100 ns since 1601 (UTC)
            var positionAt = updated > 0 ? new DateTimeOffset(DateTime.FromFileTimeUtc(updated)) : DateTimeOffset.UtcNow;

            info = GetPointer(session, SessionGetPlaybackInfo);
            bool playing = GetInt32(info, InfoPlaybackStatus) == PlaybackStatusPlaying;
            bool? shuffle = GetOptional(info, InfoIsShuffleActive, isBool: true) is { } s ? s != 0 : null;
            RepeatMode? repeat = GetOptional(info, InfoAutoRepeatMode, isBool: false) is { } r ? (RepeatMode)r : null;

            controls = GetPointer(info, InfoControls);
            bool canNext = GetBool(controls, ControlsIsNextEnabled);
            bool canPrevious = GetBool(controls, ControlsIsPreviousEnabled);
            bool canShuffle = GetBool(controls, ControlsIsShuffleEnabled);
            bool canRepeat = GetBool(controls, ControlsIsRepeatEnabled);
            bool canSeek = GetBool(controls, ControlsIsPlaybackPositionEnabled);

            if (title.Length == 0 && artist.Length == 0)
                return null;

            // The cover only changes with the song, so it's read once per song.
            string key = app + "\n" + title + "\n" + artist;
            if (key != _coverKey)
            {
                _coverKey = key;
                _cover = ReadCover(props);
            }

            return new MediaState(title, artist, app, _cover, playing, position, positionAt, duration, shuffle, repeat,
                canPrevious, canNext, canShuffle, canRepeat, canSeek);
        }
        finally
        {
            Release(controls);
            Release(info);
            Release(timeline);
            Release(props);
            Release(session);
        }
    }

    private static byte[]? ReadCover(nint props)
    {
        nint reference = 0, randomAccess = 0, stream = 0;
        try
        {
            reference = GetPointer(props, PropsThumbnail);
            if (reference == 0)
                return null;

            randomAccess = Await(GetPointer(reference, StreamReferenceOpenReadAsync));
            Guid iid = IidStream;
            Check(CreateStreamOverRandomAccessStream(randomAccess, &iid, &stream));

            using var bytes = new MemoryStream();
            byte[] buffer = new byte[64 * 1024];
            fixed (byte* p = buffer)
            {
                while (bytes.Length < 8 * 1024 * 1024)
                {
                    uint read;
                    int hr = ((delegate* unmanaged[Stdcall]<nint, byte*, uint, uint*, int>)Slot(stream, 3))(stream, p, (uint)buffer.Length, &read);
                    if (hr < 0 || read == 0)
                        break;
                    bytes.Write(buffer, 0, (int)read);
                }
            }

            return bytes.Length > 0 ? bytes.ToArray() : null;
        }
        catch (Exception ex) when (ex is COMException or TimeoutException)
        {
            return null; // no cover is fine
        }
        finally
        {
            Release(stream);
            Release(randomAccess);
            Release(reference);
        }
    }

    // ---------- WinRT plumbing ----------

    /// <summary>The session Windows' media keys control right now (AddRef'd), or 0.</summary>
    private static nint CurrentSession()
    {
        lock (Gate)
        {
            if (_manager == 0)
            {
                CoIncrementMTAUsage(out _); // thread-pool threads run in the multithreaded COM apartment
                nint factory = 0;
                nint name = CreateHString("Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager");
                try
                {
                    Guid iid = IidManagerStatics;
                    Check(RoGetActivationFactory(name, &iid, &factory));
                    _manager = Await(GetPointer(factory, ManagerStaticsRequestAsync));
                }
                finally
                {
                    WindowsDeleteString(name);
                    Release(factory);
                }
            }

            return GetPointer(_manager, ManagerGetCurrentSession);
        }
    }

    private static T Safe<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (ex is COMException or TimeoutException or InvalidCastException or UnauthorizedAccessException)
        {
            Logger.Warn(LogIdent, ex.Message);
            return default!;
        }
    }

    private static nint Slot(nint obj, int slot) => (*(nint**)obj)[slot];

    private static nint GetPointer(nint obj, int slot)
    {
        nint result = 0;
        Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(obj, slot))(obj, &result));
        return result;
    }

    private static int GetInt32(nint obj, int slot)
    {
        int result;
        Check(((delegate* unmanaged[Stdcall]<nint, int*, int>)Slot(obj, slot))(obj, &result));
        return result;
    }

    private static long GetInt64(nint obj, int slot)
    {
        long result;
        Check(((delegate* unmanaged[Stdcall]<nint, long*, int>)Slot(obj, slot))(obj, &result));
        return result;
    }

    private static bool GetBool(nint obj, int slot)
    {
        if (obj == 0)
            return false;
        byte result;
        Check(((delegate* unmanaged[Stdcall]<nint, byte*, int>)Slot(obj, slot))(obj, &result));
        return result != 0;
    }

    /// <summary>An IReference&lt;bool&gt; or IReference&lt;enum&gt; property: null when the app doesn't report it.</summary>
    private static int? GetOptional(nint obj, int slot, bool isBool)
    {
        nint reference = GetPointer(obj, slot);
        if (reference == 0)
            return null;
        try
        {
            if (isBool)
            {
                byte value;
                Check(((delegate* unmanaged[Stdcall]<nint, byte*, int>)Slot(reference, ReferenceValue))(reference, &value));
                return value;
            }

            return GetInt32(reference, ReferenceValue);
        }
        finally
        {
            Release(reference);
        }
    }

    private static string GetString(nint obj, int slot)
    {
        nint hstring = GetPointer(obj, slot);
        if (hstring == 0)
            return "";
        try
        {
            uint length;
            char* chars = WindowsGetStringRawBuffer(hstring, &length);
            return new string(chars, 0, (int)length);
        }
        finally
        {
            WindowsDeleteString(hstring);
        }
    }

    /// <summary>Waits for an IAsyncOperation&lt;object&gt; and returns its (AddRef'd) result. Releases the operation.</summary>
    private static nint Await(nint operation)
    {
        try
        {
            WaitFor(operation);
            return GetPointer(operation, AsyncOperationGetResults);
        }
        finally
        {
            Release(operation);
        }
    }

    private static bool AwaitBool(nint operation)
    {
        try
        {
            WaitFor(operation);
            byte result;
            Check(((delegate* unmanaged[Stdcall]<nint, byte*, int>)Slot(operation, AsyncOperationGetResults))(operation, &result));
            return result != 0;
        }
        finally
        {
            Release(operation);
        }
    }

    /// <summary>Polls IAsyncInfo.Status (0 started, 1 completed, 2 canceled, 3 error). These calls finish within milliseconds.</summary>
    private static void WaitFor(nint operation)
    {
        nint info = 0;
        Guid iid = IidAsyncInfo;
        Check(Marshal.QueryInterface(operation, in iid, out info));
        try
        {
            var deadline = DateTime.UtcNow + AsyncTimeout;
            while (true)
            {
                int status = GetInt32(info, AsyncInfoStatus);
                if (status == 1)
                    return;
                if (status is 2 or 3)
                    throw new COMException("Windows media call failed", status);
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException("Windows media call timed out");
                Thread.Sleep(2);
            }
        }
        finally
        {
            Release(info);
        }
    }

    private static nint CreateHString(string text)
    {
        nint hstring;
        fixed (char* p = text)
            Check(WindowsCreateString(p, (uint)text.Length, &hstring));
        return hstring;
    }

    private static void Check(int hr)
    {
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);
    }

    private static void Release(nint obj)
    {
        if (obj != 0)
            Marshal.Release(obj);
    }

    [LibraryImport("combase.dll")]
    private static partial int RoGetActivationFactory(nint activatableClassId, Guid* iid, nint* factory);

    [LibraryImport("combase.dll")]
    private static partial int WindowsCreateString(char* sourceString, uint length, nint* hstring);

    [LibraryImport("combase.dll")]
    private static partial int WindowsDeleteString(nint hstring);

    [LibraryImport("combase.dll")]
    private static partial char* WindowsGetStringRawBuffer(nint hstring, uint* length);

    [LibraryImport("combase.dll")]
    private static partial int CoIncrementMTAUsage(out nint cookie);

    [LibraryImport("shcore.dll")]
    private static partial int CreateStreamOverRandomAccessStream(nint randomAccessStream, Guid* iid, nint* stream);
}

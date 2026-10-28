using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using Nimbus.Core;

namespace Nimbus.Integrations;

/// <summary>
/// Minimal Discord Rich Presence client over Discord's local IPC pipe (\\.\pipe\discord-ipc-N).
/// Frames are: int32 opcode, int32 length (little endian), then UTF-8 JSON.
/// </summary>
internal sealed class DiscordRpc(string clientId) : IAsyncDisposable
{
    private const string LogIdent = "Discord";
    private const int OpHandshake = 0;
    private const int OpFrame = 1;
    private static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private NamedPipeClientStream? _pipe;
    private DateTime _lastAttempt = DateTime.MinValue;
    private JsonObject? _activity;

    public static byte[] Frame(int opcode, string json)
    {
        byte[] payload = Encoding.UTF8.GetBytes(json);
        byte[] frame = new byte[8 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, opcode);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), payload.Length);
        payload.CopyTo(frame, 8);
        return frame;
    }

    /// <summary>Shows (or with null, clears) the status. If Discord isn't open yet, it's retried on the next update.</summary>
    public async Task SetActivityAsync(JsonObject? activity)
    {
        _activity = activity;
        if (!await EnsureConnectedAsync())
            return;

        var message = new JsonObject
        {
            ["cmd"] = "SET_ACTIVITY",
            ["nonce"] = Guid.NewGuid().ToString(),
            ["args"] = new JsonObject
            {
                ["pid"] = Environment.ProcessId,
                ["activity"] = activity?.DeepClone(),
            },
        };

        await SendAsync(OpFrame, message.ToJsonString());
    }

    /// <summary>Resends the last status, e.g. once Discord has been opened after Roblox.</summary>
    public Task RefreshAsync() => _pipe is null ? SetActivityAsync(_activity) : Task.CompletedTask;

    private async Task<bool> EnsureConnectedAsync()
    {
        if (_pipe is { IsConnected: true })
            return true;
        if (DateTime.UtcNow - _lastAttempt < RetryEvery)
            return false;

        _lastAttempt = DateTime.UtcNow;
        for (int i = 0; i < 10; i++)
        {
            var pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(300);
                _pipe = pipe;
                await SendAsync(OpHandshake, new JsonObject { ["v"] = 1, ["client_id"] = clientId }.ToJsonString());
                _ = DrainAsync(pipe);
                Logger.Info(LogIdent, $"Connected to Discord (pipe {i})");
                return true;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
            {
                await pipe.DisposeAsync();
                _pipe = null;
            }
        }

        return false;
    }

    private async Task SendAsync(int opcode, string json)
    {
        if (_pipe is null)
            return;

        await _writeLock.WaitAsync();
        try
        {
            await _pipe.WriteAsync(Frame(opcode, json));
            await _pipe.FlushAsync();
        }
        catch (IOException ex)
        {
            Logger.Warn(LogIdent, $"Discord connection lost: {ex.Message}");
            _pipe = null;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Reads (and ignores) Discord's replies so its side of the pipe never fills up.</summary>
    private async Task DrainAsync(NamedPipeClientStream pipe)
    {
        byte[] header = new byte[8];
        try
        {
            while (pipe.IsConnected)
            {
                await pipe.ReadExactlyAsync(header);
                int length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
                if (length is < 0 or > 1 << 20)
                    break;
                await pipe.ReadExactlyAsync(new byte[length]);
            }
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or ObjectDisposedException)
        {
        }

        if (_pipe == pipe)
            _pipe = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_pipe is { IsConnected: true })
            await SetActivityAsync(null);
        if (_pipe is not null)
            await _pipe.DisposeAsync();
        _writeLock.Dispose();
    }
}

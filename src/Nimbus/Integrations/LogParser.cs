using System.Text.RegularExpressions;

namespace Nimbus.Integrations;

/// <summary>Something that happened in Roblox, read from its log file.</summary>
public abstract record LogEvent
{
    /// <summary>"! Joining game '&lt;job id&gt;' place &lt;place id&gt;": a game (or teleport) is starting.</summary>
    public sealed record JoiningGame(long PlaceId, string JobId) : LogEvent;

    /// <summary>The server's public address (the one worth looking up for a location).</summary>
    public sealed record ServerAddress(string Ip) : LogEvent;

    public sealed record Connected : LogEvent;

    /// <summary>The player chose to leave (menu → Leave, or back to the home screen).</summary>
    public sealed record LeftGame : LogEvent;

    /// <summary>The connection ended with this reason code (285 = the player left).</summary>
    public sealed record Disconnected(int Reason) : LogEvent;

    /// <summary>A game sent a BloxstrapRPC message with print("[BloxstrapRPC] {...}").</summary>
    public sealed record RpcMessage(string Json) : LogEvent;
}

internal static partial class LogParser
{
    public const int ReasonPlayerLeft = 285;

    public static LogEvent? Parse(string line)
    {
        if (line.Contains("! Joining game", StringComparison.Ordinal) && JoinRegex().Match(line) is { Success: true } join)
            return new LogEvent.JoiningGame(long.Parse(join.Groups[2].Value), join.Groups[1].Value);

        if (line.Contains("UDMUX Address", StringComparison.Ordinal) && UdmuxRegex().Match(line) is { Success: true } udmux)
            return new LogEvent.ServerAddress(udmux.Groups[1].Value);

        if (line.Contains("[DFLog::NetworkClient] Connection accepted from", StringComparison.Ordinal))
            return new LogEvent.Connected();

        if (line.Contains("leaveUGCGameInternal", StringComparison.Ordinal))
            return new LogEvent.LeftGame();

        if (line.Contains("Disconnection Notification", StringComparison.Ordinal) && DisconnectRegex().Match(line) is { Success: true } disconnect)
            return new LogEvent.Disconnected(int.Parse(disconnect.Groups[1].Value));

        int rpc = line.IndexOf("[BloxstrapRPC] ", StringComparison.Ordinal);
        if (rpc >= 0)
            return new LogEvent.RpcMessage(line[(rpc + "[BloxstrapRPC] ".Length)..].Trim());

        return null;
    }

    [GeneratedRegex(@"! Joining game '([0-9a-fA-F\-]+)' place (\d+)")]
    private static partial Regex JoinRegex();

    [GeneratedRegex(@"UDMUX Address = ([0-9a-fA-F\.:]+), Port")]
    private static partial Regex UdmuxRegex();

    [GeneratedRegex(@"Disconnection Notification\. Reason: (\d+)")]
    private static partial Regex DisconnectRegex();
}

using System.Buffers.Binary;
using System.Text;
using Nimbus.Core;
using Nimbus.Integrations;

namespace Nimbus.Tests;

public class LogParserTests
{
    // Lines copied from a real Roblox 0.745 player log.
    [Theory]
    [InlineData("2026-10-27T19:49:40.390Z,11.390570,6f74,6 [FLog::Output] ! Joining game 'f4364e05-b062-4cf3-823d-5b765ea407f7' place 3095204897 at 10.30.6.42")]
    public void Join(string line) =>
        Assert.Equal(new LogEvent.JoiningGame(3095204897, "f4364e05-b062-4cf3-823d-5b765ea407f7"), LogParser.Parse(line));

    [Fact]
    public void ServerAddressIsThePublicUdmuxOne() =>
        Assert.Equal(new LogEvent.ServerAddress("128.116.13.34"),
            LogParser.Parse("2026-10-27T19:49:40.395Z,11.395562,15d4,7 [FLog::Network] UDMUX Address = 128.116.13.34, Port = 62859 | RCC Server Address = 10.30.6.42, Port = 62859"));

    [Fact]
    public void ConnectedLeftAndDisconnected()
    {
        Assert.IsType<LogEvent.Connected>(LogParser.Parse("2026-10-27T19:49:40.488Z,11.488955,6f74,6,Info [DFLog::NetworkClient] Connection accepted from 128.116.13.34|62859"));
        Assert.IsType<LogEvent.LeftGame>(LogParser.Parse("2026-10-27T19:50:44.204Z,75.204140,2fdc,6 [FLog::SingleSurfaceApp] leaveUGCGameInternal"));
        Assert.Equal(new LogEvent.Disconnected(285), LogParser.Parse("2026-10-27T19:50:44.278Z,75.278336,0600,7 [FLog::Network] Disconnection Notification. Reason: 285"));
    }

    [Fact]
    public void RpcMessage() =>
        Assert.Equal(new LogEvent.RpcMessage("{\"command\":\"SetRichPresence\",\"data\":{}}"),
            LogParser.Parse("2026-10-27T20:00:00.000Z,1.0,1,6 [FLog::Output] [BloxstrapRPC] {\"command\":\"SetRichPresence\",\"data\":{}}"));

    [Fact]
    public void UnrelatedLinesAreIgnored()
    {
        Assert.Null(LogParser.Parse("2026-10-27T19:49:29.157Z,1013.157532,5fc0,6 [DFLog::SignalRCoreError] ID: 1 Disconnected - stop() called"));
        Assert.Null(LogParser.Parse("[FLog::Network] Sending disconnect with reason: 285"));
    }

    /// <summary>NIMBUS_REAL_LOGS=&lt;Roblox logs folder&gt;: every join in real logs has a server address and an end.</summary>
    [Fact]
    public void RealLogsParse()
    {
        if (Environment.GetEnvironmentVariable("NIMBUS_REAL_LOGS") is not { Length: > 0 } folder)
            return;

        int joins = 0, addresses = 0, disconnects = 0;
        foreach (string file in Directory.GetFiles(folder, "*_Player_*.log"))
        {
            using var reader = new StreamReader(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
            while (reader.ReadLine() is { } line)
            {
                switch (LogParser.Parse(line))
                {
                    case LogEvent.JoiningGame: joins++; break;
                    case LogEvent.ServerAddress: addresses++; break;
                    case LogEvent.Disconnected: disconnects++; break;
                }
            }
        }

        // Logs may have been cleaned up (Performance page); then there's nothing to check.
        if (joins == 0)
            return;

        // Closing Roblox mid-game ends the log without a disconnect line, so there can be fewer disconnects than joins.
        Assert.Equal(joins, addresses);
        Assert.InRange(disconnects, 1, joins);
    }
}

public class ActivityTests
{
    private static GameSession Session() => new()
    {
        PlaceId = 3095204897,
        JobId = "f4364e05-b062-4cf3-823d-5b765ea407f7",
        Started = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000),
        Game = new GameDetails(3095204897, 1, "My Game", "Some Studio", "https://tr.rbxcdn.com/icon.png"),
    };

    [Fact]
    public void DiscordFramesAreLittleEndianWithLength()
    {
        byte[] frame = DiscordRpc.Frame(1, "{}");
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(frame));
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(4)));
        Assert.Equal("{}", Encoding.UTF8.GetString(frame, 8, 2));
    }

    [Theory]
    [InlineData(277, true)]
    [InlineData(278, true)]
    [InlineData(279, true)]
    [InlineData(285, false)] // the player left
    [InlineData(267, false)] // kicked by the game
    [InlineData(273, false)] // joined from another device
    public void RejoinOnlyForConnectionProblems(int reason, bool rejoin) =>
        Assert.Equal(rejoin, ActivityWatcher.ShouldRejoin(reason));

    [Fact]
    public void ActivityShowsGameCreatorIconAndTime()
    {
        var activity = ActivityWatcher.BuildActivity(Session(), new IntegrationSettings());

        Assert.Equal("My Game", (string?)activity["details"]);
        Assert.Equal("by Some Studio", (string?)activity["state"]);
        Assert.Equal(1_800_000_000, (long?)activity["timestamps"]!["start"]);
        Assert.Equal("https://tr.rbxcdn.com/icon.png", (string?)activity["assets"]!["large_image"]);
        Assert.Single(activity["buttons"]!.AsArray());
    }

    [Fact]
    public void JoinButtonOnlyWhenAllowed()
    {
        var activity = ActivityWatcher.BuildActivity(Session(), new IntegrationSettings { DiscordJoinButton = true });
        Assert.Equal("https://www.roblox.com/games/start?placeId=3095204897&gameInstanceId=f4364e05-b062-4cf3-823d-5b765ea407f7",
            (string?)activity["buttons"]![1]!["url"]);
    }

    [Fact]
    public async Task GamesCanSetStatusAndReset()
    {
        var session = Session();
        Assert.True(await ActivityWatcher.ApplyRpcAsync(session,
            """{"command":"SetRichPresence","data":{"details":"Level 12","state":"In the lobby","timeEnd":1800000600,"largeImage":{"assetId":42,"hoverText":"Map"}}}""",
            id => Task.FromResult<string?>($"https://img/{id}")));

        var activity = ActivityWatcher.BuildActivity(session, new IntegrationSettings());
        Assert.Equal("Level 12", (string?)activity["details"]);
        Assert.Equal("In the lobby", (string?)activity["state"]);
        Assert.Equal(1_800_000_600, (long?)activity["timestamps"]!["end"]);
        Assert.Equal("https://img/42", (string?)activity["assets"]!["large_image"]);

        await ActivityWatcher.ApplyRpcAsync(session, """{"command":"SetRichPresence","data":{"details":"<reset>"}}""");
        Assert.Equal("My Game", (string?)ActivityWatcher.BuildActivity(session, new IntegrationSettings())["details"]);
        Assert.Equal("In the lobby", session.State);
    }

    [Fact]
    public async Task OtherRpcCommandsAndBrokenJsonAreIgnored()
    {
        Assert.False(await ActivityWatcher.ApplyRpcAsync(Session(), """{"command":"SomethingElse","data":{}}"""));
        Assert.False(await ActivityWatcher.ApplyRpcAsync(Session(), "{ not json"));
    }

    [Fact]
    public void LongTextsAreShortenedForDiscord()
    {
        var session = Session();
        session.Details = new string('x', 300);
        Assert.Equal(128, ((string?)ActivityWatcher.BuildActivity(session, new IntegrationSettings())["details"])!.Length);
    }
}

[Collection("Paths")]
public sealed class GameHistoryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("NimbusHistory").FullName;
    private readonly string _originalBase = Paths.Base;

    public GameHistoryTests() => Paths.OverrideBase(_root);

    public void Dispose()
    {
        Paths.OverrideBase(_originalBase);
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void NewestFirstUpdatableAndCapped()
    {
        for (int i = 0; i < GameHistory.MaxEntries + 5; i++)
            GameHistory.Add(new HistoryEntry { PlaceId = i, JobId = $"job-{i}", Name = $"Game {i}", JoinedAt = DateTimeOffset.UtcNow });

        GameHistory.Update("job-54", e => e.Location = "Frankfurt am Main, Germany");
        var list = GameHistory.Load();

        Assert.Equal(GameHistory.MaxEntries, list.Count);
        Assert.Equal("Game 54", list[0].Name);
        Assert.Equal("Frankfurt am Main, Germany", list[0].Location);
    }

    [Fact]
    public void Links() =>
        Assert.Equal("roblox://experiences/start?placeId=1&gameInstanceId=abc", GameHistory.RejoinLink(1, "abc"));
}

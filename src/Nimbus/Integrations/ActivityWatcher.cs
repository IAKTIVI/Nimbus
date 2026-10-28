using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nimbus.Core;
using WinForms = System.Windows.Forms;

namespace Nimbus.Integrations;

/// <summary>The game currently being played, as far as the log has told us.</summary>
internal sealed class GameSession
{
    public long PlaceId { get; init; }

    public string JobId { get; init; } = "";

    public DateTimeOffset Started { get; init; } = DateTimeOffset.UtcNow;

    public GameDetails? Game { get; set; }

    public bool PlayerLeaving { get; set; }

    // Set by the game itself through BloxstrapRPC
    public string? Details { get; set; }
    public string? State { get; set; }
    public long? TimeStart { get; set; }
    public long? TimeEnd { get; set; }
    public string? LargeImage { get; set; }
    public string? LargeText { get; set; }
    public string? SmallImage { get; set; }
    public string? SmallText { get; set; }
}

/// <summary>
/// Runs in a small tray icon while Roblox is open: follows Roblox's log file and drives the Discord status,
/// server location notification, game history and auto-rejoin. Exits when Roblox closes.
/// </summary>
internal sealed class ActivityWatcher(Settings settings, JsonStore<AppState> state)
{
    private const string LogIdent = "Watcher";
    private const int MaxRejoins = 3;
    private static readonly TimeSpan RejoinWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RejoinDelay = TimeSpan.FromSeconds(5);

    private IntegrationSettings Options => settings.Integrations;

    private WinForms.NotifyIcon? _tray;
    private WinForms.ToolStripMenuItem? _statusItem;
    private DiscordRpc? _discord;
    private CustomAppRunner? _apps;
    private UI.WidgetHost? _widgets;
    private GameSession? _session;
    private Process? _roblox;
    private int _joins;
    private bool _stopRequested;

    /// <summary>Whether any option needs Nimbus to stay open while Roblox runs.</summary>
    public static bool IsNeeded(Settings settings, WidgetSettings? widgets = null)
    {
        var o = settings.Integrations;
        widgets ??= WidgetStore.Load(settings);
        return o.ServerLocation || o.GameHistory || o.AutoRejoin || (o.DiscordStatus && HasDiscordId(o)) || widgets.Shown.Count > 0
            || CustomAppRunner.HasAny(o);
    }

    private static bool HasDiscordId(IntegrationSettings o) => !string.IsNullOrWhiteSpace(o.DiscordAppId) && o.DiscordAppId.All(char.IsAsciiDigit);

    /// <summary>Only connection problems are worth rejoining: 277 lost connection, 278 idle kick, 279 failed to connect.</summary>
    internal static bool ShouldRejoin(int reason) => reason is 277 or 278 or 279;

    public async Task RunAsync(Process roblox, DateTime launchedUtc)
    {
        _roblox = roblox;
        CreateTray();
        _apps = new CustomAppRunner(Options.CustomApps);
        _apps.OnRobloxStarted();
        UI.MusicOverlay? music = null;
        Mutex? claim = ClaimWatch(roblox);
        if (Options.DiscordStatus && HasDiscordId(Options))
            _discord = new DiscordRpc(Options.DiscordAppId!.Trim());

        try
        {
            music = StartMusicCard(roblox);
            StartWidgets(roblox, music);
            StartToast(roblox);
            string? log = await FindLogAsync(roblox, launchedUtc);
            if (log is not null)
            {
                Logger.Info(LogIdent, $"Following {Path.GetFileName(log)}");
                await FollowAsync(log, roblox, fromEnd: false);
            }
            else if (!roblox.HasExited)
            {
                Logger.Warn(LogIdent, "Roblox's log file didn't appear; only waiting for Roblox to close");
                await WaitAsync(roblox, Timeout.InfiniteTimeSpan);
            }

            // If Roblox was already open, the new client hands the game to that window and closes within
            // seconds. Then the open window is the one to watch, unless another Nimbus already watches it.
            if (DateTime.UtcNow - launchedUtc < HandOffWindow && FindOtherRoblox(roblox.Id) is { } open && ClaimWatch(open) is { } openClaim)
            {
                Logger.Info(LogIdent, $"Roblox handed this launch to the Roblox that was already open (pid {open.Id}); following that one");
                claim?.Dispose();
                claim = openClaim;
                _roblox = open;
                music?.Close();
                music = StartMusicCard(open);
                StartWidgets(open, music);
                StartToast(open);

                if (NewestPlayerLog() is { } openLog)
                    await FollowAsync(openLog, open, fromEnd: true); // the hand-off's "Joining game" lines follow
                else
                    await WaitAsync(open, Timeout.InfiniteTimeSpan);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(LogIdent, ex);
            await WaitAsync(_roblox ?? roblox, Timeout.InfiniteTimeSpan);
        }
        finally
        {
            _widgets?.Close();
            music?.Close();
            _toast?.Close();
            claim?.Dispose();
            EndSession();
            await _apps.OnRobloxClosedAsync();
            if (_discord is not null)
                await _discord.DisposeAsync();
            _tray?.Dispose();
        }
    }

    /// <summary>A client that closes this quickly after launching handed its link to an already-open Roblox.</summary>
    private static readonly TimeSpan HandOffWindow = TimeSpan.FromSeconds(30);

    private UI.GameToast? _toast;

    /// <summary>The in-game message card for this Roblox window (Windows notifications are muted while gaming).</summary>
    private void StartToast(Process roblox)
    {
        _toast?.Close();
        _toast = new UI.GameToast(roblox);
        _toast.Start();
    }

    /// <summary>Shows a message in the Roblox window; the tray balloon is only a fallback.</summary>
    private void Notify(string title, string text, string glyph)
    {
        if (_toast is not null)
            _toast.Post(title, text, glyph, TimeSpan.FromSeconds(6));
        else
            _tray?.ShowBalloonTip(5000, title, text, WinForms.ToolTipIcon.Info);
    }

    /// <summary>
    /// The music card always runs (it reads nothing while hidden), so the widget menu can switch it on mid-game.
    /// </summary>
    private static UI.MusicOverlay StartMusicCard(Process roblox)
    {
        var card = new UI.MusicOverlay(roblox) { UserHidden = true };
        card.Start();
        return card;
    }

    private void StartWidgets(Process roblox, UI.MusicOverlay music)
    {
        _widgets?.Close();
        _widgets = new UI.WidgetHost(roblox, settings, music);
        _widgets.Start();
    }

    /// <summary>One Nimbus helper per Roblox window: a named mutex per Roblox process id. Null if another helper has it.</summary>
    internal static Mutex? ClaimWatch(Process roblox)
    {
        var mutex = new Mutex(initiallyOwned: false, $"Nimbus.Watching.{roblox.Id}", out bool createdNew);
        if (createdNew)
            return mutex;

        mutex.Dispose();
        return null;
    }

    /// <summary>Another running Roblox client with a window (the one a hand-off went to).</summary>
    private static Process? FindOtherRoblox(int exceptId) =>
        Process.GetProcessesByName("RobloxPlayerBeta")
            .Where(p => p.Id != exceptId && !p.HasExited && p.MainWindowHandle != 0)
            .OrderBy(p => p.StartTime)
            .FirstOrDefault();

    private static string LogFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "logs");

    /// <summary>The Player log Roblox wrote to most recently.</summary>
    private static string? NewestPlayerLog() =>
        Directory.Exists(LogFolder)
            ? PlayerLogs(new DirectoryInfo(LogFolder)).OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName
            : null;

    /// <summary>
    /// Roblox's game logs. Its crash handler writes "..._Player_..._CrashHandler_last.log" next to them, often first,
    /// and that one never says which game you joined, so it's skipped.
    /// </summary>
    internal static IEnumerable<FileInfo> PlayerLogs(DirectoryInfo folder) =>
        folder.EnumerateFiles("*_Player_*.log").Where(f => IsGameLog(f.Name));

    internal static bool IsGameLog(string fileName) =>
        fileName.Contains("_Player_", StringComparison.OrdinalIgnoreCase) && !fileName.Contains("CrashHandler", StringComparison.OrdinalIgnoreCase);

    private static async Task WaitAsync(Process roblox, TimeSpan max)
    {
        var exit = roblox.WaitForExitAsync();
        await Task.WhenAny(exit, max == Timeout.InfiniteTimeSpan ? exit : Task.Delay(max));
    }

    /// <summary>The log Roblox started writing for this launch (newest Player log created after we started it).</summary>
    private static async Task<string?> FindLogAsync(Process roblox, DateTime launchedUtc)
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "logs");
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline && !roblox.HasExited)
        {
            if (Directory.Exists(folder))
            {
                var newest = PlayerLogs(new DirectoryInfo(folder))
                    .Where(f => f.CreationTimeUtc >= launchedUtc.AddSeconds(-5))
                    .OrderByDescending(f => f.CreationTimeUtc)
                    .FirstOrDefault();
                if (newest is not null)
                    return newest.FullName;
            }

            await Task.Delay(500);
        }

        return null;
    }

    private async Task FollowAsync(string log, Process roblox, bool fromEnd)
    {
        await using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (fromEnd)
            stream.Seek(0, SeekOrigin.End);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var pending = new StringBuilder();
        char[] buffer = new char[16 * 1024];
        var lastRefresh = DateTime.UtcNow;

        while (!roblox.HasExited && !_stopRequested)
        {
            int read = await reader.ReadAsync(buffer);
            if (read == 0)
            {
                await WaitAsync(roblox, TimeSpan.FromMilliseconds(250));
                if (_discord is not null && DateTime.UtcNow - lastRefresh > TimeSpan.FromSeconds(30))
                {
                    lastRefresh = DateTime.UtcNow;
                    await _discord.RefreshAsync(); // picks Discord up if it was opened after Roblox
                }
                continue;
            }

            pending.Append(buffer, 0, read);
            int newline;
            while ((newline = IndexOf(pending, '\n')) >= 0)
            {
                string line = pending.ToString(0, newline).TrimEnd('\r');
                pending.Remove(0, newline + 1);
                if (LogParser.Parse(line) is { } e)
                    await HandleAsync(e);
            }
        }
    }

    private static int IndexOf(StringBuilder sb, char c)
    {
        for (int i = 0; i < sb.Length; i++)
            if (sb[i] == c)
                return i;
        return -1;
    }

    private async Task HandleAsync(LogEvent e)
    {
        switch (e)
        {
            case LogEvent.JoiningGame join:
                EndSession(); // a teleport ends the previous place
                _joins++;
                _session = new GameSession { PlaceId = join.PlaceId, JobId = join.JobId };
                _widgets?.OnJoined(_session.Started);
                Logger.Info(LogIdent, $"Joining place {join.PlaceId}");
                await OnJoinAsync(_session);
                break;

            case LogEvent.ServerAddress server when _session is { } session:
                _ = ShowLocationAsync(session, server.Ip);
                break;

            case LogEvent.LeftGame when _session is { } session:
                session.PlayerLeaving = true;
                break;

            case LogEvent.Disconnected disconnected when _session is { } session:
                Logger.Info(LogIdent, $"Disconnected, reason {disconnected.Reason}");
                EndSession();
                if (Options.AutoRejoin && !session.PlayerLeaving && disconnected.Reason != LogParser.ReasonPlayerLeft && ShouldRejoin(disconnected.Reason))
                    await RejoinAsync(session, disconnected.Reason);
                break;

            case LogEvent.RpcMessage rpc when _session is { } session && Options.GameStatus:
                if (await ApplyRpcAsync(session, rpc.Json))
                    await UpdateDiscordAsync();
                break;
        }
    }

    private async Task OnJoinAsync(GameSession session)
    {
        session.Game = await RobloxApi.GetGameAsync(session.PlaceId);
        if (session != _session)
            return;

        _apps?.OnGameJoined(session.PlaceId, session.Game?.UniverseId);
        string name = session.Game?.Name ?? $"Place {session.PlaceId}";
        SetTrayStatus(name);

        if (Options.GameHistory)
        {
            GameHistory.Add(new HistoryEntry
            {
                PlaceId = session.PlaceId,
                JobId = session.JobId,
                Name = name,
                Creator = session.Game?.Creator,
                IconUrl = session.Game?.IconUrl,
                JoinedAt = session.Started,
            });
        }

        await UpdateDiscordAsync();
    }

    private async Task ShowLocationAsync(GameSession session, string ip)
    {
        // The address is only looked up when something shows it: the pop-up or the server widget.
        if (!Options.ServerLocation && _widgets?.WantsLocation != true)
            return;

        string? location = await RobloxApi.GetServerLocationAsync(ip);
        if (location is null || session != _session)
            return;

        _widgets?.OnLocation(location);
        if (!Options.ServerLocation)
            return; // the widget still shows it; only the pop-up and history entry are switched off

        Notify(Loc.T("Watcher.ServerLocation"), location, "");
        SetTrayStatus($"{session.Game?.Name ?? "Roblox"} - {location}");
        if (Options.GameHistory)
            GameHistory.Update(session.JobId, entry => entry.Location = location);
    }

    private void EndSession()
    {
        if (_session is not { } ended)
            return;

        _session = null;
        _widgets?.OnLeft();
        if (Options.GameHistory)
        {
            var left = DateTimeOffset.UtcNow;
            GameHistory.Update(ended.JobId, entry => entry.LeftAt ??= left);
            // Counted for the whole experience; Play in "Most played" goes to its start place.
            Playtime.Add(ended.Game is { RootPlaceId: > 0 } game ? game.RootPlaceId : ended.PlaceId, ended.Game?.UniverseId, ended.Game?.Name ?? $"Place {ended.PlaceId}", ended.Game?.IconUrl, ended.Started, left);
        }
        SetTrayStatus(null);
        _ = _discord?.SetActivityAsync(null);
    }

    private async Task UpdateDiscordAsync()
    {
        if (_discord is not null && _session is { } session)
            await _discord.SetActivityAsync(BuildActivity(session, Options));
    }

    /// <summary>The Discord status for a session. Discord wants texts of 2 to 128 characters.</summary>
    internal static JsonObject BuildActivity(GameSession session, IntegrationSettings options)
    {
        string name = session.Game?.Name ?? "Roblox";
        string? state = session.State ?? (session.Game?.Creator is { } creator ? Loc.T("History.By", creator) : null);

        var timestamps = new JsonObject { ["start"] = session.TimeStart ?? session.Started.ToUnixTimeSeconds() };
        if (session.TimeEnd is { } end)
            timestamps["end"] = end;

        var assets = new JsonObject();
        if ((session.LargeImage ?? session.Game?.IconUrl) is { } large)
        {
            assets["large_image"] = large;
            assets["large_text"] = Fit(session.LargeText ?? name);
        }
        if (session.SmallImage is { } small)
        {
            assets["small_image"] = small;
            if (session.SmallText is { } smallText)
                assets["small_text"] = Fit(smallText);
        }

        var buttons = new JsonArray
        {
            new JsonObject { ["label"] = Loc.T("Watcher.SeeGamePage"), ["url"] = $"https://www.roblox.com/games/{session.PlaceId}" },
        };
        if (options.DiscordJoinButton && session.JobId.Length > 0)
        {
            buttons.Add(new JsonObject
            {
                ["label"] = Loc.T("Watcher.JoinServer"),
                ["url"] = $"https://www.roblox.com/games/start?placeId={session.PlaceId}&gameInstanceId={session.JobId}",
            });
        }

        var activity = new JsonObject
        {
            ["details"] = Fit(session.Details ?? name),
            ["timestamps"] = timestamps,
            ["assets"] = assets,
            ["buttons"] = buttons,
        };
        if (state is not null)
            activity["state"] = Fit(state);
        return activity;
    }

    private static string Fit(string text)
    {
        text = text.Length > 128 ? text[..127] + "…" : text;
        return text.Length < 2 ? text.PadRight(2) : text;
    }

    /// <summary>
    /// BloxstrapRPC "SetRichPresence": a game can override the status text, times and images.
    /// A missing field leaves things as they are; "&lt;reset&gt;" goes back to Nimbus's own text.
    /// </summary>
    internal static async Task<bool> ApplyRpcAsync(GameSession session, string json, Func<long, Task<string?>>? assetImage = null)
    {
        assetImage ??= id => RobloxApi.GetAssetImageAsync(id);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("command", out var command) || command.GetString() != "SetRichPresence" ||
                !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                return false;

            if (TextField(data, "details", out string? details))
                session.Details = details;
            if (TextField(data, "state", out string? stateText))
                session.State = stateText;
            if (data.TryGetProperty("timeStart", out var start))
                session.TimeStart = start.TryGetInt64(out long s) && s > 0 ? s : null;
            if (data.TryGetProperty("timeEnd", out var end))
                session.TimeEnd = end.TryGetInt64(out long t) && t > 0 ? t : null;

            if (data.TryGetProperty("largeImage", out var large))
                (session.LargeImage, session.LargeText) = await ImageAsync(large, session.LargeImage, session.LargeText, assetImage);
            if (data.TryGetProperty("smallImage", out var small))
                (session.SmallImage, session.SmallText) = await ImageAsync(small, session.SmallImage, session.SmallText, assetImage);

            return true;
        }
        catch (JsonException ex)
        {
            Logger.Warn(LogIdent, $"Ignored a BloxstrapRPC message: {ex.Message}");
            return false;
        }
    }

    private static bool TextField(JsonElement data, string name, out string? value)
    {
        value = null;
        if (!data.TryGetProperty(name, out var field))
            return false;

        string? text = field.ValueKind == JsonValueKind.String ? field.GetString() : null;
        value = text is null or "<reset>" || text.Length == 0 ? null : text;
        return true;
    }

    private static async Task<(string?, string?)> ImageAsync(JsonElement image, string? url, string? text, Func<long, Task<string?>> assetImage)
    {
        if (image.ValueKind != JsonValueKind.Object)
            return (url, text);
        if (image.TryGetProperty("clear", out var clear) && clear.ValueKind == JsonValueKind.True)
            return (null, null);
        if (image.TryGetProperty("reset", out var reset) && reset.ValueKind == JsonValueKind.True)
            (url, text) = (null, null);
        if (image.TryGetProperty("assetId", out var id) && id.TryGetInt64(out long assetId))
            url = await assetImage(assetId);
        if (image.TryGetProperty("hoverText", out var hover) && hover.ValueKind == JsonValueKind.String)
            text = hover.GetString();
        return (url, text);
    }

    private async Task RejoinAsync(GameSession session, int reason)
    {
        state.Load();
        var now = DateTimeOffset.UtcNow;
        state.Value.RecentRejoins.RemoveAll(t => now - t > RejoinWindow);
        if (state.Value.RecentRejoins.Count >= MaxRejoins)
        {
            Notify(Loc.T("Watcher.RejoinStopped"), Loc.T("Watcher.RejoinLimit", MaxRejoins), "");
            return;
        }

        Notify(Loc.T("Watcher.Disconnected", reason), Loc.T("Watcher.Rejoining", session.Game?.Name ?? Loc.T("Watcher.TheGame")), "");

        // A teleport or a manual rejoin during the wait means there's nothing to do.
        int joinsBefore = _joins;
        await Task.Delay(RejoinDelay);
        if (_joins != joinsBefore || _stopRequested)
            return;

        state.Value.RecentRejoins.Add(now);
        state.Save();
        Logger.Info(LogIdent, $"Auto-rejoining place {session.PlaceId}, server {session.JobId}");

        // The old client only shows an error now; close it so the new one can start cleanly.
        try { _roblox?.Kill(); }
        catch (InvalidOperationException) { }

        Process.Start(new ProcessStartInfo(Paths.CurrentExe) { UseShellExecute = false, ArgumentList = { GameHistory.RejoinLink(session.PlaceId, session.JobId) } });
    }

    // ---------- Tray ----------

    private void CreateTray()
    {
        _statusItem = new WinForms.ToolStripMenuItem(Loc.T("Watcher.Starting")) { Enabled = false };
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(Loc.T("Watcher.OpenNimbus"), null, (_, _) => Process.Start(new ProcessStartInfo(Paths.CurrentExe) { UseShellExecute = false }));
        menu.Items.Add(Loc.T("Watcher.StopExtras"), null, (_, _) => _stopRequested = true);

        _tray = new WinForms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Paths.CurrentExe),
            Text = "Nimbus",
            ContextMenuStrip = menu,
            Visible = true,
        };
    }

    private void SetTrayStatus(string? text)
    {
        string status = text ?? Loc.T("Watcher.InApp");
        if (_statusItem is not null)
            _statusItem.Text = status;
        if (_tray is not null)
            _tray.Text = ("Nimbus - " + status) is { Length: > 63 } long_ ? long_[..63] : "Nimbus - " + status;
    }
}

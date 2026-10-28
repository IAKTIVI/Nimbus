using System.Diagnostics;
using System.Text.RegularExpressions;
using Nimbus.Core;

namespace Nimbus.Integrations;

/// <summary>
/// Starts the user's own programs (Integrations → Your programs) while Roblox runs, and closes the ones that
/// should go when Roblox closes. Driven by the activity watcher: Roblox started, game joined, Roblox closed.
/// </summary>
internal sealed partial class CustomAppRunner(IEnumerable<CustomApp> apps)
{
    private const string LogIdent = "CustomApps";

    private readonly List<CustomApp> _apps = apps.Where(a => a.Enabled && File.Exists(a.Path)).ToList();
    private readonly List<(CustomApp App, Process Process)> _started = [];
    private readonly HashSet<CustomApp> _scheduled = [];
    private bool _closed;

    public bool IsEmpty => _apps.Count == 0;

    public static bool HasAny(IntegrationSettings options) => options.CustomApps.Any(a => a.Enabled && a.Path.Length > 0);

    public void OnRobloxStarted()
    {
        foreach (var app in _apps.Where(a => a.Games.Count == 0))
            Schedule(app);
    }

    public void OnGameJoined(long placeId, long? universeId)
    {
        foreach (var app in _apps.Where(a => a.Games.Count > 0 && a.WantsGame(placeId, universeId)))
        {
            // Joining another chosen game (or a teleport) restarts it only if it was closed in between.
            if (_started.Any(s => s.App == app && !HasExited(s.Process)))
                continue;
            _scheduled.Remove(app);
            Schedule(app);
        }
    }

    public Task OnRobloxClosedAsync()
    {
        _closed = true;
        return Task.WhenAll(_started.Where(s => s.App.CloseWithRoblox).Select(s => CloseAsync(s.App, s.Process)));
    }

    private void Schedule(CustomApp app)
    {
        if (!_scheduled.Add(app))
            return;
        _ = StartAsync(app);
    }

    private async Task StartAsync(CustomApp app)
    {
        if (app.DelaySeconds > 0)
            await Task.Delay(TimeSpan.FromSeconds(app.DelaySeconds));
        if (_closed)
            return;

        // Already open (the user started it, or it was left running): leave it alone, and never close it later.
        if (IsRunning(app.Path))
        {
            Logger.Info(LogIdent, $"{app.Name} is already running");
            return;
        }

        try
        {
            var process = Process.Start(new ProcessStartInfo(app.Path, app.Arguments)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(app.Path),
            });
            if (process is not null)
                _started.Add((app, process));
            Logger.Info(LogIdent, $"Started {app.Name}");
        }
        catch (Exception ex)
        {
            Logger.Warn(LogIdent, $"Couldn't start {app.Path}: {ex.Message}");
        }
    }

    private static async Task CloseAsync(CustomApp app, Process process)
    {
        try
        {
            if (process.HasExited)
                return;

            // Ask nicely first (like clicking X), so the program can save; then make sure it's gone.
            process.CloseMainWindow();
            using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await process.WaitForExitAsync(wait.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
            }

            Logger.Info(LogIdent, $"Closed {app.Name}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Logger.Warn(LogIdent, $"Couldn't close {app.Name}: {ex.Message}");
        }
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static bool IsRunning(string exe)
    {
        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe));
        try
        {
            return processes.Any(p =>
            {
                try { return string.Equals(p.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return true; } // can't look inside (e.g. admin app): assume it's the same
            });
        }
        finally
        {
            foreach (var p in processes)
                p.Dispose();
        }
    }

    /// <summary>
    /// The place id in a game link: "https://www.roblox.com/games/920587237/Adopt-Me", "…?placeId=920587237",
    /// or just the number. Null if there's none.
    /// </summary>
    public static long? ParseGameLink(string text)
    {
        text = text.Trim();
        if (long.TryParse(text, out long bare))
            return bare > 0 ? bare : null;

        var match = GameLinkRegex().Match(text);
        return match.Success && long.TryParse(match.Groups[1].Value, out long id) && id > 0 ? id : null;
    }

    [GeneratedRegex(@"(?:roblox\.com/(?:[a-z]{2}(?:-[a-z]{2})?/)?games/|placeId=)(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex GameLinkRegex();
}

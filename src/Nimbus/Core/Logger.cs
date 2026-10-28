using System.Text;

namespace Nimbus.Core;

/// <summary>Tiny thread-safe file logger. One file per run, the newest 10 are kept.</summary>
internal static class Logger
{
    private const int KeepFiles = 10;

    private static readonly Lock Gate = new();
    private static StreamWriter? _writer;

    public static string? CurrentFile { get; private set; }

    public static void Initialize(string mode)
    {
        try
        {
            Directory.CreateDirectory(Paths.Logs);
            CurrentFile = Path.Combine(Paths.Logs, $"Nimbus_{DateTime.Now:yyyyMMdd_HHmmss}_{Environment.ProcessId}.log");
            _writer = new StreamWriter(new FileStream(CurrentFile, FileMode.Create, FileAccess.Write, FileShare.Read), Encoding.UTF8) { AutoFlush = true };
            PruneOldLogs();
        }
        catch
        {
            // Logging must never stop Roblox from launching.
            _writer = null;
        }

        Info("Logger", $"Nimbus {AppInfo.Version} starting ({mode}) from {Environment.ProcessPath}, base dir {Paths.Base}");
    }

    public static void Info(string ident, string message) => Write("INFO", ident, message);

    public static void Warn(string ident, string message) => Write("WARN", ident, message);

    public static void Error(string ident, Exception ex) => Write("ERROR", ident, ex.ToString());

    public static void Error(string ident, string message) => Write("ERROR", ident, message);

    private static void Write(string level, string ident, string message)
    {
        string line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] [{ident}] {message}";
        System.Diagnostics.Debug.WriteLine(line);

        lock (Gate)
        {
            try { _writer?.WriteLine(line); }
            catch { /* disk full / file locked: ignore */ }
        }
    }

    private static void PruneOldLogs()
    {
        var old = new DirectoryInfo(Paths.Logs)
            .GetFiles("Nimbus_*.log")
            .OrderByDescending(f => f.CreationTimeUtc)
            .Skip(KeepFiles);

        foreach (var file in old)
        {
            try { file.Delete(); }
            catch { /* still open by another Nimbus process */ }
        }
    }
}

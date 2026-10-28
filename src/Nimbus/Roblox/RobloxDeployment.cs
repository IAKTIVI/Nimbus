using System.Buffers;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Nimbus.Core;

namespace Nimbus.Roblox;

public sealed record DeployProgress(string Status, double? Fraction);

/// <summary>Downloads Roblox straight from Roblox's CDN into Versions\&lt;version-hash&gt;.</summary>
internal sealed partial class RobloxDeployment(string versionsRoot, string downloadsRoot, HttpClient http)
{
    private const string LogIdent = "Deployment";
    public const string PlayerExe = "RobloxPlayerBeta.exe";
    private const string InstalledMarker = "Nimbus.installed";
    private const int MaxParallelDownloads = 4;
    private const int Attempts = 3;
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    private static readonly string[] VersionEndpoints =
    [
        "https://clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer",
        "https://clientsettings.roblox.com/v2/client-version/WindowsPlayer",
    ];

    private static readonly string[] Mirrors =
    [
        "https://setup.rbxcdn.com",
        "https://setup-aws.rbxcdn.com",
        "https://setup-ak.rbxcdn.com",
        "https://roblox-setup.cachefly.net",
        "https://s3.amazonaws.com/setup.roblox.com",
    ];

    private const string AppSettingsXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Settings>
        	<ContentFolder>content</ContentFolder>
        	<BaseUrl>http://www.roblox.com</BaseUrl>
        </Settings>

        """;

    private int _mirror;

    public static RobloxDeployment CreateDefault() => new(Paths.Versions, Paths.Downloads, Http.Client);

    [GeneratedRegex("^version-[0-9a-f]{16}$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionHashRegex();

    /// <summary>Version hashes end up in paths and URLs, so anything else is rejected.</summary>
    public static bool IsValidVersionHash(string? value) => value is not null && VersionHashRegex().IsMatch(value);

    public string GetVersionDirectory(string versionHash)
    {
        EnsureValid(versionHash);
        return Path.Combine(versionsRoot, versionHash.ToLowerInvariant());
    }

    public bool IsInstalled(string versionHash)
    {
        string dir = GetVersionDirectory(versionHash);
        return File.Exists(Path.Combine(dir, PlayerExe)) && File.Exists(Path.Combine(dir, InstalledMarker));
    }

    public async Task<ClientVersionInfo> GetLatestVersionAsync(CancellationToken ct)
    {
        Exception? last = null;
        foreach (string url in VersionEndpoints)
        {
            try
            {
                var info = await http.GetFromJsonAsync(url, NimbusJson.Default.ClientVersionInfo, ct);
                if (info is null || !IsValidVersionHash(info.ClientVersionUpload))
                    throw new InvalidDataException($"Unexpected version response from {url}");

                Logger.Info(LogIdent, $"Latest Roblox is {info.Version} ({info.ClientVersionUpload})");
                return info;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Logger.Warn(LogIdent, $"Version check via {url} failed: {ex.Message}");
                last = ex;
            }
        }

        throw new HttpRequestException(Loc.T("Deploy.Offline"), last);
    }

    public async Task<IReadOnlyList<Package>> GetManifestAsync(string versionHash, CancellationToken ct)
    {
        EnsureValid(versionHash);
        using var response = await GetFromCdnAsync($"{versionHash}-rbxPkgManifest.txt", ct);
        return PackageManifest.Parse(await response.Content.ReadAsStringAsync(ct));
    }

    /// <summary>Downloads, verifies and extracts every package of a version. Returns the version folder.</summary>
    public async Task<string> InstallAsync(string versionHash, IProgress<DeployProgress>? progress, CancellationToken ct)
    {
        string dir = GetVersionDirectory(versionHash);
        Logger.Info(LogIdent, $"Installing {versionHash} into {dir}");
        progress?.Report(new DeployProgress(Loc.T("Deploy.PackageList"), null));

        var packages = (await GetManifestAsync(versionHash, ct)).Where(ShouldInstall).ToList();

        // Anything already here is a half-finished install: start clean.
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);

        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(downloadsRoot);

        long totalBytes = packages.Sum(p => p.PackedSize);
        long downloadedBytes = 0;
        int extracted = 0;
        long lastReport = 0;

        void Report(bool force = false)
        {
            long now = Environment.TickCount64;
            if (!force && now - Interlocked.Read(ref lastReport) < 50)
                return;

            Interlocked.Exchange(ref lastReport, now);
            double download = totalBytes == 0 ? 1 : (double)Interlocked.Read(ref downloadedBytes) / totalBytes;
            double extract = (double)Volatile.Read(ref extracted) / packages.Count;
            double fraction = Math.Clamp(0.9 * download + 0.1 * extract, 0, 1);
            progress?.Report(new DeployProgress(Loc.T("Deploy.Downloading", fraction.ToString("P0", System.Globalization.CultureInfo.CurrentUICulture)), fraction));
        }

        Report(force: true);

        // Biggest first, so RobloxApp.zip starts immediately and the small ones fill in around it.
        var options = new ParallelOptions { MaxDegreeOfParallelism = MaxParallelDownloads, CancellationToken = ct };
        await Parallel.ForEachAsync(packages.OrderByDescending(p => p.PackedSize), options, async (package, token) =>
        {
            string zip = await DownloadPackageAsync(versionHash, package, bytes =>
            {
                Interlocked.Add(ref downloadedBytes, bytes);
                Report();
            }, token);

            ZipExtractor.Extract(zip, Path.Combine(dir, PackageMap.Player[package.Name]));
            Interlocked.Increment(ref extracted);
            Report();
        });

        await File.WriteAllTextAsync(Path.Combine(dir, "AppSettings.xml"), AppSettingsXml, ct);
        await File.WriteAllTextAsync(Path.Combine(dir, InstalledMarker), versionHash, ct);
        progress?.Report(new DeployProgress(Loc.T("Deploy.Finishing"), 1));

        ClearDownloadCache();
        Logger.Info(LogIdent, $"Installed {versionHash} ({packages.Count} packages, {totalBytes / 1048576.0:F0} MB)");
        return dir;
    }

    /// <summary>Deletes one installed version (used by Repair). Throws if Roblox still has files open.</summary>
    public void Remove(string versionHash)
    {
        string dir = GetVersionDirectory(versionHash);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        Logger.Info(LogIdent, $"Removed {versionHash} for a repair");
    }

    /// <summary>Removes every version folder except <paramref name="keepVersionHash"/>. Folders in use by a running Roblox are skipped.</summary>
    public void CleanupOldVersions(string keepVersionHash)
    {
        if (!Directory.Exists(versionsRoot))
            return;

        foreach (string dir in Directory.EnumerateDirectories(versionsRoot))
        {
            if (string.Equals(Path.GetFileName(dir), keepVersionHash, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                Directory.Delete(dir, recursive: true);
                Logger.Info(LogIdent, $"Removed old version {Path.GetFileName(dir)}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logger.Warn(LogIdent, $"Couldn't remove {Path.GetFileName(dir)} yet (probably still running): {ex.Message}");
            }
        }
    }

    private static bool ShouldInstall(Package package)
    {
        if (PackageMap.Skipped.Contains(package.Name))
            return false;

        if (PackageMap.Player.ContainsKey(package.Name))
            return true;

        Logger.Warn(LogIdent, $"Package {package.Name} isn't in the package map, skipping it");
        return false;
    }

    /// <summary>
    /// Deletes the downloaded packages once a version is fully installed (~210 MB). Until then they're kept,
    /// so an interrupted install resumes without downloading finished packages again.
    /// </summary>
    private void ClearDownloadCache()
    {
        foreach (string file in Directory.EnumerateFiles(downloadsRoot))
        {
            try { File.Delete(file); }
            catch (IOException) { /* another Nimbus is installing right now */ }
        }
    }

    private async Task<string> DownloadPackageAsync(string versionHash, Package package, Action<long> onBytes, CancellationToken ct)
    {
        string cached = Path.Combine(downloadsRoot, package.Signature);
        if (File.Exists(cached) && await HashMatchesAsync(cached, package.Signature, ct))
        {
            onBytes(package.PackedSize);
            return cached;
        }

        for (int attempt = 1; ; attempt++)
        {
            long received = 0;
            try
            {
                await DownloadOnceAsync($"{versionHash}-{package.Name}", cached, package.Signature, read =>
                {
                    received += read;
                    onBytes(read);
                }, ct);
                return cached;
            }
            catch (Exception ex) when (attempt < Attempts && !ct.IsCancellationRequested)
            {
                Logger.Warn(LogIdent, $"{package.Name}: attempt {attempt} failed ({ex.Message}), retrying");
                onBytes(-received);
                await Task.Delay(TimeSpan.FromSeconds(attempt), ct);
            }
        }
    }

    /// <summary>Streams one package to disk, hashing as it goes, and only moves it into the cache if the MD5 matches.</summary>
    private async Task DownloadOnceAsync(string file, string destination, string expectedMd5, Action<int> onRead, CancellationToken ct)
    {
        string temp = destination + ".part";
        byte[] buffer = ArrayPool<byte>.Shared.Rent(1 << 16);
        try
        {
            using var response = await GetFromCdnAsync(file, ct);
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);

            await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
                while (true)
                {
                    stall.CancelAfter(StallTimeout);
                    int read;
                    try
                    {
                        read = await source.ReadAsync(buffer, stall.Token);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        throw new TimeoutException($"Download of {file} stalled");
                    }

                    if (read == 0)
                        break;

                    md5.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    onRead(read);
                }
            }

            string actual = Convert.ToHexStringLower(md5.GetHashAndReset());
            if (actual != expectedMd5)
                throw new InvalidDataException($"Checksum mismatch for {file} (expected {expectedMd5}, got {actual})");

            File.Move(temp, destination, overwrite: true);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    /// <summary>GET from the CDN, falling back through the mirrors. Sticks with whichever mirror last worked.</summary>
    private async Task<HttpResponseMessage> GetFromCdnAsync(string file, CancellationToken ct)
    {
        Exception? last = null;
        for (int i = 0; i < Mirrors.Length; i++)
        {
            int index = (Volatile.Read(ref _mirror) + i) % Mirrors.Length;
            string url = $"{Mirrors[index]}/{file}";
            try
            {
                var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                if (response.IsSuccessStatusCode)
                {
                    Volatile.Write(ref _mirror, index);
                    return response;
                }

                var status = response.StatusCode;
                response.Dispose();

                // The main CDN answers 403 (sometimes 404) for files that don't exist, e.g. a mistyped pinned version.
                if (status is HttpStatusCode.Forbidden or HttpStatusCode.NotFound && index == 0)
                    throw new FileNotFoundException(Loc.T("Deploy.Missing", file));

                last = new HttpRequestException($"HTTP {(int)status} from {url}");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                last = ex;
            }

            Logger.Warn(LogIdent, $"Mirror {Mirrors[index]} failed for {file}: {last?.Message}");
        }

        throw new HttpRequestException(Loc.T("Deploy.Failed", file), last);
    }

    private static async Task<bool> HashMatchesAsync(string path, string expectedMd5, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        return Convert.ToHexStringLower(await MD5.HashDataAsync(stream, ct)) == expectedMd5;
    }

    private static void EnsureValid(string versionHash)
    {
        if (!IsValidVersionHash(versionHash))
            throw new ArgumentException($"'{versionHash}' is not a Roblox version hash (expected version-0123456789abcdef)");
    }
}

using System.Reflection;

namespace Nimbus.Core;

internal static class AppInfo
{
    public const string Name = "Nimbus";

    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    public static string UserAgent => $"{Name}/{Version}";

    public const string GitHubUrl = "https://github.com/IAKTIVI/Nimbus";

    public const string DiscordInvite = "https://discord.gg/nbVjGGfBsa";

    /// <summary>The same invite, opened straight in the Discord app.</summary>
    public const string DiscordAppInvite = "discord://-/invite/nbVjGGfBsa";
}

namespace Nimbus.Core;

/// <summary>Things Nimbus does while Roblox is running (Integrations page).</summary>
public sealed class IntegrationSettings
{
    public bool DiscordStatus { get; set; } = true;

    /// <summary>The user's own Discord application ID (Discord needs one to show a status).</summary>
    public string? DiscordAppId { get; set; }

    /// <summary>Adds a button to the status that lets Discord friends join the same server.</summary>
    public bool DiscordJoinButton { get; set; }

    /// <summary>Let games that use the BloxstrapRPC SDK change the status text.</summary>
    public bool GameStatus { get; set; } = true;

    public bool ServerLocation { get; set; } = true;

    public bool GameHistory { get; set; } = true;

    public bool AutoRejoin { get; set; }

    /// <summary>The music card on top of the Roblox window, for whatever plays on the PC.</summary>
    public bool MusicCard { get; set; } = true;

    /// <summary>The user's own programs, started alongside Roblox (bottom of the Integrations page).</summary>
    public List<CustomApp> CustomApps { get; set; } = [];
}

/// <summary>A program Nimbus starts while Roblox runs: at launch, or when one of the chosen games is joined.</summary>
public sealed class CustomApp
{
    public bool Enabled { get; set; } = true;

    /// <summary>Full path of the .exe.</summary>
    public string Path { get; set; } = "";

    public string Arguments { get; set; } = "";

    /// <summary>Empty: start it when Roblox starts. Otherwise only when one of these games is joined.</summary>
    public List<CustomAppGame> Games { get; set; } = [];

    /// <summary>Seconds to wait before starting it.</summary>
    public int DelaySeconds { get; set; }

    /// <summary>Close it again when Roblox closes (only if Nimbus started it).</summary>
    public bool CloseWithRoblox { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);

    /// <summary>Whether joining this place should start the program. A game counts by place or by the whole experience.</summary>
    public bool WantsGame(long placeId, long? universeId) =>
        Games.Any(g => g.PlaceId == placeId || (universeId is > 0 && g.UniverseId == universeId));
}

public sealed class CustomAppGame
{
    public long PlaceId { get; set; }

    /// <summary>The experience the place belongs to, so its sub-places (teleports) count too. 0 if unknown.</summary>
    public long UniverseId { get; set; }

    public string Name { get; set; } = "";

    public string? IconUrl { get; set; }
}

/// <summary>One game session in the History page.</summary>
public sealed class HistoryEntry
{
    public long PlaceId { get; set; }

    public string? JobId { get; set; }

    public string Name { get; set; } = "";

    public string? Creator { get; set; }

    public string? IconUrl { get; set; }

    public string? Location { get; set; }

    public DateTimeOffset JoinedAt { get; set; }

    public DateTimeOffset? LeftAt { get; set; }
}

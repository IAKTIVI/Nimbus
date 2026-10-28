namespace Nimbus.Core;

public enum RobloxPriority
{
    Normal,
    AboveNormal,
    High,
}

/// <summary>Options on the Performance page.</summary>
public sealed class PerformanceSettings
{
    /// <summary>Windows CPU priority given to Roblox when it starts.</summary>
    public RobloxPriority Priority { get; set; }

    /// <summary>Written into Roblox's own "Maximum Frame Rate" setting before launch; null leaves Roblox's setting alone.</summary>
    public int? FramerateCap { get; set; }

    /// <summary>Windows "Graphics settings": run Roblox on the high-performance GPU.</summary>
    public bool HighPerformanceGpu { get; set; }

    /// <summary>Windows compatibility option "Disable fullscreen optimizations" for Roblox.</summary>
    public bool DisableFullscreenOptimizations { get; set; }

    /// <summary>Delete old Roblox logs and temporary files before every launch.</summary>
    public bool CleanOnLaunch { get; set; }
}

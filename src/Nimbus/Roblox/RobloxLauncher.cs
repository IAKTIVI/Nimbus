using System.Diagnostics;
using Nimbus.Core;

namespace Nimbus.Roblox;

internal static class RobloxLauncher
{
    private const string LogIdent = "Launcher";

    /// <summary>The name Roblox gives its single-instance event.</summary>
    private const string SingletonName = "ROBLOX_singletonEvent";

    public static Process Start(string versionDirectory, LaunchRequest request)
    {
        var startInfo = new ProcessStartInfo(Path.Combine(versionDirectory, RobloxDeployment.PlayerExe))
        {
            WorkingDirectory = versionDirectory,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(request.RobloxArgument);

        Logger.Info(LogIdent, $"Starting Roblox ({request.Kind})");
        return Process.Start(startInfo) ?? throw new InvalidOperationException("Roblox didn't start");
    }

    /// <summary>Sets Roblox's Windows CPU priority. Done straight after starting, while Nimbus still holds the process handle.</summary>
    public static void ApplyPriority(Process roblox, RobloxPriority priority)
    {
        if (priority == RobloxPriority.Normal)
            return;

        try
        {
            roblox.PriorityClass = priority == RobloxPriority.High ? ProcessPriorityClass.High : ProcessPriorityClass.AboveNormal;
            Logger.Info(LogIdent, $"Roblox priority set to {priority}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Logger.Warn(LogIdent, $"Couldn't set Roblox's priority: {ex.Message}");
        }
    }

    /// <summary>
    /// Multi-instance: if a mutex already owns Roblox's single-instance name, new clients can't
    /// create their event and so they don't close the clients that are already open.
    /// The handle must stay open until the new client has finished starting.
    /// </summary>
    public static Mutex? ClaimMultiInstance()
    {
        try
        {
            return new Mutex(initiallyOwned: true, SingletonName);
        }
        catch (Exception ex)
        {
            Logger.Warn(LogIdent, $"Couldn't enable multi-instance: {ex.Message}");
            return null;
        }
    }
}

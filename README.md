# Nimbus

A lightweight Roblox launcher for Windows with a clean frosted-glass look. One small `Nimbus.exe`, no admin rights.

## Download

1. Go to **[Releases](../../releases/latest)** and download `Nimbus.exe`.
2. Run it and click **Install Nimbus**.
   - If Windows says "Windows protected your PC", click **More info → Run anyway** (Nimbus isn't code-signed).
   - If it asks for the **.NET Desktop Runtime**, click Yes and install it (one time, from Microsoft).
3. Play from the **Roblox (Nimbus)** desktop icon or the Play button on roblox.com.

## What it does

- **Launcher:** installs and updates Roblox straight from Roblox's servers, version pinning, multiple Roblox windows, repair.
- **Engine:** every FastFlag Roblox still allows (graphics API, anti-aliasing, textures, quality, gray sky, no grass, ...), with an editor that marks flags Roblox ignores. Potato / Balanced / Quality profiles and your own.
- **Mods:** death sounds (classic "oof", two soft built-in sounds, or your own files, with a play button to listen first), classic or custom cursors (dot, circle, crosshairs, your own image), fonts (free fonts like Monocraft or Press Start 2P download when you pick them, or your own), remove the water texture, and a mods folder. Originals are always restored when you turn a mod off.
- **Performance:** CPU priority, max FPS, high-performance GPU, fullscreen optimizations, and a cleaner for logs and caches.
- **While you play:** Discord status, server location, game history with rejoin and most played games, auto-rejoin, and a **music card** in Roblox for whatever plays on your PC (Spotify app, YouTube, ...) with an optional stats line (CPU, GPU, clock, session time, server). Press **Right Ctrl** in game to switch widgets on or off and move the card.
- **Uninstalling** removes Nimbus and its Roblox copy but keeps your settings, so a reinstall picks them back up.

Nimbus never injects into Roblox or touches its anti-cheat.

## Build it yourself

Needs the .NET 10 SDK.

```
dotnet publish src/Nimbus/Nimbus.csproj -c Release -r win-x64 -o publish
dotnet test Nimbus.slnx
```

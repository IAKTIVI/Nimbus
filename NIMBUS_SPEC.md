# Nimbus — Roblox Launcher Spec

A lightweight, from-scratch Roblox bootstrapper for Windows with a clean, macOS-style glass UI.

## Tech

| | |
|---|---|
| Language / UI | C# + WPF |
| Runtime | .NET 10 (LTS, supported to Nov 2028). .NET 8 support ends Nov 10 2026, so we skip it — same code, same WPF. |
| Build | Single-file EXE, framework-dependent (small download; installer prompts for the .NET Desktop Runtime if missing) |
| Codebase | Written from scratch (no Bloxstrap/Fishstrap code) |
| Language | English only, all strings in one resource file so translations can be added later |
| Platform | Windows 10/11 (glass effects need Windows 11; Windows 10 gets a solid translucent fallback) |

## Install & updates

- **Self-installer, no admin:** first run installs to `%LocalAppData%\Nimbus`, adds a Start menu shortcut and an "Apps & features" uninstall entry.
- Registers the `roblox-player://` and `roblox://` link handlers (HKCU), so launching from the website goes through Nimbus. Uninstall restores Roblox's handlers.
- **Nimbus self-update: notify only.** It checks GitHub Releases on launch and shows "New version available" with a download link.

## Bootstrapper

- **Auto install & update Roblox** straight from Roblox's CDN (`clientsettingscdn.roblox.com` for the version, `setup.rbxcdn.com` for packages). Downloads run in parallel and each package's MD5 is verified. Versions install to `Nimbus\Versions\<version-hash>` and old ones are cleaned up.
- **Version pinning / downgrade:** stay on a chosen version hash. Warning shown in the UI: Roblox usually rejects outdated clients within days.
- **Multi-instance:** run several Roblox clients at once by holding Roblox's singleton mutex.
- **Fast launch:** no splash delays; the launch window closes as soon as Roblox starts.
- **Launch window styles (user-selectable):** Glass card (logo + status + thin progress bar), Minimal bar, or Hidden unless updating.
- **Repair / reinstall:** re-verify and re-download broken Roblox files, plus "Reset all settings".

## Engine settings (FastFlags)

Since Sept 29 2025, Roblox **ignores every FastFlag that isn't on its allowlist**. Nimbus is built around that:

- **Friendly toggles** for every allowlisted flag:
  - Rendering API: Auto / D3D11 / Vulkan / OpenGL (`FFlagDebugGraphicsPrefer*`)
  - Anti-aliasing: MSAA 0/1/2/4/8 (`FIntDebugForceMSAASamples`)
  - Texture quality override (`DFFlagTextureQualityOverrideEnabled`, `DFIntTextureQualityOverride`)
  - Graphics quality level 1–21 (`DFIntDebugFRMQualityLevelOverride`)
  - Gray sky (`FFlagDebugSkyGray`)
  - Grass distance / no grass (`FIntFRMMinGrassDistance`, `FIntFRMMaxGrassDistance`), grass motion (`FIntGrassMovementReducedMotionFactor`)
  - Mesh/part detail distance (`DFIntCSGLevelOfDetailSwitchingDistance`, `…L12`, `…L23`, `…L34`)
  - Freeze lighting updates (`DFFlagDebugPauseVoxelizer`)
  - Exclusive fullscreen via Alt+Enter (`FFlagHandleAltEnterFullscreenManually`)
  - DPI fix (keeps full render resolution at >100% scaling) (`DFFlagDisableDPIScale`)
- **Advanced raw JSON editor.** Any flag not on the allowlist is marked "Ignored by Roblox", so you're never fooled by placebo flags. The allowlist ships with Nimbus and can be refreshed from the Nimbus repo when Roblox changes it.
- Flags are written to `ClientSettings\ClientAppSettings.json` and re-applied after every Roblox update.

## Visuals

- **Remove water texture:** deletes/blanks Roblox's water texture files so the surface is plain and easier to see through. Note: how transparent water is comes from each game's `Terrain.WaterTransparency`. In your own game, set it to `1` in Studio for fully clear water.
- **Gray sky**
- **No grass**
- **Plain / dark textures:** swaps material textures (wood, brick, grass…) for flat ones.
- **Exclusive fullscreen & DPI fix**

## Mods

- Restore classic "oof" death sound (plus other classic sound/cursor presets)
- Custom cursor (presets or your own image)
- Custom font (any `.ttf`/`.otf` replaces Roblox's UI font)
- **Mods folder:** any file dropped in overrides the matching Roblox file, re-applied after every update
- All mods and visual file changes are re-applied automatically when Roblox updates

## Performance

- **Process priority:** Normal / Above normal / High, applied when Roblox starts
- **Max FPS:** sets Roblox's own Max Frame Rate option (60–240) before launch. Real FPS-unlock flags are no longer on the allowlist.
- **Cache & log cleaner:** on launch or with a button (old logs, temp downloads, HTTP cache)
- **Windows tweaks (per-app, undoable):** force the high-performance GPU and disable fullscreen optimizations for the Roblox EXE, re-applied to each new Roblox version path

## Profiles

- Built-in: **Potato**, **Balanced**, **Quality**. Each sets FastFlags + visuals + performance options at once.
- **Custom profiles:** save, rename, delete and switch your own.

## Integrations (background watcher)

A tiny tray process runs **only while Roblox is running** (~20–30 MB RAM, near-zero CPU) and exits when Roblox closes. It reads Roblox's log file, so there is no injection into the game.

- **Discord Rich Presence:** game name, icon, play time, optional Join button
- **Server location popup:** region notification on join (looks up the server IP with a geo-IP service)
- **Game history:** recently played games/servers with "Rejoin this server"
- **Game RPC support:** compatible with the BloxstrapRPC SDK, so games can set custom Discord status
- **Auto-rejoin:** detects disconnects (e.g. error 277) and rejoins the same game/server automatically (toggle + retry limit)

## UI

- macOS-inspired: frosted glass (Windows 11 Acrylic/Mica), lots of whitespace, rounded corners, simple sidebar, no clutter
- Theme: **Light / Dark / Follow Windows** toggle + **accent color picker**
- Settings pages: **Launch · Engine · Visuals · Performance · Mods · Integrations · Profiles · Appearance · About**

## Out of scope (by design)

- No injection into the Roblox process and nothing that touches Roblox's anti-cheat, which keeps accounts safe
- No FPS unlock beyond Roblox's native 240 cap, and no telemetry/network "tweak" flags (not allowlisted, they do nothing)
- No account manager / cookie storage
- No Roblox Studio support, game shortcuts or other languages (for now)

## Build order

1. **Core:** project setup, settings storage, self-installer, protocol handler, Roblox CDN install/update, launch
2. **Engine:** FastFlag toggles, advanced editor with allowlist checks, profiles
3. **Visuals & mods:** water/textures/sky/grass, oof sound, cursor, font, mods folder, re-apply on update
4. **Performance:** priority, Max FPS, GPU/fullscreen tweaks, cleaner
5. **Watcher:** log reader, Discord RPC, server location, history, BloxstrapRPC, auto-rejoin
6. **Polish:** glass theme + accent, launch window styles, update notifier, repair/reset

## Next features (decided with the user, 2026-10-28)

Keep everything that exists as it is; only add. New UI must match the existing look (frosted-glass cards, short friendly descriptions, accent toggles) and keep Nimbus lightweight.

1. **Thank-you card** — shown once, right after a *fresh* install (not after updates). Glass card in Nimbus style with: "Nimbus is your Roblox launcher ✓" (install already sets the link handler; keep that automatic), **Join the Discord** button (invite `https://discord.gg/nbVjGGfBsa`; open it in the Discord app if installed — `discord://` link — else the browser), **GitHub** button (`https://github.com/IAKTIVI/Nimbus`), **Play Roblox** button, **Open settings** button. Nothing else.
2. **Auto-update Nimbus** — toggle on the About page, **on by default**. When on: before Roblox starts, check the latest GitHub release (UpdateChecker already exists), download `Nimbus.exe`, install it quietly (`-install`, the installer can replace a running EXE), then continue launching. Never update while a game is running.
3. **Custom integrations** — new section at the bottom of the Integrations page. Each entry: program (.exe picker), optional launch arguments, optional "only for these games" (pick from the History list with icons; also allow pasting a game link), optional delay before start, optional "close it when Roblox closes". Driven by the existing ActivityWatcher (it knows launch, join and exit).
4. **Launch window customization** — Appearance page: custom title text, custom icon image (any picture, resized), and a **Preview** button that shows the launch card for a few seconds without starting Roblox. No extra styles.
5. **Languages** — start with 5: English, German, Spanish, French, Portuguese. Chosen in the installer (Welcome window, defaults to the Windows language if it's one of the 5, else English) and changeable on the Appearance page. Build it so more languages can be added later (all UI text moved to per-language resource files).

Build order: 1+2 first (small, useful for people downloading from GitHub), then 3, then 4, then 5 (biggest; touches every page). The user has no extra ideas right now.

Links: GitHub https://github.com/IAKTIVI/Nimbus · Discord https://discord.gg/nbVjGGfBsa

## Design reference

The UI follows a dark frosted-glass style: Mica backdrop, dark base layer, two soft radial glows, faint white glass panels, white pill primary buttons with an accent glow, Windows-style caption buttons. Dark by default.

## Progress

- **Nimbus 1.0.0 (2026-10-28)**: first release of the fresh repository (history squashed into one commit). Top bar is 5 icons | Play | 5 icons (Profiles moved left). New Widgets page (menu key, darken, snow, music card, stats line), moved off Integrations. Pages start at the top and fade under the title when scrolled. Glow centred behind the bar. "-" instead of "·" in all UI text. History: "Clear history" keeps playtime; "Clear playtime" button and ✕ per game in Most played. Remove water texture moved to Engine → Visuals. Mods: death sound tiles with ▶ preview (Default, Classic oof, five sounds Nimbus synthesises as WAV in `Mods/SoundPresets.cs`, the user's own files in `Mods\Sounds`; the old single `DeathSound.*` moves there); font tiles (Roblox, five OFL fonts downloaded on demand from pinned URLs and SHA-256 checked in `Mods/FontPresets.cs` into `Mods\Fonts`, own file). Play panel only opens when Roblox needs installing/updating, centred under Play. Right Ctrl menu: settings-style glass with switches, no key name, never activated (`WS_EX_NOACTIVATE` + `MA_NOACTIVATE`; the old focus grab with AttachThreadInput left keys stuck so you couldn't run), closes on Right Ctrl / Done / click outside; snow pauses while the music card is dragged. Overlays remember Roblox's window (`UI/RobloxWindowFinder.cs`) instead of walking all windows several times a second. Uninstall keeps settings, profiles, widgets, history, playtime and mods; removes the EXE, State.json, Roblox versions, downloads, logs and mod backups.
- **Top bar + widgets (2026-10-28, v0.9.3, not committed until the user approves)**: the sidebar is replaced by one glass pill bar at the top: page icons (name on hover) with a big Play button in the middle; hovering Play opens a panel with the Roblox version, update status and Launch Roblox. Widgets (Integrations → Widgets in game, `Widgets.json`): the music card, plus CPU / GPU load (`SystemLoad`: GetSystemTimes + PDH "GPU Engine" counters), clock, session time and server location shown as one line at the bottom of the music card (with music off or nothing playing, the card is just that line). Right Ctrl (changeable; `KeyWatcher` = low-level keyboard hook on its own thread that never swallows keys) opens `WidgetMenu` over the game: dim + falling snow (both switchable), switches, Reset card position; the card shows "Nothing playing" while it's open so it can be moved. Tried and dropped after user testing: a ping estimate (Roblox servers block ICMP/TCP, the last router on the route was far off), separate draggable text widgets and a snap grid. The music card no longer takes Insert. FastFlags box: explanation and example removed. The watcher skips Roblox's `*_CrashHandler_last.log` (it used to follow it sometimes and miss the game). Most played merges places of one experience (lobby + match) and plays its root place. Icons re-compressed losslessly (pixel-identical) to keep the EXE under 1 MB; re-compress the same way if `tools/make-icon.ps1` regenerates them.
- **Playtime + language default (2026-10-28, v0.9.2)**: History page → "Most played": top 5 games by total time (sessions, last played, a bar against the most played game, Play). Totals live in `Playtime.json` (per experience, so teleports add up; sessions under 30 s or over 24 h don't count), are filled from the watcher when a game ends (only while Game history is on), start from the existing History on first use, and are wiped by "Clear history". Language: the first start sets `Settings.Language` once: English for people who already had Nimbus (Settings.json or State.json exist), Windows' language for new installs.
- **Languages (2026-10-28, v0.9.0)**: English, Deutsch, Español, Français, Português (Brazil). All UI text lives in `src\Nimbus\Lang\<code>.txt` ("Key = Text", `\n` = line break, `{0}` placeholders); the build gzips them into the EXE (~9 KB each). XAML uses `{l:T Key}`, code uses `Loc.T("Key", args)`; missing keys fall back to English. `Settings.Language` null = Windows' language if supported, else English. Picked in the Welcome window (globe button, top left; reopens the window in that language) and on Appearance → Language (restarts the settings window). To add a language: copy `en.txt`, translate, add it to `Loc.Languages`; `LanguageTests` checks every key and placeholder. Built-in profile names (Potato / Balanced / Quality) stay English.
- **Launch window customization (2026-10-28, v0.9.0)**: Appearance → Launch window → Preview (the card for 4 s, nothing starts). Custom title and icon were built in 0.9.0 and removed again at the user's request (leftover `Mods\LaunchIcon.png` is deleted on start).
- **Custom integrations (2026-10-28, v0.9.0)**: Integrations → Your programs. Each program: on/off, launch arguments, "only for these games" (from History with icons, or a pasted game link / place id; matches the place or its whole experience), wait 0/5/10/30/60 s, close with Roblox (only if Nimbus started it: close window, then kill after 5 s). Run by `CustomAppRunner` from the ActivityWatcher (Roblox started / game joined / Roblox closed). A program that's already running is left alone.
- **Thank-you card + auto-update (2026-10-28, v0.9.0)**: `ThanksWindow` (`-thanks`) opens once after a fresh install (Welcome window or `-install` when Nimbus wasn't installed yet): "Nimbus is your Roblox launcher", Play Roblox, Settings, Discord (`discord://-/invite/…` when the app is registered, else the browser), GitHub. Auto-update (About page, on by default, `Settings.AutoUpdate`): before each launch the installed single-file EXE checks the latest GitHub release (4 s limit), downloads its `Nimbus.exe` asset, checks it's a real EXE, runs it with `-update` (quiet install, no windows, exit code 0/1), then starts the new copy with the same launch (`NIMBUS_JUST_UPDATED` stops a second check) and exits. Skipped while any Roblox runs; any failure just launches as before. Releases must attach a file named exactly `Nimbus.exe`.
- **Music card (2026-10-28, v0.8)**: player card glued to the top right of the Roblox window (a separate topmost window; nothing injected into Roblox) for whatever plays on the PC (Spotify app, YouTube or any site in a browser...), read and controlled through Windows' media session API (GlobalSystemMediaTransportControls) called via the raw WinRT ABI in `PcMedia.cs` so Nimbus stays < 1 MB. No account or Premium needed. Never interrupts play: clicks don't take focus (WS_EX_NOACTIVATE + MA_NOACTIVATE), shown only while Roblox is in front, Insert toggles it (only claimed while Roblox is active). Not shown in exclusive fullscreen.
- **Step 6 (polish): done** (2026-10-28, v0.6.0). Appearance page (Follow Windows / Light / Dark, 7 accent colors + any #RRGGBB, accent tints the glow), launch window styles (Glass card / Minimal bar / Hidden unless updating), Repair Roblox (fresh download, keeps settings & mods), Reset all settings, notify-only update check against GitHub Releases (off until `UpdateChecker.Repository` is set). Installer can now update while a Nimbus helper is running (moves the running EXE aside as `Nimbus.exe.old`, deleted on a later start).
- **Step 5 (while you play): done** (2026-10-28, v0.5.0). A tray-icon watcher follows Roblox's log while it runs (join `! Joining game`, server `UDMUX Address`, leave `leaveUGCGameInternal`, `Disconnection Notification. Reason: N`, `[BloxstrapRPC] {...}`). Discord status over Discord's local IPC pipe (needs the user's own Discord Application ID), optional Join button, BloxstrapRPC SetRichPresence; server location via ipinfo.io as a Windows notification; History page (icon, creator, location, Rejoin server / Play); auto-rejoin for reasons 277/278/279 only, max 3 in 10 min, cancelled by teleports. Game info from apis/games/thumbnails.roblox.com.
- **Step 4 (performance): done** (2026-10-27, v0.4.0). Performance page: CPU priority (Normal / Above normal / High, set right after Roblox starts), Max FPS (writes only `FramerateCap` in Roblox's `GlobalBasicSettings_13.xml`, skipped while Roblox runs), high-performance GPU + disable fullscreen optimizations (per-EXE Windows options that follow each Roblox update and are removed on toggle-off / uninstall), clean-up of Roblox logs, temp files, installer downloads, asset cache and Nimbus leftovers (with sizes; in-use files skipped), optional light clean on every launch. Nimbus now deletes its ~210 MB package downloads after a successful install. Profiles still cover only the Engine page.
- **v0.3.1** (2026-10-27): "Roblox (Nimbus)" desktop icon (switch on the Launch page) and built-in cursor presets drawn by Nimbus (Dot, Circle, Circle + dot, Crosshair, Gap crosshair) in a tile picker; your own cursor image is kept when switching.
- **Step 3 (mods): done** (2026-10-27, v0.3.0). Mods page: remove water texture (hides the 25 `water\normal_XX.dds` files), death sound (classic = Roblox's own `oof.ogg`, or your own file), cursor (classic = Roblox's own older arrows, or your own image resized to 64x64), custom font (all 43 font families pointed at it), and the `Modifications` folder. Every original is backed up per Roblox version and put back when a mod is turned off; verified byte-for-byte against real Roblox files. Dropped: "plain / dark textures" — Roblox no longer ships material textures locally (only Plastic), it streams them; Texture quality → Lowest on the Engine page is the working alternative. No copyrighted sounds or images are bundled.
- **Step 2 (engine): done** (2026-10-27, v0.2.0). Engine page: pickers/toggles for all 18 allowlisted flags, advanced JSON editor that marks each flag "Works" / "Ignored by Roblox", flags written to `ClientSettings\ClientAppSettings.json` on every launch. Profiles page: Potato / Balanced / Quality presets, save/rename/delete your own, reset to Roblox defaults. Not done yet: refreshing the allowlist online (needs the GitHub repo first).
- **Step 1 (core): done** (2026-10-27). Self-installer, link handling, Roblox download/update from the CDN (parallel, MD5-checked, cached), version pinning, multi-instance, glass settings window, launch card, welcome window. 20 tests pass (incl. a real Roblox download). Release build: one 523 KB `Nimbus.exe`, settings window ~52 MB RAM.

## Build

- Needs the .NET 10 SDK (installed).
- Release: `dotnet publish src/Nimbus/Nimbus.csproj -c Release -r win-x64 -o publish` → `publish\Nimbus.exe`
- Tests: `dotnet test Nimbus.slnx` (set `NIMBUS_INTEGRATION=1` to include the real Roblox download).
- `NIMBUS_HOME=<folder>` makes Nimbus use another data folder (for testing without touching the real install).
- Install / update: run `publish\Nimbus.exe` from File Explorer (or `Nimbus.exe -install` from a normal terminal). Don't install from a tool running inside a Store/MSIX app (for example a terminal built into such an app): Windows redirects its AppData writes into that app's private storage, and Nimbus refuses to install there.
- Icon: save the artwork as `tools\icon-source.png`, then run `tools\make-icon.ps1`.

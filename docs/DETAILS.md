# Compatibility layer (dev builds)

A BepInEx 6 IL2CPP plugin for **CarX Drift Racing Online 2** (Unity 6000.3, BepInEx 6.0.0-be.791). Versions are
not bumped for dev builds; the log and the release notes carry the commit hash (the "build id") instead.

Other mods build on it: the shared API (`GameHooks.Create`) is documented in [API.md](API.md). This page covers what
the plugin does by itself, how to build it, and how to check it works.

## The status line

An in-game menu is planned. For now the plugin draws a single line in the **bottom-left corner of the screen**,
always on:

```
4 mods loaded
4 mods loaded, 3 with errors        <- "3 with errors" in red
3 mods loaded, 1 failed to load     <- "1 failed to load" in red
```

- **Loaded** is every BepInEx plugin that started, not counting the layer itself.
- **With errors** is a loaded mod that has logged an *Error* or *Fatal* line under its own log source (its plugin
  name). Warnings do not count, and errors logged under some other source name cannot be tied to a mod. How a mod
  should log is in [API.md](API.md#logging-conventions).
- **Failed to load** is a mod BepInEx logged `Error loading [X]` or `Could not load [X]` for.
- Mods that loaded before the layer are picked up by reading `BepInEx/LogOutput.log` once at startup, so keep
  BepInEx's disk log enabled (it is by default).
- Turn the line off with `ShowStatusLine = false` in `BepInEx/config/carx.modmanager.cfg`.

It is drawn with Unity's IMGUI inside the engine, so it works on DX11 and DX12 alike and sits under ReShade
instead of hooking `Present`.

## Source layout

| Path | What it is |
|---|---|
| `src/Plugin.cs` | The BepInEx plugin: GUID, name, version, build id, the `ShowStatusLine` setting. |
| `src/Api/GameHooks.cs` | The public API for other mods. |
| `src/StatusLine/ModStatusTracker.cs` | Listens to the BepInEx log and works out the text of the line. |
| `src/StatusLine/StatusLine.cs` | Draws the line (position, scale, drop shadow); disables itself after repeated drawing errors. |
| `src/StatusLine/StatusLineBehaviour.cs` | The one injected Unity component whose `OnGUI` calls `StatusLine.Draw`. |
| `src/StatusLine/Imgui.cs` | The few IMGUI calls the line uses, each guarded against being stripped from the game build. |
| `ci/UnityStubs/` | Stand-ins for Unity's assemblies, used only by CI to compile (see below). |

## Download

Every push to `main` builds the plugin on GitHub Actions and republishes the **0.1-alpha** pre-release in this repo's
Releases (a zip only). Extract `CarXModManager.zip` into `BepInEx/plugins`; it holds the `CarXModManager` folder
with `CarXModManager.dll` and `CarXModManager.xml` (the API's IntelliSense documentation, for mod authors).

CI has no access to the game's interop assemblies, so it compiles against stand-ins in `ci/UnityStubs/` with the
same assembly names and member shapes; the DLL binds to the real ones when the game loads it. The stand-ins are
written from knowledge of Unity's API, so a wrong signature shows up as a `MissingMethodException` or a
`STATUSLINE_DRAW_ERROR` line in `LogOutput.log`, not as a build error.

## Build yourself

```bash
dotnet build -c Release
```

The game is expected at `D:/SteamLibrary/steamapps/common/CarX Drift Racing Online 2`. Override with
`-p:GameDirectory="..."`. The build copies `CarXModManager.dll` to `BepInEx/plugins/CarXModManager/`
(`-p:DeployToGame=false` to skip).

To build somewhere without the game installed, take BepInEx from NuGet and point at a folder holding just the
interop DLLs the plugin uses (`UnityEngine.CoreModule`, `UnityEngine.IMGUIModule`, copied from the game's
`BepInEx/interop`):

```bash
dotnet build -c Release -p:UseBepInExPackages=true -p:GameInteropDirectory=/path/to/interop-min
```

## Checking it works

After launching, `BepInEx/LogOutput.log` should contain, under `[CarX Mod Manager]`:

1. `COMPATLAYER_LOADED` - the plugin loaded (with the build id).
2. `STATUSLINE_FIRST_DRAW` - IMGUI runs on this Unity build; it also prints the graphics API in use.

and the line should be visible at the bottom-left. If IL2CPP stripped an IMGUI method the game build needs, you
get `IMGUI_CALL_UNAVAILABLE - <name>` instead. All log codes the layer writes are listed in
[API.md](API.md#log-codes-the-layer-writes).

## Notes

- Il2CppInterop warns that Unity 6.3 is newer than its signature database. It works today but is the most likely
  thing to break on a game update.
- The earlier overlay window (tabs, theme, key picker, mouse capture, conflict scan) was removed; it is in the git
  history if the in-game menu wants any of it back.

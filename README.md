# CarX Mod Manager (dev builds)

A BepInEx 6 IL2CPP plugin for **CarX Drift Racing Online 2** (Unity 6000.3, BepInEx 6.0.0-be.791). Versions are
not bumped for dev builds; the title in the log and the release notes carry the commit hash instead.

An in-game menu is planned. For now the plugin does one thing: it draws a single line in the **bottom-left corner
of the screen**, always on:

```
4 mods loaded
4 mods loaded, 3 with errors        <- "3 with errors" in red
3 mods loaded, 1 failed to load     <- "1 failed to load" in red
```

- **Loaded** is every BepInEx plugin that started, not counting the manager itself.
- **With errors** is a loaded mod that has logged an *Error* or *Fatal* line under its own log source (its plugin
  name). Warnings do not count, and errors logged under some other source name cannot be tied to a mod.
- **Failed to load** is a mod BepInEx logged `Error loading [X]` or `Could not load [X]` for.
- Mods that loaded before the manager are picked up by reading `BepInEx/LogOutput.log` once at startup, so keep
  BepInEx's disk log enabled (it is by default).
- Turn the line off with `ShowStatusLine = false` in `BepInEx/config/carx.modmanager.cfg`.

It is drawn with Unity's IMGUI inside the engine, so it works on DX11 and DX12 alike and sits under ReShade
instead of hooking `Present`.

## Download

Every push to `main` builds the plugin on GitHub Actions and refreshes the **latest** pre-release in this repo's
Releases (a zip only). Extract `CarXModManager.zip` into the game folder; it holds
`BepInEx/plugins/CarXModManager/CarXModManager.dll`.

CI has no access to the game's interop assemblies, so it compiles against stand-ins in `ci/UnityStubs/` with the
same assembly names and member shapes; the DLL binds to the real ones when the game loads it. The stand-ins are
written from knowledge of Unity's API, so a wrong signature shows up as a `MissingMethodException` /
`CARXMM_FAULT` line in `LogOutput.log`, not as a build error.

## Build yourself

```bash
dotnet build -c Release
```

Defaults match the Ferrari 458 project: the game is expected at
`D:/SteamLibrary/steamapps/common/CarX Drift Racing Online 2`. Override with `-p:GameDirectory="..."`. The build
copies `CarXModManager.dll` to `BepInEx/plugins/CarXModManager/` (`-p:DeployToGame=false` to skip).

To build somewhere without the game installed, take BepInEx from NuGet and point at a folder holding just the
interop DLLs the plugin uses (`UnityEngine.CoreModule`, `UnityEngine.IMGUIModule`, copied from the game's
`BepInEx/interop`):

```bash
dotnet build -c Release -p:UseBepInExPackages=true -p:GameInteropDirectory=/path/to/interop-min
```

## Checking it works

After launching, `BepInEx/LogOutput.log` should contain:

1. `CARXMM_LOADED` - the plugin loaded.
2. `CARXMM_ONGUI_FIRED` - IMGUI runs on this Unity build; it also prints the graphics API in use.

and the line should be visible at the bottom-left. If IL2CPP stripped an IMGUI method the game build needs, you
get `CARXMM_UI_UNAVAILABLE - <name>` instead.

## Notes

- Il2CppInterop warns that Unity 6.3 is newer than its signature database. It works today but is the most likely
  thing to break on a game update.
- The earlier overlay window (tabs, theme, key picker, mouse capture, conflict scan) was removed; it is in the git
  history if the in-game menu wants any of it back.

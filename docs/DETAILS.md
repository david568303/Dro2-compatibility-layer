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

## Shared API for other mods (`CarXModManager.Api`)

Other mods reference `CarXModManager.dll` and declare `[BepInDependency("carx.modmanager")]`. Game-wide helpers live
here so each mod doesn't re-implement them:

- `SafePatch.TryPatch(harmony, log, typeName, assemblyName, methodName, parameterCheck, prefix, postfix)` patches a
  game method found by name at run time (no compile-time dependency on the game's types). Every failure (type
  renamed, method changed, patch refused) is logged as an **Error under the calling mod's own log source**, so the
  mod shows up in the status line as "with errors" and a game update that moves a method is visible at once.
- `TextureTools.TryRead` / `TryWriteInPlace` copy the pixels of a readable `Texture2D` out as RGBA bytes and write
  processed pixels back (SetPixels32 + Apply), so the processing in between can be plain managed code.
- `TextureReplacer` hands the game a processed version of a texture *before it first uses it*, so there is no frame
  showing the original. Call `Replace(texture)` from a prefix on the game method that receives the texture and assign
  the result back to the argument. Textures with Read/Write disabled are copied on the GPU first (the original is
  left alone); results are cached per texture and old copies are destroyed, so reopening previews does not leak.
  `TextureReplacer` can also keep results on disk (`cache:` argument, a `TextureCacheOptions`): finished textures are
  stored under a hash (SHA-256) of the source pixels plus a settings key, so a texture that did not change is never
  processed twice, even across game restarts. Entries are compressed, written atomically and trimmed least-recently-used
  to a size limit.
- `MainThread.Post(name, work)` runs work on Unity's main thread from any thread, so heavy processing can run in the
  background and only the Unity-facing part runs on the main thread.

## Download

Every push to `main` builds the plugin on GitHub Actions and republishes the **0.1-alpha** pre-release in this repo's
Releases (a zip only). Extract `CarXModManager.zip` into `BepInEx/plugins`; it holds
the `CarXModManager` folder.

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

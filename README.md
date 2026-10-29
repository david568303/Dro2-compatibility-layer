# CarX Mod Manager (milestone 1)

A BepInEx 6 IL2CPP plugin for **CarX Drift Racing Online 2** (Unity 6000.3, IL2CPP, BepInEx 6.0.0-be.791) that
gives mods one shared in-game overlay and a small, stable API.

**Status: compiles against the real BepInEx 6.0.0-be.791 packages, with the Unity types stubbed; never run in
the game.** The game's own interop assemblies (`BepInEx/interop`) only exist on a machine that has launched the
game once, so the Unity-facing calls (IMGUI in `src/Ui.cs`, the New Input System key read) are unverified until
a build against the real interop succeeds. Send the compiler output and `LogOutput.log` back and it gets fixed
from there.

## Download

Every push to `main` builds the plugin on GitHub Actions and refreshes the **latest** pre-release in this repo's
Releases. Extract `CarXModManager.zip` into the game folder (it holds
`BepInEx/plugins/CarXModManager/CarXModManager.dll`). CI has no access to the game's interop assemblies, so it
compiles against stand-ins in `ci/UnityStubs/` with the same assembly names and member shapes; the DLL binds to
the real ones when the game loads it. Those stand-ins are written from knowledge of Unity's API, so a wrong
signature shows up as a `MissingMethodException` / `CARXMM_FAULT` line in `LogOutput.log`, not as a build error.

## What milestone 1 does

- Overlay opened with **Delete** by default. Change it in **Settings -> Change key** (press the key you want) or
  in the config file; Settings shows both the Input System name and what your keyboard layout calls the key, since
  e.g. the main Delete key (`Delete`) and the numpad's Del/Supr (`NumpadPeriod`) are different keys. Avoid Home,
  which ReShade uses. Drawn with Unity IMGUI inside the engine, so it works on DX11 and DX12 alike and sits under
  ReShade instead of hooking `Present`.
- Flat, square, dark "mod menu" look (accent title bar, tab row, cards). If the game's build lacks the styling
  calls it falls back to the plain default look and logs `CARXMM_THEME_UNAVAILABLE`.
- The mouse is **captured** while the overlay is open: the game's Input System mouse device is disabled so clicks,
  movement and scrolling no longer reach the game, and it is re-enabled on close. Turn it off with
  `BlockGameMouse = false` in `BepInEx/config/carx.modmanager.cfg` if the menu stops responding to clicks.
- **Mods** tab: every plugin BepInEx loaded, with GUID, version, dependencies and declared impact.
- **Panels** tab: panels other mods register through `ModManagerApi.RegisterPanel`. A panel that throws three
  times is isolated instead of breaking the overlay.
- **Conflicts** tab: methods patched by more than one Harmony owner, flagged when a `bool` prefix can skip the
  original method.
- **Log** tab: live BepInEx log. **Settings** tab: online-safe flag, UI scale, graphics API, versions.
- Frees the cursor while open and restores it on close.

Not done yet: turning mods on/off (needs a preloader patcher that runs before the chainloader), blocking mods
online (the flag only warns; there is no reliable "am I online" signal yet), and blocking keyboard and gamepad
input to the game while the overlay is open (only the mouse is captured).

## Build

```bash
dotnet build -c Release
```

Defaults match the Ferrari 458 project: the game is expected at
`D:/SteamLibrary/steamapps/common/CarX Drift Racing Online 2`. Override with
`-p:GameDirectory="..."`. The build copies `CarXModManager.dll` to `BepInEx/plugins/CarXModManager/`
(`-p:DeployToGame=false` to skip).

To build somewhere without the game installed, take BepInEx from NuGet and point at a folder holding just the
interop DLLs the plugin uses (`UnityEngine.CoreModule`, `UnityEngine.IMGUIModule`, `Unity.InputSystem`,
`Il2Cppmscorlib`, copied from the game's `BepInEx/interop`):

```bash
dotnet build -c Release -p:UseBepInExPackages=true -p:GameInteropDirectory=/path/to/interop-min
```

## First test checklist

Launch once with `-force-d3d11` and once with `-force-d3d12` (Steam launch options), then check
`BepInEx/LogOutput.log` for:

1. `CARXMM_LOADED` - the plugin loaded.
2. `CARXMM_ONGUI_FIRED` - IMGUI callbacks run on Unity 6.3 IL2CPP. It also prints the graphics API in use.
   If this line never appears, the overlay needs a uGUI fallback.
3. Press Delete in a race or menu: `CARXMM_OPEN`, the window appears, the cursor is free.
4. The Ferrari mod is listed under **Mods**.

## Using it from a mod

Reference `CarXModManager.dll` and add a soft dependency:

```csharp
[BepInDependency("carx.modmanager", BepInDependency.DependencyFlags.SoftDependency)]
[ModImpact(ModImpact.Cosmetic)]            // Cosmetic = local visuals/audio/UI only
public sealed class Plugin : BasePlugin { ... }
```

Keep calls into the manager in a separate class so the mod still loads when the manager is absent:

```csharp
internal static class ManagerBridge
{
    private static bool _missing;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool Query() => CarXModManager.Api.ModManagerApi.IsOverlayOpen;

    public static bool OverlayOpen
    {
        get
        {
            if (_missing) return false;
            try { return Query(); }
            catch (Exception e) when (e is FileNotFoundException or TypeLoadException) { _missing = true; return false; }
        }
    }
}
```

For example, in the Ferrari mod, start `ReadHotkeys()` with `if (ManagerBridge.OverlayOpen) return;` so F6/F7/F9
do nothing while the overlay is open. Add this to that project's csproj:

```xml
<Reference Include="$(GameDirectory)/BepInEx/plugins/CarXModManager/CarXModManager.dll" Private="false" />
```

## Limits of the "compatibility layer"

- Prefix/postfix patches work on IL2CPP methods; transpilers do not (there is no IL to rewrite).
- Inlined methods and Burst-compiled DOTS code cannot be hooked.
- Only Harmony patches show up in the conflict scan.
- Il2CppInterop warns that Unity 6.3 is newer than its signature database. It works today but is the most
  likely thing to break on a game update.

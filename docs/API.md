# Compatibility layer API

For authors of other CarX Drift Racing Online 2 mods (BepInEx 6, IL2CPP). The layer gives your mod one shared tool,
`GameHooks.Create`, and it watches the BepInEx log so your mod shows up in the on-screen status line without any extra
code.

Only what is described on this page is public API. Everything else in the plugin is internal and may change (the two
Unity components `StatusLineBehaviour` and `OverlayBehaviour` are public only because IL2CPP injection needs it).

- [Using the layer from your mod](#using-the-layer-from-your-mod)
- [GameHooks.Create](#gamehookscreate)
- [Logging conventions](#logging-conventions)
- [Log codes the layer writes](#log-codes-the-layer-writes)
- [Plugin constants](#plugin-constants)

## Using the layer from your mod

1. Reference `CarXModManager.dll` (from the release zip, or `BepInEx/plugins/CarXModManager/`) **for compiling only**
   (do not copy it into your own output). `CarXModManager.xml` sits next to it and gives you IntelliSense.
2. Make BepInEx load the layer before your mod:

```csharp
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

[BepInPlugin("yourname.yourmod", "Your Mod", "0.1.0")]
[BepInDependency("carx.modmanager", BepInDependency.DependencyFlags.HardDependency)]
public sealed class MyMod : BasePlugin
{
    internal static ManualLogSource Logger = null!;

    public override void Load()
    {
        Logger = Log;
        var harmony = new Harmony("yourname.yourmod");
        // ... GameHooks.Create(...) calls, see below
    }
}
```

`"carx.modmanager"` is `CarXModManager.Plugin.PluginGuid`. Use the string if your own plugin class is also called
`Plugin`. With a hard dependency BepInEx refuses to load your mod when the layer is missing, which is what you want if
you call `GameHooks`.

Players install the layer once (its own release); your mod's download should link to it and say it is required. Do not
bundle the layer's DLL in your zip.

## GameHooks.Create

```csharp
namespace CarXModManager.Api;

public static bool GameHooks.Create(
    Harmony harmony, ManualLogSource log,
    string typeName, string assemblyName, string methodName,
    Func<ParameterInfo[], bool>? selectOverload = null,
    MethodInfo? prefix = null, MethodInfo? postfix = null)
```

Creates a hook on **one** game method: your `prefix` runs before it, your `postfix` after it. Returns `true` if the
hook was created and `false` if not; it never throws, and every failure is logged as an **Error under your mod's own
log source** (see the codes below). Your mod then appears as "with errors" in the status line, so a game update that
moves or renames a method is noticed straight away.

| Parameter | Meaning |
|---|---|
| `harmony` | Your mod's own Harmony instance, `new Harmony("yourname.yourmod")`. |
| `log` | Your mod's log source (`Log` inside a `BasePlugin`). Failures are written here so they count against your mod. |
| `typeName` | Full class name with namespace, e.g. `"CarX.Game.SomeNamespace.SomeClass"`. |
| `assemblyName` | Interop assembly holding the class, without `.dll`. The game's own code is `"CarX.DR3.Assembly"`. Searched first; if the class is not there, every loaded assembly is searched. |
| `methodName` | Name of the method. |
| `selectOverload` | Only needed when the name is not unique. Gets each candidate's parameters; return `true` for the one you want. The hook is created only if **exactly one** method matches. `null` means the name alone must be unique. |
| `prefix` | A **static** method that runs before the game method, or `null`. |
| `postfix` | A **static** method that runs after the game method, or `null`. At least one of `prefix` / `postfix` is required. |

### Writing the prefix / postfix

These are ordinary [Harmony](https://harmony.pardeike.net/articles/patching-prefix.html) patch methods. The parts you
will use most:

| In your method's parameter list | What you get |
|---|---|
| `__0`, `__1`, `__2`, ... | The game method's arguments **by position** (this always works, whatever the parameters are called). |
| `ref Type __0` | The same argument, but assigning to it changes what the game receives (prefix only). |
| `__instance` | The object the method is called on (instance methods). |
| `__result` | The return value (postfix). Use `ref` to replace it. |
| a `bool` return value (prefix) | Returning `false` skips the game method. A `void` prefix always lets it run. |

Example: change the first argument of a game method before the game sees it.

```csharp
bool hooked = GameHooks.Create(harmony, Log,
    typeName: "CarX.Game.SomeNamespace.SomeClass",
    assemblyName: "CarX.DR3.Assembly",
    methodName: "Load",
    selectOverload: p => p.Length == 2 && p[0].ParameterType == typeof(string),
    prefix: typeof(MyMod).GetMethod(nameof(LoadPrefix), BindingFlags.NonPublic | BindingFlags.Static));

private static void LoadPrefix(ref string __0)
{
    try { __0 = __0.Trim(); }
    catch (Exception exception) { Logger.LogError($"MYMOD_LOAD_HOOK_FAILED — {exception}"); }
}
```

Example: read the result of a game method.

```csharp
GameHooks.Create(harmony, Log, "CarX.Game.SomeNamespace.SomeClass", "CarX.DR3.Assembly", "GetPrice",
    postfix: typeof(MyMod).GetMethod(nameof(PricePostfix), BindingFlags.NonPublic | BindingFlags.Static));

private static void PricePostfix(int __result) => Logger.LogInfo($"MYMOD_PRICE — {__result}");
```

### Things to know

- **`true` means installed, not called.** The IL2CPP compiler may have inlined a small method into its callers, and
  then the method is never called. If your hook never fires, hook the caller instead (or as well). One approach is to
  hook the same value at more than one level of the call chain and change it in whichever hook runs first.
- **A prefix runs before the game uses its arguments**, on the thread that calls the game method (normally Unity's main
  thread). That is how a mod can replace a value (a texture, a name, a setting) with no frame in which the original is
  visible. Because of this, keep hooks quick, and do not call Unity APIs from another thread.
- **Do not let an exception escape your hook.** It would be thrown inside the game's own call. Wrap the body in
  `try/catch` and log with `LogError`, as in the example.
- **One method per call.** To hook three methods, call `Create` three times. If one of them cannot be found, the others
  are still created, and the failed one is in the log.
- **Several mods may hook the same method.** Harmony runs all prefixes and postfixes; use your own Harmony id.

### What it writes to the log

All of these are written to **your** log source:

| Code | Level | Meaning |
|---|---|---|
| `HOOK_CREATED` | Info | The hook was installed on `type.method`. |
| `HOOK_TARGET_MISSING` | Error | The type was not found: a typo in `typeName`, or (if it used to work) the game was updated. |
| `HOOK_TARGET_UNCLEAR` | Error | Zero or several methods matched the name and `selectOverload`; exactly one is required. The message says how many and what to do. |
| `HOOK_NOTHING_TO_RUN` | Error | Neither `prefix` nor `postfix` was given. |
| `HOOK_FAILED` | Error | Harmony refused the patch (message and stack trace follow). |

## Logging conventions

The status line counts a mod as "with errors" when **its own log source** has logged an `Error` or `Fatal` line. A
BepInEx plugin's log source is its plugin name, so inside a `BasePlugin` just use `Log`:

- `Log.LogError(...)` for something that went wrong and affects the mod's job. The mod shows red in the status line.
- `Log.LogWarning(...)` for expected or harmless problems. Warnings do not count.
- `Log.LogInfo(...)` for normal progress.

A mod that BepInEx could not load at all is shown as "failed to load".

Please follow the layer's message style, so logs are easy to search: `PREFIX_EVENT — sentence`, where the prefix is
your mod's name in capitals and the event is in `UPPER_SNAKE_CASE`, e.g.
`MYMOD_LOADED — dev build 79e59c7.`, `MYMOD_LOAD_HOOK_FAILED — ...`.

## Log codes the layer writes

Written under the source `[CarX Mod Manager]` (the layer's plugin name).

| Code | Level | Meaning |
|---|---|---|
| `COMPATLAYER_LOADED` | Info | The plugin loaded; includes the build id. |
| `STATUSLINE_FIRST_DRAW` | Info | IMGUI is running. Also prints the graphics API, Unity version and game version. |
| `STATUSLINE_DRAW_ERROR` | Warning | Drawing the status line failed (the first three are logged). |
| `STATUSLINE_DISABLED` | Error | Too many drawing errors; the status line is off until the game restarts. Mods are unaffected. |
| `IMGUI_CALL_UNAVAILABLE` | Warning | An IMGUI method was stripped from this game build, so that part of the line is skipped. |

The integrations' own codes (`INTEGRATIONS_*`, `SERVERBROWSER_*`, `TRACKPICKER_*`, `OVERLAY_*`, `INPUT_*`) are listed in
[DETAILS.md](DETAILS.md#checking-it-works).

## Plugin constants

`CarXModManager.Plugin` exposes four constants/values:

| Member | Value |
|---|---|
| `PluginGuid` | `"carx.modmanager"`, for `[BepInDependency]`. |
| `PluginName` | `"CarX Mod Manager"`, the plugin's name and log source name. |
| `PluginVersion` | `"0.1.0"`. Dev builds do not bump it, so it does not tell builds apart. |
| `BuildId` | The short commit hash of the build (`"local"` for a local build). Written to the log at startup. |

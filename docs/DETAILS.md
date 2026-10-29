# Compatibility layer / mod manager (dev builds)

A BepInEx 6 IL2CPP plugin for **CarX Drift Racing Online 2** (Unity 6000.3, BepInEx 6.0.0-be.791). Versions are
not bumped for dev builds; the log and the release notes carry the commit hash (the "build id") instead.

It does three things:

1. a **status line** in the corner of the screen showing how many mods loaded and which have errors;
2. a shared **API** other mods build on (`GameHooks.Create`), documented in [API.md](API.md);
3. **integrations** with the game's own screens: a better server browser and a map picker for room creation (below).

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

## Integrations with the game's screens

Each one is independent: it has its own section and an `Enabled` switch in `BepInEx/config/carx.modmanager.cfg`, and
`[Integrations] Enabled = false` turns all of them off. Their panels are drawn with IMGUI over the game, in a flat square
style, and can be **dragged by their title bar**; the position is remembered (`PanelAnchor`, `PanelOffsetX`,
`PanelOffsetY` in the config, in pixels at 1080p).

### Server browser (`[ServerBrowser]`)

The panel appears while the server browser is open.

- **Search**: type to match room name, map, configuration or region. Every word must match (`alps eu` finds EU rooms on
  Alps maps); case and accents do not matter. Works instantly on every loaded room.
- **Hide full / empty / locked**: switches (remembered between sessions).
- **Sort**: cycles the game's own order, most players, free slots, name, map, region, lowest ping (remembered). The game's
  header sort (ping, players, track) still works; "game order" keeps whatever it produced.
- **Load all**: the game loads the next page of rooms only when you scroll to the bottom, so a search would only see the
  first page. With this on, the mod keeps requesting pages until nothing new arrives (at most `MaxPagesToLoad`).
- **Reset** clears all of the above.

### Map picker (`[TrackPicker]`)

The panel appears on the **create room** screen, next to the map selector (which only steps through maps one at a time).
It lists every map the screen offers, official and mod, with mod maps marked.

- **Search** by map name; **click a map** to select it (the same as stepping to it with the arrows).
- **Fav** marks a favourite; favourites are listed first. Stored in `BepInEx/config/carx.modmanager.favorite-maps.txt`
  (one map id per line; you can edit it).
- Switches: **Favourites** (only favourites), **Mods** (only mod maps), **A-Z**.

### Settings

| Section / key | Default | What it does |
|---|---|---|
| `[Display] ShowStatusLine` | true | The status line. |
| `[Integrations] Enabled` | true | Master switch for the integrations. |
| `[Integrations] BlockGameInput` | true | While the pointer is over a panel or you type in one, hold the game's own input back (so Backspace or clicks do not also reach the game). Turn off only if it causes trouble. |
| `[ServerBrowser] Enabled`, `HideFull`, `HideEmpty`, `HideLocked`, `Sort`, `LoadAllRooms`, `MaxPagesToLoad` | on, off, off, off, Server, true, 30 | See above; the switches and sort are also changed in the panel and remembered. |
| `[TrackPicker] Enabled`, `OnlyFavorites`, `OnlyMods`, `SortByName` | on, off, off, off | See above; also changed in the panel and remembered. |
| `PanelAnchor`, `PanelOffsetX`, `PanelOffsetY` (per integration) | TopRight 24/24 (server browser), TopLeft 24/120 (map picker) | Where the panel sits; dragging the panel changes the offsets. |

### How the integrations work

How the game's UI is built, and where the hooks go, is in [GAME-UI.md](GAME-UI.md). In short:

- **Server browser**: a prefix on `UILobbyRoomBrowserContext.FillRoomList(rooms, placeholders)` replaces `rooms` with the
  matching rooms in the chosen order before the game draws them, and keeps the full list so a changed search redraws without
  asking the server again. More pages are requested by invoking the view's `scrollRectValueChanged` event (what the game's
  controller listens to when you scroll to the bottom). Hooks on `OnActivated` / `OnDeactivated` / `SetBrowserState` track
  when the screen is open.
- **Map picker**: hooks on `UICreateRTMPRoomContext.OnActivated` / `OnDeactivated` (and the two selector-building methods)
  find the screen; the panel reads `m_trackOptions` and calls the selector's own `m_trackSelector.Select(id)`.
- **Input**: while a panel is in use the mod takes the game's own input lock (`InputLocker.LockInput`), the mechanism the
  game uses during screen transitions, and releases it as soon as the panel is no longer in use.

Everything fails soft: an exception in a hook is logged and, after five, that integration stands down and leaves the game's
own screen alone; if the integrations cannot be installed at all, the status line and the API are unaffected.

## Source layout

| Path | What it is |
|---|---|
| `src/Plugin.cs` | The BepInEx plugin: GUID, name, version, build id, the `ShowStatusLine` setting; starts the status line and installs the integrations. |
| `src/Api/GameHooks.cs` | The public API for other mods. |
| `src/StatusLine/` | The status line: log tracker, drawing, the injected component, guarded IMGUI calls. |
| `src/Integrations/` | One folder per integration (`ServerBrowser`, `TrackPicker`: the hooks, the state, the panel), `IIntegration`, `HookSpec` (hooks declared as data) and `IntegrationHost` (installs them). |
| `src/Overlay/` | What the panels run on: the host that draws and positions them, guarded IMGUI (`PanelGui`), the flat theme, `InputGuard`, the injected component. |
| `src/Il2Cpp/` | Reading and building the game's collection objects. |
| `src/Logic/` | Game-independent code (search, filters, sorting, paging, favourites, panel placement), covered by the tests. |
| `tests/` | The logic tests. |
| `tools/` | `HookCheck`, `StubGen` and `regenerate-stubs.sh` (see below). |
| `ci/GameStubs/` | Generated stand-ins for the game's assemblies, used only by CI to compile. |

## Download

Every push to `main` builds the plugin on GitHub Actions and republishes the **0.1-alpha** pre-release in this repo's
Releases (a zip only). Extract `CarXModManager.zip` into `BepInEx/plugins`; it holds the `CarXModManager` folder
with `CarXModManager.dll` and `CarXModManager.xml` (the API's IntelliSense documentation, for mod authors).

CI has no access to the game's interop assemblies, so it compiles against the generated stand-ins in `ci/GameStubs/`
(each folder is an assembly with the game's real name, holding only the types and members the plugin uses); the DLL binds
to the real ones when the game loads it.

## Build yourself

```bash
dotnet build -c Release
```

The game is expected at `D:/SteamLibrary/steamapps/common/CarX Drift Racing Online 2`. Override with
`-p:GameDirectory="..."`. The build copies `CarXModManager.dll` to `BepInEx/plugins/CarXModManager/`
(`-p:DeployToGame=false` to skip).

To build somewhere without the game installed, take BepInEx from NuGet and use the stand-ins:

```bash
while read -r p; do dotnet build "ci/GameStubs/$p" -c Release -o ci/stubs-out; done < ci/GameStubs/build-order.txt
dotnet build -c Release -p:UseBepInExPackages=true -p:GameInteropDirectory="$PWD/ci/stubs-out"
```

## Tests and tools

| What | How |
|---|---|
| Logic tests (search, filters, sorting, favourites, paging, panel placement) | `dotnet run --project tests` (also run by CI before a release) |
| Hooks vs. the game's real assemblies | `dotnet build tools/HookCheck -o /tmp/hc`, copy `CarXModManager.dll` (built against the game) next to it, then `dotnet /tmp/hc/HookCheck.dll --interop <game interop folder> --plugin /tmp/hc/CarXModManager.dll` |
| Regenerate and verify the CI stand-ins | `tools/regenerate-stubs.sh <game interop folder>` |

`HookCheck` verifies every declared hook: the target exists exactly once and each prefix/postfix parameter (`__instance`,
`__0`...) has the right type. `regenerate-stubs.sh` rewrites `ci/GameStubs` from what the plugin references and proves the
CI build references exactly the same game members as a build against the real game. Run it after the code starts using a
game type or member it did not use before; until then CI fails to compile, which is the reminder.

## Checking it works

After launching, `BepInEx/LogOutput.log` should contain, under `[CarX Mod Manager]`:

| Code | Meaning |
|---|---|
| `COMPATLAYER_LOADED` | The plugin loaded (with the build id). |
| `STATUSLINE_FIRST_DRAW` | IMGUI runs on this Unity build; it also prints the graphics API in use. The line should be visible at the bottom-left. |
| `INTEGRATIONS_LOADED` | Which integrations are on (`ServerBrowser: on, TrackPicker: on`). `INTEGRATIONS_DISABLED` = the master switch is off. |
| `HOOK_CREATED` | A hook was installed (one per game method). Any `HOOK_*` error means the game changed. |
| `OVERLAY_FIRST_DRAW` | The panels' IMGUI works; lists IMGUI calls this game build stripped (the panels fall back to plain controls). |
| `SERVERBROWSER_OPENED` / `_LISTED` / `_PAGE_REQUESTED` / `_ALL_LOADED` / `_CLOSED` | The browser opened, how many rooms were listed (and shown), paging progress. |
| `TRACKPICKER_OPENED` / `_OPTIONS` / `_SELECT` | The create-room screen opened, how many maps it offers, a map was picked. |
| `INPUT_HELD` / `INPUT_RELEASED` | The game's input was held back for a panel, and given back. |
| `SERVERBROWSER_ERROR`, `TRACKPICKER_ERROR`, `*_STOOD_DOWN`, `OVERLAY_*` errors | An integration hit errors and switched itself off. |

If IL2CPP stripped an IMGUI method the status line needs you get `IMGUI_CALL_UNAVAILABLE - <name>` instead. All log codes
of the API are in [API.md](API.md#log-codes-the-layer-writes).

## Not verified yet

Written without being able to run the game: that the panels draw where they are useful (positions are guesses until seen on
screen; they can be dragged), that holding the game's input lock while the pointer is over a panel behaves in every menu,
and that invoking `scrollRectValueChanged` makes the game load the next page. The logs above show which of these works.

## Notes

- Il2CppInterop warns that Unity 6.3 is newer than its signature database. It works today but is the most likely
  thing to break on a game update.
- The earlier overlay window (tabs, theme, key picker, mouse capture, conflict scan) was removed; it is in the git
  history if the in-game menu wants any of it back.

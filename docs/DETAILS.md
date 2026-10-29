# Compatibility layer / mod manager (dev builds)

A BepInEx 6 IL2CPP plugin for **CarX Drift Racing Online 2** (Unity 6000.3, BepInEx 6.0.0-be.791). Versions are
not bumped for dev builds; the log and the release notes carry the commit hash (the "build id") instead.

It does three things:

1. a **status line** in the corner of the screen showing how many mods loaded and which have errors;
2. a shared **API** other mods build on (`GameHooks.Create`), documented in [API.md](API.md);
3. **integrations** with the game's own screens: a cleaner main menu and Play menu (Play at the right, the server browser under
   Multiplayer), a better server browser, and a map list for room creation, all made inside the game's own screens (below).

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
`[Integrations] Enabled = false` turns all of them off.

**Nothing is drawn over the game.** The added controls are ordinary Unity UI objects (the same `Image` / `Button` /
TextMeshPro the game's screens are made of) created inside the game's own screen, styled after a button the screen already
has, so they scale, sort, click and hide with the screen. **Nothing is changed unless a checklist passes first**: before
adding anything the mod checks that every piece it depends on exists (the list it takes space from, a button to copy the
look from, the screen's root, room on screen and not cut off by a mask) and that the building blocks work in this game build
(a dry run in a throw-away object). If any check fails, the game's screen is left exactly as the game made it and one
`*_CHECK_FAILED` line in the log lists every failed check and why, and the status line shows a red
`mod manager: ServerBrowser not working (see log)`; a game update that moves things shows up there instead of as a half-broken
screen. The note is only there while something is wrong; otherwise the status line is unchanged.

### Server browser (`[ServerBrowser]`)

A control bar is added to the server browser screen, in a strip taken from the bottom of the room list (the list gets
slightly shorter; `BarPlacement` chooses another place).

- **Search**: click the *Search* button, then type; Enter, Escape or a click elsewhere ends it, Backspace erases, Delete
  clears. Every word must match room name, map, configuration or region (`alps eu` finds EU rooms on Alps maps); case and
  accents do not matter. Works instantly on every loaded room.
- **Hide full / empty / locked**: switches (remembered between sessions).
- **Sort**: cycles the game's own order, most players, free slots, name, map, region, lowest ping (remembered). The game's
  header sort (ping, players, track) still works; "game order" keeps whatever it produced.
- **Load all**: the game loads the next page of rooms only when you scroll to the bottom, so a search would only see the
  first page. With this on, the mod keeps requesting pages until nothing new arrives (at most `MaxPagesToLoad`).
- **Reset** clears all of the above. A line shows how many rooms are shown of how many are loaded.

### Map list (`[TrackPicker]`)

A **Browse maps** button is added next to the map selector on the **create room** screen (which only steps through maps one at a
time). It opens a list of every map the screen offers, official and mod, with mod maps marked, eight per page.

- **Search** by map name (click *Search*, then type); **click a map** to select it (the same as stepping to it with the
  arrows) and the list closes.
- **Fav** marks a favourite; favourites are listed first. Stored in `BepInEx/config/carx.modmanager.favorite-maps.txt`
  (one map id per line; you can edit it).
- Switches: **Favourites** (only favourites), **Mods** (only mod maps), **A-Z**. **Previous / Next** change the page; the
  list opens on the page of the map that is selected.

### Main menu (`[MainMenu]`)

The game puts the big **Play** button at the bottom left, above a row of tiles that starts with the server browser's globe tile
(then Test drive, Workshop, My cars, Buy a car, Mods); the small icons (settings, info, Discord) and Quit are at the bottom right.
The globe tile opens the multiplayer lobby, which is also what the **Play menu's multiplayer tile** opens, so the main menu had the
same destination twice. The mod manager therefore:

- moves **Play to the right edge of the menu**, lined up with the Quit button (`PlaySide = Right`; `Left` keeps the game's place);
  the thin line the designer put under Play moves with it;
- **removes the globe tile** (`ServerBrowserTile = Hidden`, the default): the tile row closes the gap, and the server browser is opened
  from the Play menu's Multiplayer section instead. If the tile was the selected entry (the game selects the first one) the selection goes
  to Play, so keyboard and gamepad keep working. `BesidePlay` keeps the tile and puts it on Play's left (a square as tall as Play,
  `Gap` units away); `InRow` leaves it where the game put it. **The tile is never removed without a replacement**: if the Play menu is
  ever seen without an active multiplayer lobby tile (`PLAYMENU_NO_LOBBY_TILE`), the main menu gets its tile back.

Sizes, looks and behaviour of every other entry are untouched.

- The menu is not one list (Play is placed by hand, the tiles and the small icons are arranged by two of the game's layout groups,
  Quit by hand), so this is written for that structure: entries are found through their `MenuItem` components (`Play`,
  `OnlineMenu`). The menu has two entries of type `Play` (the visible button under the screen's root, and a leftover under the game's
  logo that the logo's mask cuts off); the one closest to the root is used.
- Keyboard / gamepad navigation: an entry whose neighbours were set by hand would still point at its old neighbours, so it is
  switched to automatic navigation (nearest entry in the pressed direction).
- The menu's original layout is written to `BepInEx/carx.modmanager.ui-mainmenu.txt` before anything is changed.

### Play menu (`[PlayMenu]`)

The Play menu (the game modes screen that opens when Play is pressed) has eight mode tiles in the designer's order, with nothing saying
which are single player and which online. The mod manager puts them in two titled sections, in one row with a wider gap between the
sections (`SectionGap`) or, if the tiles are too big for that, one row per section (`Layout`):

| Section | Tiles, in this order |
|---|---|
| **SINGLE PLAYER** | Career, Free ride, XDS training, Duels |
| **MULTIPLAYER** | **Multiplayer lobby (the server browser)**, Multiplayer duels, Top 32 (async), Top 32 (real time) |

The tiles are the game's own objects, so hover, the description text, selection, gamepad, "new" markers and locked modes all work as
before; only their place, order and (if they have to shrink to fit, never below 55%) scale change. The titles are text in the font of the
screen's own header. The arrangement is centred where the tiles were and stays inside the free part of the screen: the header and
description texts are obstacles it never covers. A tile that sits in one of the game's layout groups (which would undo any placement) is
moved out of it to the screen's root. `SectionTitles = false` hides the titles. The screen's original layout is written to
`BepInEx/carx.modmanager.ui-playmenu.txt` before anything is changed.

### Settings

| Section / key | Default | What it does |
|---|---|---|
| `[Display] ShowStatusLine` | true | The status line. |
| `[Integrations] Enabled` | true | Master switch for the integrations. |
| `[Integrations] BlockGameInput` | true | While you type into one of the added search buttons, hold the game's own input back (so Backspace or letters do not also reach the game). Turn off only if it causes trouble. |
| `[ServerBrowser] Enabled`, `ShowControlBar`, `HideFull`, `HideEmpty`, `HideLocked`, `Sort`, `LoadAllRooms`, `MaxPagesToLoad` | on, on, off, off, off, Server, true, 30 | See above; the switches and sort are also changed in the bar and remembered. `ShowControlBar = false` keeps loading all pages but adds no buttons. |
| `[ServerBrowser] BarPlacement` | Auto | `Auto` (below the list, or at the bottom of the screen if the game's layout would undo resizing the list), `BelowList`, `AboveList`, `ScreenBottom`, `ScreenTop`. |
| `[TrackPicker] Enabled`, `OnlyFavorites`, `OnlyMods`, `SortByName` | on, off, off, off | See above; also changed in the list and remembered. |
| `[MainMenu] Enabled`, `PlaySide`, `ServerBrowserTile`, `Gap` | on, Right, Hidden, 12 | Rearrange the main menu (see above). `PlaySide`: `Right` or `Left` (the game's). `ServerBrowserTile`: `Hidden`, `BesidePlay` or `InRow`. `Gap` is the space between that tile and Play for `BesidePlay`, in the screen's units (1080 units high). |
| `[PlayMenu] Enabled`, `Layout`, `SectionTitles`, `Gap`, `SectionGap` | on, Auto, true, 16, 48 | Reorganise the Play menu into sections (see above). `Layout`: `Auto` (one row if the tiles fit it at 90% or more, else one row per section), `Line`, `Stacked`. |
| `[TrackPicker] ButtonPlacement`, `ButtonOffsetX`, `ButtonOffsetY` | Auto, 0, 0 | Where the Browse maps button goes (`Auto` picks the first of `Right`, `Left`, `OnLabel` where it is fully visible); the offsets move it afterwards, in the screen's units. |

### How the integrations work

How the game's UI is built, and where the hooks go, is in [GAME-UI.md](GAME-UI.md). In short:

- **Server browser**: a prefix on `UILobbyRoomBrowserContext.FillRoomList(rooms, placeholders)` replaces `rooms` with the
  matching rooms in the chosen order before the game draws them, and keeps its own copy of the full list so a changed search
  redraws without asking the server again. (The rooms are read by position, `Count` and the indexer, never by enumerating:
  an IL2CPP list's enumerator is a boxed struct that the interop cannot call.) More pages are requested by invoking the view's
  `scrollRectValueChanged` event (what the game's controller listens to when you scroll to the bottom). Hooks on `OnActivated` /
  `OnDeactivated` / `SetBrowserState` track when the screen is open. Half a second after the screen opens (so it has finished
  arranging itself) the control bar is checked and added.
- **Map list**: hooks on `UICreateRTMPRoomContext.OnActivated` / `OnDeactivated` (and the two selector-building methods) find
  the screen; the list reads `m_trackOptions` and calls the selector's own `m_trackSelector.Select(id)`.
- **Typing**: this game build has no usable IMGUI text field (`GUILayout.TextField` is stripped), so a search button is a button
  that shows the text, and while it is active the keys are read directly: typed characters from the Input System keyboard's
  `onTextInput` event and from IMGUI key events, whichever reports first (logged once as `TEXTINPUT_SOURCE`; the other is
  then ignored so nothing is typed twice). While typing, the game's input is held with its own input lock
  (`InputLocker.LockInput`), the mechanism the game uses during screen transitions, and released when typing ends.
- **Main menu and Play menu** share one engine (`ScreenRearranger`); neither has a hook. Twice a second the mod looks for the open
  screen (`UIMainMenuContext`, `UIRaceGameModesContext`), waits half a second for it to settle, runs its checks, rearranges it,
  verifies the result and **checks again every half second** that the game has not moved things back (an animation, a reset): they are
  put back up to three times, after that the change is undone for good (`*_OVERRIDDEN`). A screen that is not ready (too few entries,
  no Play entry) is waited for (`*_WAITING`), which does not count as a failed try; each opening gets three tries with a growing pause,
  and after three openings whose every try failed the screen is left alone for the session. An exception undoes the change and stops
  touching the screen. Checks (main menu): a Play entry exists and is a button, Play's parent is not a layout group, the server browser
  entry (if on screen) is a button, the new places are inside the usable area (the screen's 16:9 part, minus a margin), not cut off by
  a mask and free of every other entry. Checks (Play menu): the tiles exist and can be moved, nothing sits inside the tiles' own area,
  and the sections fit the free area at a readable size. An entry is compared by its centre (and, for the Play menu, size within 15%),
  so its own hover animation does not count as being moved.
- **Layout reference**: when the controls are added, the layout of the screen (every object with its size, position and
  components) is written to `BepInEx/carx.modmanager.ui-serverbrowser.txt` / `ui-createroom.txt`. It shows what is where, so a
  control can be moved to a free spot, and is the first thing to look at if a control ends up somewhere unexpected.

Everything fails soft: an exception in a hook or in per-frame work is logged and, after five, that integration stands down and
leaves the game's own screen alone; a control that cannot be built is removed again; if the integrations cannot be installed at
all, the status line and the API are unaffected.

## Source layout

| Path | What it is |
|---|---|
| `src/Plugin.cs` | The BepInEx plugin: GUID, name, version, build id, the `ShowStatusLine` setting; starts the status line and installs the integrations. |
| `src/Api/GameHooks.cs` | The public API for other mods. |
| `src/StatusLine/` | The status line: log tracker, drawing, the injected component, guarded IMGUI calls. |
| `src/Integrations/` | One folder per integration (`ServerBrowser`: hooks, state, `ServerBrowserBar`; `TrackPicker`: hooks, state, `TrackPickerPopup`; `MainMenu` and `PlayMenu`: rearrange those screens on top of `ScreenRearranger`), `IIntegration`, `HookSpec` (hooks declared as data) and `IntegrationHost` (installs and runs them). |
| `src/Ui/` | Building controls inside the game's screens: `GameUi` (create buttons/texts styled after the game's own, find where there is room, the dry run), `UiDump` (layout reference file), `RectMemory` (put a moved object back exactly), `TextCapture` / `InputSystemKeyboard` (typing into a search button), `InputGuard` (the game's input lock), `IntegrationsBehaviour` (the injected component that runs it all). |
| `src/Il2Cpp/` | Reading and building the game's collection objects. |
| `src/Logic/` | Game-independent code (search, filters, sorting, paging, favourites, text editing, the preflight checklist, placement geometry), covered by the tests. |
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
| Logic tests (search, filters, sorting, favourites, page loading, text editing, preflight, placement geometry, the map list pager, the placement of the main menu's Play and server browser entries, the sections of the Play menu) | `dotnet run --project tests` (also run by CI before a release) |
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
| `SERVERBROWSER_OPENED` / `_LIST_KIND` / `_LISTED` / `_PAGE_REQUESTED` / `_ALL_LOADED` / `_CLOSED` | The browser opened, what kind of list the game passes its rooms in, how many rooms were listed (and shown), paging progress. `SERVERBROWSER_UNSUPPORTED_LIST` = a list of a kind this version cannot read (the game's list is left alone; the line names its type). |
| `SERVERBROWSER_CHECKS` / `SERVERBROWSER_CHECK_FAILED` | The checklist before the control bar was added: all passed (with what was found), or which checks failed and why (the screen is then left as the game made it). `SERVERBROWSER_BUILD_FAILED` = building failed and was undone. |
| `SERVERBROWSER_UI_BUILT` | The control bar was added; names the layout reference file. |
| `TRACKPICKER_OPENED` / `_OPTIONS` / `_SELECT` | The create-room screen opened, how many maps it offers, a map was picked. |
| `TRACKPICKER_CHECKS` / `TRACKPICKER_CHECK_FAILED` / `TRACKPICKER_BUILD_FAILED` / `TRACKPICKER_UI_BUILT` | The same four for the Browse maps button and list. |
| `MAINMENU_WATCHING` / `PLAYMENU_WATCHING`, `*_UI_DUMP`, `*_WAITING` | The rearranging of that screen is on; where the screen's original layout was written; the screen is not ready yet (too few entries or tiles) and the reason. |
| `MAINMENU_CHECKS` / `_CHECK_FAILED` / `_ARRANGED` / `_VERIFY_FAILED`, the same four with `PLAYMENU_` | The checklist passed (with what was found) or failed (which checks and why; the screen is then left as the game made it); the screen was rearranged (with every moved entry's box before and after); the result did not look right and was put back. |
| `PLAYMENU_NO_LOBBY_TILE` | The Play menu was seen without an active multiplayer lobby tile, so the server browser cannot be opened from it and the main menu keeps its own tile. |
| `*_MOVED` / `*_OVERRIDDEN` / `*_ENTRIES_CHANGED` (`MAINMENU_`, `PLAYMENU_`) | The game moved the entries and they were put back; the game keeps moving them so the change was undone for good; the set of entries changed and the screen is arranged again. |
| `TEXTINPUT_SOURCE` | Typing into a search button works, and through which source (the Input System or IMGUI key events). `TEXTINPUT_INPUTSYSTEM_UNAVAILABLE` = the Input System could not be used; IMGUI key events are used alone. |
| `INPUT_HELD` / `INPUT_RELEASED` | The game's input was held back while typing, and given back. |
| `UI_STEP_SKIPPED` | A cosmetic step (for example keeping a control out of the gamepad navigation) is not supported in this game build; the control works without it. |
| `SERVERBROWSER_ERROR`, `TRACKPICKER_ERROR`, `*_STOOD_DOWN`, `UI_CLICK_FAILED`, `TEXTINPUT_ERROR` | A hook, a click or per-frame work failed; after five errors an integration switches itself off. |

If IL2CPP stripped an IMGUI method the status line needs you get `IMGUI_CALL_UNAVAILABLE - <name>` instead. All log codes
of the API are in [API.md](API.md#log-codes-the-layer-writes).

## Not verified yet

Written without being able to run the game: that the control bar and the Browse maps button sit somewhere sensible (the
placement is worked out from the screen's layout and checked, but only the first run shows the result; the layout reference
files and the `BarPlacement` / `ButtonPlacement` settings are how it gets corrected), that typing reaches the search buttons on
this game build (`TEXTINPUT_SOURCE` says it did), that holding the game's input lock while typing behaves in every menu, and that
invoking `scrollRectValueChanged` makes the game load the next page. The logs above show which of these works.

## Notes

- Il2CppInterop warns that Unity 6.3 is newer than its signature database. It works today but is the most likely
  thing to break on a game update.
- The earlier overlay window (tabs, theme, key picker, mouse capture, conflict scan) was removed; it is in the git
  history if the in-game menu wants any of it back. So are the first versions of the integrations, whose search box and
  switches were IMGUI panels drawn over the game; they were replaced by controls inside the game's own screens.

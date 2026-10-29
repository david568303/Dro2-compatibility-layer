# How the game's UI is built (and where this mod hooks in)

Notes from reading the game's interop assemblies (`BepInEx/interop`) for the mod manager's integrations with the game's
screens. They describe structure, not looks: the layout and styling live in Unity prefabs, which are not in the interop.

## The pattern

- A screen is a **context** (the view, a `MonoBehaviour`, e.g. `UILobbyRoomBrowserContext`) plus a **controller**
  (`UI...ContextController<T>`, a generic class) that loads the data and talks to the server. The view exposes methods the
  controller calls (`FillRoomList`, `SetTracks`, ...) and events it listens to (`scrollRectValueChanged`, `roomSelected`, ...).
- Views are filled through a data-binding layer (`Binding.dll`): the view holds `ListProperty` / `StringProperty` /
  `BoolProperty` fields wired to prefab components, and list rows are prefab cells (`NavigateListElement<TData>`) bound to a
  `ListElementData` subclass.
- Contexts are activated and deactivated by a state machine (`OnActivated` / `OnDeactivated`); `blockedByAnotherContext`
  says a popup covers a view.
- The UI works with gamepad and keyboard/mouse; `InputLocker` (a scene component) locks the game's input actions during
  transitions (`LockInput(name)` returns a handle to dispose).
- The controllers are generic over their view type and the closed type does not appear in the interop, so the integrations hook the
  **non-generic views** (and read data classes), never the controllers.

## Server browser (lobby rooms)

| Part | Class | Notes |
|---|---|---|
| List + headers + side panel | `UILobbyRoomBrowserContext` | `FillRoomList(IEnumerable<ListElementData> rooms, IEnumerable<ListElementData> placeholders)`, `SetBrowserState(LobbyRoomBrowserState)`, `scrollRectValueChanged`, `blockedByAnotherContext`; headers: track, room name, configuration, players; UI references used when adding controls: `m_scrollRect` (the list), `m_findRoomByCodeButton`, `m_createRoomButton` (plain `Button`s with TextMeshPro text, the look to copy) |
| A row | `UILobbyRoomPreviewElement` bound to `LobbyRoomPreviewData` | `roomName`, `mapName`, `mapDisplayName` (set later for mod maps), `roomConfig`, `serverCluster`, `currentPlayers`, `maxPlayers`, `ping`, `locked`, `roomInfo` (tags, race type, secret code) |
| Details panel | `UILobbyRoomPreview`, `UIRTMPRoomPlayerInfoListElement` | weather, time, tyres, tags, minimap; players with ping and rating (loaded when a room is selected: `IRoomAdditionalInfo`) |
| Filters screen | `UILobbyRoomFiltersContext`, `LobbyRoomsFilterModel` | server-side: per-track toggles and a room-name filter, on a separate screen with pages |
| Data | `LobbyRoomCommandsModel.GetRoomsAsync(filters, sorting, offset, limit)` | paged; server-side sort only by ping, players, track (`SortHeader`) |

## Main menu

From the layout the mod manager wrote (`carx.modmanager.ui-mainmenu.txt`), in the units of a 1920 x 1080 root:

- `UIMainMenu(Clone)` (`UIMainMenuContext`, a `BaseContext` that has no `OnActivated` of its own, so the mod manager looks the screen
  up instead of hooking it) holds, directly: `Start` (the big **Play** button, 401 x 100 at the bottom left, placed by hand), a thin
  `Separator` image under it, `MenuItems`, `QuitButton` (60 x 60 at the bottom right), `SmallMenuItems`, and a `CarXLogo` object (with a
  mask) that also contains a hidden second Play entry.
- `MenuItems` is a **horizontal layout group**, content-sized, holding `OnlineToggle` (the globe tile, 76 x 114, the server
  browser), `TestDriveButton` (320 x 114), `WorkshopButton`, `GarageButton`, `StoreButton`, `ModsButton` (240 x 114 each) and two
  thin `Separator`s. `SmallMenuItems` is a second horizontal layout group with `SettingsButton`, `InfoButton`, `DiscordButton`.
- Every entry is a `Button` with `MenuItem` (whose `Type` is a `MenuItemType`: Play, TestDrive, Workshop, Store, Garage, Settings,
  Info, Credits, Quit, OnlineMenu, Discord, Mods), `Animator`, `UIAnimateBySelect`, sounds, and children `Default` / `Selected` /
  `Pressed` (stacked images switched by bindings). The context also holds one `Button` field per entry except the server browser
  and Discord. The controller (`UIMainMenuContextController<T>`) reacts to `menuButtonPressedEvent(MenuItem)`.
- Pressing Play opens the game modes screen (`UIRaceGameModesContext`, one `UIRaceGameModeButton` per mode: career, free ride, XDS
  training, duels, the multiplayer lobby = server browser, multiplayer duels, top 32 variants). *Not touched by the mod manager.*

## Map selection

- **Global map** (`UIGlobalMapMarkersContext`, `UIGlobalMapNavigationController<T>`): regions are tabs (`m_regionsTabs`); each
  region shows a track list (`m_tracks`, `TrackListData`: `trackId`, `trackName`, `locked`, `isMod`, `isFromModIo`,
  download state) via `SetTracks(IEnumerable<TrackListData>)`; mods are a special region, loaded from mod.io
  (`LoadModIconsAsync`, subscribed-mods watcher). Selecting a track focuses its 3D marker (`GlobalMapModel`).
  *Not used by the integrations yet.*
- **Create room** (`UICreateRTMPRoomContext`): about 20 one-value left/right selectors (`MultiValueSelector<T>`, each with
  `m_buttonPrevious`, `m_buttonNext` and `m_currentOptionText`); the map is `m_trackSelector` (`StringMultiValueSelector`) with
  its choices in `m_trackOptions` (`ValueOption<string>`: `id`, `GetText()`), mod maps resolved through `m_modDisplayNames`.
  `Select(id)` does what the arrows do. The screen has two tabs, `m_mainSettingsTabButton` and `m_regulationsTabButton` (text
  buttons, the look to copy).
- **Free ride area selection** (`UIFreeRideAreaSelectionContext`, `FreeRideListElementData`): the list of areas on the map.

## Text input in the game

The game has real TextMeshPro input fields (chat, room code, password, hex colour) and masks its own input actions while one is
focused (`TMPInputFieldExtension`, `InputFieldConfigureInputEnvironment`). The mod manager does not create input fields; a search
button reads the keys itself (see "How the integrations work" in DETAILS.md).

## Not known without screenshots or the prefabs

Where things sit on screen and how they look (colours, fonts, sizes), which screen areas are free for new controls, and how the
gamepad navigation order runs. The mod manager therefore works out the placement at run time from the screen's own layout, checks
it, and writes the layout to `BepInEx/carx.modmanager.ui-*.txt`; the placement settings correct a wrong guess.

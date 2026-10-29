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
| List + headers + side panel | `UILobbyRoomBrowserContext` | `FillRoomList(IEnumerable<ListElementData> rooms, IEnumerable<ListElementData> placeholders)`, `SetBrowserState(LobbyRoomBrowserState)`, `scrollRectValueChanged`, `blockedByAnotherContext`; headers: track, room name, configuration, players |
| A row | `UILobbyRoomPreviewElement` bound to `LobbyRoomPreviewData` | `roomName`, `mapName`, `mapDisplayName` (set later for mod maps), `roomConfig`, `serverCluster`, `currentPlayers`, `maxPlayers`, `ping`, `locked`, `roomInfo` (tags, race type, secret code) |
| Details panel | `UILobbyRoomPreview`, `UIRTMPRoomPlayerInfoListElement` | weather, time, tyres, tags, minimap; players with ping and rating (loaded when a room is selected: `IRoomAdditionalInfo`) |
| Filters screen | `UILobbyRoomFiltersContext`, `LobbyRoomsFilterModel` | server-side: per-track toggles and a room-name filter, on a separate screen with pages |
| Data | `LobbyRoomCommandsModel.GetRoomsAsync(filters, sorting, offset, limit)` | paged; server-side sort only by ping, players, track (`SortHeader`) |

## Map selection

- **Global map** (`UIGlobalMapMarkersContext`, `UIGlobalMapNavigationController<T>`): regions are tabs (`m_regionsTabs`); each
  region shows a track list (`m_tracks`, `TrackListData`: `trackId`, `trackName`, `locked`, `isMod`, `isFromModIo`,
  download state) via `SetTracks(IEnumerable<TrackListData>)`; mods are a special region, loaded from mod.io
  (`LoadModIconsAsync`, subscribed-mods watcher). Selecting a track focuses its 3D marker (`GlobalMapModel`).
  *Not used by the integrations yet.*
- **Create room** (`UICreateRTMPRoomContext`): about 20 one-value left/right selectors (`MultiValueSelector<T>`); the map is
  `m_trackSelector` (`StringMultiValueSelector`) with its choices in `m_trackOptions` (`ValueOption<string>`: `id`, `GetText()`),
  mod maps resolved through `m_modDisplayNames`. `Select(id)` does what the arrows do.
- **Free ride area selection** (`UIFreeRideAreaSelectionContext`, `FreeRideListElementData`): the list of areas on the map.

## Not known without screenshots or the prefabs

Where things sit on screen and how they look (colours, fonts, sizes), which screen areas are free for a panel, and how the
gamepad navigation order runs. The panels' default positions are therefore guesses, adjustable in the config.

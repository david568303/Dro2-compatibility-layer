using Binding;
using BepInEx.Configuration;
using BepInEx.Logging;
using CarX.Game.DR3Console.DR3.Scripts.States.Controllers.Lobby;
using CarX.Game.DR3Console.Tournament.UI.Contexts;
using HarmonyLib;
using CarXModManager.Il2Cpp;
using CarXModManager.Logic;
using CarXModManager.Overlay;
using UnityEngine;
using Il2CppRooms = Il2CppSystem.Collections.Generic.IEnumerable<Binding.ListElementData>;

namespace CarXModManager.Integrations.ServerBrowser;

/// <summary>
/// Upgrades the server browser (the lobby's list of rooms):
/// <list type="bullet">
/// <item>a search box and "hide full / empty / locked" switches that work instantly on every loaded room, instead of
/// the game's server-side filters on a separate screen;</item>
/// <item>extra sort orders (most players, free slots, name, map, region, lowest ping) on top of the game's ping / players /
/// track headers;</item>
/// <item>automatic loading of every page of rooms (the game only loads the next page when you scroll to the bottom, so a
/// search or filter would only ever see the first page).</item>
/// </list>
/// How it works: the game builds the list through <c>UILobbyRoomBrowserContext.FillRoomList(rooms, placeholders)</c>. A
/// prefix on that method replaces <c>rooms</c> with the filtered and sorted subset before the game draws it, and keeps the
/// full list so a change of search text can redraw without asking the server again. Further pages are requested by
/// invoking the view's <c>scrollRectValueChanged</c> event, which is what the game's own controller listens to when the
/// player scrolls to the bottom. The panel (<see cref="ServerBrowserPanel"/>) holds the controls.
/// </summary>
internal sealed class ServerBrowserIntegration : IIntegration
{
    private const string GameAssembly = "CarX.DR3.Assembly";
    private const string ViewTypeName = "CarX.Game.DR3Console.Tournament.UI.Contexts.UILobbyRoomBrowserContext";

    /// <summary>Seconds to wait after the last change of the search text before redrawing the list (so typing stays smooth).</summary>
    private const float RefilterDelaySeconds = 0.15f;

    /// <summary>Hook errors after which the integration stops touching the game's list.</summary>
    private const int ErrorsBeforeStandingDown = 5;

    /// <summary>The one instance (static hook methods reach the integration through it).</summary>
    internal static ServerBrowserIntegration? Instance { get; private set; }

    private ConfigEntry<bool> _enabled = null!, _hideFull = null!, _hideEmpty = null!, _hideLocked = null!, _loadAll = null!;
    private ConfigEntry<RoomSort> _sort = null!;
    private ConfigEntry<int> _maxPages = null!;
    private ConfigEntry<PanelAnchor> _anchor = null!;
    private ConfigEntry<float> _offsetX = null!, _offsetY = null!;

    private UILobbyRoomBrowserContext? _view;
    private LobbyRoomBrowserState _browserState = LobbyRoomBrowserState.Loading;
    private bool _browserStateKnown;
    private int _listedLogs;
    private Il2CppRooms? _fullList;
    private Il2CppRooms? _placeholders;
    private bool _refilling;
    private float _refilterAt = -1f;
    private int _errors;

    private readonly PageLoader _pages = new();

    /// <inheritdoc />
    public string Name => "ServerBrowser";

    /// <inheritdoc />
    public bool IsEnabled => _enabled.Value;

    /// <summary>The player's current search, switches and sort order.</summary>
    public RoomQuery Query { get; } = new();

    /// <summary>Rooms the game has loaded so far.</summary>
    public int TotalRooms { get; private set; }

    /// <summary>Rooms currently shown after the search and switches.</summary>
    public int ShownRooms { get; private set; }

    /// <summary>True while more pages are being requested.</summary>
    public bool IsLoadingMore => _loadAll.Value && _view != null && !_pages.IsComplete && _browserState != LobbyRoomBrowserState.NoRooms;

    /// <summary>True when the player asked for every page to be loaded automatically.</summary>
    public bool LoadsAllRooms => _loadAll.Value;

    /// <summary>True while the server browser screen is open and nothing is drawn over it.</summary>
    public bool IsPanelVisible
    {
        get
        {
            UILobbyRoomBrowserContext? view = _view;
            if (view == null) return false; // never opened, or destroyed by a scene change without a deactivation
            try { return !view.blockedByAnotherContext; }
            catch (Exception) { return true; }
        }
    }

    /// <summary>Where the panel sits (from the config); setting it remembers the new position (the player dragged the panel).</summary>
    public PanelPlacement Placement
    {
        get => new(_anchor.Value, _offsetX.Value, _offsetY.Value);
        set { _offsetX.Value = value.OffsetX; _offsetY.Value = value.OffsetY; }
    }

    /// <inheritdoc />
    public void BindConfig(ConfigFile config)
    {
        _enabled = config.Bind(Name, "Enabled", true,
            "Search, filters, extra sort orders and automatic loading of all rooms in the server browser. false = the game's normal server browser.");
        _hideFull = config.Bind(Name, "HideFull", false, "Hide rooms that have no free slot. Also switchable in the panel; remembered.");
        _hideEmpty = config.Bind(Name, "HideEmpty", false, "Hide rooms with nobody in them. Also switchable in the panel; remembered.");
        _hideLocked = config.Bind(Name, "HideLocked", false, "Hide rooms that need a password. Also switchable in the panel; remembered.");
        _sort = config.Bind(Name, "Sort", RoomSort.Server,
            "Extra sort order. Server = the order of the game's own sort header (ping, players or track). Also selectable in the panel; remembered.");
        _loadAll = config.Bind(Name, "LoadAllRooms", true,
            "Keep loading pages of rooms until every room is loaded, so the search and switches see all of them. false = the game's normal scroll-to-load.");
        _maxPages = config.Bind(Name, "MaxPagesToLoad", 30,
            new ConfigDescription("Most extra pages loaded automatically (a safety limit).", new AcceptableValueRange<int>(1, 200)));
        _anchor = config.Bind(Name, "PanelAnchor", PanelAnchor.TopRight, "Which corner or edge of the screen the panel is positioned from.");
        _offsetX = config.Bind(Name, "PanelOffsetX", 24f, "Panel distance from the anchor, sideways, in pixels at 1080p.");
        _offsetY = config.Bind(Name, "PanelOffsetY", 24f, "Panel distance from the anchor, up or down, in pixels at 1080p.");

        Query.HideFull = _hideFull.Value;
        Query.HideEmpty = _hideEmpty.Value;
        Query.HideLocked = _hideLocked.Value;
        Query.Sort = _sort.Value;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Harmony passes a hooked call's arguments by position: <c>__0</c> is the first, <c>__instance</c> the object the method runs on.
    /// <c>SetBrowserState</c> is optional: without it the automatic page loading relies on timeouts only.
    /// </remarks>
    public IReadOnlyList<HookSpec> Hooks { get; } = new[]
    {
        new HookSpec(ViewTypeName, GameAssembly, "FillRoomList", p => p.Length == 2, PrefixName: nameof(FillRoomListPrefix)),
        new HookSpec(ViewTypeName, GameAssembly, "OnActivated", p => p.Length == 0, PostfixName: nameof(OnActivatedPostfix)),
        new HookSpec(ViewTypeName, GameAssembly, "OnDeactivated", p => p.Length == 0, PostfixName: nameof(OnDeactivatedPostfix)),
        new HookSpec(ViewTypeName, GameAssembly, "SetBrowserState", p => p.Length == 1, PostfixName: nameof(SetBrowserStatePostfix), Required: false),
    };

    /// <inheritdoc />
    public bool Install(Harmony harmony, ManualLogSource log)
    {
        Instance = this;
        bool created = HookInstaller.Install(harmony, log, typeof(ServerBrowserIntegration), Hooks);
        OverlayHost.Register(new ServerBrowserPanel(this));
        return created;
    }

    // ---- hooks --------------------------------------------------------------------------------------------------------

    private static void FillRoomListPrefix(UILobbyRoomBrowserContext __instance, ref Il2CppRooms __0, Il2CppRooms __1)
    {
        ServerBrowserIntegration? self = Instance;
        if (self == null || self._errors >= ErrorsBeforeStandingDown) return;
        try { self.OnFillRoomList(__instance, ref __0, __1); }
        catch (Exception exception) { self.ReportError("FillRoomList", exception); }
    }

    private static void OnActivatedPostfix(UILobbyRoomBrowserContext __instance)
    {
        ServerBrowserIntegration? self = Instance;
        if (self == null) return;
        try { self.OnOpened(__instance); }
        catch (Exception exception) { self.ReportError("OnActivated", exception); }
    }

    private static void OnDeactivatedPostfix()
    {
        ServerBrowserIntegration? self = Instance;
        if (self == null) return;
        try { self.OnClosed(); }
        catch (Exception exception) { self.ReportError("OnDeactivated", exception); }
    }

    private static void SetBrowserStatePostfix(LobbyRoomBrowserState __0)
    {
        ServerBrowserIntegration? self = Instance;
        if (self == null) return;
        self._browserState = __0;
        self._browserStateKnown = true;
    }

    // ---- the list ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// The game is about to draw <paramref name="source"/>: note what it holds, and when the player has a search, a switch
    /// or a sort set, replace it with the matching rooms in the chosen order.
    /// </summary>
    private void OnFillRoomList(UILobbyRoomBrowserContext view, ref Il2CppRooms source, Il2CppRooms placeholders)
    {
        _view = view;
        List<ListElementData> items = Il2CppCollections.ToManagedList(source);
        var rows = new List<RoomRow>(items.Count);
        foreach (ListElementData item in items)
        {
            LobbyRoomPreviewData? room = item?.TryCast<LobbyRoomPreviewData>();
            if (room == null) return; // not a list of rooms: leave the game's list alone
            rows.Add(ReadRow(room));
        }

        if (!_refilling) NoteListedByGame(rows.Count, source, placeholders);

        int[] order = Query.Apply(rows);
        if (_listedLogs < 25 && (rows.Count != TotalRooms || order.Length != ShownRooms || _listedLogs == 0))
        {
            _listedLogs++;
            Plugin.LogSource.LogInfo($"SERVERBROWSER_LISTED — the game listed {rows.Count} room(s); showing {order.Length}{(Query.ChangesList ? " after search/filters/sort" : " (nothing filtered)")}.");
        }
        TotalRooms = rows.Count;
        ShownRooms = order.Length;
        if (!Query.ChangesList) return;

        var shown = new List<ListElementData>(order.Length);
        foreach (int index in order) shown.Add(items[index]);
        source = Il2CppCollections.ToIl2CppEnumerable(shown);
    }

    /// <summary>The facts about one room that the search, switches and sort use.</summary>
    private static RoomRow ReadRow(LobbyRoomPreviewData room)
    {
        string map = room.mapDisplayName;
        if (string.IsNullOrEmpty(map)) map = room.mapName;
        return new RoomRow
        {
            Name = room.roomName ?? string.Empty,
            Map = map ?? string.Empty,
            Config = room.roomConfig ?? string.Empty,
            Region = room.serverCluster ?? string.Empty,
            Players = room.currentPlayers,
            MaxPlayers = room.maxPlayers,
            Locked = room.locked,
            Ping = room.ping,
        };
    }

    /// <summary>Remembers the game's full list (to redraw it later) and lets the page loader see that rooms arrived.</summary>
    private void NoteListedByGame(int total, Il2CppRooms source, Il2CppRooms placeholders)
    {
        _pages.OnListed(total, Time.unscaledTime);
        _fullList = source;
        _placeholders = placeholders;
    }

    private void OnOpened(UILobbyRoomBrowserContext view)
    {
        _view = view;
        Query.Search = string.Empty;
        TotalRooms = 0;
        ShownRooms = 0;
        _pages.Reset();
        _browserState = LobbyRoomBrowserState.Loading;
        _browserStateKnown = false;
        _listedLogs = 0;
        Plugin.LogSource.LogInfo($"SERVERBROWSER_OPENED — the server browser opened; search and filters are available (load all pages: {_loadAll.Value}).");
    }

    private void OnClosed()
    {
        Plugin.LogSource.LogInfo($"SERVERBROWSER_CLOSED — {TotalRooms} rooms were loaded, {_pages.PagesRequested} extra page(s) requested.");
        _view = null;
        _fullList = null;
        _placeholders = null;
        _refilterAt = -1f;
    }

    // ---- the panel's actions --------------------------------------------------------------------------------------

    /// <summary>Sets the search text; the list is redrawn shortly after the player stops typing.</summary>
    public void SetSearch(string text)
    {
        Query.Search = text;
        QueueRefilter();
    }

    /// <summary>Switches "hide full rooms" on or off (remembered).</summary>
    public void SetHideFull(bool value) { Query.HideFull = value; _hideFull.Value = value; QueueRefilter(); }

    /// <summary>Switches "hide empty rooms" on or off (remembered).</summary>
    public void SetHideEmpty(bool value) { Query.HideEmpty = value; _hideEmpty.Value = value; QueueRefilter(); }

    /// <summary>Switches "hide locked rooms" on or off (remembered).</summary>
    public void SetHideLocked(bool value) { Query.HideLocked = value; _hideLocked.Value = value; QueueRefilter(); }

    /// <summary>Chooses the sort order (remembered).</summary>
    public void SetSort(RoomSort sort) { Query.Sort = sort; _sort.Value = sort; QueueRefilter(); }

    /// <summary>Switches automatic loading of every page on or off (remembered).</summary>
    public void SetLoadAll(bool value)
    {
        _loadAll.Value = value;
        if (value) _pages.Restart();
    }

    /// <summary>Clears the search, the switches and the sort order.</summary>
    public void ResetQuery()
    {
        Query.Search = string.Empty;
        SetHideFull(false);
        SetHideEmpty(false);
        SetHideLocked(false);
        SetSort(RoomSort.Server);
    }

    private void QueueRefilter() => _refilterAt = Time.unscaledTime + RefilterDelaySeconds;

    // ---- per frame ---------------------------------------------------------------------------------------------------

    /// <summary>Per-frame work: redraw the list after the search changed, and request the next page of rooms if needed.</summary>
    public void Update()
    {
        if (_view == null) return;
        float now = Time.unscaledTime;
        try
        {
            if (_refilterAt >= 0f && now >= _refilterAt)
            {
                _refilterAt = -1f;
                Refill();
            }
            if (_loadAll.Value) TickPageLoading(now);
        }
        catch (Exception exception)
        {
            ReportError("Update", exception);
        }
    }

    /// <summary>Draws the game's full list again through the hook, so the current search and switches are applied.</summary>
    private void Refill()
    {
        UILobbyRoomBrowserContext? view = _view;
        Il2CppRooms? full = _fullList, placeholders = _placeholders;
        if (view == null || full == null || placeholders == null) return;
        _refilling = true;
        try { view.FillRoomList(full, placeholders); }
        finally { _refilling = false; }
    }

    /// <summary>Asks for the next page of rooms when the page loader says it is time (see <see cref="PageLoader"/>).</summary>
    private void TickPageLoading(float now)
    {
        if (_view == null) return;
        // loading, refreshing or empty: not now. If the state hook never reported, go by whether any rooms are listed.
        bool canRequest = _browserStateKnown ? _browserState == LobbyRoomBrowserState.HasRooms : TotalRooms > 0;
        switch (_pages.Tick(now, canRequest, _maxPages.Value))
        {
            case PageAction.Request:
                // "scrolled to the bottom": the same event the game's controller listens to for loading the next page
                _view.scrollRectValueChanged?.Invoke(new Vector2(0f, 0f));
                if (_pages.PagesRequested <= 3)
                    Plugin.LogSource.LogInfo($"SERVERBROWSER_PAGE_REQUESTED — asked the game for more rooms (request {_pages.PagesRequested}, {TotalRooms} rooms so far).");
                break;
            case PageAction.FinishedNoMoreRooms:
                Plugin.LogSource.LogInfo($"SERVERBROWSER_ALL_LOADED — {TotalRooms} rooms; no more arrived after {_pages.PagesRequested} extra page request(s).");
                break;
            case PageAction.FinishedPageLimit:
                Plugin.LogSource.LogInfo($"SERVERBROWSER_PAGE_LIMIT — stopped at {TotalRooms} rooms after {_pages.PagesRequested} extra page(s) (MaxPagesToLoad).");
                break;
        }
    }

    // ---- errors -------------------------------------------------------------------------------------------------------

    private void ReportError(string where, Exception exception)
    {
        _errors++;
        if (_errors <= 3) Plugin.LogSource.LogError($"SERVERBROWSER_ERROR — {where}: {exception}");
        if (_errors == ErrorsBeforeStandingDown)
            Plugin.LogSource.LogError("SERVERBROWSER_STOOD_DOWN — too many errors; the server browser upgrades are off for this session and the game's own list is left alone.");
    }
}

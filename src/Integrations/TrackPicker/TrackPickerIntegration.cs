using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using CarX.Game.DR3Console.DR3.Scripts.States.Contexts.UI.WorldMap.FreeRide;
using HarmonyLib;
using CarXModManager.Logic;
using CarXModManager.Overlay;
using UnityEngine;

namespace CarXModManager.Integrations.TrackPicker;

/// <summary>
/// A searchable map picker for the "create room" screen. The game offers the map as a one-value left/right selector, so
/// reaching one map among many (official maps plus every mod map you have) means stepping through them one at a time.
/// This integration lists all of them in a panel next to the screen: type to search, click a map to select it, mark favourites
/// (listed first), or show only mod maps.
/// <para>
/// How it works: the screen's <c>UICreateRTMPRoomContext</c> keeps the map choices in its track selector
/// (<c>m_trackSelector</c>, a <c>StringMultiValueSelector</c>) and the choices in <c>m_trackOptions</c>. The panel reads those
/// and calls the selector's own <c>Select(id)</c>, which is exactly what the arrows do, so the rest of the screen (areas,
/// configurations, preview) updates as usual.
/// </para>
/// </summary>
internal sealed class TrackPickerIntegration : IIntegration
{
    private const string GameAssembly = "CarX.DR3.Assembly";
    private const string ViewTypeName = "CarX.Game.DR3Console.DR3.Scripts.States.Contexts.UI.WorldMap.FreeRide.UICreateRTMPRoomContext";

    /// <summary>Seconds between refreshes of the map list while the panel is open (mod map names arrive a little late).</summary>
    private const float RefreshSeconds = 0.5f;

    /// <summary>Errors after which the integration stops reading the game's screen.</summary>
    private const int ErrorsBeforeStandingDown = 5;

    /// <summary>The one instance (static hook methods reach the integration through it).</summary>
    internal static TrackPickerIntegration? Instance { get; private set; }

    private ConfigEntry<bool> _enabled = null!, _onlyFavorites = null!, _onlyMods = null!, _sortByName = null!;
    private ConfigEntry<PanelAnchor> _anchor = null!;
    private ConfigEntry<float> _offsetX = null!, _offsetY = null!;

    private FavoriteStore? _favorites;
    private UICreateRTMPRoomContext? _view;
    private readonly List<(string Id, string Label, bool IsMod)> _options = new();
    private readonly List<TrackEntry> _entries = new();
    private int[] _visible = Array.Empty<int>();
    private float _nextRefresh;
    private bool _dirty = true;
    private bool _optionsLogged;
    private int _errors;

    /// <inheritdoc />
    public string Name => "TrackPicker";

    /// <inheritdoc />
    public bool IsEnabled => _enabled.Value;

    /// <summary>The player's current search and switches.</summary>
    public TrackQuery Query { get; } = new();

    /// <summary>The id of the map the screen has selected right now, or empty.</summary>
    public string SelectedId { get; private set; } = string.Empty;

    /// <summary>Every map the screen offers, in the game's order, with favourite marks.</summary>
    public IReadOnlyList<TrackEntry> Entries => _entries;

    /// <summary>Indices into <see cref="Entries"/> of the maps to show, in display order.</summary>
    public IReadOnlyList<int> Visible => _visible;

    /// <summary>True while the create-room screen is open (and nothing is drawn over it).</summary>
    public bool IsPanelVisible
    {
        get
        {
            UICreateRTMPRoomContext? view = _view;
            if (view == null || _errors >= ErrorsBeforeStandingDown) return false;
            try { return view.isMainSettingsActive; }
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
            "A searchable map list next to the create-room screen's map selector. false = the game's normal screen.");
        _onlyFavorites = config.Bind(Name, "OnlyFavorites", false, "Show only favourite maps in the list. Also switchable in the panel; remembered.");
        _onlyMods = config.Bind(Name, "OnlyMods", false, "Show only mod (custom) maps in the list. Also switchable in the panel; remembered.");
        _sortByName = config.Bind(Name, "SortByName", false, "List maps A to Z instead of in the game's order (favourites always first). Also switchable in the panel; remembered.");
        _anchor = config.Bind(Name, "PanelAnchor", PanelAnchor.TopLeft, "Which corner or edge of the screen the panel is positioned from.");
        _offsetX = config.Bind(Name, "PanelOffsetX", 24f, "Panel distance from the anchor, sideways, in pixels at 1080p.");
        _offsetY = config.Bind(Name, "PanelOffsetY", 120f, "Panel distance from the anchor, up or down, in pixels at 1080p.");

        Query.OnlyFavorites = _onlyFavorites.Value;
        Query.OnlyMods = _onlyMods.Value;
        Query.SortByName = _sortByName.Value;
    }

    /// <inheritdoc />
    /// <remarks>The three optional hooks tell the integration when the screen rebuilt its map choices, so the list updates at once.</remarks>
    public IReadOnlyList<HookSpec> Hooks { get; } = new[]
    {
        new HookSpec(ViewTypeName, GameAssembly, "OnActivated", p => p.Length == 0, PostfixName: nameof(OnActivatedPostfix)),
        new HookSpec(ViewTypeName, GameAssembly, "OnDeactivated", p => p.Length == 0, PostfixName: nameof(OnDeactivatedPostfix)),
        new HookSpec(ViewTypeName, GameAssembly, "InitTrackSelector", p => p.Length == 0, PostfixName: nameof(OptionsChangedPostfix), Required: false),
        new HookSpec(ViewTypeName, GameAssembly, "InitModTrackSelectors", p => p.Length == 0, PostfixName: nameof(OptionsChangedPostfix), Required: false),
    };

    /// <inheritdoc />
    public bool Install(Harmony harmony, ManualLogSource log)
    {
        Instance = this;
        _favorites = new FavoriteStore(Path.Combine(Paths.ConfigPath, "carx.modmanager.favorite-maps.txt"));
        bool created = HookInstaller.Install(harmony, log, typeof(TrackPickerIntegration), Hooks);
        OverlayHost.Register(new TrackPickerPanel(this));
        return created;
    }

    // ---- hooks --------------------------------------------------------------------------------------------------------

    private static void OnActivatedPostfix(UICreateRTMPRoomContext __instance)
    {
        TrackPickerIntegration? self = Instance;
        if (self == null) return;
        self._view = __instance;
        self._dirty = true;
        self._nextRefresh = 0f;
        self._optionsLogged = false;
        self.Query.Search = string.Empty;
        Plugin.LogSource.LogInfo("TRACKPICKER_OPENED — the create-room screen opened; the map list is available.");
    }

    private static void OnDeactivatedPostfix()
    {
        TrackPickerIntegration? self = Instance;
        if (self == null) return;
        self._view = null;
        self._options.Clear();
        self._entries.Clear();
        self._visible = Array.Empty<int>();
    }

    private static void OptionsChangedPostfix()
    {
        TrackPickerIntegration? self = Instance;
        if (self != null) self._dirty = true;
    }

    // ---- the list ---------------------------------------------------------------------------------------------------

    /// <summary>Per-frame work while the screen is open: re-read the game's map choices now and then and rebuild the visible list.</summary>
    public void Update()
    {
        UICreateRTMPRoomContext? view = _view;
        if (view == null || _errors >= ErrorsBeforeStandingDown) return;
        float now = Time.unscaledTime;
        try
        {
            if (now >= _nextRefresh)
            {
                _nextRefresh = now + RefreshSeconds;
                ReadOptions(view);
                SelectedId = view.m_trackSelector?.value ?? string.Empty;
            }
            if (_dirty) Rebuild();
        }
        catch (Exception exception)
        {
            ReportError("Update", exception);
        }
    }

    /// <summary>Copies the screen's map choices (id, name, mod or not) and notes whether they changed since last time.</summary>
    private void ReadOptions(UICreateRTMPRoomContext view)
    {
        var list = view.m_trackOptions;
        int count = list?.Count ?? 0;
        var fresh = new List<(string Id, string Label, bool IsMod)>(count);
        for (int i = 0; i < count; i++)
        {
            var option = list![i];
            string id = option.id ?? string.Empty;
            if (id.Length == 0) continue;
            string label = option.GetText();
            if (string.IsNullOrWhiteSpace(label)) label = id;
            fresh.Add((id, label, view.IsModTrack(id)));
        }
        if (fresh.SequenceEqual(_options)) return;
        _options.Clear();
        _options.AddRange(fresh);
        _dirty = true;
        if (!_optionsLogged && fresh.Count > 0)
        {
            _optionsLogged = true;
            Plugin.LogSource.LogInfo($"TRACKPICKER_OPTIONS — the screen offers {fresh.Count} map(s), {fresh.Count(o => o.IsMod)} of them mods.");
        }
    }

    /// <summary>Builds <see cref="Entries"/> (with favourite marks) and <see cref="Visible"/> from the current options and query.</summary>
    private void Rebuild()
    {
        _dirty = false;
        _entries.Clear();
        foreach ((string id, string label, bool isMod) in _options)
            _entries.Add(new TrackEntry { Id = id, Label = isMod ? label + "  (mod)" : label, IsMod = isMod, IsFavorite = _favorites?.Contains(id) ?? false });
        _visible = Query.Apply(_entries);
    }

    // ---- the panel's actions --------------------------------------------------------------------------------------

    /// <summary>Sets the search text.</summary>
    public void SetSearch(string text) { Query.Search = text; _dirty = true; }

    /// <summary>Shows only favourites (remembered).</summary>
    public void SetOnlyFavorites(bool value) { Query.OnlyFavorites = value; _onlyFavorites.Value = value; _dirty = true; }

    /// <summary>Shows only mod maps (remembered).</summary>
    public void SetOnlyMods(bool value) { Query.OnlyMods = value; _onlyMods.Value = value; _dirty = true; }

    /// <summary>Orders each group A to Z (remembered).</summary>
    public void SetSortByName(bool value) { Query.SortByName = value; _sortByName.Value = value; _dirty = true; }

    /// <summary>Marks the map as a favourite, or removes the mark.</summary>
    public void ToggleFavorite(string id)
    {
        _favorites?.Toggle(id);
        _dirty = true;
    }

    /// <summary>Selects the map on the game's screen, exactly as stepping to it with the arrows would.</summary>
    public void Select(string id)
    {
        Plugin.LogSource.LogInfo($"TRACKPICKER_SELECT — selecting '{id}'.");
        try { _view?.m_trackSelector?.Select(id); }
        catch (Exception exception) { ReportError("Select", exception); }
        SelectedId = id;
    }

    private void ReportError(string where, Exception exception)
    {
        _errors++;
        if (_errors <= 3) Plugin.LogSource.LogError($"TRACKPICKER_ERROR — {where}: {exception}");
        if (_errors == ErrorsBeforeStandingDown)
            Plugin.LogSource.LogError("TRACKPICKER_STOOD_DOWN — too many errors; the map list is off for this session and the game's own selector is left alone.");
    }
}

using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using CarX.Game.DR3Console.DR3.Scripts.States.Contexts.UI.WorldMap.FreeRide;
using HarmonyLib;
using CarXModManager.Logic;
using CarXModManager.Ui;
using UnityEngine;

namespace CarXModManager.Integrations.TrackPicker;

/// <summary>
/// A searchable map list for the "create room" screen. The game offers the map as a one-value left/right selector, so
/// reaching one map among many (official maps plus every mod map you have) means stepping through them one at a time.
/// This integration adds a "Browse maps" button next to the selector that opens a list of all of them: search, mark favourites
/// (listed first), show only mod maps, and click a map to select it.
/// <para>
/// How it works: the screen's <c>UICreateRTMPRoomContext</c> keeps the map choices in its track selector
/// (<c>m_trackSelector</c>, a <c>StringMultiValueSelector</c>) and the choices in <c>m_trackOptions</c>. The list reads those
/// and calls the selector's own <c>Select(id)</c>, which is exactly what the arrows do, so the rest of the screen (areas,
/// configurations, preview) updates as usual. The button and the list are real objects of the game's own screen
/// (<see cref="TrackPickerPopup"/>), built only after a checklist of the screen's structure passed.
/// </para>
/// </summary>
internal sealed class TrackPickerIntegration : IIntegration
{
    private const string GameAssembly = "CarX.DR3.Assembly";
    private const string ViewTypeName = "CarX.Game.DR3Console.DR3.Scripts.States.Contexts.UI.WorldMap.FreeRide.UICreateRTMPRoomContext";

    /// <summary>Seconds between refreshes of the map list while the screen is open (mod map names arrive a little late).</summary>
    private const float RefreshSeconds = 0.5f;

    /// <summary>Seconds after the screen opens before the button is added, so the game has finished arranging (and animating) the screen.</summary>
    private const float SettleSeconds = 0.4f;

    /// <summary>Tries to add the button each time the screen opens; a screen that is still sliding in can fail a check that passes a moment later.</summary>
    private const int BuildAttemptsPerOpening = 3;

    /// <summary>Seconds between those tries, multiplied by the number of tries so far.</summary>
    private const float BuildRetrySeconds = 1f;

    /// <summary>Openings of the screen whose every try failed, after which the button is not tried again this session (each was logged with what was missing).</summary>
    private const int MaxFailedOpenings = 3;

    /// <summary>Errors after which the integration stops reading the game's screen.</summary>
    private const int ErrorsBeforeStandingDown = 5;

    /// <summary>The one instance (static hook methods reach the integration through it).</summary>
    internal static TrackPickerIntegration? Instance { get; private set; }

    private ConfigEntry<bool> _enabled = null!, _onlyFavorites = null!, _onlyMods = null!, _sortByName = null!;
    private ConfigEntry<ButtonPlacement> _buttonPlacement = null!;
    private ConfigEntry<float> _offsetX = null!, _offsetY = null!;

    private FavoriteStore? _favorites;
    private UICreateRTMPRoomContext? _view;
    private readonly List<(string Id, string Label, bool IsMod)> _options = new();
    private readonly List<TrackEntry> _entries = new();
    private int[] _visible = Array.Empty<int>();
    private readonly TrackPickerPopup _popup;
    private float _nextRefresh;
    private float _buildAt = -1f;
    private int _buildAttempt;
    private int _failedOpenings;
    private bool _dirty = true;
    private bool _optionsLogged;
    private int _errors;

    /// <summary>Creates the integration (its hooks and controls are set up later, by <see cref="Install"/>).</summary>
    public TrackPickerIntegration() => _popup = new TrackPickerPopup(this);

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

    /// <summary>The page of the list that is shown (0 is the first). May be past the last page right after the list shrank; the popup limits it.</summary>
    public int Page { get; private set; }

    /// <inheritdoc />
    public void BindConfig(ConfigFile config)
    {
        _enabled = config.Bind(Name, "Enabled", true,
            "A searchable map list next to the create-room screen's map selector. false = the game's normal screen.");
        _onlyFavorites = config.Bind(Name, "OnlyFavorites", false, "Show only favourite maps in the list. Also switchable in the list; remembered.");
        _onlyMods = config.Bind(Name, "OnlyMods", false, "Show only mod (custom) maps in the list. Also switchable in the list; remembered.");
        _sortByName = config.Bind(Name, "SortByName", false, "List maps A to Z instead of in the game's order (favourites always first). Also switchable in the list; remembered.");
        _buttonPlacement = config.Bind(Name, "ButtonPlacement", ButtonPlacement.Auto,
            "Where the Browse maps button goes relative to the map selector. Auto = the first of Right, Left and OnLabel where it is fully visible.");
        _offsetX = config.Bind(Name, "ButtonOffsetX", 0f, "Move the Browse maps button sideways by this many units (negative = left), after it was placed.");
        _offsetY = config.Bind(Name, "ButtonOffsetY", 0f, "Move the Browse maps button up by this many units (negative = down), after it was placed.");

        Query.OnlyFavorites = _onlyFavorites.Value;
        Query.OnlyMods = _onlyMods.Value;
        Query.SortByName = _sortByName.Value;
    }

    /// <inheritdoc />
    /// <remarks>The two optional hooks tell the integration when the screen rebuilt its map choices, so the list updates at once.</remarks>
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
        return HookInstaller.Install(harmony, log, typeof(TrackPickerIntegration), Hooks);
    }

    // ---- hooks --------------------------------------------------------------------------------------------------------

    private static void OnActivatedPostfix(UICreateRTMPRoomContext __instance)
    {
        TrackPickerIntegration? self = Instance;
        if (self == null) return;
        try
        {
            self._view = __instance;
            self._dirty = true;
            self._nextRefresh = 0f;
            self._optionsLogged = false;
            self.Query.Search = string.Empty;
            self.Page = 0;
            self._buildAttempt = 0;
            self._buildAt = self._failedOpenings < MaxFailedOpenings && !self._popup.IsBuiltFor(__instance) ? Time.unscaledTime + SettleSeconds : -1f;
            Plugin.LogSource.LogInfo("TRACKPICKER_OPENED — the create-room screen opened; the Browse maps button will be added.");
        }
        catch (Exception exception) { self.ReportError("OnActivated", exception); }
    }

    private static void OnDeactivatedPostfix()
    {
        TrackPickerIntegration? self = Instance;
        if (self == null) return;
        try
        {
            self._popup.Close();
            self._view = null;
            self._buildAt = -1f;
            self._options.Clear();
            self._entries.Clear();
            self._visible = Array.Empty<int>();
        }
        catch (Exception exception) { self.ReportError("OnDeactivated", exception); }
    }

    private static void OptionsChangedPostfix()
    {
        TrackPickerIntegration? self = Instance;
        if (self != null) self._dirty = true;
    }

    // ---- per frame --------------------------------------------------------------------------------------------------------

    /// <inheritdoc />
    /// <remarks>While the screen is open: adds the button once the screen has settled, re-reads the game's map choices now and then, rebuilds the visible list, and keeps an open list up to date.</remarks>
    public void Update()
    {
        UICreateRTMPRoomContext? view = _view;
        if (view == null || _errors >= ErrorsBeforeStandingDown) return;
        float now = Time.unscaledTime;
        try
        {
            if (!view.gameObject.activeInHierarchy)
            {
                _popup.Close(); // the screen went away without telling us: nothing of ours may stay on screen
                return;
            }
            if (_buildAt >= 0f && now >= _buildAt)
            {
                _buildAt = -1f;
                _buildAttempt++;
                bool last = _buildAttempt >= BuildAttemptsPerOpening;
                if (!_popup.TryBuild(view, _buttonPlacement.Value, _offsetX.Value, _offsetY.Value, _buildAttempt, BuildAttemptsPerOpening))
                {
                    if (last) _failedOpenings++;
                    else _buildAt = now + BuildRetrySeconds * _buildAttempt;
                }
            }
            if (now >= _nextRefresh)
            {
                _nextRefresh = now + RefreshSeconds;
                ReadOptions(view);
                SelectedId = view.m_trackSelector?.value ?? string.Empty;
            }
            if (_dirty) Rebuild();
            _popup.Refresh();
        }
        catch (Exception exception)
        {
            ReportError("Update", exception);
        }
    }

    /// <inheritdoc />
    public void Shutdown() => _popup.Close();

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
        Page = Pager.Clamp(Page, _visible.Length, TrackPickerPopup.PageSize);
    }

    // ---- the list's actions --------------------------------------------------------------------------------------------

    /// <summary>Sets the search text and goes back to the first page.</summary>
    public void SetSearch(string text) { Query.Search = text; Page = 0; _dirty = true; }

    /// <summary>Shows only favourites (remembered).</summary>
    public void SetOnlyFavorites(bool value) { Query.OnlyFavorites = value; _onlyFavorites.Value = value; Page = 0; _dirty = true; }

    /// <summary>Shows only mod maps (remembered).</summary>
    public void SetOnlyMods(bool value) { Query.OnlyMods = value; _onlyMods.Value = value; Page = 0; _dirty = true; }

    /// <summary>Orders each group A to Z (remembered).</summary>
    public void SetSortByName(bool value) { Query.SortByName = value; _sortByName.Value = value; Page = 0; _dirty = true; }

    /// <summary>Goes to the next page (<paramref name="step"/> = 1) or the previous one (-1), staying within the pages that exist.</summary>
    public void ChangePage(int step) => Page = Pager.Clamp(Page + step, _visible.Length, TrackPickerPopup.PageSize);

    /// <summary>Goes to the page that holds the map the screen has selected (the first page if it is not in the list).</summary>
    public void GoToSelectedPage()
    {
        if (_dirty) Rebuild();
        int position = Array.FindIndex(_visible, index => index < _entries.Count && _entries[index].Id == SelectedId);
        Page = position < 0 ? 0 : Pager.PageOf(position, TrackPickerPopup.PageSize);
    }

    /// <summary>Marks or unmarks the map in row <paramref name="row"/> of the current page as a favourite.</summary>
    public void ToggleFavoriteOnPage(int row)
    {
        if (!TryEntryOnPage(row, out TrackEntry track)) return;
        _favorites?.Toggle(track.Id);
        _dirty = true;
    }

    /// <summary>Selects the map in row <paramref name="row"/> of the current page on the game's screen, exactly as stepping to it with the arrows would.</summary>
    /// <returns><c>true</c> if a map was selected.</returns>
    public bool SelectOnPage(int row)
    {
        if (!TryEntryOnPage(row, out TrackEntry track)) return false;
        Plugin.LogSource.LogInfo($"TRACKPICKER_SELECT — selecting '{track.Id}'.");
        try { _view?.m_trackSelector?.Select(track.Id); }
        catch (Exception exception) { ReportError("Select", exception); return false; }
        SelectedId = track.Id;
        return true;
    }

    private bool TryEntryOnPage(int row, out TrackEntry track)
    {
        track = default;
        int position = Pager.FirstIndex(Pager.Clamp(Page, _visible.Length, TrackPickerPopup.PageSize), TrackPickerPopup.PageSize) + row;
        if (row < 0 || row >= TrackPickerPopup.PageSize || position >= _visible.Length) return false;
        int index = _visible[position];
        if (index >= _entries.Count) return false;
        track = _entries[index];
        return true;
    }

    private void ReportError(string where, Exception exception)
    {
        _errors++;
        if (_errors <= 3) Plugin.LogSource.LogError($"TRACKPICKER_ERROR — {where}: {exception}");
        if (_errors == ErrorsBeforeStandingDown)
        {
            Plugin.LogSource.LogError("TRACKPICKER_STOOD_DOWN — too many errors; the map list is off for this session and the game's own selector is left alone.");
            _popup.Remove();
            IntegrationHealth.Report(Name, "stopped after errors");
        }
    }
}

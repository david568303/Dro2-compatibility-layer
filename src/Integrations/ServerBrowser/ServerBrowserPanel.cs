using CarXModManager.Logic;
using CarXModManager.Overlay;
using UnityEngine;

namespace CarXModManager.Integrations.ServerBrowser;

/// <summary>
/// The server browser's controls: a search box, switches that hide full, empty or locked rooms, a sort selector, and a
/// line saying how many rooms are shown and whether more are still loading. Shown only while the server browser is open.
/// </summary>
internal sealed class ServerBrowserPanel : IOverlayPanel
{
    private const string SearchControlName = "qol.serverbrowser.search";

    private readonly ServerBrowserIntegration _integration;

    /// <summary>Creates the panel for the given integration, which owns the state and the actions.</summary>
    public ServerBrowserPanel(ServerBrowserIntegration integration) => _integration = integration;

    /// <inheritdoc />
    public bool IsVisible => _integration.IsPanelVisible;

    /// <inheritdoc />
    public PanelPlacement Placement { get => _integration.Placement; set => _integration.Placement = value; }

    /// <inheritdoc />
    public Vector2 Size => new(440f, 186f);

    /// <inheritdoc />
    public void Update() => _integration.Update();

    /// <inheritdoc />
    public bool Draw()
    {
        RoomQuery query = _integration.Query;

        PanelGui.BeginVertical();
        PanelGui.Title("Server browser   (drag here to move)");
        PanelGui.MutedLabel(CountsText());

        // search
        PanelGui.BeginHorizontal();
        string search = PanelGui.TextField(query.Search, SearchControlName);
        if (search != query.Search) _integration.SetSearch(search);
        if (PanelGui.Button("Clear", false, 64f)) { _integration.SetSearch(string.Empty); PanelGui.ClearFocus(); }
        PanelGui.EndHorizontal();
        bool typing = PanelGui.IsFocused(SearchControlName);
        if (typing) EndTextEntryOnEnterOrEscape();
        if (search.Length == 0 && !typing) PanelGui.MutedLabel("Type to search by room, map, configuration or region.");
        else PanelGui.MutedLabel(" ");

        // what to hide
        PanelGui.BeginHorizontal();
        bool hideFull = PanelGui.Toggle(query.HideFull, "Hide full");
        if (hideFull != query.HideFull) _integration.SetHideFull(hideFull);
        bool hideEmpty = PanelGui.Toggle(query.HideEmpty, "Hide empty");
        if (hideEmpty != query.HideEmpty) _integration.SetHideEmpty(hideEmpty);
        bool hideLocked = PanelGui.Toggle(query.HideLocked, "Hide locked");
        if (hideLocked != query.HideLocked) _integration.SetHideLocked(hideLocked);
        PanelGui.EndHorizontal();

        // order, loading, reset
        PanelGui.BeginHorizontal();
        if (PanelGui.Button($"Sort: {SortLabel(query.Sort)}")) _integration.SetSort(NextSort(query.Sort));
        bool loadAll = PanelGui.Toggle(_integration.LoadsAllRooms, "Load all");
        if (loadAll != _integration.LoadsAllRooms) _integration.SetLoadAll(loadAll);
        if (PanelGui.Button("Reset", false, 64f)) _integration.ResetQuery();
        PanelGui.EndHorizontal();

        PanelGui.EndVertical();
        return typing;
    }

    /// <summary>"12 of 57 rooms", with "loading more..." while further pages are still coming.</summary>
    private string CountsText()
    {
        string text = _integration.TotalRooms == _integration.ShownRooms
            ? $"{_integration.TotalRooms} rooms"
            : $"{_integration.ShownRooms} of {_integration.TotalRooms} rooms";
        return _integration.IsLoadingMore ? text + "  -  loading more..." : text;
    }

    /// <summary>Enter or Escape inside the search box leaves it, so the keyboard goes back to the game.</summary>
    private static void EndTextEntryOnEnterOrEscape()
    {
        Event? current = Event.current;
        if (current == null || current.type != EventType.KeyDown) return;
        if (current.keyCode is KeyCode.Return or KeyCode.KeypadEnter or KeyCode.Escape)
        {
            PanelGui.ClearFocus();
            current.Use();
        }
    }

    private static string SortLabel(RoomSort sort) => sort switch
    {
        RoomSort.MostPlayers => "most players",
        RoomSort.FreeSlots => "free slots",
        RoomSort.Name => "name",
        RoomSort.Map => "map",
        RoomSort.Region => "region",
        RoomSort.LowestPing => "lowest ping",
        _ => "game order",
    };

    private static RoomSort NextSort(RoomSort sort) => (RoomSort)(((int)sort + 1) % Enum.GetValues<RoomSort>().Length);
}

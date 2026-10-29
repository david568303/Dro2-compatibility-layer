using CarXModManager.Logic;
using CarXModManager.Overlay;
using UnityEngine;

namespace CarXModManager.Integrations.TrackPicker;

/// <summary>
/// The map picker's controls: a search box, switches (favourites only, mods only, A to Z) and the scrollable list of maps
/// with a Fav button on each row. The map the screen has selected is highlighted. Shown only on the create-room screen.
/// </summary>
internal sealed class TrackPickerPanel : IOverlayPanel
{
    private const string SearchControlName = "qol.trackpicker.search";
    private const float ListHeight = 270f;

    private readonly TrackPickerIntegration _integration;
    private Vector2 _scroll;

    /// <summary>Creates the panel for the given integration, which owns the state and the actions.</summary>
    public TrackPickerPanel(TrackPickerIntegration integration) => _integration = integration;

    /// <inheritdoc />
    public bool IsVisible => _integration.IsPanelVisible;

    /// <inheritdoc />
    public PanelPlacement Placement { get => _integration.Placement; set => _integration.Placement = value; }

    /// <inheritdoc />
    public Vector2 Size => new(380f, 424f);

    /// <inheritdoc />
    public void Update() => _integration.Update();

    /// <inheritdoc />
    public bool Draw()
    {
        TrackQuery query = _integration.Query;

        PanelGui.BeginVertical();
        PanelGui.Title("Maps   (drag here to move)");
        PanelGui.MutedLabel(CountsText());

        PanelGui.BeginHorizontal();
        string search = PanelGui.TextField(query.Search, SearchControlName);
        if (search != query.Search) _integration.SetSearch(search);
        if (PanelGui.Button("Clear", false, 64f)) { _integration.SetSearch(string.Empty); PanelGui.ClearFocus(); }
        PanelGui.EndHorizontal();
        bool typing = PanelGui.IsFocused(SearchControlName);
        if (typing) EndTextEntryOnEnterOrEscape();

        PanelGui.BeginHorizontal();
        bool onlyFavorites = PanelGui.Toggle(query.OnlyFavorites, "Favourites");
        if (onlyFavorites != query.OnlyFavorites) _integration.SetOnlyFavorites(onlyFavorites);
        bool onlyMods = PanelGui.Toggle(query.OnlyMods, "Mods");
        if (onlyMods != query.OnlyMods) _integration.SetOnlyMods(onlyMods);
        bool sortByName = PanelGui.Toggle(query.SortByName, "A-Z");
        if (sortByName != query.SortByName) _integration.SetSortByName(sortByName);
        PanelGui.EndHorizontal();

        _scroll = PanelGui.BeginScroll(_scroll, ListHeight);
        IReadOnlyList<TrackEntry> entries = _integration.Entries;
        foreach (int index in _integration.Visible)
        {
            if (index >= entries.Count) break; // the list was rebuilt while drawing
            TrackEntry track = entries[index];
            PanelGui.BeginHorizontal();
            if (PanelGui.Button("Fav", track.IsFavorite, 48f)) _integration.ToggleFavorite(track.Id);
            if (PanelGui.Button(track.Label, track.Id == _integration.SelectedId)) _integration.Select(track.Id);
            PanelGui.EndHorizontal();
        }
        PanelGui.EndScroll();

        PanelGui.EndVertical();
        return typing;
    }

    /// <summary>"12 of 57 maps", or just the total when nothing is filtered out.</summary>
    private string CountsText()
    {
        int total = _integration.Entries.Count, shown = _integration.Visible.Count;
        return total == shown ? $"{total} maps" : $"{shown} of {total} maps";
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
}

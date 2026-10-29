using CarX.Game.DR3Console.DR3.Scripts.States.Contexts.UI.WorldMap.FreeRide;
using CarXModManager.Integrations;
using CarXModManager.Logic;
using CarXModManager.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarXModManager.Integrations.TrackPicker;

/// <summary>Where the "Browse maps" button goes relative to the game's map selector (the <c>[TrackPicker] ButtonPlacement</c> setting).</summary>
internal enum ButtonPlacement
{
    /// <summary>The first of Right, Left and OnLabel where the button is fully visible.</summary>
    Auto,
    /// <summary>Just right of the map selector.</summary>
    Right,
    /// <summary>Just left of the map selector.</summary>
    Left,
    /// <summary>Inside the map selector, at the right end of the map name.</summary>
    OnLabel,
}

/// <summary>
/// The map picker's controls, built inside the game's create-room screen: a "Browse maps" button next to the game's map selector,
/// and the list it opens, a popup of the game's own look with a search button, switches (favourites, mods, A to Z), eight maps
/// per page with a favourite mark each, and previous / next buttons. Choosing a map selects it in the game's own selector, exactly
/// as the selector's arrows would.
/// <para>
/// Nothing is changed unless the checks in <see cref="TryBuild"/> all pass (see <see cref="Preflight"/>); if one fails, the screen is
/// left exactly as the game made it and the log says which piece was missing.
/// </para>
/// </summary>
internal sealed class TrackPickerPopup
{
    /// <summary>How many maps a page of the list shows.</summary>
    public const int PageSize = 8;

    private const float Gap = 6f;

    private readonly TrackPickerIntegration _owner;
    private GameObject? _button, _root;
    private IntPtr _builtFor;
    private UiButton? _close, _search, _favorites, _mods, _sortByName, _previous, _next;
    private TMP_Text? _title, _pageLabel;
    private readonly UiButton?[] _favoriteMarks = new UiButton?[PageSize];
    private readonly UiButton?[] _mapButtons = new UiButton?[PageSize];

    /// <summary>Creates the popup for the given integration, which owns the map list, the search and the actions.</summary>
    public TrackPickerPopup(TrackPickerIntegration owner) => _owner = owner;

    /// <summary>True if the controls exist for this very screen (a new instance of the screen needs new controls).</summary>
    public bool IsBuiltFor(UICreateRTMPRoomContext view) => _root != null && _builtFor == view.Pointer;

    /// <summary>True while the list is shown.</summary>
    public bool IsOpen => _root != null && _root.activeSelf;

    /// <summary>
    /// Runs the checks and, only if they all pass, adds the button to <paramref name="view"/> and builds the (hidden) list.
    /// </summary>
    /// <param name="view">The create-room screen the game just opened.</param>
    /// <param name="requested">The placement from the settings.</param>
    /// <param name="offsetX">Extra distance to move the button sideways, from the settings.</param>
    /// <param name="offsetY">Extra distance to move the button up, from the settings.</param>
    /// <param name="attempt">Which try this is for this opening of the screen (a screen that is still sliding in can fail a check that passes a moment later).</param>
    /// <param name="attempts">How many tries there are. Failures before the last are logged as warnings that say another try follows; the last one is an error and shows on the status line.</param>
    /// <returns><c>true</c> if the controls were built; <c>false</c> if a check failed (already logged) or building failed (already undone and logged).</returns>
    public bool TryBuild(UICreateRTMPRoomContext view, ButtonPlacement requested, float offsetX, float offsetY, int attempt, int attempts)
    {
        bool last = attempt >= attempts;
        string tryNote = last ? string.Empty : $" (try {attempt} of {attempts}; trying again shortly)";
        var report = new Preflight();
        RectTransform? selectorRect = null, labelRect = null, canvas = null;
        ButtonLook? look = null;
        Box buttonBox = default;
        ButtonPlacement chosen = ButtonPlacement.Right;

        report.Check("the screen is open", () => (view.gameObject.activeInHierarchy, view.gameObject.activeInHierarchy ? "active" : "not active"));
        report.Check("the map selector", () =>
        {
            var selector = view.m_trackSelector;
            selectorRect = GameUi.RectOf(selector);
            if (selectorRect == null) return (false, "the screen has no map selector");
            labelRect = GameUi.RectOf(selector.m_currentOptionText);
            Rect rect = selectorRect.rect;
            return (rect.width > 40f && rect.height > 10f, $"{rect.width:0} x {rect.height:0} units");
        });
        report.Check("the screen's root", () =>
        {
            canvas = selectorRect == null ? null : GameUi.CanvasRoot(selectorRect);
            return (canvas != null && canvas.rect.width > 300f && canvas.rect.height > 300f, canvas == null ? "none found above the selector" : $"{canvas.rect.width:0} x {canvas.rect.height:0} units");
        });
        report.Check("a text button of the screen to copy the look from", () =>
        {
            Button? reference = GameUi.FirstWithText(view.m_mainSettingsTabButton, view.m_regulationsTabButton);
            if (reference == null) return (false, "neither the main-settings nor the regulations tab button has a TextMeshPro text");
            look = GameUi.CaptureLook(reference);
            return (true, $"'{reference.gameObject.name}', {look.Height:0} units high");
        });
        report.Check("the building blocks work in this game build", () => look == null ? (false, "no look to try") : GameUi.DryRun(look));
        if (report.Passed)
        {
            report.Check("a visible place for the Browse maps button", () =>
            {
                float height = Math.Clamp(look!.Height, 28f, 44f);
                Box screen = new(canvas!.rect.x, canvas.rect.y, canvas.rect.width, canvas.rect.height);
                Box selectorBox = GameUi.BoxIn(canvas, selectorRect!);
                Box labelBox = labelRect != null ? GameUi.BoxIn(canvas, labelRect) : selectorBox;
                List<Box> clippers = GameUi.ClippersOf(canvas, selectorRect!);
                var candidates = new (ButtonPlacement Where, Box Box)[]
                {
                    (ButtonPlacement.Right, ScreenGeometry.Beside(selectorBox, Side.Right, 150f, height, 8f)),
                    (ButtonPlacement.Left, ScreenGeometry.Beside(selectorBox, Side.Left, 150f, height, 8f)),
                    (ButtonPlacement.OnLabel, ScreenGeometry.InsideRightEnd(labelBox, 110f, height, 6f)),
                };
                foreach ((ButtonPlacement where, Box box) in candidates)
                {
                    if (requested != ButtonPlacement.Auto && requested != where) continue;
                    if (!ScreenGeometry.IsVisible(box, screen, clippers)) continue;
                    chosen = where;
                    buttonBox = box.Moved(offsetX, offsetY);
                    return (true, $"{where}, at ({box.X:0},{box.Y:0}) {box.Width:0} x {box.Height:0}");
                }
                return (false, requested == ButtonPlacement.Auto
                    ? "right of the selector, left of it and on its label would all be off screen or cut off"
                    : $"{requested} would be off screen or cut off; try Auto");
            });
        }

        if (!report.Passed)
        {
            string failure = $"TRACKPICKER_CHECK_FAILED — {report.Describe()} The create-room screen is left as the game made it; the game's own map selector works as before.{tryNote}";
            if (last)
            {
                Plugin.LogSource.LogError(failure);
                IntegrationHealth.Report(_owner.Name, "the Browse maps button's checks failed");
            }
            else Plugin.LogSource.LogWarning(failure);
            return false;
        }
        Plugin.LogSource.LogInfo($"TRACKPICKER_CHECKS — {report.Describe()}");

        try
        {
            Build(view, selectorRect!, canvas!, look!, buttonBox);
            var built = new Preflight();
            VerifyBuilt(built, canvas!);
            if (!built.Passed)
            {
                string failure = $"TRACKPICKER_VERIFY_FAILED — the Browse maps button was added but does not look right, so it was removed again: {built.Describe()} The screen is as the game made it.{tryNote}";
                Remove();
                if (last)
                {
                    Plugin.LogSource.LogError(failure);
                    IntegrationHealth.Report(_owner.Name, "the Browse maps button did not verify");
                }
                else Plugin.LogSource.LogWarning(failure);
                return false;
            }
            IntegrationHealth.Clear(_owner.Name);
            string dump = UiDump.Write("createroom", view.transform, 9, 1500);
            Plugin.LogSource.LogInfo($"TRACKPICKER_UI_BUILT — Browse maps button added ({chosen}) and verified: {built.Describe()} The screen's layout was written for reference: {dump}");
            return true;
        }
        catch (Exception exception)
        {
            Plugin.LogSource.LogError($"TRACKPICKER_BUILD_FAILED — the controls could not be built and were removed again; the screen is as the game made it. {exception}");
            Remove();
            IntegrationHealth.Report(_owner.Name, "the controls could not be built");
            return false;
        }
    }

    /// <summary>Checks the result right after building: the button is fully visible, and the list fits on the screen.</summary>
    private void VerifyBuilt(Preflight report, RectTransform canvas)
    {
        Box screen = new(canvas.rect.x, canvas.rect.y, canvas.rect.width, canvas.rect.height);
        report.Check("the Browse maps button is fully visible", () =>
        {
            RectTransform rect = _button!.GetComponent<RectTransform>();
            Box box = GameUi.BoxIn(canvas, rect);
            bool visible = ScreenGeometry.IsVisible(box, screen, GameUi.ClippersOf(canvas, rect)) && box.Width >= 60f && box.Height >= 16f;
            return (visible, $"{box.Width:0} x {box.Height:0} at ({box.X:0},{box.Y:0})");
        });
        report.Check("the list fits on the screen", () =>
        {
            Box box = GameUi.BoxIn(canvas, _root!.transform.Find("Panel").gameObject.GetComponent<RectTransform>());
            return (screen.Contains(box), $"{box.Width:0} x {box.Height:0}");
        });
    }

    // ---- building ---------------------------------------------------------------------------------------------------------

    private void Build(UICreateRTMPRoomContext view, RectTransform selectorRect, RectTransform canvas, ButtonLook look, Box buttonBox)
    {
        _builtFor = view.Pointer;

        // the button next to the game's map selector
        RectTransform buttonParent = GameUi.RectOf(selectorRect.parent) ?? canvas;
        UiButton open = GameUi.CreateButton("CarXModManager.BrowseMapsButton", buttonParent, look, "Browse maps");
        _button = open.Object;
        open.Object.AddComponent<LayoutElement>().ignoreLayout = true; // a layout group of the game must not move it
        GameUi.PlaceOver(open.Object.GetComponent<RectTransform>(), buttonParent, canvas, buttonBox);
        GameUi.OnClick(open, "Browse maps", Open);

        // the list: a dimming layer over the whole screen with the panel on it (siblings, so a click on the panel is not a click outside it)
        RectTransform root = GameUi.CreateRect("CarXModManager.MapsPopup", canvas);
        GameUi.Fill(root);
        _root = root.gameObject;
        Image dim = GameUi.CreateBox("Dim", root, new Color(0f, 0f, 0f, 0.6f), true);
        GameUi.Fill(dim.rectTransform);
        GameUi.AddClickArea(dim, "Close the map list", Close);

        float panelWidth = Math.Min(760f, canvas.rect.width * 0.9f), panelHeight = Math.Min(660f, canvas.rect.height * 0.92f);
        Image panel = GameUi.CreateBox("Panel", root, new Color(0.07f, 0.07f, 0.09f, 0.97f), true);
        RectTransform panelRect = panel.rectTransform;
        panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(panelWidth, panelHeight);
        panelRect.anchoredPosition = Vector2.zero;
        RectTransform content = GameUi.CreateRect("Content", panelRect);
        GameUi.Fill(content, 14f, 14f);

        float rowHeight = Math.Clamp((panelHeight - 28f - 10f * Gap) / 11f, 26f, 46f);
        void Place(RectTransform rect, int row, float left, float right)
        {
            float top = row * (rowHeight + Gap);
            rect.anchorMin = new Vector2(left, 1f);
            rect.anchorMax = new Vector2(right, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(Gap / 2f, -(top + rowHeight));
            rect.offsetMax = new Vector2(-Gap / 2f, -top);
        }
        UiButton Add(string name, string text, int row, float left, float right)
        {
            UiButton button = GameUi.CreateButton(name, content, look, text);
            Place(button.Object.GetComponent<RectTransform>(), row, left, right);
            return button;
        }

        _title = GameUi.CreateText("Title", content, look, "Maps", TextAlignmentOptions.MidlineLeft);
        Place(_title.rectTransform, 0, 0f, 0.72f);
        _close = Add("Close", "Close", 0, 0.76f, 1f);
        _search = Add("Search", "Search", 1, 0f, 0.42f);
        _favorites = Add("Favourites", "Favourites", 1, 0.42f, 0.63f);
        _mods = Add("Mods", "Mods", 1, 0.63f, 0.82f);
        _sortByName = Add("SortByName", "A-Z", 1, 0.82f, 1f);
        _previous = Add("Previous", "< Previous", 2, 0f, 0.25f);
        _pageLabel = GameUi.CreateText("Page", content, look, "Page 1 of 1", TextAlignmentOptions.Midline);
        Place(_pageLabel.rectTransform, 2, 0.25f, 0.75f);
        _next = Add("Next", "Next >", 2, 0.75f, 1f);

        GameUi.OnClick(_close, "Close", Close);
        GameUi.OnClick(_search, "Search", () => TextCapture.Begin(this, () => _owner.Query.Search, _owner.SetSearch));
        GameUi.OnClick(_favorites, "Favourites only", () => _owner.SetOnlyFavorites(!_owner.Query.OnlyFavorites));
        GameUi.OnClick(_mods, "Mods only", () => _owner.SetOnlyMods(!_owner.Query.OnlyMods));
        GameUi.OnClick(_sortByName, "Sort A to Z", () => _owner.SetSortByName(!_owner.Query.SortByName));
        GameUi.OnClick(_previous, "Previous page", () => _owner.ChangePage(-1));
        GameUi.OnClick(_next, "Next page", () => _owner.ChangePage(1));

        for (int i = 0; i < PageSize; i++)
        {
            int row = i;
            UiButton mark = Add($"Favourite{i}", "Fav", 3 + i, 0f, 0.15f);
            UiButton map = Add($"Map{i}", string.Empty, 3 + i, 0.15f, 1f);
            map.Label.alignment = TextAlignmentOptions.MidlineLeft;
            GameUi.OnClick(mark, "Favourite mark", () => _owner.ToggleFavoriteOnPage(row));
            GameUi.OnClick(map, "Choose map", () =>
            {
                if (_owner.SelectOnPage(row)) Close();
            });
            _favoriteMarks[i] = mark;
            _mapButtons[i] = map;
        }

        _root.SetActive(false);
    }

    // ---- showing and closing -------------------------------------------------------------------------------------------

    /// <summary>Opens the list on the page that holds the map the screen has selected, above everything else on the screen.</summary>
    public void Open()
    {
        if (_root == null) return;
        _owner.GoToSelectedPage();
        _root.transform.SetAsLastSibling();
        _root.SetActive(true);
        Refresh();
    }

    /// <summary>Closes the list (and ends a search being typed in it).</summary>
    public void Close()
    {
        if (TextCapture.IsActiveFor(this)) TextCapture.End();
        if (_root != null && _root.activeSelf) _root.SetActive(false);
    }

    /// <summary>Removes the button and the list from the screen.</summary>
    public void Remove()
    {
        Close();
        GameUi.Destroy(_button);
        GameUi.Destroy(_root);
        _button = null;
        _root = null;
        _builtFor = IntPtr.Zero;
    }

    /// <summary>Brings the open list's texts, marks and page up to date. Call every frame; does nothing while the list is closed.</summary>
    public void Refresh()
    {
        if (!IsOpen || _search == null || _favorites == null || _mods == null || _sortByName == null || _previous == null || _next == null
            || _title == null || _pageLabel == null) return;

        TrackQuery query = _owner.Query;
        IReadOnlyList<TrackEntry> entries = _owner.Entries;
        IReadOnlyList<int> visible = _owner.Visible;
        int page = Pager.Clamp(_owner.Page, visible.Count, PageSize), pages = Pager.PageCount(visible.Count, PageSize);

        string title = visible.Count == entries.Count ? $"Maps - {entries.Count}" : $"Maps - {visible.Count} of {entries.Count}";
        if (_title.text != title) _title.text = title;
        string pageText = $"Page {page + 1} of {pages}";
        if (_pageLabel.text != pageText) _pageLabel.text = pageText;

        bool typing = TextCapture.IsActiveFor(this);
        _search.SetOn(typing);
        _search.SetText(typing ? $"Search: {query.Search}{(TextCapture.CaretVisible ? "|" : " ")}"
            : query.Search.Length == 0 ? "Search: (click, then type)" : $"Search: {query.Search}");
        _favorites.SetText("Favourites");
        _favorites.SetOn(query.OnlyFavorites);
        _mods.SetText("Mods");
        _mods.SetOn(query.OnlyMods);
        _sortByName.SetText("A-Z");
        _sortByName.SetOn(query.SortByName);
        _previous.SetVisible(page > 0);
        _next.SetVisible(page < pages - 1);

        int onPage = Pager.ItemsOnPage(page, visible.Count, PageSize);
        for (int i = 0; i < PageSize; i++)
        {
            UiButton? mark = _favoriteMarks[i], map = _mapButtons[i];
            if (mark == null || map == null) continue;
            bool used = i < onPage;
            mark.SetVisible(used);
            map.SetVisible(used);
            if (!used) continue;
            int index = visible[Pager.FirstIndex(page, PageSize) + i];
            if (index >= entries.Count) continue; // the list was rebuilt meanwhile; the next frame has it right
            TrackEntry track = entries[index];
            mark.SetText("Fav");
            mark.SetOn(track.IsFavorite);
            map.SetText(track.Label);
            map.SetOn(track.Id == _owner.SelectedId);
        }
    }
}

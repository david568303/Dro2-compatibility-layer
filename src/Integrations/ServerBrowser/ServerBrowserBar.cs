using CarX.Game.DR3Console.Tournament.UI.Contexts;
using CarXModManager.Integrations;
using CarXModManager.Logic;
using CarXModManager.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarXModManager.Integrations.ServerBrowser;

/// <summary>Where the server browser's control bar goes (the <c>[ServerBrowser] BarPlacement</c> setting).</summary>
internal enum BarPlacement
{
    /// <summary>Below the room list if the game's layout allows it, otherwise at the bottom of the screen.</summary>
    Auto,
    /// <summary>In a strip taken from the bottom of the room list (the list gets slightly shorter).</summary>
    BelowList,
    /// <summary>In a strip taken from the top of the room list (the list gets slightly shorter).</summary>
    AboveList,
    /// <summary>Floating at the bottom edge of the screen.</summary>
    ScreenBottom,
    /// <summary>Floating at the top edge of the screen.</summary>
    ScreenTop,
}

/// <summary>
/// The server browser's controls, built as real buttons inside the game's own server browser screen: a search button (click, then type),
/// switches that hide full, empty or locked rooms, a sort button, the "load all pages" switch, a reset button and a line with how many
/// rooms are shown. The buttons are made like the screen's own buttons (same background, same font), in a strip taken from the room
/// list, so the screen looks like the game made it with a few more buttons.
/// <para>
/// Nothing is changed unless the checks in <see cref="TryBuild"/> all pass (see <see cref="Preflight"/>); if one fails, the screen is
/// left exactly as the game made it and the log says which piece was missing.
/// </para>
/// </summary>
internal sealed class ServerBrowserBar
{
    private const float MinListWidth = 360f, MinListHeight = 240f;
    private const float Gap = 6f;

    private readonly ServerBrowserIntegration _owner;
    private GameObject? _root;
    private IntPtr _builtFor;
    private ButtonLook? _look;
    private UiButton? _search, _hideFull, _hideEmpty, _hideLocked, _sort, _loadAll, _reset;
    private TMP_Text? _counts;
    private string _countsText = string.Empty;
    private bool _floating;
    private RectTransform? _list;
    private Vector2 _listOffsetMin, _listOffsetMax;

    /// <summary>Creates the bar for the given integration, which owns the search text, the switches and their actions.</summary>
    public ServerBrowserBar(ServerBrowserIntegration owner) => _owner = owner;

    /// <summary>True if the bar exists for this very screen (a new instance of the screen needs a new bar).</summary>
    public bool IsBuiltFor(UILobbyRoomBrowserContext view) => _root != null && _builtFor == view.Pointer;

    /// <summary>
    /// Runs the checks and, only if they all pass, builds the bar inside <paramref name="view"/>.
    /// </summary>
    /// <param name="view">The server browser screen the game just opened.</param>
    /// <param name="requested">The placement from the settings.</param>
    /// <param name="attempt">Which try this is for this opening of the screen (a screen that is still sliding in can fail a check that passes a moment later).</param>
    /// <param name="attempts">How many tries there are. Failures before the last are logged as warnings that say another try follows; the last one is an error and shows on the status line.</param>
    /// <returns><c>true</c> if the bar was built; <c>false</c> if a check failed (already logged) or building failed (already undone and logged).</returns>
    public bool TryBuild(UILobbyRoomBrowserContext view, BarPlacement requested, int attempt, int attempts)
    {
        bool last = attempt >= attempts;
        string tryNote = last ? string.Empty : $" (try {attempt} of {attempts}; trying again shortly)";
        var report = new Preflight();
        ScrollRect? scroll = null;
        RectTransform? list = null, canvas = null;
        Button? reference = null;
        ButtonLook? look = null;

        report.Check("the screen is open", () => (view.gameObject.activeInHierarchy, view.gameObject.activeInHierarchy ? "active" : "not active"));
        report.Check("the room list", () =>
        {
            scroll = view.m_scrollRect;
            list = GameUi.RectOf(scroll);
            if (list == null) return (false, "the screen has no scroll view for its rooms");
            Rect rect = list.rect;
            return (rect.width >= MinListWidth && rect.height >= MinListHeight, $"{rect.width:0} x {rect.height:0} units");
        });
        report.Check("the screen's root", () =>
        {
            canvas = list == null ? null : GameUi.CanvasRoot(list);
            return (canvas != null && canvas.rect.width > 100f, canvas == null ? "none found above the list" : $"{canvas.rect.width:0} x {canvas.rect.height:0} units");
        });
        report.Check("a button of the screen to copy the look from", () =>
        {
            reference = GameUi.FirstWithText(view.m_findRoomByCodeButton, view.m_createRoomButton);
            if (reference == null) return (false, "neither the find-by-code nor the create-room button exists with a TextMeshPro text");
            look = GameUi.CaptureLook(reference);
            return (true, $"'{reference.gameObject.name}', {look.Height:0} units high");
        });
        report.Check("the building blocks work in this game build", () => look == null ? (false, "no look to try") : GameUi.DryRun(look));

        BarPlacement placement = BarPlacement.ScreenBottom;
        float barHeight = 0f;
        if (report.Passed)
        {
            Transform? listParent = list!.parent;
            bool listHasLayout = listParent != null && listParent.gameObject.GetComponent<LayoutGroup>() != null;
            report.Check("the placement of the bar", () =>
            {
                float rowHeight = Math.Clamp(look!.Height, 28f, 44f);
                barHeight = 2f * rowHeight + 3f * Gap;
                placement = requested == BarPlacement.Auto ? (listHasLayout ? BarPlacement.ScreenBottom : BarPlacement.BelowList) : requested;
                if (listHasLayout && placement is BarPlacement.BelowList or BarPlacement.AboveList)
                    return (false, $"{placement} was chosen but the game's layout arranges the list (a layout group would undo the change); use Auto or ScreenBottom");

                Box screen = new(canvas!.rect.x, canvas.rect.y, canvas.rect.width, canvas.rect.height);
                Box listBox = GameUi.BoxIn(canvas, list);
                Box strip = placement switch
                {
                    BarPlacement.BelowList => new Box(listBox.X, listBox.Y, listBox.Width, barHeight),
                    BarPlacement.AboveList => new Box(listBox.X, listBox.Top - barHeight, listBox.Width, barHeight),
                    BarPlacement.ScreenTop => new Box(screen.CenterX - FloatingWidth(screen) / 2f, screen.Top - 24f - barHeight, FloatingWidth(screen), barHeight),
                    _ => new Box(screen.CenterX - FloatingWidth(screen) / 2f, screen.Y + 24f, FloatingWidth(screen), barHeight),
                };
                bool visible = ScreenGeometry.IsVisible(strip, screen, placement is BarPlacement.BelowList or BarPlacement.AboveList ? GameUi.ClippersOf(canvas, list) : Array.Empty<Box>());
                return (visible, $"{placement}, strip at ({strip.X:0},{strip.Y:0}) {strip.Width:0} x {strip.Height:0}{(visible ? "" : ", which is not fully visible on screen")}");
            });
        }

        if (!report.Passed)
        {
            string failure = $"SERVERBROWSER_CHECK_FAILED — {report.Describe()} The server browser screen is left as the game made it; the room list features that do not need the bar (loading all pages) still work.{tryNote}";
            if (last)
            {
                Plugin.LogSource.LogError(failure);
                IntegrationHealth.Report(_owner.Name, "the control bar's checks failed");
            }
            else Plugin.LogSource.LogWarning(failure);
            return false;
        }
        Plugin.LogSource.LogInfo($"SERVERBROWSER_CHECKS — {report.Describe()}");

        try
        {
            Build(view, list!, canvas!, look!, placement, barHeight);
            var built = new Preflight();
            VerifyBuilt(built, list!, canvas!);
            if (!built.Passed)
            {
                string failure = $"SERVERBROWSER_VERIFY_FAILED — the control bar was added but does not look right, so it was removed again and the list restored: {built.Describe()} The screen is as the game made it.{tryNote}";
                Remove();
                if (last)
                {
                    Plugin.LogSource.LogError(failure);
                    IntegrationHealth.Report(_owner.Name, "the control bar did not verify");
                }
                else Plugin.LogSource.LogWarning(failure);
                return false;
            }
            IntegrationHealth.Clear(_owner.Name);
            string dump = UiDump.Write("serverbrowser", view.transform, 9, 1200);
            Plugin.LogSource.LogInfo($"SERVERBROWSER_UI_BUILT — control bar added ({placement}) and verified: {built.Describe()} The screen's layout was written for reference: {dump}");
            return true;
        }
        catch (Exception exception)
        {
            Plugin.LogSource.LogError($"SERVERBROWSER_BUILD_FAILED — the control bar could not be built and was removed again; the screen is as the game made it. {exception}");
            Remove();
            IntegrationHealth.Report(_owner.Name, "the control bar could not be built");
            return false; // an exception is not a timing problem: reported at once, and the caller does not try this opening again
        }
    }

    private static float FloatingWidth(Box screen) => Math.Min(1100f, screen.Width * 0.8f);

    private void Build(UILobbyRoomBrowserContext view, RectTransform list, RectTransform canvas, ButtonLook look, BarPlacement placement, float barHeight)
    {
        _look = look;
        _floating = placement is BarPlacement.ScreenBottom or BarPlacement.ScreenTop;
        _list = list;
        _listOffsetMin = list.offsetMin;
        _listOffsetMax = list.offsetMax;
        RectTransform parent = _floating ? canvas : GameUi.RectOf(list.parent) ?? canvas;
        RectTransform bar = GameUi.CreateRect("CarXModManager.ServerBrowserBar", parent);
        _root = bar.gameObject;
        _builtFor = view.Pointer;

        switch (placement)
        {
            case BarPlacement.BelowList:
            {
                Vector2 min = list.offsetMin, max = list.offsetMax, anchorMin = list.anchorMin, anchorMax = list.anchorMax;
                bar.anchorMin = new Vector2(anchorMin.x, anchorMin.y);
                bar.anchorMax = new Vector2(anchorMax.x, anchorMin.y);
                bar.pivot = new Vector2(0.5f, 0f);
                bar.offsetMin = new Vector2(min.x, min.y);
                bar.offsetMax = new Vector2(max.x, min.y + barHeight);
                list.offsetMin = new Vector2(min.x, min.y + barHeight + Gap);
                break;
            }
            case BarPlacement.AboveList:
            {
                Vector2 min = list.offsetMin, max = list.offsetMax, anchorMin = list.anchorMin, anchorMax = list.anchorMax;
                bar.anchorMin = new Vector2(anchorMin.x, anchorMax.y);
                bar.anchorMax = new Vector2(anchorMax.x, anchorMax.y);
                bar.pivot = new Vector2(0.5f, 1f);
                bar.offsetMin = new Vector2(min.x, max.y - barHeight);
                bar.offsetMax = new Vector2(max.x, max.y);
                list.offsetMax = new Vector2(max.x, max.y - barHeight - Gap);
                break;
            }
            default:
            {
                bool top = placement == BarPlacement.ScreenTop;
                float width = FloatingWidth(new Box(0f, 0f, canvas.rect.width, canvas.rect.height));
                bar.anchorMin = bar.anchorMax = new Vector2(0.5f, top ? 1f : 0f);
                bar.pivot = new Vector2(0.5f, top ? 1f : 0f);
                bar.sizeDelta = new Vector2(width, barHeight);
                bar.anchoredPosition = new Vector2(0f, top ? -24f : 24f);
                break;
            }
        }

        GameUi.Fill(GameUi.CreateBox("Backdrop", bar, new Color(0f, 0f, 0f, 0.35f), false).rectTransform);

        const float split = 0.62f;
        _search = AddButton(bar, "Search", look, 0f, split, 0.5f, 1f);
        _counts = GameUi.CreateText("Counts", bar, look, string.Empty, TextAlignmentOptions.MidlineRight, new Color(look.TextColor.r, look.TextColor.g, look.TextColor.b, 0.8f));
        GameUi.Cell(_counts.rectTransform, split, 1f, 0.5f, 1f, Gap);
        _hideFull = AddButton(bar, "HideFull", look, 0f / 6f, 1f / 6f, 0f, 0.5f);
        _hideEmpty = AddButton(bar, "HideEmpty", look, 1f / 6f, 2f / 6f, 0f, 0.5f);
        _hideLocked = AddButton(bar, "HideLocked", look, 2f / 6f, 3f / 6f, 0f, 0.5f);
        _sort = AddButton(bar, "Sort", look, 3f / 6f, 4.4f / 6f, 0f, 0.5f);
        _loadAll = AddButton(bar, "LoadAll", look, 4.4f / 6f, 5.4f / 6f, 0f, 0.5f);
        _reset = AddButton(bar, "Reset", look, 5.4f / 6f, 1f, 0f, 0.5f);

        GameUi.OnClick(_search, "Search", StartSearch);
        GameUi.OnClick(_hideFull, "Hide full", () => _owner.SetHideFull(!_owner.Query.HideFull));
        GameUi.OnClick(_hideEmpty, "Hide empty", () => _owner.SetHideEmpty(!_owner.Query.HideEmpty));
        GameUi.OnClick(_hideLocked, "Hide locked", () => _owner.SetHideLocked(!_owner.Query.HideLocked));
        GameUi.OnClick(_sort, "Sort", () => _owner.SetSort((RoomSort)(((int)_owner.Query.Sort + 1) % Enum.GetValues<RoomSort>().Length)));
        GameUi.OnClick(_loadAll, "Load all", () => _owner.SetLoadAll(!_owner.LoadsAllRooms));
        GameUi.OnClick(_reset, "Reset", _owner.ResetQuery);

        _reset.SetText("Reset");
        Refresh(view);
    }

    /// <summary>Checks the result right after building: the bar is on screen and big enough, every button has room, and the list is still usable.</summary>
    private void VerifyBuilt(Preflight report, RectTransform list, RectTransform canvas)
    {
        Box screen = new(canvas.rect.x, canvas.rect.y, canvas.rect.width, canvas.rect.height);
        report.Check("the bar is on screen", () =>
        {
            Box box = GameUi.BoxIn(canvas, _root!.GetComponent<RectTransform>());
            return (ScreenGeometry.IsVisible(box, screen, Array.Empty<Box>()) && box.Width >= 300f && box.Height >= 40f, $"{box.Width:0} x {box.Height:0} at ({box.X:0},{box.Y:0})");
        });
        report.Check("every button has room", () =>
        {
            float narrowest = float.MaxValue, lowest = float.MaxValue;
            foreach (UiButton? button in new[] { _search, _hideFull, _hideEmpty, _hideLocked, _sort, _loadAll, _reset })
            {
                Box box = GameUi.BoxIn(canvas, button!.Object.GetComponent<RectTransform>());
                narrowest = Math.Min(narrowest, box.Width);
                lowest = Math.Min(lowest, box.Height);
            }
            return (narrowest >= 40f && lowest >= 16f, $"smallest {narrowest:0} x {lowest:0}");
        });
        report.Check("the room list is still usable", () => (list.rect.height >= 120f && list.rect.width >= MinListWidth, $"{list.rect.width:0} x {list.rect.height:0} units"));
    }

    private UiButton AddButton(RectTransform bar, string name, ButtonLook look, float left, float right, float bottom, float top)
    {
        UiButton button = GameUi.CreateButton(name, bar, look, name);
        GameUi.Cell(button.Object.GetComponent<RectTransform>(), left, right, bottom, top, Gap);
        return button;
    }

    private void StartSearch()
    {
        TextCapture.Begin(this, () => _owner.Query.Search, _owner.SetSearch);
    }

    /// <summary>Ends a running search (the screen is closing).</summary>
    public void EndSearch()
    {
        if (TextCapture.IsActiveFor(this)) TextCapture.End();
    }

    /// <summary>Brings the bar's texts and switch states up to date. Call every frame while the screen is open; does nothing if the bar was not built.</summary>
    /// <param name="view">The screen (a floating bar is shown only while it is open and not covered by another).</param>
    public void Refresh(UILobbyRoomBrowserContext view)
    {
        if (_root == null || _search == null || _look == null) return;
        if (_floating)
        {
            bool visible = view.gameObject.activeInHierarchy && !view.blockedByAnotherContext;
            if (_root.activeSelf != visible) _root.SetActive(visible);
        }

        RoomQuery query = _owner.Query;
        bool typing = TextCapture.IsActiveFor(this);
        _search.SetOn(typing);
        _search.SetText(typing ? $"Search: {query.Search}{(TextCapture.CaretVisible ? "|" : " ")}"
            : query.Search.Length == 0 ? "Search: (click, then type)" : $"Search: {query.Search}");

        _hideFull!.SetText($"Hide full: {OnOff(query.HideFull)}");
        _hideFull.SetOn(query.HideFull);
        _hideEmpty!.SetText($"Hide empty: {OnOff(query.HideEmpty)}");
        _hideEmpty.SetOn(query.HideEmpty);
        _hideLocked!.SetText($"Hide locked: {OnOff(query.HideLocked)}");
        _hideLocked.SetOn(query.HideLocked);
        _sort!.SetText($"Sort: {SortLabel(query.Sort)}");
        _loadAll!.SetText($"Load all: {OnOff(_owner.LoadsAllRooms)}");
        _loadAll.SetOn(_owner.LoadsAllRooms);

        string counts = CountsText();
        if (counts != _countsText && _counts != null)
        {
            _countsText = counts;
            _counts.text = counts;
        }
    }

    /// <summary>Removes the bar and gives the room list its original size back.</summary>
    public void Remove()
    {
        EndSearch();
        if (_list != null)
        {
            _list.offsetMin = _listOffsetMin;
            _list.offsetMax = _listOffsetMax;
            _list = null;
        }
        GameUi.Destroy(_root);
        _root = null;
        _builtFor = IntPtr.Zero;
    }

    private string CountsText()
    {
        string text = _owner.TotalRooms == _owner.ShownRooms
            ? $"{_owner.TotalRooms} rooms"
            : $"{_owner.ShownRooms} of {_owner.TotalRooms} rooms";
        return _owner.IsLoadingMore ? text + " - loading more..." : text;
    }

    private static string OnOff(bool value) => value ? "ON" : "OFF";

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
}

using BepInEx.Configuration;
using CarX.Game.DR3Console.DR3.Scripts.States.Contexts.UI.GameModes;
using CarXModManager.Logic;
using CarXModManager.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarXModManager.Integrations.PlayMenu;

/// <summary>One mode tile of the Play menu, with what is needed to move it and put it back.</summary>
internal sealed class PlayTile
{
    public UIRaceGameModeButton Button = null!;
    public string Mode = string.Empty;
    public int Section;
    public RectTransform Rect = null!;
    public Box Original, Planned;
    public RectMemory Memory = null!;
    public Transform? OldParent;
    public int OldIndex;
    public bool Reparented;
}

/// <summary>What the Play menu rearranging changed.</summary>
internal sealed class PlayMenuState : RearrangedState
{
    public RectTransform Canvas = null!, Root = null!;
    public float Scale = 1f;
    public List<PlayTile> Tiles = new();
    public List<(GameObject Object, RectTransform Rect, Box Planned)> Titles = new();
    public List<Box> Obstacles = new();
    public List<(Selectable Selectable, Navigation.Mode Mode)> Navigation = new();
}

/// <summary>
/// Reorganises the Play menu (the game modes screen that opens when Play is pressed). The game shows its eight mode tiles in the order its
/// designer chose, with nothing saying which are single player and which online. This puts the tiles in two titled sections:
/// <b>SINGLE PLAYER</b> (career, free ride, XDS training, duels) and <b>MULTIPLAYER</b> (the multiplayer lobby, which is the server browser,
/// then multiplayer duels and the two Top 32 modes), in one row with a wider gap between the sections or, if the tiles are too big for that,
/// one row per section. The server browser is the first tile of the Multiplayer section: clicking it opens the server browser, as the main
/// menu's duplicate globe tile (removed by the main menu integration) used to.
/// <para>
/// The tiles themselves are the game's own objects, so everything they do (hover, description text, selection, gamepad, "new" markers,
/// locked modes) is unchanged; only their place, order and (if they have to shrink to fit) scale change. A tile that sits in one of the game's
/// layout groups (which would undo any placement) is moved out of it to the screen's root. The layout never leaves the free area: the
/// header and description texts are obstacles, and the tiles may not shrink below a readable size. Nothing is changed unless the checks in
/// <see cref="Arrange"/> pass; the result is verified; and it keeps being verified (see <see cref="ScreenRearranger{TView, TState}"/>).
/// The screen's original layout is written to <c>BepInEx/carx.modmanager.ui-playmenu.txt</c> before anything is changed.
/// </para>
/// </summary>
internal sealed class PlayMenuIntegration : ScreenRearranger<UIRaceGameModesContext, PlayMenuState>
{
    /// <summary>Space kept free between the usable area and the edge of the screen.</summary>
    private const float Margin = 24f;

    /// <summary>Space kept between the arrangement and the header and description texts.</summary>
    private const float ObstaclePad = 12f;

    /// <summary>Height of a section's title and the space between it and its tiles, in the screen's units.</summary>
    private const float TitleHeight = 34f, TitleGap = 6f;

    /// <summary>The fewest tiles that make the screen worth arranging (a screen that is still being filled has fewer).</summary>
    private const int MinTiles = 4;

    private static readonly string[] SectionTitles = { "SINGLE PLAYER", "MULTIPLAYER" };

    private ConfigEntry<bool> _enabled = null!, _titles = null!;
    private ConfigEntry<SectionMode> _layout = null!;
    private ConfigEntry<float> _gap = null!, _sectionGap = null!;

    private int _tilesFrame = -1;
    private List<PlayTile> _tiles = new();

    /// <inheritdoc />
    public override string Name => "PlayMenu";

    /// <inheritdoc />
    protected override string Code => "PLAYMENU";

    /// <inheritdoc />
    protected override string DumpName => "playmenu";

    /// <inheritdoc />
    public override bool IsEnabled => _enabled.Value;

    /// <inheritdoc />
    protected override string InstallMessage => $"the Play menu will be reorganised into single player and multiplayer sections when it opens (layout: {_layout.Value}).";

    /// <inheritdoc />
    public override void BindConfig(ConfigFile config)
    {
        _enabled = config.Bind(Name, "Enabled", true,
            "Reorganise the Play menu into SINGLE PLAYER and MULTIPLAYER sections (the server browser is the first multiplayer tile). false = the game's Play menu as designed.");
        _layout = config.Bind(Name, "Layout", SectionMode.Auto,
            "Auto = one row with a wider gap between the sections if the tiles fit it at nearly full size, otherwise one row per section. Line = always one row. Stacked = always one row per section.");
        _titles = config.Bind(Name, "SectionTitles", true, "Show the SINGLE PLAYER / MULTIPLAYER titles above the sections.");
        _gap = config.Bind(Name, "Gap", 16f, new ConfigDescription("Space between neighbouring tiles, in the screen's units (1080 units high).", new AcceptableValueRange<float>(0f, 120f)));
        _sectionGap = config.Bind(Name, "SectionGap", 48f, new ConfigDescription("Space between the sections, in the screen's units.", new AcceptableValueRange<float>(0f, 300f)));
    }

    // ---- finding the tiles -----------------------------------------------------------------------------------------------

    /// <summary>
    /// The mode tiles that are active and at least partly on screen, in the order they should appear: the single player modes, then the
    /// multiplayer modes with the server browser (the multiplayer lobby) first. Looked up once per frame.
    /// </summary>
    private List<PlayTile> Tiles(UIRaceGameModesContext view, RectTransform canvas)
    {
        if (_tilesFrame == Time.frameCount) return _tiles;
        _tilesFrame = Time.frameCount;

        var screen = new Box(canvas.rect.x, canvas.rect.y, canvas.rect.width, canvas.rect.height);
        var tiles = new List<PlayTile>();
        var seen = new HashSet<IntPtr>();
        void Add(UIRaceGameModeButton? button, string mode, int section)
        {
            if (button == null || !button.gameObject.activeInHierarchy || !seen.Add(button.Pointer)) return;
            RectTransform? rect = GameUi.RectOf(button);
            if (rect == null) return;
            Box box = GameUi.BoxIn(canvas, rect);
            if (!(box.Right > screen.X && box.X < screen.Right && box.Top > screen.Y && box.Y < screen.Top)) return;
            tiles.Add(new PlayTile { Button = button, Mode = mode, Section = section, Rect = rect, Original = box });
        }
        Add(view.m_careerButton, "Career", 0);
        Add(view.m_freerideButton, "Freeride", 0);
        Add(view.m_xdsTrainingButton, "XdsTraining", 0);
        Add(view.m_duelsButton, "Duels", 0);
        Add(view.m_rtmpLobbyButton, "RTMPLobby", 1);
        Add(view.m_mpDuelsButton, "MultiplayerDuels", 1);
        Add(view.m_asyncTop32Button, "AsyncTop32", 1); // the game's own order, so bumper-key navigation through the modes stays natural
        Add(view.m_rtmpTop32Button, "RTMPTop32", 1);
        return _tiles = tiles;
    }

    /// <inheritdoc />
    protected override string? NotReadyBecause(UIRaceGameModesContext view, RectTransform canvas)
    {
        List<PlayTile> tiles = Tiles(view, canvas);
        if (tiles.Count < MinTiles) return $"only {tiles.Count} mode tiles so far";

        // the main menu's own server browser tile is only removed while this screen offers the same thing
        bool hasLobby = tiles.Any(tile => tile.Mode == "RTMPLobby");
        if (ServerBrowserAccess.PlayMenuHasLobbyTile != hasLobby)
        {
            ServerBrowserAccess.PlayMenuHasLobbyTile = hasLobby;
            if (!hasLobby)
                Plugin.LogSource.LogWarning("PLAYMENU_NO_LOBBY_TILE — the Play menu has no active multiplayer lobby tile, so the server browser cannot be opened from it; the main menu keeps its own server browser tile.");
        }
        return null;
    }

    /// <inheritdoc />
    protected override string SignatureOf(UIRaceGameModesContext view, RectTransform canvas) =>
        string.Join(",", Tiles(view, canvas).Select(tile => tile.Mode));

    /// <inheritdoc />
    protected override Transform DumpRoot(UIRaceGameModesContext view, RectTransform canvas) =>
        CommonAncestor(Tiles(view, canvas).Select(tile => tile.Rect.transform).Append(view.transform)) ?? view.transform;

    // ---- arranging -------------------------------------------------------------------------------------------------------

    /// <summary>The header and description texts: the arrangement must not cover them.</summary>
    private static List<Box> ObstaclesOf(UIRaceGameModesContext view, RectTransform canvas)
    {
        var boxes = new List<Box>();
        foreach (TMP_Text? text in new[] { view.m_headerText, view.m_modeDescriptionText })
        {
            RectTransform? rect = GameUi.RectOf(text);
            if (text != null && rect != null && text.gameObject.activeInHierarchy) boxes.Add(GameUi.BoxIn(canvas, rect));
        }
        return boxes;
    }

    /// <inheritdoc />
    protected override PlayMenuState? Arrange(UIRaceGameModesContext view, RectTransform canvas, string signature, int attempt, bool last)
    {
        List<PlayTile> tiles = Tiles(view, canvas);
        // measured again here: an earlier arrangement may just have been undone in this very frame, after the tiles were first looked up
        foreach (PlayTile tile in tiles) tile.Original = GameUi.BoxIn(canvas, tile.Rect);
        var report = new Preflight();
        RectTransform? root = GameUi.RectOf(view);
        Box menu = GameUi.BoxIn(canvas, root ?? canvas);
        float usableWidth = Math.Min(menu.Width, menu.Height * 16f / 9f);
        var usable = new Box(menu.CenterX - usableWidth / 2f + Margin, menu.Y + Margin, usableWidth - 2f * Margin, menu.Height - 2f * Margin);
        var screen = new Box(canvas.rect.x, canvas.rect.y, canvas.rect.width, canvas.rect.height);
        Box original = MenuUnion(tiles.Select(tile => tile.Original));
        List<Box> obstacles = ObstaclesOf(view, canvas);
        TMP_Text? titleSource = view.m_headerText ?? view.m_modeDescriptionText;
        SectionPlan? plan = null;
        bool titles = _titles.Value && titleSource != null;

        report.Check("the screen's root", root != null, root == null ? "the Play menu has no RectTransform" : $"menu {menu.Width:0} x {menu.Height:0} units, usable {usable.Width:0} x {usable.Height:0}");
        report.Check("the mode tiles", true, $"{tiles.Count}: {string.Join(", ", tiles.Select(tile => tile.Mode))}; they span {original.Width:0} x {original.Height:0} units");
        report.Check("every tile can be moved", () =>
        {
            string[] bad = tiles.Where(tile => GameUi.RectOf(tile.Rect.parent) == null).Select(tile => tile.Mode).ToArray();
            return (bad.Length == 0, bad.Length == 0 ? "yes" : $"no UI parent: {string.Join(", ", bad)}");
        });
        if (report.Passed)
        {
            report.Check("the new arrangement fits", () =>
            {
                Box? area = SectionLayout.FreeArea(usable, original, obstacles, ObstaclePad);
                if (area == null) return (false, "the header or description text sits inside the tiles' own area, so the tiles cannot be rearranged safely");
                IReadOnlyList<IReadOnlyList<int>> sections = Enumerable.Range(0, SectionTitles.Length)
                    .Select(section => (IReadOnlyList<int>)Enumerable.Range(0, tiles.Count).Where(i => tiles[i].Section == section).ToArray()).ToArray();
                plan = SectionLayout.Plan(tiles.Select(tile => tile.Original).ToList(), sections, area.Value, original, _gap.Value, _sectionGap.Value, TitleHeight, TitleGap, titles, _layout.Value);
                bool visible = plan.Fits && plan.Tiles.Where(box => box.HasArea).Concat(plan.Labels).All(box => ScreenGeometry.IsVisible(box, screen, Array.Empty<Box>()));
                return (plan.Fits && visible, plan.Fits && !visible ? "the new places would be cut off" : $"{plan.Detail}, in a free area of {area.Value.Width:0} x {area.Value.Height:0} units");
            });
        }

        if (!report.Passed)
        {
            ReportFailure($"PLAYMENU_CHECK_FAILED — {report.Describe()} The Play menu is left as the game made it.{TryNote(attempt, last)}", last, "the checks failed");
            return null;
        }
        Plugin.LogSource.LogInfo($"PLAYMENU_CHECKS — {report.Describe()}");

        var state = new PlayMenuState { Signature = signature, Canvas = canvas, Root = root!, Scale = plan!.Scale, Tiles = tiles, Obstacles = obstacles };
        for (int i = 0; i < tiles.Count; i++)
        {
            PlayTile tile = tiles[i];
            tile.Planned = plan.Tiles[i];
            tile.Memory = new RectMemory(tile.Rect);
            tile.OldParent = tile.Rect.parent;
            tile.OldIndex = tile.Rect.GetSiblingIndex();
            tile.Reparented = false;
        }

        string before = string.Join("; ", tiles.Select(tile => $"{tile.Mode} {Describe(tile.Original)}"));
        try
        {
            if (titles) CreateTitles(state, titleSource!, plan, tiles);
            Reapply(state);
            UseAutomaticNavigation(state);

            var verified = new Preflight();
            Verify(verified, state, screen);
            if (!verified.Passed)
            {
                Undo(state);
                ReportFailure($"PLAYMENU_VERIFY_FAILED — the Play menu was rearranged but does not look right, so it was put back: {verified.Describe()}{TryNote(attempt, last)}", last, "the arrangement did not verify");
                return null;
            }

            ReportWorking();
            string after = string.Join("; ", tiles.Select(tile => $"{tile.Mode} {Describe(GameUi.BoxIn(canvas, tile.Rect))}"));
            Plugin.LogSource.LogInfo($"PLAYMENU_ARRANGED — {plan.Detail}{(state.Tiles.Any(tile => tile.Reparented) ? $"; {state.Tiles.Count(tile => tile.Reparented)} tiles were moved out of a layout group of the game" : string.Empty)}; verified: {verified.Describe()} Before: {before}. After: {after}.");
            return state;
        }
        catch (Exception)
        {
            Undo(state);
            throw; // reported by the base class, which stops touching the screen
        }
    }

    /// <summary>Creates the SINGLE PLAYER / MULTIPLAYER titles in the font of the screen's own header text.</summary>
    private static void CreateTitles(PlayMenuState state, TMP_Text source, SectionPlan plan, List<PlayTile> tiles)
    {
        ButtonLook look = GameUi.CaptureTextLook(source);
        look.FontSize = Math.Min(look.FontSize, 26f);
        int[] sectionsWithTiles = Enumerable.Range(0, SectionTitles.Length).Where(section => tiles.Any(tile => tile.Section == section)).ToArray();
        for (int k = 0; k < sectionsWithTiles.Length && k < plan.Labels.Length; k++)
        {
            Color color = look.TextColor;
            TMP_Text title = GameUi.CreateText($"CarXModManager.PlayMenuTitle{k}", state.Root, look, SectionTitles[sectionsWithTiles[k]], TextAlignmentOptions.MidlineLeft, new Color(color.r, color.g, color.b, 0.9f));
            state.Titles.Add((title.gameObject, GameUi.RectOf(title)!, plan.Labels[k]));
        }
    }

    /// <inheritdoc />
    /// <remarks>Puts every tile and title where the plan says (moving a tile out of a layout group first), shrinking the tiles if the plan says so.</remarks>
    protected override void Reapply(PlayMenuState state)
    {
        foreach (PlayTile tile in state.Tiles)
        {
            if (!tile.Reparented && InLayoutGroup(tile.Rect, state.Root))
            {
                Transform oldParent = tile.Rect.parent;
                tile.Rect.SetParent(state.Root, false);
                tile.Rect.SetAsLastSibling();
                tile.Reparented = true;
                RebuildLayout(oldParent);
            }
            if (state.Scale < 1f)
            {
                Vector3 original = tile.Memory.OriginalScale;
                tile.Rect.localScale = new Vector3(original.x * state.Scale, original.y * state.Scale, original.z);
            }
            GameUi.PlaceOver(tile.Rect, GameUi.RectOf(tile.Rect.parent)!, state.Canvas, tile.Planned);
        }
        foreach ((GameObject obj, RectTransform rect, Box planned) in state.Titles)
            if (obj != null) GameUi.PlaceOver(rect, state.Root, state.Canvas, planned);
    }

    /// <summary>True if one of the game's layout groups (below <paramref name="root"/>) positions the object, which would undo any placement.</summary>
    private static bool InLayoutGroup(RectTransform rect, RectTransform root)
    {
        for (Transform? t = rect.parent; t != null && t.Pointer != root.Pointer; t = t.parent)
            if (t.gameObject.GetComponent<LayoutGroup>() != null) return true;
        return false;
    }

    private static void RebuildLayout(Transform? layoutParent)
    {
        RectTransform? rect = GameUi.RectOf(layoutParent);
        if (rect != null && rect.gameObject.GetComponent<LayoutGroup>() != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
    }

    /// <summary>
    /// A tile whose keyboard / gamepad neighbours were set by hand would still point at its old neighbours after the move; those tiles go to
    /// automatic navigation (Unity picks the nearest tile in the pressed direction). Tiles that already use automatic navigation are not touched.
    /// </summary>
    private static void UseAutomaticNavigation(PlayMenuState state)
    {
        foreach (PlayTile tile in state.Tiles)
        {
            Selectable? selectable = tile.Button.m_button != null ? tile.Button.m_button : tile.Rect.gameObject.GetComponent<Selectable>();
            if (selectable == null) continue;
            Navigation navigation = selectable.navigation;
            if (navigation.mode != Navigation.Mode.Explicit) continue;
            state.Navigation.Add((selectable, navigation.mode));
            navigation.mode = Navigation.Mode.Automatic;
            selectable.navigation = navigation;
        }
    }

    // ---- checking, holding, undoing --------------------------------------------------------------------------------------

    /// <summary>Checks the result right after the move: the tiles and titles are where planned and the right size, visible, and cover neither each other nor the screen's texts.</summary>
    private static void Verify(Preflight report, PlayMenuState state, Box screen)
    {
        var boxes = new List<Box>();
        report.Check("the tiles are where they were planned, at the planned size", () =>
        {
            string[] off = state.Tiles.Where(tile =>
            {
                Box actual = GameUi.BoxIn(state.Canvas, tile.Rect);
                boxes.Add(actual);
                return !NearPlan(actual, tile.Planned);
            }).Select(tile => tile.Mode).ToArray();
            return (off.Length == 0, off.Length == 0 ? "yes" : $"not as planned: {string.Join(", ", off)}");
        });
        report.Check("the titles are where they were planned", () =>
        {
            int off = 0;
            foreach ((GameObject obj, RectTransform rect, Box planned) in state.Titles)
            {
                Box actual = GameUi.BoxIn(state.Canvas, rect);
                boxes.Add(actual);
                if (obj == null || !NearPlan(actual, planned)) off++;
            }
            return (off == 0, off == 0 ? $"{state.Titles.Count} titles" : $"{off} not as planned");
        });
        report.Check("everything is fully visible", () =>
        {
            int hidden = boxes.Count(box => !ScreenGeometry.IsVisible(box, screen, Array.Empty<Box>()));
            return (hidden == 0, hidden == 0 ? "yes" : $"{hidden} outside the screen or without size");
        });
        report.Check("nothing covers anything else", () =>
        {
            bool each = !MenuLayoutOverlap(boxes);
            int covering = boxes.Count(box => state.Obstacles.Any(obstacle => Overlaps(box, obstacle)));
            return (each && covering == 0, each && covering == 0 ? "yes" : each ? "the header or description text is covered" : "tiles or titles overlap");
        });
    }

    private static bool Overlaps(Box a, Box b) =>
        !(a.Right <= b.X + 0.5f || b.Right <= a.X + 0.5f || a.Top <= b.Y + 0.5f || b.Top <= a.Y + 0.5f);

    private static bool MenuLayoutOverlap(IReadOnlyList<Box> boxes)
    {
        for (int i = 0; i < boxes.Count; i++)
            for (int j = i + 1; j < boxes.Count; j++)
                if (Overlaps(boxes[i], boxes[j])) return true;
        return false;
    }

    /// <summary>
    /// True if something is where it was planned and about the planned size. The centre is compared with room for the tile's own hover
    /// animation, and the size with 15% to spare: a tile the game's animator keeps at full size when it was shrunk to fit would not pass.
    /// </summary>
    private static bool NearPlan(Box actual, Box planned)
    {
        float tolerance = Math.Max(4f, 0.08f * Math.Max(planned.Width, planned.Height));
        float sizeTolerance = Math.Max(6f, 0.15f * Math.Max(planned.Width, planned.Height));
        return Math.Abs(actual.CenterX - planned.CenterX) <= tolerance && Math.Abs(actual.CenterY - planned.CenterY) <= tolerance
            && Math.Abs(actual.Width - planned.Width) <= sizeTolerance && Math.Abs(actual.Height - planned.Height) <= sizeTolerance;
    }

    /// <inheritdoc />
    protected override bool Holds(PlayMenuState state)
    {
        foreach (PlayTile tile in state.Tiles)
        {
            if (tile.Rect == null) return false;
            if (tile.Reparented && tile.Rect.parent.Pointer != state.Root.Pointer) return false;
            if (!NearPlan(GameUi.BoxIn(state.Canvas, tile.Rect), tile.Planned)) return false;
        }
        foreach ((GameObject obj, RectTransform rect, Box planned) in state.Titles)
            if (obj == null || !NearPlan(GameUi.BoxIn(state.Canvas, rect), planned)) return false;
        return true;
    }

    /// <inheritdoc />
    protected override void Undo(PlayMenuState state)
    {
        foreach ((Selectable selectable, Navigation.Mode mode) in state.Navigation)
        {
            if (selectable == null) continue;
            Navigation navigation = selectable.navigation;
            navigation.mode = mode;
            selectable.navigation = navigation;
        }
        state.Navigation.Clear();

        foreach ((GameObject obj, RectTransform _, Box _) in state.Titles) GameUi.Destroy(obj);
        state.Titles.Clear();

        var oldParents = new List<Transform>();
        foreach (PlayTile tile in state.Tiles)
        {
            if (tile.Rect == null) continue;
            if (tile.Reparented && tile.OldParent != null)
            {
                oldParents.Add(tile.OldParent);
                tile.Rect.SetParent(tile.OldParent, false);
                tile.Rect.SetSiblingIndex(tile.OldIndex);
                tile.Reparented = false;
            }
            tile.Memory.Restore();
        }
        foreach (Transform parent in oldParents) RebuildLayout(parent);
    }

    private static Box MenuUnion(IEnumerable<Box> boxes)
    {
        float left = float.MaxValue, bottom = float.MaxValue, right = float.MinValue, top = float.MinValue;
        foreach (Box box in boxes)
        {
            left = Math.Min(left, box.X);
            bottom = Math.Min(bottom, box.Y);
            right = Math.Max(right, box.Right);
            top = Math.Max(top, box.Top);
        }
        return new Box(left, bottom, right - left, top - bottom);
    }

    private static string Describe(Box box) => $"({box.X:0},{box.Y:0},{box.Width:0}x{box.Height:0})";
}

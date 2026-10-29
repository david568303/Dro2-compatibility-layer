using BepInEx.Configuration;
using CarX.Game.DR3Console.Contexts.UI;
using CarXModManager.Logic;
using CarXModManager.Ui;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CarXModManager.Integrations.MainMenu;

/// <summary>Which side of the main menu the Play entry is on (the <c>[MainMenu] PlaySide</c> setting).</summary>
internal enum PlaySide
{
    /// <summary>Play at the right edge of the menu, lined up with the Quit button.</summary>
    Right,
    /// <summary>The game's own arrangement (Play at the left): Play is not moved.</summary>
    Left,
}

/// <summary>What happens to the server browser tile of the main menu (the <c>[MainMenu] ServerBrowserTile</c> setting).</summary>
internal enum ServerBrowserTile
{
    /// <summary>Removed from the main menu: the server browser opens from the Play menu's Multiplayer section instead (same destination).</summary>
    Hidden,
    /// <summary>Kept, placed directly on Play's left (needs <see cref="PlaySide.Right"/>).</summary>
    BesidePlay,
    /// <summary>Kept in the tile row where the game put it.</summary>
    InRow,
}

/// <summary>What the main menu rearranging changed.</summary>
internal sealed class MainMenuState : RearrangedState
{
    public RectTransform Canvas = null!;
    public bool MovesPlay;
    public RectTransform PlayRect = null!, PlayParent = null!;
    public Box PlayPlanned;
    public RectMemory PlayMemory = null!;
    public RectTransform? OnlineRect;              // the server browser tile, when it is placed beside Play
    public Box OnlinePlanned;
    public RectMemory? OnlineMemory;
    public Transform? OnlineOldParent;
    public int OnlineOldIndex;
    public GameObject? HiddenTile;                 // the server browser tile, when it is removed from the menu
    public Transform? HiddenTileParent;
    public RectTransform? DecorationRect;          // the thin line under Play
    public Box DecorationPlanned;
    public RectMemory? DecorationMemory;
    public List<MenuItem> Entries = new();         // every entry of the menu (navigation, overlap checks)
    public List<(Selectable Selectable, Navigation.Mode Mode)> Navigation = new();
}

/// <summary>
/// Rearranges the main menu's primary entries. The game puts the big Play button at the bottom left, above a row of tiles that begins with
/// the server browser's globe tile. This moves <b>Play to the right edge of the menu</b> (lined up with the Quit button) and takes the
/// <b>server browser tile out of the menu</b>: the Play menu already has a multiplayer lobby entry that opens the very same server browser,
/// so the tile was a duplicate shortcut (<c>[MainMenu] ServerBrowserTile</c> can also keep it beside Play or in the row). The thin line the
/// designer put under Play moves with it and the tile row closes the gap. Sizes, looks and behaviour of all other entries are untouched.
/// <para>
/// The menu is not one list: Play is placed by hand, the tiles are arranged by one of the game's layout groups, the small icons by another,
/// Quit by hand. So this is written for that structure, found through the entries' <c>MenuItem</c> components (<c>Play</c>,
/// <c>OnlineMenu</c>). Nothing is changed unless the checks in <see cref="Arrange"/> pass; the result is verified; and it keeps being
/// verified (see <see cref="ScreenRearranger{TView, TState}"/>).
/// </para>
/// </summary>
internal sealed class MainMenuIntegration : ScreenRearranger<UIMainMenuContext, MainMenuState>
{
    /// <summary>Space kept free between the usable area and the edge of the menu's own screen.</summary>
    private const float Margin = 24f;

    /// <summary>The fewest entries that make the menu worth arranging (a menu that is still being filled has fewer).</summary>
    private const int MinEntries = 4;

    private ConfigEntry<bool> _enabled = null!;
    private ConfigEntry<PlaySide> _playSide = null!;
    private ConfigEntry<ServerBrowserTile> _serverBrowserTile = null!;
    private ConfigEntry<float> _gap = null!;

    private int _itemsFrame = -1;
    private List<MenuItem> _items = new();

    /// <inheritdoc />
    public override string Name => "MainMenu";

    /// <inheritdoc />
    protected override string Code => "MAINMENU";

    /// <inheritdoc />
    protected override string DumpName => "mainmenu";

    /// <inheritdoc />
    public override bool IsEnabled => _enabled.Value;

    /// <inheritdoc />
    protected override string InstallMessage =>
        $"the main menu will be rearranged when it opens (Play side: {_playSide.Value}, server browser tile: {_serverBrowserTile.Value}).";

    /// <inheritdoc />
    protected override bool Wanted => _playSide.Value == PlaySide.Right || EffectiveTileMode == ServerBrowserTile.Hidden;

    /// <summary>
    /// What is done with the server browser tile: the setting, except that a tile is not removed once the Play menu has been seen without a
    /// multiplayer lobby tile (it would leave no way to the server browser).
    /// </summary>
    private ServerBrowserTile EffectiveTileMode =>
        _serverBrowserTile.Value == ServerBrowserTile.Hidden && ServerBrowserAccess.PlayMenuHasLobbyTile == false ? ServerBrowserTile.InRow : _serverBrowserTile.Value;

    /// <inheritdoc />
    public override void BindConfig(ConfigFile config)
    {
        _enabled = config.Bind(Name, "Enabled", true, "Rearrange the main menu (see PlaySide and ServerBrowserTile). false = the game's menu as designed.");
        _playSide = config.Bind(Name, "PlaySide", PlaySide.Right,
            "Right = the Play entry at the right edge of the menu. Left = the game's own arrangement (Play is not moved).");
        _serverBrowserTile = config.Bind(Name, "ServerBrowserTile", ServerBrowserTile.Hidden,
            "Hidden = the globe tile is removed from the main menu; the server browser opens from the Play menu (Multiplayer). BesidePlay = kept, placed on Play's left (needs PlaySide = Right). InRow = kept where the game put it.");
        _gap = config.Bind(Name, "Gap", 12f,
            new ConfigDescription("Space between the server browser tile and Play (ServerBrowserTile = BesidePlay), in the screen's units (1080 units high).", new AcceptableValueRange<float>(0f, 120f)));
    }

    // ---- finding the entries -------------------------------------------------------------------------------------------

    /// <summary>
    /// Every menu entry that is active and at least partly on screen (an entry the player cannot see at all is left alone). They are found
    /// by searching everything under the screen and under the container that holds the screen's own buttons (the context has one field per
    /// entry, but the server browser and Discord entries have none). Looked up once per frame.
    /// </summary>
    private List<MenuItem> Items(UIMainMenuContext view, RectTransform canvas)
    {
        if (_itemsFrame == Time.frameCount) return _items;
        _itemsFrame = Time.frameCount;

        var screen = new Box(canvas.rect.x, canvas.rect.y, canvas.rect.width, canvas.rect.height);
        var roots = new List<Transform> { view.transform };
        Button?[] buttons =
        {
            view.m_playButton, view.m_testDriveButton, view.m_workshopButton, view.m_storeButton, view.m_garageButton,
            view.m_settingsButton, view.m_infoButton, view.m_creditsButton, view.m_modsButton, view.m_quitButton,
        };
        Transform? container = CommonAncestor(buttons.Where(button => button != null).Select(button => button!.transform));
        if (container != null) roots.Add(container);

        var items = new List<MenuItem>();
        var seen = new HashSet<IntPtr>();
        foreach (Transform root in roots)
            foreach (MenuItem item in root.gameObject.GetComponentsInChildren<MenuItem>(true))
            {
                if (item == null || !item.gameObject.activeInHierarchy || !seen.Add(item.Pointer)) continue;
                RectTransform? rect = GameUi.RectOf(item);
                if (rect == null) continue;
                Box box = GameUi.BoxIn(canvas, rect);
                bool visibleAtAll = box.Right > screen.X && box.X < screen.Right && box.Top > screen.Y && box.Y < screen.Top;
                if (visibleAtAll) items.Add(item);
            }
        return _items = items;
    }

    /// <summary>
    /// The Play entry the player sees. The menu has two entries of type Play (the visible button under the screen's root, and a leftover under
    /// the game's logo that is cut off by the logo's mask), so the one closest to the root wins, the bigger one if they are equally close.
    /// </summary>
    private static MenuItem? FindPlay(UIMainMenuContext view, List<MenuItem> items) =>
        items.Where(item => KindOf(item) == "Play")
            .OrderBy(item => Depth(item.transform, view.transform))
            .ThenByDescending(item => GameUi.RectOf(item)?.rect.width * GameUi.RectOf(item)?.rect.height ?? 0f)
            .FirstOrDefault();

    private static MenuItem? FindOnline(List<MenuItem> items) => items.FirstOrDefault(item => KindOf(item) == "OnlineMenu");

    private static int Depth(Transform node, Transform root)
    {
        int depth = 0;
        for (Transform? t = node; t != null && t.Pointer != root.Pointer; t = t.parent) depth++;
        return depth;
    }

    private static string KindOf(MenuItem item) => item.Type.ToString();

    /// <inheritdoc />
    protected override string? NotReadyBecause(UIMainMenuContext view, RectTransform canvas)
    {
        List<MenuItem> items = Items(view, canvas);
        if (items.Count < MinEntries) return $"only {items.Count} entries so far";
        return FindPlay(view, items) == null ? "no Play entry yet" : null;
    }

    /// <inheritdoc />
    protected override string SignatureOf(UIMainMenuContext view, RectTransform canvas)
    {
        List<MenuItem> items = Items(view, canvas);
        // the server browser tile only matters when it is placed beside Play (a hidden one is not in the list any more)
        string online = _serverBrowserTile.Value == ServerBrowserTile.BesidePlay ? (FindOnline(items) != null).ToString() : "-";
        return $"play:{FindPlay(view, items) != null}|online:{online}|tile:{EffectiveTileMode}";
    }

    /// <inheritdoc />
    protected override Transform DumpRoot(UIMainMenuContext view, RectTransform canvas) =>
        CommonAncestor(Items(view, canvas).Select(item => item.transform).Append(view.transform)) ?? view.transform;

    // ---- arranging -------------------------------------------------------------------------------------------------------

    /// <inheritdoc />
    protected override MainMenuState? Arrange(UIMainMenuContext view, RectTransform canvas, string signature, int attempt, bool last)
    {
        List<MenuItem> items = Items(view, canvas);
        bool movePlay = _playSide.Value == PlaySide.Right;
        ServerBrowserTile tileMode = EffectiveTileMode == ServerBrowserTile.BesidePlay && !movePlay ? ServerBrowserTile.InRow : EffectiveTileMode;

        var report = new Preflight();
        MenuItem? play = null, online = null;
        RectTransform? playRect = null, onlineRect = null, playParent = null, decorationRect = null;
        Box playBox = default, onlineBox = default, decorationBox = default;
        PairPlan? plan = null;
        var screen = new Box(canvas.rect.x, canvas.rect.y, canvas.rect.width, canvas.rect.height);
        Box menu = GameUi.BoxIn(canvas, GameUi.RectOf(view) ?? canvas);
        float usableWidth = Math.Min(menu.Width, menu.Height * 16f / 9f);
        var usable = new Box(menu.CenterX - usableWidth / 2f + Margin, menu.Y + Margin, usableWidth - 2f * Margin, menu.Height - 2f * Margin);

        report.Check("the screen's root", true, $"menu {menu.Width:0} x {menu.Height:0} units, usable {usable.Width:0} x {usable.Height:0}");
        report.Check("the Play entry", () =>
        {
            play = FindPlay(view, items);
            if (play == null) return (false, "no entry of type Play");
            playRect = GameUi.RectOf(play);
            if (playRect == null) return (false, "the Play entry has no RectTransform");
            playBox = GameUi.BoxIn(canvas, playRect);
            string[] ignored = items.Where(item => KindOf(item) == "Play" && item.Pointer != play.Pointer).Select(item => item.gameObject.name).ToArray();
            return (true, $"'{play.gameObject.name}' at {Describe(playBox)}{(ignored.Length > 0 ? $"; also ignored: {string.Join(", ", ignored)}" : string.Empty)}");
        });
        report.Check("Play can be moved by hand", () =>
        {
            if (play!.gameObject.GetComponent<Button>() == null) return (false, "the Play entry is not a button");
            playParent = GameUi.RectOf(playRect!.parent);
            if (playParent == null) return (false, "Play has no UI parent");
            if (movePlay && playParent.gameObject.GetComponent<LayoutGroup>() != null) return (false, $"Play's parent '{playParent.gameObject.name}' is a layout group, which would undo the move");
            return (true, $"its parent is '{playParent.gameObject.name}'");
        });
        report.Check("the server browser entry", () =>
        {
            online = FindOnline(items);
            if (online == null) return (true, "not on screen");
            onlineRect = GameUi.RectOf(online);
            if (onlineRect == null || online.gameObject.GetComponent<Button>() == null) return (false, $"'{online.gameObject.name}' is not a button with a RectTransform");
            onlineBox = GameUi.BoxIn(canvas, onlineRect);
            return (true, $"'{online.gameObject.name}' at {Describe(onlineBox)} inside '{onlineRect.parent.gameObject.name}', {(tileMode == ServerBrowserTile.Hidden ? "to be removed from the menu" : tileMode == ServerBrowserTile.BesidePlay ? "to go beside Play" : "stays in the row")}");
        });
        if (report.Passed && movePlay)
        {
            report.Check("the new places are free", () =>
            {
                List<Box> others = items.Where(item => item.Pointer != play!.Pointer && (online == null || item.Pointer != online.Pointer))
                    .Select(item => GameUi.BoxIn(canvas, GameUi.RectOf(item)!)).ToList();
                // the menu's right margin is where its right-hand entries (Quit, the small icons) end
                float rightEdge = others.Where(box => box.CenterX > menu.CenterX).Select(box => box.Right).DefaultIfEmpty(usable.Right).Max();
                Box? beside = tileMode == ServerBrowserTile.BesidePlay && online != null ? onlineBox : null;
                plan = PlayPairLayout.PlaceAtRight(playBox, beside, rightEdge, _gap.Value, usable, others);
                bool visible = plan.Fits && ScreenGeometry.IsVisible(plan.Play, screen, GameUi.ClippersOf(canvas, playRect!))
                    && (plan.Online == null || ScreenGeometry.IsVisible(plan.Online.Value, screen, Array.Empty<Box>()));
                return (plan.Fits && visible, plan.Fits && !visible ? "the new places would be cut off" : plan.Detail);
            });
        }

        if (!report.Passed)
        {
            ReportFailure($"MAINMENU_CHECK_FAILED — {report.Describe()} The main menu is left as the game made it.{TryNote(attempt, last)}", last, "the checks failed");
            return null;
        }
        Plugin.LogSource.LogInfo($"MAINMENU_CHECKS — {report.Describe()}");

        // the thin line the designer put under Play goes with it
        for (int i = 0; movePlay && i < playParent!.childCount && decorationRect == null; i++)
        {
            Transform child = playParent.GetChild(i);
            RectTransform? candidate = GameUi.RectOf(child);
            if (candidate == null || candidate.Pointer == playRect!.Pointer || !child.gameObject.activeInHierarchy || child.childCount != 0) continue;
            if (child.gameObject.GetComponent<Image>() == null) continue;
            Box box = GameUi.BoxIn(canvas, candidate);
            if (!PlayPairLayout.IsUnderline(box, playBox)) continue;
            decorationRect = candidate;
            decorationBox = box;
        }

        var moved = new MainMenuState
        {
            Signature = signature, Canvas = canvas, Entries = items, MovesPlay = movePlay,
            PlayRect = playRect!, PlayParent = playParent!, PlayMemory = new RectMemory(playRect!),
        };
        if (movePlay) moved.PlayPlanned = plan!.Play;
        if (online != null && tileMode == ServerBrowserTile.BesidePlay && plan?.Online != null)
        {
            moved.OnlineRect = onlineRect;
            moved.OnlinePlanned = plan.Online.Value;
            moved.OnlineMemory = new RectMemory(onlineRect!);
            moved.OnlineOldParent = onlineRect!.parent;
            moved.OnlineOldIndex = onlineRect.GetSiblingIndex();
        }
        if (online != null && tileMode == ServerBrowserTile.Hidden)
        {
            moved.HiddenTile = online.gameObject;
            moved.HiddenTileParent = online.transform.parent;
        }
        if (decorationRect != null)
        {
            moved.DecorationRect = decorationRect;
            moved.DecorationPlanned = PlayPairLayout.MoveWith(decorationBox, playBox, plan!.Play);
            moved.DecorationMemory = new RectMemory(decorationRect);
        }

        string before = $"Play {Describe(playBox)}{(online != null ? $"; {online.gameObject.name} {Describe(onlineBox)}" : string.Empty)}";
        try
        {
            Reapply(moved);
            UseAutomaticNavigation(moved);

            var verified = new Preflight();
            VerifyMoved(verified, moved, screen);
            if (!verified.Passed)
            {
                Undo(moved);
                ReportFailure($"MAINMENU_VERIFY_FAILED — the menu was rearranged but does not look right, so it was put back: {verified.Describe()}{TryNote(attempt, last)}", last, "the arrangement did not verify");
                return null;
            }

            ReportWorking();
            string after = $"Play {Describe(GameUi.BoxIn(canvas, moved.PlayRect))}{(moved.OnlineRect != null ? $"; {onlineName(online)} {Describe(GameUi.BoxIn(canvas, moved.OnlineRect))}" : string.Empty)}";
            string what = movePlay ? plan!.Detail : "Play stays where the game put it";
            string tile = moved.HiddenTile != null ? $"; the server browser tile ('{moved.HiddenTile.name}') was removed from the menu (the Play menu's multiplayer entry opens the same server browser)" : string.Empty;
            Plugin.LogSource.LogInfo($"MAINMENU_ARRANGED — {what}{tile}{(moved.DecorationRect != null ? $"; the line under Play ('{moved.DecorationRect.gameObject.name}') moved with it" : string.Empty)}; verified: {verified.Describe()} Before: {before}. After: {after}.");
            return moved;
        }
        catch (Exception)
        {
            Undo(moved);
            throw; // reported by the base class, which stops touching the menu
        }

        static string onlineName(MenuItem? item) => item == null ? "the server browser entry" : item.gameObject.name;
    }

    /// <inheritdoc />
    /// <remarks>Puts Play, the server browser tile and the line under Play where the plan says, and hides the server browser tile if it is to be removed.</remarks>
    protected override void Reapply(MainMenuState moved)
    {
        if (moved.OnlineRect != null && moved.OnlineRect.parent.Pointer != moved.PlayParent.Pointer)
        {
            Transform oldParent = moved.OnlineRect.parent;
            moved.OnlineRect.SetParent(moved.PlayParent, false);
            moved.OnlineRect.SetSiblingIndex(moved.PlayRect.GetSiblingIndex() + 1);
            RebuildLayout(oldParent); // the tile row closes the gap
        }
        if (moved.MovesPlay) GameUi.PlaceOver(moved.PlayRect, moved.PlayParent, moved.Canvas, moved.PlayPlanned);
        if (moved.OnlineRect != null) GameUi.PlaceOver(moved.OnlineRect, moved.PlayParent, moved.Canvas, moved.OnlinePlanned);
        if (moved.DecorationRect != null) GameUi.PlaceOver(moved.DecorationRect, moved.PlayParent, moved.Canvas, moved.DecorationPlanned);
        if (moved.HiddenTile != null && moved.HiddenTile.activeSelf)
        {
            // the tile was probably the selected one (the game selects the first entry): give the selection to Play so keyboard and gamepad keep working
            EventSystem? events = EventSystem.current;
            bool hadSelection = events != null && events.currentSelectedGameObject != null && events.currentSelectedGameObject.Pointer == moved.HiddenTile.Pointer;
            moved.HiddenTile.SetActive(false);
            RebuildLayout(moved.HiddenTileParent); // the tile row closes the gap
            if (hadSelection) events!.SetSelectedGameObject(moved.PlayRect.gameObject);
        }
    }

    /// <summary>Makes a layout group lay out its children now (does nothing for an object that is not one).</summary>
    private static void RebuildLayout(Transform? layoutParent)
    {
        RectTransform? rect = GameUi.RectOf(layoutParent);
        if (rect != null && rect.gameObject.GetComponent<LayoutGroup>() != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
    }

    /// <summary>
    /// An entry whose keyboard / gamepad neighbours were set by hand would still point at its old neighbours after the move; those entries
    /// go to automatic navigation (Unity picks the nearest entry in the pressed direction). Entries that already use automatic navigation
    /// are not touched. <see cref="Undo"/> puts the old setting back.
    /// </summary>
    private static void UseAutomaticNavigation(MainMenuState moved)
    {
        foreach (MenuItem item in moved.Entries)
        {
            Selectable? selectable = item.gameObject.GetComponent<Selectable>();
            if (selectable == null) continue;
            Navigation navigation = selectable.navigation;
            if (navigation.mode != Navigation.Mode.Explicit) continue;
            moved.Navigation.Add((selectable, navigation.mode));
            navigation.mode = Navigation.Mode.Automatic;
            selectable.navigation = navigation;
        }
    }

    // ---- checking, holding, undoing --------------------------------------------------------------------------------------

    /// <summary>Checks the result right after the move: the entries are where they were planned, fully visible, and cover no other entry.</summary>
    private static void VerifyMoved(Preflight report, MainMenuState moved, Box screen)
    {
        var placed = new List<(string Name, RectTransform Rect, Box Planned)>();
        if (moved.MovesPlay) placed.Add(("Play", moved.PlayRect, moved.PlayPlanned));
        if (moved.OnlineRect != null) placed.Add(("the server browser entry", moved.OnlineRect, moved.OnlinePlanned));
        var boxes = new List<Box>();
        report.Check("they are where they were planned", () =>
        {
            string[] off = placed.Where(p =>
            {
                Box actual = GameUi.BoxIn(moved.Canvas, p.Rect);
                boxes.Add(actual);
                return !NearPlan(actual, p.Planned);
            }).Select(p => p.Name).ToArray();
            return (off.Length == 0, off.Length == 0 ? "yes" : $"elsewhere: {string.Join(", ", off)}");
        });
        report.Check("they are fully visible", () =>
        {
            int hidden = boxes.Count(box => !ScreenGeometry.IsVisible(box, screen, Array.Empty<Box>()));
            return (hidden == 0, hidden == 0 ? "yes" : $"{hidden} outside the screen or without size");
        });
        report.Check("they cover no other entry", () =>
        {
            var movedPointers = placed.Select(p => p.Rect.Pointer).ToHashSet();
            List<Box> others = moved.Entries
                .Select(item => GameUi.RectOf(item)).Where(rect => rect != null && !movedPointers.Contains(rect.Pointer))
                .Select(rect => GameUi.BoxIn(moved.Canvas, rect!)).ToList();
            int covered = boxes.Count(box => others.Any(other => Overlaps(box, other)));
            bool each = !AnyOverlap(boxes);
            return (covered == 0 && each, covered == 0 && each ? "yes" : "an entry is covered");
        });
        report.Check("the server browser tile is gone from the menu", () =>
            (moved.HiddenTile == null || !moved.HiddenTile.activeInHierarchy, moved.HiddenTile == null ? "not asked for" : "removed"));
    }

    private static bool Overlaps(Box a, Box b) =>
        !(a.Right <= b.X + 0.5f || b.Right <= a.X + 0.5f || a.Top <= b.Y + 0.5f || b.Top <= a.Y + 0.5f);

    private static bool AnyOverlap(IReadOnlyList<Box> boxes)
    {
        for (int i = 0; i < boxes.Count; i++)
            for (int j = i + 1; j < boxes.Count; j++)
                if (Overlaps(boxes[i], boxes[j])) return true;
        return false;
    }

    /// <summary>
    /// True if an entry is where it was planned. The centre is compared, with room for the entry's own hover animation (a slight enlargement
    /// about its pivot moves the centre a little), not its size.
    /// </summary>
    private static bool NearPlan(Box actual, Box planned)
    {
        float tolerance = Math.Max(4f, 0.08f * Math.Max(planned.Width, planned.Height));
        return Math.Abs(actual.CenterX - planned.CenterX) <= tolerance && Math.Abs(actual.CenterY - planned.CenterY) <= tolerance;
    }

    /// <inheritdoc />
    protected override bool Holds(MainMenuState moved)
    {
        if (moved.PlayRect == null) return false;
        if (moved.MovesPlay && !NearPlan(GameUi.BoxIn(moved.Canvas, moved.PlayRect), moved.PlayPlanned)) return false;
        if (moved.OnlineRect != null
            && (moved.OnlineRect.parent.Pointer != moved.PlayParent.Pointer || !NearPlan(GameUi.BoxIn(moved.Canvas, moved.OnlineRect), moved.OnlinePlanned))) return false;
        if (moved.DecorationRect != null && !NearPlan(GameUi.BoxIn(moved.Canvas, moved.DecorationRect), moved.DecorationPlanned)) return false;
        if (moved.HiddenTile != null && moved.HiddenTile.activeSelf) return false; // the game showed the removed tile again
        return true;
    }

    /// <inheritdoc />
    protected override void Undo(MainMenuState moved)
    {
        foreach ((Selectable selectable, Navigation.Mode mode) in moved.Navigation)
        {
            if (selectable == null) continue;
            Navigation navigation = selectable.navigation;
            navigation.mode = mode;
            selectable.navigation = navigation;
        }
        moved.Navigation.Clear();

        if (moved.HiddenTile != null) moved.HiddenTile.SetActive(true);
        if (moved.OnlineRect != null && moved.OnlineOldParent != null && moved.OnlineRect.parent.Pointer != moved.OnlineOldParent.Pointer)
        {
            moved.OnlineRect.SetParent(moved.OnlineOldParent, false);
            moved.OnlineRect.SetSiblingIndex(moved.OnlineOldIndex);
        }
        moved.OnlineMemory?.Restore();
        moved.PlayMemory.Restore();
        moved.DecorationMemory?.Restore();
        RebuildLayout(moved.OnlineOldParent);
        RebuildLayout(moved.HiddenTileParent);
    }

    private static string Describe(Box box) => $"({box.X:0},{box.Y:0},{box.Width:0}x{box.Height:0})";
}

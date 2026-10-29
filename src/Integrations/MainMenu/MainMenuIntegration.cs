using BepInEx.Configuration;
using BepInEx.Logging;
using CarX.Game.DR3Console.Contexts.UI;
using CarXModManager.Logic;
using CarXModManager.Ui;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CarXModManager.Integrations.MainMenu;

/// <summary>Which side of the main menu the Play entry is on (the <c>[MainMenu] PlaySide</c> setting).</summary>
internal enum PlaySide
{
    /// <summary>Play at the right edge of the menu with the server browser entry on its left.</summary>
    Right,
    /// <summary>The game's own arrangement (Play at the left): nothing is changed.</summary>
    Left,
}

/// <summary>
/// Rearranges the main menu's primary entries. The game puts the big Play button at the bottom left, above a row of tiles that begins with
/// the server browser's globe tile. This moves <b>Play to the right edge of the menu</b> (lined up with the Quit button) and puts the
/// <b>server browser entry directly on its left</b>, as a square as tall as Play on the same line; the thin line the designer put under Play
/// moves with it, and the row of tiles closes the gap the globe tile left. Sizes, looks and behaviour of all entries are untouched.
/// <para>
/// The menu is not one list: Play is placed by hand, the tiles are arranged by one of the game's layout groups, the small icons by another,
/// Quit by hand. So this is written for that structure, found through the entries' <c>MenuItem</c> components (<c>Play</c>,
/// <c>OnlineMenu</c>), and it follows the same rules as the other integrations: nothing is changed unless the checks in
/// <see cref="Arrange"/> pass (Play and the server browser entry exist and are buttons, Play's parent is not a layout group, the new
/// places are inside the usable area and free); the result is verified and undone if it is wrong; and it keeps being verified, because
/// the game may move its entries again (an animation, the screen being reset), in which case the change is undone and the log says so. The
/// menu's original layout is written to <c>BepInEx/carx.modmanager.ui-mainmenu.txt</c> before anything is changed.
/// </para>
/// </summary>
internal sealed class MainMenuIntegration : IIntegration
{
    /// <summary>Seconds between looks at whether the main menu is open.</summary>
    private const float PollSeconds = 0.5f;

    /// <summary>Seconds the menu must have been on screen before it is touched, so the game has finished arranging (and animating) it.</summary>
    private const float SettleSeconds = 0.5f;

    /// <summary>Tries to arrange the menu each time it opens; a menu that is still sliding in can fail a check that passes a moment later.</summary>
    private const int AttemptsPerOpening = 3;

    /// <summary>Times the game may move the entries back before the change is undone for good.</summary>
    private const int ReappliesBeforeGivingUp = 3;

    /// <summary>Openings of the menu whose every try failed, after which it is left alone for the rest of the session.</summary>
    private const int MaxFailedOpenings = 3;

    /// <summary>Space kept free between the usable area and the edge of the menu's own screen.</summary>
    private const float Margin = 24f;

    /// <summary>The fewest entries that make the menu worth arranging (a menu that is still being filled has fewer).</summary>
    private const int MinEntries = 4;

    private ConfigEntry<bool> _enabled = null!;
    private ConfigEntry<PlaySide> _playSide = null!;
    private ConfigEntry<float> _gap = null!;

    private UIMainMenuContext? _view;
    private float _nextPoll, _visibleSince = -1f, _retryAt;
    private int _attempt, _failedOpenings;
    private bool _dumped, _waitingLogged;
    private Moved? _moved;

    /// <inheritdoc />
    public string Name => "MainMenu";

    /// <inheritdoc />
    public bool IsEnabled => _enabled.Value;

    /// <inheritdoc />
    /// <remarks>None: the menu is found by looking for it twice a second, not by hooking a method.</remarks>
    public IReadOnlyList<HookSpec> Hooks { get; } = Array.Empty<HookSpec>();

    /// <inheritdoc />
    public void BindConfig(ConfigFile config)
    {
        _enabled = config.Bind(Name, "Enabled", true, "Rearrange the main menu (see PlaySide). false = the game's menu as designed.");
        _playSide = config.Bind(Name, "PlaySide", PlaySide.Right,
            "Right = the Play entry at the right edge of the menu with the server browser entry on its left. Left = the game's own arrangement (nothing is changed).");
        _gap = config.Bind(Name, "Gap", 12f,
            new ConfigDescription("Space between the server browser entry and Play, in the screen's units (1080 units high).", new AcceptableValueRange<float>(0f, 120f)));
    }

    /// <inheritdoc />
    public bool Install(Harmony harmony, ManualLogSource log)
    {
        log.LogInfo($"MAINMENU_WATCHING — the main menu will be rearranged when it opens (Play side: {_playSide.Value}; no hooks needed).");
        return true;
    }

    /// <inheritdoc />
    public void Shutdown() { /* the game is closing: nothing to put back */ }

    // ---- per frame --------------------------------------------------------------------------------------------------------

    /// <inheritdoc />
    /// <remarks>Twice a second: finds the main menu when it is open, waits for it to settle, arranges it, and checks the arrangement still holds.</remarks>
    public void Update()
    {
        float now = Time.unscaledTime;
        if (now < _nextPoll) return;
        _nextPoll = now + PollSeconds;

        UIMainMenuContext? view = FindOpenMenu();
        if (view == null)
        {
            _visibleSince = -1f; // the menu is not on screen
            return;
        }
        if (_view == null || _view.Pointer != view.Pointer)
        {
            _view = view; // a new instance of the screen: whatever was done to the old one is gone with it
            _moved = null;
            _visibleSince = -1f;
        }
        if (_visibleSince < 0f)
        {
            _visibleSince = now;
            _attempt = 0;
            _retryAt = 0f;
            _waitingLogged = false;
            return;
        }
        if (now - _visibleSince < SettleSeconds || now < _retryAt || _failedOpenings >= MaxFailedOpenings) return;

        try { Maintain(view, now); }
        catch (Exception exception)
        {
            Plugin.LogSource.LogError($"MAINMENU_ERROR — {exception}");
            _failedOpenings = MaxFailedOpenings; // an exception is a bug, not a timing problem: stop touching the menu
            Undo(_moved);
            _moved = null;
            IntegrationHealth.Report(Name, "stopped after an error");
        }
    }

    private UIMainMenuContext? FindOpenMenu()
    {
        UIMainMenuContext? known = _view;
        if (known != null && known.gameObject.activeInHierarchy) return known;
        UIMainMenuContext? found = Object.FindObjectOfType<UIMainMenuContext>();
        return found != null && found.gameObject.activeInHierarchy ? found : null;
    }

    private void Maintain(UIMainMenuContext view, float now)
    {
        RectTransform? canvas = GameUi.CanvasRoot(view.transform);
        if (canvas == null) return;
        List<MenuItem> items = CollectItems(view, canvas);
        if (items.Count == 0) return;

        if (!_dumped)
        {
            _dumped = true;
            Transform root = CommonAncestor(items.Select(item => item.transform).Append(view.transform)) ?? view.transform;
            Plugin.LogSource.LogInfo($"MAINMENU_UI_DUMP — the main menu's original layout: {UiDump.Write("mainmenu", root, 7, 1500)}");
        }
        if (_playSide.Value == PlaySide.Left) return; // the game's own arrangement was asked for

        // not ready (hidden behind another screen, still being filled, sliding in): wait, without counting it as a failed try
        if (items.Count < MinEntries || FindPlay(view, items) == null)
        {
            if (!_waitingLogged)
            {
                _waitingLogged = true;
                Plugin.LogSource.LogInfo($"MAINMENU_WAITING — the main menu is not ready to be rearranged yet: {(items.Count < MinEntries ? $"only {items.Count} entries so far" : "no Play entry yet")}.");
            }
            return;
        }

        string signature = Signature(items);
        if (_moved != null && _moved.Signature == signature)
        {
            if (!Holds(_moved)) Reapply(_moved);
            return;
        }

        if (_moved != null)
        {
            Plugin.LogSource.LogInfo("MAINMENU_ENTRIES_CHANGED — the Play or server browser entry appeared or disappeared; arranging again.");
            Undo(_moved);
            _moved = null;
        }
        if (_attempt >= AttemptsPerOpening) return;
        _attempt++;
        bool last = _attempt >= AttemptsPerOpening;
        if (!Arrange(view, canvas, items, signature, _attempt, last))
        {
            if (last) _failedOpenings++;
            else _retryAt = now + _attempt; // a screen still sliding in gets a little longer each time
        }
    }

    // ---- finding the entries -------------------------------------------------------------------------------------------

    /// <summary>
    /// Every menu entry that is active and at least partly on screen (an entry the player cannot see at all is left alone). They are found
    /// by searching everything under the screen and under the container that holds the screen's own buttons (the context has one field per
    /// entry, but the server browser and Discord entries have none).
    /// </summary>
    private static List<MenuItem> CollectItems(UIMainMenuContext view, RectTransform canvas)
    {
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
        return items;
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

    /// <summary>The lowest object that holds all the given objects, or <c>null</c> for none.</summary>
    private static Transform? CommonAncestor(IEnumerable<Transform> transforms)
    {
        List<Transform> list = transforms.ToList();
        if (list.Count == 0) return null;
        for (Transform? candidate = list[0].parent ?? list[0]; candidate != null; candidate = candidate.parent)
        {
            Transform current = candidate;
            if (list.All(t => IsUnder(t, current))) return candidate;
        }
        return null;
    }

    private static bool IsUnder(Transform node, Transform ancestor)
    {
        for (Transform? t = node; t != null; t = t.parent)
            if (t.Pointer == ancestor.Pointer) return true;
        return false;
    }

    private static string KindOf(MenuItem item) => item.Type.ToString();

    /// <summary>Which of the two entries this moves are on screen, so a change in that means arranging again.</summary>
    private string Signature(List<MenuItem> items) => $"play:{FindPlay(_view!, items) != null},online:{FindOnline(items) != null}";

    // ---- arranging -------------------------------------------------------------------------------------------------------

    /// <summary>What was changed, so it can be checked and undone.</summary>
    private sealed class Moved
    {
        public string Signature = string.Empty;
        public RectTransform Canvas = null!;
        public RectTransform PlayRect = null!, PlayParent = null!;
        public Box PlayPlanned;
        public RectMemory PlayMemory = null!;
        public RectTransform? OnlineRect;
        public Box OnlinePlanned;
        public RectMemory? OnlineMemory;
        public Transform? OnlineOldParent;
        public int OnlineOldIndex;
        public RectTransform? DecorationRect;   // the thin line under Play
        public Box DecorationPlanned;
        public RectMemory? DecorationMemory;
        public List<MenuItem> Entries = new();  // every entry of the menu (navigation, overlap checks)
        public List<(Selectable Selectable, Navigation.Mode Mode)> Navigation = new();
        public int Reapplies;
    }

    private bool Arrange(UIMainMenuContext view, RectTransform canvas, List<MenuItem> items, string signature, int attempt, bool last)
    {
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
            if (playParent.gameObject.GetComponent<LayoutGroup>() != null) return (false, $"Play's parent '{playParent.gameObject.name}' is a layout group, which would undo the move");
            return (true, $"its parent is '{playParent.gameObject.name}'");
        });
        report.Check("the server browser entry", () =>
        {
            online = FindOnline(items);
            if (online == null) return (true, "not on screen; only Play moves");
            onlineRect = GameUi.RectOf(online);
            if (onlineRect == null || online.gameObject.GetComponent<Button>() == null) return (false, $"'{online.gameObject.name}' is not a button with a RectTransform");
            onlineBox = GameUi.BoxIn(canvas, onlineRect);
            return (true, $"'{online.gameObject.name}' at {Describe(onlineBox)} inside '{onlineRect.parent.gameObject.name}'");
        });
        if (report.Passed)
        {
            report.Check("the new places are free", () =>
            {
                List<Box> others = items.Where(item => item.Pointer != play!.Pointer && (online == null || item.Pointer != online.Pointer))
                    .Select(item => GameUi.BoxIn(canvas, GameUi.RectOf(item)!)).ToList();
                // the menu's right margin is where its right-hand entries (Quit, the small icons) end
                float rightEdge = others.Where(box => box.CenterX > menu.CenterX).Select(box => box.Right).DefaultIfEmpty(usable.Right).Max();
                plan = PlayPairLayout.PlaceAtRight(playBox, online == null ? null : onlineBox, rightEdge, _gap.Value, usable, others);
                bool visible = plan.Fits && ScreenGeometry.IsVisible(plan.Play, screen, GameUi.ClippersOf(canvas, playRect!))
                    && (plan.Online == null || ScreenGeometry.IsVisible(plan.Online.Value, screen, Array.Empty<Box>()));
                return (plan.Fits && visible, plan.Fits && !visible ? "the new places would be cut off" : plan.Detail);
            });
        }

        string tryNote = last ? string.Empty : $" (try {attempt} of {AttemptsPerOpening}; trying again shortly)";
        if (!report.Passed)
        {
            string failure = $"MAINMENU_CHECK_FAILED — {report.Describe()} The main menu is left as the game made it.{tryNote}";
            if (last)
            {
                Plugin.LogSource.LogError(failure);
                IntegrationHealth.Report(Name, "the checks failed");
            }
            else Plugin.LogSource.LogWarning(failure);
            return false;
        }
        Plugin.LogSource.LogInfo($"MAINMENU_CHECKS — {report.Describe()}");

        // the thin line the designer put under Play goes with it
        for (int i = 0; i < playParent!.childCount && decorationRect == null; i++)
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

        var moved = new Moved
        {
            Signature = signature, Canvas = canvas, Entries = items,
            PlayRect = playRect!, PlayParent = playParent, PlayPlanned = plan!.Play, PlayMemory = new RectMemory(playRect!),
        };
        if (onlineRect != null)
        {
            moved.OnlineRect = onlineRect;
            moved.OnlinePlanned = plan.Online!.Value;
            moved.OnlineMemory = new RectMemory(onlineRect);
            moved.OnlineOldParent = onlineRect.parent;
            moved.OnlineOldIndex = onlineRect.GetSiblingIndex();
        }
        if (decorationRect != null)
        {
            moved.DecorationRect = decorationRect;
            moved.DecorationPlanned = PlayPairLayout.MoveWith(decorationBox, playBox, plan.Play);
            moved.DecorationMemory = new RectMemory(decorationRect);
        }

        var before = $"Play {Describe(playBox)}{(online != null ? $"; {online.gameObject.name} {Describe(onlineBox)}" : string.Empty)}";
        try
        {
            Apply(moved);
            UseAutomaticNavigation(moved);

            var verified = new Preflight();
            VerifyMoved(verified, moved, screen);
            if (!verified.Passed)
            {
                string failure = $"MAINMENU_VERIFY_FAILED — the menu was rearranged but does not look right, so it was put back: {verified.Describe()}{tryNote}";
                Undo(moved);
                if (last)
                {
                    Plugin.LogSource.LogError(failure);
                    IntegrationHealth.Report(Name, "the arrangement did not verify");
                }
                else Plugin.LogSource.LogWarning(failure);
                return false;
            }

            _moved = moved;
            IntegrationHealth.Clear(Name);
            string after = $"Play {Describe(GameUi.BoxIn(canvas, moved.PlayRect))}{(moved.OnlineRect != null ? $"; {online!.gameObject.name} {Describe(GameUi.BoxIn(canvas, moved.OnlineRect))}" : string.Empty)}";
            Plugin.LogSource.LogInfo($"MAINMENU_ARRANGED — {plan.Detail}{(moved.DecorationRect != null ? $"; the line under Play ('{moved.DecorationRect.gameObject.name}') moved with it" : string.Empty)}; verified: {verified.Describe()} Before: {before}. After: {after}.");
            return true;
        }
        catch (Exception)
        {
            Undo(moved);
            throw; // reported by Update, which stops touching the menu
        }
    }

    /// <summary>Puts Play, the server browser entry and the line under Play on their planned boxes (moving the server browser entry out of the tile row first).</summary>
    private static void Apply(Moved moved)
    {
        if (moved.OnlineRect != null && moved.OnlineRect.parent.Pointer != moved.PlayParent.Pointer)
        {
            Transform oldParent = moved.OnlineRect.parent;
            moved.OnlineRect.SetParent(moved.PlayParent, false);
            moved.OnlineRect.SetSiblingIndex(moved.PlayRect.GetSiblingIndex() + 1);
            RebuildLayout(oldParent); // the tile row closes the gap
        }
        GameUi.PlaceOver(moved.PlayRect, moved.PlayParent, moved.Canvas, moved.PlayPlanned);
        if (moved.OnlineRect != null) GameUi.PlaceOver(moved.OnlineRect, moved.PlayParent, moved.Canvas, moved.OnlinePlanned);
        if (moved.DecorationRect != null) GameUi.PlaceOver(moved.DecorationRect, moved.PlayParent, moved.Canvas, moved.DecorationPlanned);
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
    private static void UseAutomaticNavigation(Moved moved)
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
    private static void VerifyMoved(Preflight report, Moved moved, Box screen)
    {
        var placed = new List<(string Name, RectTransform Rect, Box Planned)> { ("Play", moved.PlayRect, moved.PlayPlanned) };
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
            bool each = !MenuOverlap(boxes);
            return (covered == 0 && each, covered == 0 && each ? "yes" : "an entry is covered");
        });
    }

    private static bool Overlaps(Box a, Box b) =>
        !(a.Right <= b.X + 0.5f || b.Right <= a.X + 0.5f || a.Top <= b.Y + 0.5f || b.Top <= a.Y + 0.5f);

    private static bool MenuOverlap(IReadOnlyList<Box> boxes)
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

    /// <summary>True while every moved entry is still where it was put (the game may move them again: an animation, the screen being reset).</summary>
    private static bool Holds(Moved moved)
    {
        if (moved.PlayRect == null || (moved.OnlineRect == null && moved.OnlineOldParent != null)) return false;
        if (!NearPlan(GameUi.BoxIn(moved.Canvas, moved.PlayRect), moved.PlayPlanned)) return false;
        if (moved.OnlineRect != null
            && (moved.OnlineRect.parent.Pointer != moved.PlayParent.Pointer || !NearPlan(GameUi.BoxIn(moved.Canvas, moved.OnlineRect), moved.OnlinePlanned))) return false;
        if (moved.DecorationRect != null && !NearPlan(GameUi.BoxIn(moved.Canvas, moved.DecorationRect), moved.DecorationPlanned)) return false;
        return true;
    }

    /// <summary>The game moved the entries: put them back where they were planned, a few times; after that the change is undone for good.</summary>
    private void Reapply(Moved moved)
    {
        moved.Reapplies++;
        if (moved.Reapplies <= ReappliesBeforeGivingUp)
        {
            Apply(moved);
            Plugin.LogSource.LogWarning($"MAINMENU_MOVED — the game moved the menu entries; they were put back (time {moved.Reapplies} of {ReappliesBeforeGivingUp}).");
            return;
        }
        Undo(moved);
        _moved = null;
        _failedOpenings = MaxFailedOpenings;
        Plugin.LogSource.LogError("MAINMENU_OVERRIDDEN — the game keeps moving the menu entries (an animation or a reset), so the change was undone and the menu is left as designed. The original layout is in carx.modmanager.ui-mainmenu.txt.");
        IntegrationHealth.Report(Name, "the game keeps moving the entries");
    }

    private static void Undo(Moved? moved)
    {
        if (moved == null) return;
        foreach ((Selectable selectable, Navigation.Mode mode) in moved.Navigation)
        {
            if (selectable == null) continue;
            Navigation navigation = selectable.navigation;
            navigation.mode = mode;
            selectable.navigation = navigation;
        }
        moved.Navigation.Clear();

        if (moved.OnlineRect != null && moved.OnlineOldParent != null && moved.OnlineRect.parent.Pointer != moved.OnlineOldParent.Pointer)
        {
            moved.OnlineRect.SetParent(moved.OnlineOldParent, false);
            moved.OnlineRect.SetSiblingIndex(moved.OnlineOldIndex);
        }
        moved.OnlineMemory?.Restore();
        moved.PlayMemory.Restore();
        moved.DecorationMemory?.Restore();
        RebuildLayout(moved.OnlineOldParent);
    }

    private static string Describe(Box box) => $"({box.X:0},{box.Y:0},{box.Width:0}x{box.Height:0})";
}

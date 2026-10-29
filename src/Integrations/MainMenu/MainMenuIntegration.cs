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

/// <summary>How the main menu's entries are arranged (the <c>[MainMenu] Arrangement</c> setting).</summary>
internal enum MenuArrangement
{
    /// <summary>Keep the menu's direction (a row stays a row, a column a column) and tidy it.</summary>
    Tidy,
    /// <summary>One row, left to right (wrapping into more rows if it is too long).</summary>
    Row,
    /// <summary>One column, top to bottom (wrapping into more columns if it is too tall).</summary>
    Column,
}

/// <summary>
/// Tidies the main menu. The game's menu entries (Play, the server browser, Garage, Settings, ...) are placed where the screen's designer
/// put them; this puts them in reading order (Play first, the server browser right after it), the same distance apart, on one common
/// alignment, as one group where the menu was and inside the usable part of the screen (the central 16:9 area on a wider monitor). The
/// entries keep their sizes, their look and everything they do; only their positions and order change. If the entries are arranged by one
/// of the game's layout groups instead of placed by hand, the group is kept and only its order and spacing change.
/// <para>
/// This follows the same rules as the other integrations: nothing is changed unless the checks in <see cref="Arrange"/> pass (the entries
/// exist and are buttons, are positioned in a way this can change, and the new arrangement fits); the result is verified and undone if
/// it is wrong; and it keeps being verified, because the game may move its entries again (an animation, the screen being reset), in which
/// case the change is undone and the log says so. The menu's original layout is written to
/// <c>BepInEx/carx.modmanager.ui-mainmenu.txt</c> before anything is changed.
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

    /// <summary>Space kept free between the arranged menu and the edge of the usable area.</summary>
    private const float Margin = 24f;

    /// <summary>The fewest entries that make the menu worth arranging (a menu that is still being filled has fewer).</summary>
    private const int MinEntries = 4;

    private ConfigEntry<bool> _enabled = null!;
    private ConfigEntry<MenuArrangement> _arrangement = null!;
    private ConfigEntry<float> _gap = null!;

    private UIMainMenuContext? _view;
    private float _nextPoll, _visibleSince = -1f, _retryAt;
    private int _attempt, _failedOpenings;
    private bool _dumped, _waitingLogged;
    private Arranged? _arranged;

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
        _enabled = config.Bind(Name, "Enabled", true,
            "Tidy the main menu: entries in reading order (Play first, the server browser next), evenly spaced and aligned. false = the game's menu as designed.");
        _arrangement = config.Bind(Name, "Arrangement", MenuArrangement.Tidy,
            "Tidy = keep the menu's direction (row or column) and tidy it. Row or Column force one direction (wrapping into more lines if the menu is too long). Ignored if the game arranges the entries with a layout group (only its order and spacing change then).");
        _gap = config.Bind(Name, "Gap", 18f,
            new ConfigDescription("Space between neighbouring entries, in the screen's units (1080 units high).", new AcceptableValueRange<float>(2f, 120f)));
    }

    /// <inheritdoc />
    public bool Install(Harmony harmony, ManualLogSource log)
    {
        log.LogInfo("MAINMENU_WATCHING — the main menu will be tidied when it opens (no hooks needed).");
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
            _arranged = null;
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
            Undo(_arranged);
            _arranged = null;
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

        // not ready (hidden behind another screen, still being filled, sliding in): wait, without counting it as a failed try
        string? waiting = NotReadyBecause(items, canvas);
        if (waiting != null)
        {
            if (!_waitingLogged)
            {
                _waitingLogged = true;
                Plugin.LogSource.LogInfo($"MAINMENU_WAITING — the main menu is not ready to be arranged yet: {waiting}.");
            }
            return;
        }

        string signature = Signature(items);
        if (_arranged != null && _arranged.Signature == signature)
        {
            if (!Holds(_arranged)) Reapply(_arranged);
            return;
        }

        if (_arranged != null)
        {
            Plugin.LogSource.LogInfo("MAINMENU_ENTRIES_CHANGED — the set of menu entries changed; arranging again.");
            Undo(_arranged);
            _arranged = null;
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

    /// <summary>Why the menu cannot be arranged yet (too few entries, no Play entry, an entry off screen), or <c>null</c> if it can.</summary>
    private static string? NotReadyBecause(List<MenuItem> items, RectTransform canvas)
    {
        if (items.Count < MinEntries) return $"only {items.Count} entries so far";
        if (!items.Any(item => KindOf(item) == "Play")) return "no Play entry yet";
        var screen = new Box(canvas.rect.x, canvas.rect.y, canvas.rect.width, canvas.rect.height);
        string[] off = items.Where(item => !screen.Contains(GameUi.BoxIn(canvas, GameUi.RectOf(item) ?? canvas), 2f)).Select(KindOf).ToArray();
        return off.Length > 0 ? $"{string.Join(", ", off)} {(off.Length == 1 ? "is" : "are")} outside the screen (the menu may be hidden or sliding in)" : null;
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

    private static string Signature(List<MenuItem> items) => string.Join(",", items.Select(KindOf).OrderBy(kind => kind, StringComparer.Ordinal));

    // ---- arranging -------------------------------------------------------------------------------------------------------

    /// <summary>How the entries are positioned in the game.</summary>
    private enum Positioning
    {
        /// <summary>By hand: each entry has its own place, which this can set.</summary>
        ByHand,
        /// <summary>By one horizontal or vertical layout group of the game, which decides the places; only its order and spacing can change.</summary>
        LayoutGroup,
    }

    /// <summary>What was changed, so it can be checked and undone.</summary>
    private sealed class Arranged
    {
        public string Signature = string.Empty;
        public Positioning Positioning;
        public List<MenuItem> Items = new();
        public List<RectTransform> Rects = new();          // the object that is positioned for each entry
        public Box[] Planned = Array.Empty<Box>();         // by hand: where each entry should be
        public List<RectMemory> Memories = new();          // by hand: where each entry was
        public HorizontalOrVerticalLayoutGroup? Group;     // layout group: the group
        public float OldSpacing, AppliedSpacing;
        public List<int> OldSiblingIndexes = new();        // layout group: where each entry's slot was among its siblings
        public int[] Places = Array.Empty<int>();          // layout group: the sibling positions the entries occupy, in order
        public int[] ByRank = Array.Empty<int>();          // layout group: the entries (indexes) in reading order
        public List<(Selectable Selectable, Navigation.Mode Mode)> Navigation = new(); // entries whose explicit navigation was set to automatic
        public RectTransform Canvas = null!;
        public int Reapplies;
    }

    private bool Arrange(UIMainMenuContext view, RectTransform canvas, List<MenuItem> items, string signature, int attempt, bool last)
    {
        var report = new Preflight();
        Positioning positioning = Positioning.ByHand;
        HorizontalOrVerticalLayoutGroup? group = null;
        var rects = new List<RectTransform>();
        MenuPlan? plan = null;
        MenuFlow flow = MenuFlow.Column;
        var screen = new Box(canvas.rect.x, canvas.rect.y, canvas.rect.width, canvas.rect.height);
        float usableWidth = Math.Min(screen.Width, screen.Height * 16f / 9f); // on a wider monitor the central 16:9 part is where the game's screens are laid out
        var usable = new Box(screen.CenterX - usableWidth / 2f + Margin, screen.Y + Margin, usableWidth - 2f * Margin, screen.Height - 2f * Margin);

        report.Check("the screen's root", true, $"{screen.Width:0} x {screen.Height:0} units, usable {usable.Width:0} x {usable.Height:0}");
        report.Check("the menu has its entries", true, $"{items.Count} on screen: {string.Join(", ", items.Select(KindOf))}");
        report.Check("every entry is a button", () =>
        {
            string[] notButtons = items.Where(item => item.gameObject.GetComponent<Button>() == null).Select(KindOf).ToArray();
            return (notButtons.Length == 0, notButtons.Length == 0 ? "yes" : $"no button on: {string.Join(", ", notButtons)}");
        });
        report.Check("every entry can be moved", () =>
        {
            foreach (MenuItem item in items)
            {
                RectTransform? rect = GameUi.RectOf(item);
                if (rect == null) return (false, $"'{KindOf(item)}' has no RectTransform");
                if (GameUi.RectOf(rect.parent) == null) return (false, $"'{KindOf(item)}' has no UI parent");
                rects.Add(rect);
            }
            return (true, "yes");
        });
        if (report.Passed)
        {
            report.Check("how the entries are positioned", () =>
            {
                (LayoutGroup? found, bool mixed) = FindLayoutGroup(canvas, rects);
                if (mixed) return (false, "some entries are arranged by a layout group and some are not (or by different groups); not changed");
                if (found == null) return (true, "by hand");
                group = found.TryCast<HorizontalOrVerticalLayoutGroup>();
                if (group == null) return (false, "the entries are arranged by a grid layout group; not supported yet");
                positioning = Positioning.LayoutGroup;
                flow = found.TryCast<HorizontalLayoutGroup>() != null ? MenuFlow.Row : MenuFlow.Column;
                return (true, $"by the game's {(flow == MenuFlow.Row ? "horizontal" : "vertical")} layout group '{found.gameObject.name}'");
            });
        }
        if (report.Passed && positioning == Positioning.ByHand)
        {
            report.Check("the new arrangement fits", () =>
            {
                var entries = Enumerable.Range(0, items.Count).Select(i => new MenuEntry(KindOf(items[i]), GameUi.BoxIn(canvas, rects[i]))).ToList();
                flow = _arrangement.Value switch
                {
                    MenuArrangement.Row => MenuFlow.Row,
                    MenuArrangement.Column => MenuFlow.Column,
                    _ => MenuLayout.DetectFlow(entries.Select(entry => entry.Box).ToList()),
                };
                plan = MenuLayout.Plan(entries, usable, flow, _gap.Value);
                bool visible = plan.Fits && plan.Boxes.All(box => ScreenGeometry.IsVisible(box, screen, Array.Empty<Box>()));
                return (plan.Fits && visible, plan.Detail);
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

        var arranged = new Arranged { Signature = signature, Positioning = positioning, Items = items, Canvas = canvas };
        var before = items.Select((item, i) => $"{KindOf(item)} {Describe(GameUi.BoxIn(canvas, rects[i]))}").ToList();
        try
        {
            if (positioning == Positioning.ByHand) PlaceByHand(arranged, rects, plan!);
            else ArrangeInGroup(arranged, group!, items, rects);
            UseAutomaticNavigation(arranged);

            var verified = new Preflight();
            VerifyArranged(verified, arranged, screen);
            if (!verified.Passed)
            {
                string failure = $"MAINMENU_VERIFY_FAILED — the menu was rearranged but does not look right, so it was put back: {verified.Describe()}{tryNote}";
                Undo(arranged);
                if (last)
                {
                    Plugin.LogSource.LogError(failure);
                    IntegrationHealth.Report(Name, "the arrangement did not verify");
                }
                else Plugin.LogSource.LogWarning(failure);
                return false;
            }

            _arranged = arranged;
            IntegrationHealth.Clear(Name);
            var after = items.Select((item, i) => $"{KindOf(item)} {Describe(GameUi.BoxIn(canvas, GameUi.RectOf(item)!))}");
            string what = positioning == Positioning.ByHand ? plan!.Detail : "order and spacing set on the game's layout group";
            Plugin.LogSource.LogInfo($"MAINMENU_ARRANGED — {what}; verified: {verified.Describe()} Before: {string.Join("; ", before)}. After: {string.Join("; ", after)}.");
            return true;
        }
        catch (Exception)
        {
            Undo(arranged);
            throw; // reported by Update, which stops touching the menu
        }
    }

    /// <summary>
    /// The layout group that arranges the entries, if there is one: all entries must be arranged by the same single group. Only objects below
    /// the screen's root are looked at.
    /// </summary>
    private static (LayoutGroup? Group, bool Mixed) FindLayoutGroup(RectTransform canvas, List<RectTransform> rects)
    {
        LayoutGroup? first = null;
        int arranged = 0;
        foreach (RectTransform rect in rects)
        {
            LayoutGroup? group = null;
            for (Transform? t = rect.parent; t != null && t.Pointer != canvas.Pointer; t = t.parent)
            {
                group = t.gameObject.GetComponent<LayoutGroup>();
                if (group != null) break;
            }
            if (group == null) continue;
            arranged++;
            if (first == null) first = group;
            else if (first.Pointer != group.Pointer) return (null, true);
        }
        return arranged == 0 ? (null, false) : arranged == rects.Count ? (first, false) : (null, true);
    }

    /// <summary>Places every entry on its planned box, remembering where it was.</summary>
    private static void PlaceByHand(Arranged arranged, List<RectTransform> rects, MenuPlan plan)
    {
        arranged.Rects = rects;
        arranged.Planned = plan.Boxes;
        arranged.Memories = rects.Select(rect => new RectMemory(rect)).ToList();
        Apply(arranged);
    }

    /// <summary>Puts the entries where the plan says (or, for a layout group, sets the group's order and spacing again).</summary>
    private static void Apply(Arranged arranged)
    {
        if (arranged.Positioning == Positioning.LayoutGroup)
        {
            for (int k = 0; k < arranged.ByRank.Length; k++) arranged.Rects[arranged.ByRank[k]].SetSiblingIndex(arranged.Places[k]);
            arranged.Group!.spacing = arranged.AppliedSpacing;
            LayoutRebuilder.ForceRebuildLayoutImmediate(GameUi.RectOf(arranged.Group)!);
            return;
        }
        for (int i = 0; i < arranged.Rects.Count; i++)
        {
            RectTransform rect = arranged.Rects[i];
            GameUi.PlaceOver(rect, GameUi.RectOf(rect.parent)!, arranged.Canvas, arranged.Planned[i]);
        }
    }

    /// <summary>
    /// The entries are arranged by one of the game's layout groups: put them in reading order (inside the places they already occupy among
    /// their siblings) and set the group's spacing.
    /// </summary>
    private void ArrangeInGroup(Arranged arranged, HorizontalOrVerticalLayoutGroup group, List<MenuItem> items, List<RectTransform> rects)
    {
        // the group's direct children that hold the entries (an entry may sit deeper, inside a wrapper that is the group's child)
        var slots = new List<RectTransform>();
        foreach (RectTransform rect in rects)
        {
            Transform slot = rect;
            while (slot.parent != null && slot.parent.Pointer != group.transform.Pointer) slot = slot.parent;
            slots.Add(slot.gameObject.GetComponent<RectTransform>());
        }

        arranged.Group = group;
        arranged.OldSpacing = group.spacing;
        arranged.Rects = slots;
        arranged.OldSiblingIndexes = slots.Select(slot => slot.GetSiblingIndex()).ToList();
        arranged.Places = arranged.OldSiblingIndexes.OrderBy(index => index).ToArray();
        arranged.ByRank = Enumerable.Range(0, items.Count).OrderBy(i => MenuLayout.Rank(KindOf(items[i]))).ThenBy(i => arranged.OldSiblingIndexes[i]).ToArray();
        arranged.AppliedSpacing = _gap.Value;
        Apply(arranged);
    }

    /// <summary>
    /// An entry whose keyboard / gamepad neighbours were set by hand would still point at its old neighbours after the move; those entries
    /// go to automatic navigation (Unity picks the nearest entry in the pressed direction). Entries that already use automatic navigation
    /// are not touched. <see cref="Undo"/> puts the old setting back.
    /// </summary>
    private static void UseAutomaticNavigation(Arranged arranged)
    {
        foreach (MenuItem item in arranged.Items)
        {
            Selectable? selectable = item.gameObject.GetComponent<Selectable>();
            if (selectable == null) continue;
            Navigation navigation = selectable.navigation;
            if (navigation.mode != Navigation.Mode.Explicit) continue;
            arranged.Navigation.Add((selectable, navigation.mode));
            navigation.mode = Navigation.Mode.Automatic;
            selectable.navigation = navigation;
        }
    }

    // ---- checking, holding, undoing --------------------------------------------------------------------------------------

    /// <summary>Checks the result right after arranging: where the entries are, that all are fully visible, that none overlap, and (for a layout group) that they are in reading order.</summary>
    private static void VerifyArranged(Preflight report, Arranged arranged, Box screen)
    {
        List<MenuItem> items = arranged.Items;
        var boxes = new List<Box>();
        report.Check("every entry is where it was planned", () =>
        {
            for (int i = 0; i < items.Count; i++) boxes.Add(GameUi.BoxIn(arranged.Canvas, GameUi.RectOf(items[i])!));
            if (arranged.Positioning == Positioning.LayoutGroup) return (true, "the game's layout group decides the places");
            int off = Enumerable.Range(0, boxes.Count).Count(i => !NearPlan(boxes[i], arranged.Planned[i]));
            return (off == 0, off == 0 ? "yes" : $"{off} of {items.Count} entries are somewhere else");
        });
        report.Check("every entry is fully visible", () =>
        {
            int hidden = boxes.Count(box => !ScreenGeometry.IsVisible(box, screen, Array.Empty<Box>()));
            return (hidden == 0, hidden == 0 ? "yes" : $"{hidden} entries are outside the screen or have no size");
        });
        report.Check("no entry covers another", () => (!MenuLayout.AnyOverlap(boxes), "yes"));
        if (arranged.Positioning == Positioning.LayoutGroup)
        {
            report.Check("the entries read in order", () =>
            {
                MenuFlow flow = arranged.Group!.TryCast<HorizontalLayoutGroup>() != null ? MenuFlow.Row : MenuFlow.Column;
                int[] ranks = Enumerable.Range(0, items.Count)
                    .OrderBy(i => flow == MenuFlow.Row ? boxes[i].X : -boxes[i].Top)
                    .Select(i => MenuLayout.Rank(KindOf(items[i]))).ToArray();
                return (ranks.SequenceEqual(ranks.OrderBy(rank => rank)), "Play first, the server browser next");
            });
        }
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

    /// <summary>True while every entry is still where it was put (the game may move them again: an animation, the screen being reset).</summary>
    private static bool Holds(Arranged arranged)
    {
        if (arranged.Items.Any(item => item == null)) return false;
        if (arranged.Positioning == Positioning.LayoutGroup)
        {
            if (arranged.Group == null || Math.Abs(arranged.Group.spacing - arranged.AppliedSpacing) > 0.01f) return false;
            int[] ranksBySibling = Enumerable.Range(0, arranged.Rects.Count)
                .OrderBy(i => arranged.Rects[i].GetSiblingIndex())
                .Select(i => MenuLayout.Rank(KindOf(arranged.Items[i]))).ToArray();
            return ranksBySibling.SequenceEqual(ranksBySibling.OrderBy(rank => rank));
        }
        for (int i = 0; i < arranged.Items.Count; i++)
            if (!NearPlan(GameUi.BoxIn(arranged.Canvas, GameUi.RectOf(arranged.Items[i])!), arranged.Planned[i])) return false;
        return true;
    }

    /// <summary>The game moved the entries: put them back where they were planned, a few times; after that the change is undone for good.</summary>
    private void Reapply(Arranged arranged)
    {
        arranged.Reapplies++;
        if (arranged.Reapplies <= ReappliesBeforeGivingUp)
        {
            Apply(arranged);
            Plugin.LogSource.LogWarning($"MAINMENU_MOVED — the game moved the menu entries; they were put back (time {arranged.Reapplies} of {ReappliesBeforeGivingUp}).");
            return;
        }
        Undo(arranged);
        _arranged = null;
        _failedOpenings = MaxFailedOpenings;
        Plugin.LogSource.LogError("MAINMENU_OVERRIDDEN — the game keeps moving the menu entries (an animation or a reset), so the change was undone and the menu is left as designed. The original layout is in carx.modmanager.ui-mainmenu.txt.");
        IntegrationHealth.Report(Name, "the game keeps moving the entries");
    }

    private static void Undo(Arranged? arranged)
    {
        if (arranged == null) return;
        foreach ((Selectable selectable, Navigation.Mode mode) in arranged.Navigation)
        {
            if (selectable == null) continue;
            Navigation navigation = selectable.navigation;
            navigation.mode = mode;
            selectable.navigation = navigation;
        }
        arranged.Navigation.Clear();

        if (arranged.Positioning == Positioning.ByHand)
        {
            foreach (RectMemory memory in arranged.Memories) memory.Restore();
            return;
        }
        if (arranged.Group != null) arranged.Group.spacing = arranged.OldSpacing;
        foreach (int i in Enumerable.Range(0, arranged.Rects.Count).OrderBy(i => arranged.OldSiblingIndexes[i]))
            if (arranged.Rects[i] != null) arranged.Rects[i].SetSiblingIndex(arranged.OldSiblingIndexes[i]);
        if (arranged.Group != null) LayoutRebuilder.ForceRebuildLayoutImmediate(GameUi.RectOf(arranged.Group)!);
    }

    private static string Describe(Box box) => $"({box.X:0},{box.Y:0},{box.Width:0}x{box.Height:0})";
}

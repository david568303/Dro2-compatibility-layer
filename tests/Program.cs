using CarXModManager.Logic;

// A tiny test runner: each Check prints ok/FAIL, the exit code is the number of failures.
int failures = 0;
void Check(bool condition, string what)
{
    Console.WriteLine((condition ? "ok   " : "FAIL ") + what);
    if (!condition) failures++;
}

RoomRow Room(string name, string map = "Track", string config = "XDS 2000", string region = "EU", int players = 3, int max = 8, bool locked = false, int ping = 40) =>
    new() { Name = name, Map = map, Config = config, Region = region, Players = players, MaxPlayers = max, Locked = locked, Ping = ping };

string[] Names(RoomQuery query, RoomRow[] rows) => query.Apply(rows).Select(i => rows[i].Name).ToArray();

// ---- text search ----------------------------------------------------------------------------------------------
Check(TextSearch.Normalize("Café ÉCOLE") == "cafe ecole", "Normalize lower-cases and strips accents");
Check(TextSearch.SplitQuery("  ring   SANC ").SequenceEqual(new[] { "ring", "sanc" }), "SplitQuery splits on spaces and normalises");
Check(TextSearch.SplitQuery("   ").Length == 0, "a blank query has no words");
Check(TextSearch.ContainsAll("sanctuary ring", TextSearch.SplitQuery("ring sanc")), "words match in any order");
Check(!TextSearch.ContainsAll("sanctuary ring", TextSearch.SplitQuery("ring track")), "every word must be found");

// ---- room query: nothing set --------------------------------------------------------------------------------------
var rows = new[]
{
    Room("Alpha", players: 8, max: 8, map: "Zeta Hills", region: "US", ping: 120),
    Room("bravo", players: 0, max: 8, map: "Alps", region: "EU", ping: 35),
    Room("Charlie", players: 5, max: 8, locked: true, map: "Mod: Sunset", region: "EU", ping: 0),
    Room("delta", players: 2, max: 2, map: "Alps", region: "Asia", ping: 200),
};
var query = new RoomQuery();
Check(!query.ChangesList, "a fresh query changes nothing");
Check(Names(query, rows).SequenceEqual(new[] { "Alpha", "bravo", "Charlie", "delta" }), "a fresh query keeps every room in the game's order");

// ---- hiding ----------------------------------------------------------------------------------------------------------
Check(Names(new RoomQuery { HideFull = true }, rows).SequenceEqual(new[] { "bravo", "Charlie" }), "HideFull removes rooms with no free slot");
Check(Names(new RoomQuery { HideEmpty = true }, rows).SequenceEqual(new[] { "Alpha", "Charlie", "delta" }), "HideEmpty removes rooms with nobody in them");
Check(Names(new RoomQuery { HideLocked = true }, rows).SequenceEqual(new[] { "Alpha", "bravo", "delta" }), "HideLocked removes password rooms");
Check(Names(new RoomQuery { HideFull = true, HideEmpty = true, HideLocked = true }, rows).Length == 0, "all three switches together can leave nothing");
Check(Room("x", players: 3, max: 0).IsFull == false, "a room with unknown capacity is never full");

// ---- searching -------------------------------------------------------------------------------------------------------
Check(Names(new RoomQuery { Search = "alps" }, rows).SequenceEqual(new[] { "bravo", "delta" }), "search matches the map name");
Check(Names(new RoomQuery { Search = "CHARLIE" }, rows).SequenceEqual(new[] { "Charlie" }), "search ignores case");
Check(Names(new RoomQuery { Search = "alps asia" }, rows).SequenceEqual(new[] { "delta" }), "search words can come from different fields");
Check(Names(new RoomQuery { Search = "sunset" }, rows).SequenceEqual(new[] { "Charlie" }), "search finds a mod map by its display name");
Check(Names(new RoomQuery { Search = "nothing like this" }, rows).Length == 0, "a search with no match shows nothing");
Check(new RoomQuery { Search = "x" }.ChangesList, "a search counts as changing the list");

// ---- sorting -------------------------------------------------------------------------------------------------------
Check(Names(new RoomQuery { Sort = RoomSort.MostPlayers }, rows).SequenceEqual(new[] { "Alpha", "Charlie", "delta", "bravo" }), "MostPlayers: players descending");
Check(Names(new RoomQuery { Sort = RoomSort.FreeSlots }, rows).SequenceEqual(new[] { "bravo", "Charlie", "Alpha", "delta" }), "FreeSlots: the room with most free slots first, full rooms last in the game's order");
Check(Names(new RoomQuery { Sort = RoomSort.Name }, rows).SequenceEqual(new[] { "Alpha", "bravo", "Charlie", "delta" }), "Name sorts case-insensitively");
Check(Names(new RoomQuery { Sort = RoomSort.Map }, rows).SequenceEqual(new[] { "bravo", "delta", "Charlie", "Alpha" }), "Map sorts by map then name");
Check(Names(new RoomQuery { Sort = RoomSort.LowestPing }, rows).SequenceEqual(new[] { "bravo", "Alpha", "delta", "Charlie" }), "LowestPing puts unknown pings last");
Check(Names(new RoomQuery { Sort = RoomSort.Region }, rows).SequenceEqual(new[] { "delta", "bravo", "Charlie", "Alpha" }), "Region sorts by region, then ping");
var tied = new[] { Room("one", players: 4), Room("two", players: 4), Room("three", players: 4) };
Check(Names(new RoomQuery { Sort = RoomSort.MostPlayers }, tied).SequenceEqual(new[] { "one", "two", "three" }), "sorting is stable: ties keep the server's order");

// ---- filters and sort together -------------------------------------------------------------------------------------
Check(Names(new RoomQuery { HideFull = true, Sort = RoomSort.Name }, rows).SequenceEqual(new[] { "bravo", "Charlie" }), "filter then sort");
Check(new RoomQuery().Apply(Array.Empty<RoomRow>()).Length == 0, "no rooms gives no rooms");

// ---- tracks --------------------------------------------------------------------------------------------------------
TrackEntry Track(string id, string label, bool mod = false, bool fav = false) => new() { Id = id, Label = label, IsMod = mod, IsFavorite = fav };
string[] TrackIds(TrackQuery q, TrackEntry[] list) => q.Apply(list).Select(i => list[i].Id).ToArray();
var tracks = new[] { Track("t1", "Tokyo Docks"), Track("t2", "Alpine Pass", fav: true), Track("m1", "Sunset Café", mod: true), Track("m2", "Zero Gravity", mod: true, fav: true) };
Check(TrackIds(new TrackQuery(), tracks).SequenceEqual(new[] { "t2", "m2", "t1", "m1" }), "tracks: favourites first, then the game's order");
Check(TrackIds(new TrackQuery { OnlyFavorites = true }, tracks).SequenceEqual(new[] { "t2", "m2" }), "tracks: only favourites");
Check(TrackIds(new TrackQuery { OnlyMods = true }, tracks).SequenceEqual(new[] { "m2", "m1" }), "tracks: only mods (favourite first)");
Check(TrackIds(new TrackQuery { Search = "cafe" }, tracks).SequenceEqual(new[] { "m1" }), "tracks: search ignores accents");
Check(TrackIds(new TrackQuery { SortByName = true }, tracks).SequenceEqual(new[] { "t2", "m2", "m1", "t1" }), "tracks: A to Z inside each group (Sunset before Tokyo)");
Check(TrackIds(new TrackQuery { SortByName = true, OnlyMods = true }, tracks).SequenceEqual(new[] { "m2", "m1" }), "tracks: filters and A to Z together");
Check(new TrackQuery().Apply(Array.Empty<TrackEntry>()).Length == 0, "tracks: an empty list stays empty");

// ---- favourites ---------------------------------------------------------------------------------------------------
string favoritesPath = Path.Combine(Path.GetTempPath(), "qol-test-" + Guid.NewGuid().ToString("N"), "favorites.txt");
var store = new FavoriteStore(favoritesPath);
Check(store.Count == 0 && !store.Contains("a"), "favourites: a new store is empty");
Check(store.Toggle("a") && store.Contains("a"), "favourites: toggling adds");
Check(store.Toggle("b with spaces"), "favourites: ids may contain spaces");
Check(!store.Toggle("a") && !store.Contains("a"), "favourites: toggling again removes");
var reopened = new FavoriteStore(favoritesPath);
Check(reopened.Contains("b with spaces") && !reopened.Contains("a") && reopened.Count == 1, "favourites: they survive a restart");
File.WriteAllText(favoritesPath, "# comment\n\n  x  \ny\n");
var edited = new FavoriteStore(favoritesPath);
Check(edited.Contains("x") && edited.Contains("y") && edited.Count == 2, "favourites: comments and blank lines are ignored, ids are trimmed");
var broken = new FavoriteStore(Path.Combine(Path.GetTempPath(), "qol-no-such-dir-" + Guid.NewGuid().ToString("N"), "sub", "f.txt"));
Check(broken.Toggle("k") && broken.Contains("k"), "favourites: a missing folder is created");
Directory.Delete(Path.GetDirectoryName(favoritesPath)!, true);

// ---- page loading ----------------------------------------------------------------------------------------------------
{
    var loader = new PageLoader(timeoutSeconds: 3f, gapSeconds: 0.35f);
    loader.OnListed(20, 0f);
    Check(loader.Tick(0.1f, true, 30) == PageAction.Wait, "paging: waits a moment after the game listed rooms");
    Check(loader.Tick(0.4f, true, 30) == PageAction.Request, "paging: then asks for the next page");
    Check(loader.PagesRequested == 1, "paging: the request is counted");
    Check(loader.Tick(0.5f, true, 30) == PageAction.Wait, "paging: does not ask again while waiting for the page");
    loader.OnListed(40, 1.0f);
    Check(loader.Tick(1.1f, true, 30) == PageAction.Wait, "paging: a short pause after the page arrived");
    Check(loader.Tick(1.5f, true, 30) == PageAction.Request, "paging: the arrival lets the next request go out");
    Check(loader.Tick(4.0f, true, 30) == PageAction.Wait, "paging: still waiting just under the timeout");
    Check(loader.Tick(4.6f, true, 30) == PageAction.FinishedNoMoreRooms, "paging: no new rooms within the timeout means all are loaded");
    Check(loader.IsComplete && loader.Tick(9f, true, 30) == PageAction.Wait, "paging: nothing more is asked once complete");
    loader.OnListed(60, 10f);
    Check(!loader.IsComplete, "paging: a page that arrives late restarts loading");
    Check(loader.Tick(10.5f, true, 30) == PageAction.Request, "paging: and the next page is requested");
}
{
    var loader = new PageLoader();
    loader.OnListed(20, 0f);
    Check(loader.Tick(1f, false, 30) == PageAction.Wait, "paging: nothing is requested while the game cannot take a request");
    Check(loader.PagesRequested == 0, "paging: and nothing is counted");
}
{
    var loader = new PageLoader();
    loader.OnListed(20, 0f);
    Check(loader.Tick(1f, true, 2) == PageAction.Request, "paging: limit of 2, first request");
    loader.OnListed(40, 1.5f);
    Check(loader.Tick(2f, true, 2) == PageAction.Request, "paging: limit of 2, second request");
    loader.OnListed(60, 2.5f);
    Check(loader.Tick(3f, true, 2) == PageAction.FinishedPageLimit && loader.IsComplete, "paging: the page limit stops it");
}
{
    var loader = new PageLoader();
    loader.OnListed(20, 0f);
    loader.Tick(1f, true, 30);
    loader.OnListed(40, 1.5f);
    loader.Tick(2f, true, 30);
    loader.Tick(6f, true, 30); // complete
    Check(loader.IsComplete, "paging: reached the end");
    loader.OnListed(20, 7f);
    Check(!loader.IsComplete && loader.PagesRequested == 0, "paging: a shorter list (the game refreshed) starts over");
    Check(loader.Tick(7.5f, true, 30) == PageAction.Request, "paging: and loads page two again");
}
{
    var loader = new PageLoader();
    loader.OnListed(20, 0f);
    loader.Tick(1f, true, 30);
    loader.Tick(5f, true, 30);
    Check(loader.IsComplete, "paging: complete");
    loader.Restart();
    Check(!loader.IsComplete && loader.PagesRequested == 0, "paging: Restart clears a finished load");
    loader.OnListed(20, 5.1f);
    loader.Reset();
    Check(loader.Tick(5.2f, true, 30) == PageAction.Request, "paging: Reset forgets everything (no gap wait after a reset)");
}

// ---- text editing ----------------------------------------------------------------------------------------------------
Check(TextEditing.Insert("ab", 'c') == "abc", "typing: a printable character is appended");
Check(TextEditing.Insert("ab", ' ') == "ab ", "typing: a space is text too");
Check(TextEditing.Insert("ab", '\b') == "ab" && TextEditing.Insert("ab", '\r') == "ab" && TextEditing.Insert("ab", '\u007f') == "ab", "typing: control characters (Backspace, Enter, Delete) are not text");
Check(TextEditing.Insert("ab", 'é') == "abé", "typing: accented letters are accepted");
Check(TextEditing.Insert(new string('x', TextEditing.MaxLength), 'y') == new string('x', TextEditing.MaxLength), "typing: a full box takes no more");
Check(TextEditing.Backspace("abc") == "ab" && TextEditing.Backspace("") == "", "typing: Backspace removes the last character and is safe on empty text");
{
    var repeat = new KeyRepeat(delaySeconds: 0.5f, intervalSeconds: 0.1f);
    Check(repeat.Update(true, 0f) == 1, "key repeat: acts once when the key goes down");
    Check(repeat.Update(true, 0.3f) == 0, "key repeat: nothing while the delay has not passed");
    Check(repeat.Update(true, 0.5f) == 1, "key repeat: starts repeating after the delay");
    Check(repeat.Update(true, 0.55f) == 0, "key repeat: and waits for the interval");
    Check(repeat.Update(true, 0.85f) == 3, "key repeat: catches up on the repeats that were due (0.6, 0.7, 0.8)");
    Check(repeat.Update(false, 0.9f) == 0, "key repeat: stops when released");
    Check(repeat.Update(true, 1.0f) == 1, "key repeat: a new press acts at once again");
    var stalled = new KeyRepeat(0.5f, 0.1f);
    stalled.Update(true, 0f);
    Check(stalled.Update(true, 100f) <= 4, "key repeat: a long stall gives only a few catch-up repeats");
    Check(stalled.Update(true, 100.05f) == 0, "key repeat: and then resumes at the normal pace");
}
{
    var choice = new KeySourceLock();
    Check(choice.Owner == KeySource.None, "key source: none is chosen before any event");
    Check(choice.Accept(KeySource.InputSystem), "key source: the first source to report is used");
    Check(!choice.Accept(KeySource.Imgui), "key source: and the other one is then ignored (no letter is typed twice)");
    Check(choice.Accept(KeySource.InputSystem) && choice.Owner == KeySource.InputSystem, "key source: the chosen one keeps being used");
    var other = new KeySourceLock();
    Check(other.Accept(KeySource.Imgui) && !other.Accept(KeySource.InputSystem), "key source: if only IMGUI reports, IMGUI is used");
}

// ---- preflight checklist ---------------------------------------------------------------------------------------------
{
    var none = new Preflight();
    Check(none.Passed && none.FailedCount == 0 && none.Describe() == "no checks were run", "preflight: with no checks it passes and says so");

    var good = new Preflight();
    good.Check("the room list", true, "1010 x 640");
    good.Check("a button to copy the look from", () => (true, "'Find room'"));
    Check(good.Passed && good.Results.Count == 2, "preflight: all checks passed");
    Check(good.Describe() == "2 of 2 checks passed: the room list (1010 x 640); a button to copy the look from ('Find room')", "preflight: the log line lists what was found");

    var bad = new Preflight();
    bad.Check("the room list", true, "1010 x 640");
    bad.Check("the label", false, "no text under the button");
    bad.Check("the space", () => throw new InvalidOperationException("the object was destroyed"));
    Check(!bad.Passed && bad.FailedCount == 2, "preflight: a failed check, and one that threw, both fail it");
    Check(bad.Results[2] == new CheckResult("the space", false, "InvalidOperationException: the object was destroyed"), "preflight: an exception becomes the failure detail instead of escaping");
    Check(bad.Describe() == "2 of 3 checks failed: the label (no text under the button); the space (InvalidOperationException: the object was destroyed). Passed: the room list.",
        "preflight: the log line names every failed check, then the passed ones");
    Check(new Preflight().Check("x", false) == false && new Preflight().Check("y", true), "preflight: Check returns whether it passed, so checks chain");
}

// ---- problem list (what the status line shows about the mod manager itself) -----------------------------------------------
{
    var problems = new ProblemList();
    Check(!problems.Any && problems.Describe() == "" && problems.Version == 0, "problems: none to begin with, the line is unchanged");
    problems.Report("TrackPicker", "checks failed");
    problems.Report("ServerBrowser", "could not be installed");
    Check(problems.Any && problems.Describe() == "mod manager: ServerBrowser, TrackPicker not working (see log)", "problems: names are listed in order, with a pointer to the log");
    int version = problems.Version;
    problems.Report("TrackPicker", "checks failed");
    Check(problems.Version == version, "problems: reporting the same thing again changes nothing");
    problems.Report("TrackPicker", "stopped after errors");
    Check(problems.Version == version + 1 && problems.ReasonFor("TrackPicker") == "stopped after errors", "problems: a new reason is a change");
    problems.Clear("ServerBrowser");
    Check(problems.Describe() == "mod manager: TrackPicker not working (see log)" && problems.ReasonFor("ServerBrowser") == null, "problems: a part that works again disappears from the line");
    int before = problems.Version;
    problems.Clear("ServerBrowser");
    problems.Clear("TrackPicker");
    Check(problems.Version == before + 1 && !problems.Any && problems.Describe() == "", "problems: clearing something that is not listed changes nothing; when all are cleared the line is as before");
}

// ---- menu layout (the main menu's clean arrangement) --------------------------------------------------------------------
{
    Check(MenuLayout.Rank("Play") < MenuLayout.Rank("OnlineMenu") && MenuLayout.Rank("OnlineMenu") < MenuLayout.Rank("Garage")
        && MenuLayout.Rank("Garage") < MenuLayout.Rank("Quit") && MenuLayout.Rank("SomethingNew") > MenuLayout.Rank("Quit"),
        "menu: Play comes first, the server browser next, Quit last, unknown entries after all known ones");

    var inARow = new[] { new Box(0f, 0f, 100f, 40f), new Box(300f, 5f, 100f, 40f), new Box(600f, -5f, 100f, 40f) };
    var inAColumn = new[] { new Box(0f, 0f, 100f, 40f), new Box(10f, -200f, 100f, 40f), new Box(-5f, -400f, 100f, 40f) };
    Check(MenuLayout.DetectFlow(inARow) == MenuFlow.Row && MenuLayout.DetectFlow(inAColumn) == MenuFlow.Column && MenuLayout.DetectFlow(new[] { inARow[0] }) == MenuFlow.Column,
        "menu: entries spread sideways are a row, spread vertically a column, a single entry a column");

    var area = new Box(-960f, -540f, 1920f, 1080f);

    // the game's order is wrong (the server browser left of Play, Quit in the middle): the plan fixes order, gaps and alignment
    var scattered = new[]
    {
        new MenuEntry("OnlineMenu", new Box(-700f, -300f, 200f, 60f)),
        new MenuEntry("Play", new Box(-300f, -310f, 240f, 80f)),
        new MenuEntry("Quit", new Box(0f, -290f, 150f, 50f)),
        new MenuEntry("Garage", new Box(300f, -305f, 200f, 60f)),
    };
    MenuPlan row = MenuLayout.Plan(scattered, area, MenuFlow.Row, 20f);
    Check(row.Fits && row.Lines == 1 && row.Boxes.Length == 4, "menu: a row of four fits in one line");
    // reading order is Play, OnlineMenu, Garage, Quit: indices 1, 0, 3, 2
    Check(row.Boxes[1].X < row.Boxes[0].X && row.Boxes[0].X < row.Boxes[3].X && row.Boxes[3].X < row.Boxes[2].X, "menu: Play first, then the server browser, then Garage, Quit last");
    Check(Math.Abs(row.Boxes[1].Right + 20f - row.Boxes[0].X) < 0.01f && Math.Abs(row.Boxes[0].Right + 20f - row.Boxes[3].X) < 0.01f && Math.Abs(row.Boxes[3].Right + 20f - row.Boxes[2].X) < 0.01f,
        "menu: the same gap between every pair of neighbours");
    Check(row.Boxes.All(box => Math.Abs(box.CenterY - row.Boxes[0].CenterY) < 0.01f), "menu: a row shares one centre line");
    Check(row.Boxes.Select((box, i) => (box.Width, box.Height)).SequenceEqual(scattered.Select(entry => (entry.Box.Width, entry.Box.Height))), "menu: the sizes the game designed are kept");
    Check(!MenuLayout.AnyOverlap(row.Boxes) && row.Boxes.All(box => area.Contains(box)), "menu: nothing overlaps and everything is inside the area");
    Box before = MenuLayout.Union(scattered.Select(entry => entry.Box)), after = MenuLayout.Union(row.Boxes);
    Check(Math.Abs(before.CenterX - after.CenterX) < 0.01f && Math.Abs(before.CenterY - after.CenterY) < 0.01f, "menu: the group stays centred where the menu was");

    var stacked = new[]
    {
        new MenuEntry("Settings", new Box(-900f, 100f, 300f, 50f)),
        new MenuEntry("Play", new Box(-880f, -100f, 340f, 90f)),
        new MenuEntry("OnlineMenu", new Box(-905f, -400f, 280f, 50f)),
    };
    MenuPlan column = MenuLayout.Plan(stacked, area, MenuFlow.Column, 16f);
    Check(column.Fits && column.Boxes.All(box => Math.Abs(box.X - column.Boxes[0].X) < 0.01f), "menu: a column shares one left edge");
    Check(column.Boxes[1].Y > column.Boxes[2].Y && column.Boxes[2].Y > column.Boxes[0].Y, "menu: top to bottom is Play, the server browser, Settings");
    Check(Math.Abs((column.Boxes[1].Y - 16f) - column.Boxes[2].Top) < 0.01f && Math.Abs((column.Boxes[2].Y - 16f) - column.Boxes[0].Top) < 0.01f, "menu: equal gaps in a column");
    Check(!MenuLayout.AnyOverlap(column.Boxes) && column.Boxes.All(box => area.Contains(box)), "menu: the column overlaps nothing and stays inside the area");

    // too long for one line: wraps, keeping the order
    var many = Enumerable.Range(0, 6).Select(i => new MenuEntry(new[] { "Play", "OnlineMenu", "Garage", "TestDrive", "Workshop", "Store" }[i], new Box(i * 50f, 0f, 400f, 60f))).ToArray();
    MenuPlan wrapped = MenuLayout.Plan(many, new Box(0f, 0f, 1000f, 500f), MenuFlow.Row, 20f);
    Check(wrapped.Fits && wrapped.Lines == 3, "menu: six entries 400 wide do not fit one 1000-wide line and wrap into three rows of two");
    Check(wrapped.Boxes[0].Y > wrapped.Boxes[2].Y && wrapped.Boxes[2].Y > wrapped.Boxes[4].Y && Math.Abs(wrapped.Boxes[0].Y - wrapped.Boxes[1].Y) < 0.01f, "menu: wrapped rows read top to bottom, left to right");
    Check(!MenuLayout.AnyOverlap(wrapped.Boxes) && wrapped.Boxes.All(box => new Box(0f, 0f, 1000f, 500f).Contains(box)), "menu: the wrapped menu overlaps nothing and fits");

    // a slightly smaller gap saves a line
    var three = Enumerable.Range(0, 3).Select(i => new MenuEntry(new[] { "Play", "OnlineMenu", "Garage" }[i], new Box(i * 400f, 0f, 300f, 60f))).ToArray();
    MenuPlan tighter = MenuLayout.Plan(three, new Box(0f, 0f, 920f, 200f), MenuFlow.Row, 20f);
    Check(tighter.Fits && tighter.Lines == 1 && Math.Abs(tighter.Gap - 10f) < 0.01f, "menu: a smaller gap is used when that keeps the menu in one line");

    // a group at the edge is moved inside
    var atEdge = new[] { new MenuEntry("Play", new Box(-960f, 0f, 200f, 60f)), new MenuEntry("Garage", new Box(-960f, -100f, 200f, 60f)) };
    MenuPlan nudged = MenuLayout.Plan(atEdge, new Box(-936f, -516f, 1872f, 1032f), MenuFlow.Column, 20f);
    Check(nudged.Fits && nudged.Boxes.All(box => box.X >= -936f - 0.01f), "menu: a group that would stick out of the area is moved inside");

    // can not fit
    MenuPlan hopeless = MenuLayout.Plan(new[] { new MenuEntry("Play", new Box(0f, 0f, 3000f, 60f)) }, area, MenuFlow.Row, 20f);
    Check(!hopeless.Fits && hopeless.Detail.Contains("'Play'") && hopeless.Boxes.Length == 0, "menu: an entry bigger than the area is reported, not forced");
    MenuPlan tooTall = MenuLayout.Plan(Enumerable.Range(0, 30).Select(i => new MenuEntry("Play", new Box(0f, i * 10f, 200f, 100f))).ToArray(), new Box(0f, 0f, 400f, 300f), MenuFlow.Column, 10f);
    Check(!tooTall.Fits, "menu: a menu that needs more room than there is does not fit");
    Check(!MenuLayout.Plan(Array.Empty<MenuEntry>(), area, MenuFlow.Row, 10f).Fits, "menu: no entries is not a plan");

    // many random menus: never overlapping, always inside, always in reading order
    var random = new Random(7);
    bool allSound = true;
    for (int round = 0; round < 200 && allSound; round++)
    {
        int count = random.Next(2, 13);
        var kinds = new[] { "Play", "OnlineMenu", "Garage", "TestDrive", "Workshop", "Store", "Mods", "Settings", "Info", "Credits", "Discord", "Quit" };
        var entries = Enumerable.Range(0, count).Select(i => new MenuEntry(kinds[i], new Box(random.Next(-900, 700), random.Next(-500, 400), random.Next(80, 360), random.Next(30, 120)))).OrderBy(_ => random.Next()).ToArray();
        MenuFlow flow = random.Next(2) == 0 ? MenuFlow.Row : MenuFlow.Column;
        MenuPlan plan = MenuLayout.Plan(entries, area, flow, random.Next(4, 40));
        if (!plan.Fits) continue;
        allSound &= !MenuLayout.AnyOverlap(plan.Boxes) && plan.Boxes.All(box => area.Contains(box));
        // reading order: sorting the boxes by line then position gives ascending ranks
        var ranked = Enumerable.Range(0, count).OrderBy(i => flow == MenuFlow.Row ? -Math.Round(plan.Boxes[i].CenterY) : Math.Round(plan.Boxes[i].X))
            .ThenBy(i => flow == MenuFlow.Row ? plan.Boxes[i].X : -plan.Boxes[i].Top).Select(i => MenuLayout.Rank(entries[i].Kind)).ToArray();
        allSound &= ranked.SequenceEqual(ranked.OrderBy(rank => rank));
    }
    Check(allSound, "menu: 200 random menus never overlap, stay inside the area and read in order");
}

// ---- placement geometry ------------------------------------------------------------------------------------------------
{
    var screen = new Box(0f, 0f, 1920f, 1080f);
    var selector = new Box(100f, 500f, 500f, 40f);
    Box right = ScreenGeometry.Beside(selector, Side.Right, 120f, 36f, 8f);
    Check(right == new Box(608f, 502f, 120f, 36f), "geometry: a control beside the widget is centred on its height, past the gap");
    Box left = ScreenGeometry.Beside(selector, Side.Left, 120f, 36f, 8f);
    Check(left == new Box(-28f, 502f, 120f, 36f), "geometry: and on the left side likewise");
    Check(ScreenGeometry.IsVisible(right, screen, Array.Empty<Box>()), "geometry: a control on the screen is visible");
    Check(!ScreenGeometry.IsVisible(left, screen, Array.Empty<Box>()), "geometry: a control past the screen edge is not visible");
    Check(!ScreenGeometry.IsVisible(right, screen, new[] { new Box(0f, 0f, 700f, 1080f) }), "geometry: a control cut off by a mask is not visible");
    Check(ScreenGeometry.IsVisible(right, screen, new[] { new Box(0f, 0f, 740f, 1080f) }), "geometry: a control inside the mask is visible");
    Check(!ScreenGeometry.IsVisible(new Box(10f, 10f, 0f, 30f), screen, Array.Empty<Box>()), "geometry: a control with no width is not visible");
    Box inside = ScreenGeometry.InsideRightEnd(selector, 60f, 30f, 4f);
    Check(inside == new Box(536f, 505f, 60f, 30f) && selector.Contains(inside), "geometry: the inside-right-end box lies inside the widget");
}

// ---- pager (the map list, a page at a time) ------------------------------------------------------------------------------
{
    Check(Pager.PageCount(0, 8) == 1 && Pager.PageCount(8, 8) == 1 && Pager.PageCount(9, 8) == 2 && Pager.PageCount(57, 8) == 8, "pager: page counts (an empty list still has one page)");
    Check(Pager.Clamp(-3, 57, 8) == 0 && Pager.Clamp(99, 57, 8) == 7 && Pager.Clamp(4, 57, 8) == 4 && Pager.Clamp(5, 0, 8) == 0, "pager: the page number stays within the pages that exist");
    Check(Pager.FirstIndex(3, 8) == 24, "pager: first index of a page");
    Check(Pager.ItemsOnPage(0, 57, 8) == 8 && Pager.ItemsOnPage(7, 57, 8) == 1 && Pager.ItemsOnPage(0, 0, 8) == 0 && Pager.ItemsOnPage(9, 57, 8) == 0, "pager: the last page is short, a missing page is empty");
    Check(Pager.PageOf(0, 8) == 0 && Pager.PageOf(7, 8) == 0 && Pager.PageOf(8, 8) == 1 && Pager.PageOf(-1, 8) == 0, "pager: which page holds an item");
}

Console.WriteLine(failures == 0 ? "all tests passed" : $"{failures} test(s) failed");
return failures;

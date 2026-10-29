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

// ---- main menu: Play at the right, the server browser entry on its left (numbers are from the game's own layout dump) ---------
{
    var play = new Box(-920f, -327f, 401f, 100f);            // "Start", the big PLAY button at the bottom left
    var online = new Box(-920f, -450f, 76f, 114f);           // "OnlineToggle", the globe tile at the left end of the tile row
    var usable = new Box(-936f, -516f, 1872f, 1032f);        // the 1920 x 1080 menu root with a 24 unit margin
    var others = new[]
    {
        new Box(-839f, -450f, 320f, 114f), new Box(-507f, -450f, 240f, 114f), new Box(-262f, -450f, 240f, 114f), // test drive, workshop, garage
        new Box(-10f, -450f, 240f, 114f), new Box(235f, -450f, 240f, 114f),                                      // store, mods
        new Box(625f, -450f, 60f, 60f), new Box(690f, -450f, 60f, 60f), new Box(755f, -450f, 60f, 60f),          // settings, info, discord
        new Box(865f, -450f, 60f, 60f),                                                                           // quit
    };
    PairPlan plan = PlayPairLayout.PlaceAtRight(play, online, 925f, 12f, usable, others);
    Check(plan.Fits, "play pair: the real menu has room for Play and the server browser entry at the right");
    Check(plan.Play == new Box(524f, -327f, 401f, 100f), "play pair: Play keeps its size and line up with the menu's right edge (Quit's right edge)");
    Check(plan.Online == new Box(412f, -327f, 100f, 100f), "play pair: the server browser entry is a square as tall as Play, 12 units to its left, on the same line");
    Check(plan.Play.Y == play.Y && plan.Online!.Value.Y == play.Y, "play pair: both stay on the line Play was on");
    PairPlan alone = PlayPairLayout.PlaceAtRight(play, null, 925f, 12f, usable, others);
    Check(alone.Fits && alone.Online == null && alone.Play == plan.Play, "play pair: without a server browser entry only Play moves");
    PairPlan crowded = PlayPairLayout.PlaceAtRight(play, online, 925f, 12f, usable, others.Append(new Box(500f, -300f, 200f, 50f)).ToArray());
    Check(!crowded.Fits && crowded.Detail.Contains("cover another"), "play pair: a place that is taken is refused, and the reason says so");
    PairPlan outside = PlayPairLayout.PlaceAtRight(play, online, 1200f, 12f, usable, others);
    Check(!outside.Fits && outside.Detail.Contains("outside the usable area"), "play pair: a right edge outside the usable area is refused");
    PairPlan squeezed = PlayPairLayout.PlaceAtRight(play, online, 925f, 12f, new Box(480f, -516f, 456f, 1032f), Array.Empty<Box>());
    Check(!squeezed.Fits, "play pair: if the server browser entry would not fit beside Play it is refused too");

    var separator = new Box(-920f, -332f, 400f, 2f);
    Check(PlayPairLayout.IsUnderline(separator, play), "play pair: the thin line the designer put under Play is recognised");
    Check(!PlayPairLayout.IsUnderline(new Box(-920f, -400f, 400f, 2f), play) && !PlayPairLayout.IsUnderline(new Box(-920f, -332f, 400f, 40f), play)
        && !PlayPairLayout.IsUnderline(new Box(-100f, -332f, 400f, 2f), play) && !PlayPairLayout.IsUnderline(new Box(-920f, -332f, 20f, 2f), play),
        "play pair: a line far below, a thick box, a line elsewhere or a tiny mark is not an underline");
    Check(PlayPairLayout.MoveWith(separator, play, plan.Play) == new Box(524f, -332f, 400f, 2f), "play pair: the underline keeps its place relative to Play when Play moves");
}

// ---- sections of tiles (the Play menu: single player and multiplayer) -----------------------------------------------------
{
    IReadOnlyList<int> single = new[] { 0, 1, 2, 3 }, multi = new[] { 4, 5, 6, 7 };
    var sections = new[] { single, multi };
    Box[] Tiles(float w, float h) => Enumerable.Range(0, 8).Select(i => new Box(i * 10f, 0f, w, h)).ToArray();
    var original = new Box(-800f, -150f, 1600f, 300f);
    var usable = new Box(-936f, -516f, 1872f, 1032f);

    // narrow cards fit in one row at full size: sections in order, equal gaps inside a section, a wider gap between sections, titles above each section
    SectionPlan line = SectionLayout.Plan(Tiles(200f, 300f), sections, usable, original, 16f, 48f, 34f, 6f, true, SectionMode.Auto);
    Check(line.Fits && line.Mode == SectionMode.Line && line.Scale == 1f && line.Labels.Length == 2, "sections: eight narrow tiles fit one row at full size, with a title per section");
    Check(Enumerable.Range(0, 7).All(i => line.Tiles[i].X < line.Tiles[i + 1].X), "sections: tiles read left to right in the order given");
    Check(Math.Abs(line.Tiles[1].X - line.Tiles[0].Right - 16f) < 0.01f && Math.Abs(line.Tiles[4].X - line.Tiles[3].Right - 48f) < 0.01f, "sections: 16 units between tiles of a section, 48 between sections");
    Check(line.Labels[0].X == line.Tiles[0].X && Math.Abs(line.Labels[0].Right - line.Tiles[3].Right) < 0.01f && line.Labels[1].X == line.Tiles[4].X, "sections: each title spans its own section");
    Check(line.Labels[0].Y >= line.Tiles.Max(box => box.Top) && Math.Abs(line.Labels[0].Y - line.Labels[1].Y) < 0.01f, "sections: titles sit above the tiles, on one line");
    Check(line.Tiles.All(box => usable.Contains(box)) && line.Labels.All(box => usable.Contains(box)), "sections: everything is inside the free area");
    Box groupBox = MenuGroupBox(line);
    Check(Math.Abs(groupBox.CenterX - original.CenterX) < 0.01f, "sections: the group is centred where the tiles were");

    // wide cards do not fit one row: each section gets its own row
    SectionPlan stacked = SectionLayout.Plan(Tiles(400f, 300f), sections, usable, original, 16f, 48f, 34f, 6f, true, SectionMode.Auto);
    Check(stacked.Fits && stacked.Mode == SectionMode.Stacked && stacked.Scale == 1f, "sections: wide tiles go in one row per section");
    Check(stacked.Tiles[0].Y > stacked.Tiles[4].Y && Math.Abs(stacked.Tiles[0].X - stacked.Tiles[4].X) < 0.01f && Math.Abs(stacked.Tiles[0].Y - stacked.Tiles[3].Y) < 0.01f,
        "sections: the first section is above the second, rows start at the same left edge, tiles of a row share a line");
    Check(stacked.Labels[0].Top > stacked.Tiles[0].Top && stacked.Labels[1].Top > stacked.Tiles[4].Top && stacked.Labels[1].Top <= stacked.Tiles[0].Y, "sections: each title is above its own row and below the row before it");

    // too big for the area: shrunk uniformly, but never enlarged
    SectionPlan shrunk = SectionLayout.Plan(Tiles(300f, 400f), sections, new Box(-936f, -300f, 1872f, 600f), original, 16f, 48f, 34f, 6f, true, SectionMode.Auto);
    Check(shrunk.Fits && shrunk.Scale < 1f && Math.Abs(shrunk.Tiles[0].Width - 300f * shrunk.Scale) < 0.01f, "sections: tiles that do not fit are shrunk uniformly");
    Check(SectionLayout.Plan(Tiles(100f, 100f), sections, usable, original, 16f, 48f, 34f, 6f, true, SectionMode.Auto).Scale == 1f, "sections: small tiles are never enlarged");
    SectionPlan tiny = SectionLayout.Plan(Tiles(300f, 400f), sections, new Box(0f, 0f, 700f, 300f), original, 16f, 48f, 34f, 6f, true, SectionMode.Auto);
    Check(!tiny.Fits && tiny.Detail.Contains("readable minimum"), "sections: a free area that would shrink the tiles below the readable minimum is refused");

    // forced modes and no titles
    Check(SectionLayout.Plan(Tiles(200f, 300f), sections, usable, original, 16f, 48f, 34f, 6f, true, SectionMode.Stacked).Mode == SectionMode.Stacked, "sections: a forced mode is used when it fits");
    SectionPlan plain = SectionLayout.Plan(Tiles(200f, 300f), sections, usable, original, 16f, 48f, 34f, 6f, false, SectionMode.Line);
    Check(plain.Fits && plain.Labels.Length == 0, "sections: with titles off there are none");
    var oneEmpty = new[] { single, (IReadOnlyList<int>)Array.Empty<int>() };
    SectionPlan onlyOne = SectionLayout.Plan(Tiles(200f, 300f).Take(4).ToArray(), oneEmpty, usable, original, 16f, 48f, 34f, 6f, true, SectionMode.Auto);
    Check(onlyOne.Fits && onlyOne.Labels.Length == 1, "sections: an empty section is skipped, with no title");
    Check(!SectionLayout.Plan(Tiles(200f, 300f), new[] { (IReadOnlyList<int>)Array.Empty<int>() }, usable, original, 16f, 48f, 34f, 6f, true, SectionMode.Auto).Fits, "sections: nothing to arrange is not a plan");

    // the free area around the original tiles
    var header = new Box(-900f, 420f, 600f, 60f);       // above
    var description = new Box(-900f, -400f, 1000f, 80f); // below
    Box? free = SectionLayout.FreeArea(usable, original, new[] { header, description }, 10f);
    Check(free.HasValue && Math.Abs(free.Value.Top - 410f) < 0.01f && Math.Abs(free.Value.Y - (-310f)) < 0.01f && free.Value.X == usable.X && free.Value.Right == usable.Right,
        "sections: a header above and a description below cut the free area, with room kept around them");
    Box? beside = SectionLayout.FreeArea(usable, original, new[] { new Box(-936f, -100f, 100f, 200f) }, 10f);
    Check(beside.HasValue && Math.Abs(beside.Value.X - (-826f)) < 0.01f, "sections: a panel at the left side cuts the free area from the left");
    Check(SectionLayout.FreeArea(usable, original, new[] { new Box(0f, 0f, 100f, 50f) }, 10f) == null, "sections: an element inside the tiles' own region means they cannot be rearranged safely");
    Box? whole = SectionLayout.FreeArea(usable, original, Array.Empty<Box>(), 10f);
    Check(whole == usable, "sections: with nothing in the way the whole usable area is free");
    Box? kept = SectionLayout.FreeArea(new Box(0f, 0f, 100f, 100f), new Box(-50f, -50f, 300f, 300f), Array.Empty<Box>(), 10f);
    Check(kept.HasValue && kept.Value.Contains(new Box(-50f, -50f, 300f, 300f)), "sections: the free area always contains the tiles' original region");

    static Box MenuGroupBox(SectionPlan plan)
    {
        IEnumerable<Box> all = plan.Tiles.Where(box => box.HasArea).Concat(plan.Labels);
        return MenuUnion(all);
    }
    static Box MenuUnion(IEnumerable<Box> boxes)
    {
        float left = boxes.Min(b => b.X), bottom = boxes.Min(b => b.Y), right = boxes.Max(b => b.Right), top = boxes.Max(b => b.Top);
        return new Box(left, bottom, right - left, top - bottom);
    }
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

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

// ---- panel placement and dragging ----------------------------------------------------------------------------------
{
    const float W = 100f, H = 50f, SW = 1920f, SH = 1080f;
    (float X, float Y) At(PanelAnchor a, float ox, float oy) => new PanelPlacement(a, ox, oy).Position(W, H, SW, SH);
    Check(At(PanelAnchor.TopLeft, 10, 20) == (10, 20), "placement: top-left");
    Check(At(PanelAnchor.TopRight, 10, 20) == (1810, 20), "placement: top-right is measured from the right edge");
    Check(At(PanelAnchor.BottomLeft, 10, 20) == (10, 1010), "placement: bottom-left is measured from the bottom edge");
    Check(At(PanelAnchor.BottomRight, 10, 20) == (1810, 1010), "placement: bottom-right");
    Check(At(PanelAnchor.TopCenter, 30, 20) == (940, 20), "placement: top-centre offsets from the middle (positive = right)");
    Check(At(PanelAnchor.BottomCenter, 0, 20) == (910, 1010), "placement: bottom-centre");

    PanelPlacement Moved(PanelAnchor a, float ox, float oy, float dx, float dy) => new PanelPlacement(a, ox, oy).MovedBy(dx, dy, W, H, SW, SH);
    Check(Moved(PanelAnchor.TopLeft, 10, 20, 5, 7) == new PanelPlacement(PanelAnchor.TopLeft, 15, 27), "drag: top-left follows the pointer");
    Check(Moved(PanelAnchor.TopRight, 10, 20, 5, 7) == new PanelPlacement(PanelAnchor.TopRight, 5, 27), "drag: moving right shrinks the offset from the right edge");
    Check(Moved(PanelAnchor.BottomLeft, 10, 20, 5, 7) == new PanelPlacement(PanelAnchor.BottomLeft, 15, 13), "drag: moving down shrinks the offset from the bottom edge");
    Check(Moved(PanelAnchor.BottomRight, 10, 20, 5, 7) == new PanelPlacement(PanelAnchor.BottomRight, 5, 13), "drag: bottom-right");
    Check(Moved(PanelAnchor.TopCenter, 30, 20, 5, 7) == new PanelPlacement(PanelAnchor.TopCenter, 35, 27), "drag: top-centre");
    // the panel's position on screen must follow the pointer for every anchor
    foreach (PanelAnchor anchor in Enum.GetValues<PanelAnchor>())
    {
        var start = new PanelPlacement(anchor, 40, 60);
        (float x0, float y0) = start.Position(W, H, SW, SH);
        (float x1, float y1) = start.MovedBy(25, -15, W, H, SW, SH).Position(W, H, SW, SH);
        Check(x1 == x0 + 25 && y1 == y0 - 15, $"drag: the panel moves exactly with the pointer ({anchor})");
    }
    // clamping keeps the panel on screen
    var clampedLeft = Moved(PanelAnchor.TopLeft, 10, 20, -500, -500);
    Check(clampedLeft.Position(W, H, SW, SH) == (0f, 0f), "drag: cannot leave through the top-left");
    var clampedRight = Moved(PanelAnchor.TopRight, 10, 20, 5000, 5000);
    Check(clampedRight.Position(W, H, SW, SH) == (SW - W, SH - H), "drag: cannot leave through the bottom-right");
    var clampedCenter = Moved(PanelAnchor.TopCenter, 0, 20, 9999, 0);
    Check(clampedCenter.Position(W, H, SW, SH).X == SW - W, "drag: a centred panel stops at the right edge");
    var tiny = new PanelPlacement(PanelAnchor.TopLeft, 0, 0).MovedBy(10, 10, 3000f, 2000f, SW, SH);
    Check(tiny.Position(3000f, 2000f, SW, SH) == (0f, 0f), "drag: a panel bigger than the screen sits at the corner");
}

Console.WriteLine(failures == 0 ? "all tests passed" : $"{failures} test(s) failed");
return failures;

namespace CarXModManager.Logic;

/// <summary>How the server browser's rooms are ordered.</summary>
internal enum RoomSort
{
    /// <summary>The order the game's own sort header produced (ping, players or track). Nothing is reordered.</summary>
    Server,
    /// <summary>Most players first.</summary>
    MostPlayers,
    /// <summary>Most free slots first.</summary>
    FreeSlots,
    /// <summary>Room name, A to Z.</summary>
    Name,
    /// <summary>Map name, A to Z.</summary>
    Map,
    /// <summary>Server region, A to Z, lowest ping first within a region.</summary>
    Region,
    /// <summary>Lowest ping first; rooms with an unknown ping last.</summary>
    LowestPing,
}

/// <summary>The few facts about one room that searching, filtering and sorting need. Read from the game's room data once per refresh.</summary>
internal readonly struct RoomRow
{
    /// <summary>The room's name as the player sees it.</summary>
    public string Name { get; init; }
    /// <summary>The map's display name (the mod's name for a mod map).</summary>
    public string Map { get; init; }
    /// <summary>The room's configuration (race type) text.</summary>
    public string Config { get; init; }
    /// <summary>The server region the room runs on.</summary>
    public string Region { get; init; }
    /// <summary>Players in the room now.</summary>
    public int Players { get; init; }
    /// <summary>Most players the room takes. 0 means unknown.</summary>
    public int MaxPlayers { get; init; }
    /// <summary>True if the room needs a password.</summary>
    public bool Locked { get; init; }
    /// <summary>Ping in milliseconds to the room's region; 0 or less means unknown.</summary>
    public int Ping { get; init; }

    /// <summary>True if no more players fit.</summary>
    public bool IsFull => MaxPlayers > 0 && Players >= MaxPlayers;
    /// <summary>True if nobody is in the room.</summary>
    public bool IsEmpty => Players <= 0;
    /// <summary>Free slots; 0 when the capacity is unknown.</summary>
    public int FreeSlots => MaxPlayers > 0 ? Math.Max(0, MaxPlayers - Players) : 0;
}

/// <summary>
/// The player's search text, switches and sort order for the room list, and the code that applies them.
/// Pure code (no game or Unity types), covered by the test project.
/// </summary>
internal sealed class RoomQuery
{
    /// <summary>Words that must all appear in the room's name, map, configuration or region. Empty matches everything.</summary>
    public string Search { get; set; } = string.Empty;

    /// <summary>Hide rooms that have no free slot.</summary>
    public bool HideFull { get; set; }

    /// <summary>Hide rooms with nobody in them.</summary>
    public bool HideEmpty { get; set; }

    /// <summary>Hide rooms that need a password.</summary>
    public bool HideLocked { get; set; }

    /// <summary>How to order the rooms that remain.</summary>
    public RoomSort Sort { get; set; } = RoomSort.Server;

    /// <summary>True if anything here would change the list (a search, a hidden kind of room, or a re-sort).</summary>
    public bool ChangesList => !string.IsNullOrWhiteSpace(Search) || HideFull || HideEmpty || HideLocked || Sort != RoomSort.Server;

    /// <summary>
    /// Applies the query to the rooms and returns the indices of the rooms to show, in display order. Sorting is stable:
    /// rooms that compare equal keep the order the server gave them.
    /// </summary>
    /// <param name="rows">All rooms the game has loaded, in the game's order.</param>
    public int[] Apply(IReadOnlyList<RoomRow> rows)
    {
        string[] words = TextSearch.SplitQuery(Search);
        var kept = new List<int>(rows.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            RoomRow row = rows[i];
            if (HideFull && row.IsFull) continue;
            if (HideEmpty && row.IsEmpty) continue;
            if (HideLocked && row.Locked) continue;
            if (words.Length > 0 && !TextSearch.ContainsAll(TextSearch.Normalize($"{row.Name} {row.Map} {row.Config} {row.Region}"), words)) continue;
            kept.Add(i);
        }

        IOrderedEnumerable<int>? ordered = Sort switch
        {
            RoomSort.MostPlayers => kept.OrderByDescending(i => rows[i].Players).ThenByDescending(i => rows[i].FreeSlots),
            RoomSort.FreeSlots => kept.OrderByDescending(i => rows[i].FreeSlots),
            RoomSort.Name => kept.OrderBy(i => rows[i].Name, StringComparer.CurrentCultureIgnoreCase),
            RoomSort.Map => kept.OrderBy(i => rows[i].Map, StringComparer.CurrentCultureIgnoreCase).ThenBy(i => rows[i].Name, StringComparer.CurrentCultureIgnoreCase),
            RoomSort.Region => kept.OrderBy(i => rows[i].Region, StringComparer.CurrentCultureIgnoreCase).ThenBy(i => PingOrLast(rows[i])),
            RoomSort.LowestPing => kept.OrderBy(i => PingOrLast(rows[i])),
            _ => null,
        };
        return (ordered ?? kept.AsEnumerable()).ToArray();
    }

    /// <summary>The ping, or a huge value for an unknown ping so those rooms sort last.</summary>
    private static int PingOrLast(RoomRow row) => row.Ping > 0 ? row.Ping : int.MaxValue;
}

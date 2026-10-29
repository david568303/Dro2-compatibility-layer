namespace CarXModManager.Logic;

/// <summary>One track in a list the player can pick from.</summary>
internal readonly struct TrackEntry
{
    /// <summary>The id the game uses for the track (a track id, or the id of a mod map).</summary>
    public string Id { get; init; }
    /// <summary>The name the player sees.</summary>
    public string Label { get; init; }
    /// <summary>True for a mod (custom) map.</summary>
    public bool IsMod { get; init; }
    /// <summary>True if the player marked it as a favourite.</summary>
    public bool IsFavorite { get; init; }
}

/// <summary>
/// The player's search and switches for a track list, and the code that applies them. Favourites come first; within each
/// group the game's own order is kept (or A to Z when <see cref="SortByName"/> is on).
/// Pure code with no game or Unity dependency, covered by the test project.
/// </summary>
internal sealed class TrackQuery
{
    /// <summary>Words that must all appear in the track's name. Empty matches everything.</summary>
    public string Search { get; set; } = string.Empty;

    /// <summary>Show only favourites.</summary>
    public bool OnlyFavorites { get; set; }

    /// <summary>Show only mod (custom) maps.</summary>
    public bool OnlyMods { get; set; }

    /// <summary>Order each group A to Z instead of keeping the game's order.</summary>
    public bool SortByName { get; set; }

    /// <summary>Returns the indices of the tracks to show, in display order.</summary>
    /// <param name="tracks">All tracks, in the game's order.</param>
    public int[] Apply(IReadOnlyList<TrackEntry> tracks)
    {
        string[] words = TextSearch.SplitQuery(Search);
        var kept = new List<int>(tracks.Count);
        for (int i = 0; i < tracks.Count; i++)
        {
            TrackEntry track = tracks[i];
            if (OnlyFavorites && !track.IsFavorite) continue;
            if (OnlyMods && !track.IsMod) continue;
            if (words.Length > 0 && !TextSearch.ContainsAll(TextSearch.Normalize(track.Label), words)) continue;
            kept.Add(i);
        }

        IEnumerable<int> ordered = kept;
        if (SortByName) ordered = kept.OrderBy(i => tracks[i].Label, StringComparer.CurrentCultureIgnoreCase);
        return ordered.OrderByDescending(i => tracks[i].IsFavorite).ToArray(); // stable: keeps the order chosen above
    }
}

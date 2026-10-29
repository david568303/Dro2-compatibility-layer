namespace CarXModManager.Logic;

/// <summary>
/// Where the main menu's Play entry and the server browser entry go to put Play at the right edge of the menu with the server browser
/// entry directly on its left.
/// </summary>
/// <param name="Fits">Whether the new places are usable (inside the usable area and not covering another entry).</param>
/// <param name="Detail">A short description for the log, or why it does not fit.</param>
/// <param name="Play">The Play entry's new box (the same size as now).</param>
/// <param name="Online">The server browser entry's new box (a square as tall as Play), or <c>null</c> if there is no such entry on screen.</param>
internal sealed record PairPlan(bool Fits, string Detail, Box Play, Box? Online);

/// <summary>
/// Pure placement rules for the main menu's Play entry and server browser entry, covered by the test project. The caller supplies where
/// everything is now; this works out where the two go. Everything else in the menu stays where the game put it.
/// </summary>
internal static class PlayPairLayout
{
    /// <summary>Overlap (in units) that still counts as touching rather than overlapping.</summary>
    private const float OverlapTolerance = 0.5f;

    /// <summary>
    /// Puts Play against <paramref name="rightEdge"/> (same height and size as now) and the server browser entry on its left, as a square as
    /// tall as Play, <paramref name="gap"/> away and on the same line. Fails if either would leave <paramref name="usable"/> or cover one of
    /// <paramref name="others"/> (the menu's other entries).
    /// </summary>
    /// <param name="play">Where the Play entry is now.</param>
    /// <param name="online">Where the server browser entry is now, or <c>null</c> if there is none on screen.</param>
    /// <param name="rightEdge">The x the right side of Play lines up with (the menu's right margin).</param>
    /// <param name="gap">Space between the server browser entry and Play.</param>
    /// <param name="usable">Where entries may go.</param>
    /// <param name="others">Every other entry of the menu, where it is now.</param>
    public static PairPlan PlaceAtRight(Box play, Box? online, float rightEdge, float gap, Box usable, IReadOnlyList<Box> others)
    {
        var newPlay = new Box(rightEdge - play.Width, play.Y, play.Width, play.Height);
        Box? newOnline = online == null ? null : new Box(newPlay.X - gap - play.Height, play.Y, play.Height, play.Height);

        var placed = new List<(string Name, Box Box)> { ("Play", newPlay) };
        if (newOnline != null) placed.Add(("The server browser entry", newOnline.Value));

        foreach ((string name, Box box) in placed)
        {
            if (!usable.Contains(box))
                return Failure($"{name} would be outside the usable area ({box.X:0},{box.Y:0},{box.Width:0}x{box.Height:0})", newPlay, newOnline);
            if (others.Any(other => Overlaps(box, other)))
                return Failure($"{name} would cover another menu entry ({box.X:0},{box.Y:0},{box.Width:0}x{box.Height:0})", newPlay, newOnline);
        }

        string detail = newOnline == null
            ? $"Play to the right edge at x {newPlay.X:0}"
            : $"Play to the right edge at x {newPlay.X:0}, the server browser entry {gap:0} units to its left at x {newOnline.Value.X:0}";
        return new PairPlan(true, detail, newPlay, newOnline);
    }

    /// <summary>
    /// True if <paramref name="decoration"/> is a thin line right under Play (such as the separator the designer put below the Play button):
    /// at most 6 units tall, directly below it (within 12 units) and not wider than Play plus a little.
    /// </summary>
    public static bool IsUnderline(Box decoration, Box play) =>
        decoration.Height <= 6f
        && decoration.Top <= play.Y + 2f && decoration.Top >= play.Y - 12f
        && decoration.X >= play.X - 12f && decoration.Right <= play.Right + 12f
        && decoration.Width >= play.Width * 0.5f;

    /// <summary>Where a decoration goes when Play moves from <paramref name="oldPlay"/> to <paramref name="newPlay"/>: it keeps its place relative to Play.</summary>
    public static Box MoveWith(Box decoration, Box oldPlay, Box newPlay) => decoration.Moved(newPlay.X - oldPlay.X, newPlay.Y - oldPlay.Y);

    private static bool Overlaps(Box a, Box b) =>
        !(a.Right <= b.X + OverlapTolerance || b.Right <= a.X + OverlapTolerance || a.Top <= b.Y + OverlapTolerance || b.Top <= a.Y + OverlapTolerance);

    private static PairPlan Failure(string reason, Box play, Box? online) => new(false, reason, play, online);
}

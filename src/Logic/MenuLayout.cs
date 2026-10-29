namespace CarXModManager.Logic;

/// <summary>One entry of a menu: what it is (its kind, e.g. <c>Play</c>) and where it is now.</summary>
/// <param name="Kind">The entry's kind, named like the game's own menu item types.</param>
/// <param name="Box">Where the entry is now, in the coordinates of the screen's root.</param>
internal readonly record struct MenuEntry(string Kind, Box Box);

/// <summary>Whether a menu runs left to right (a row) or top to bottom (a column).</summary>
internal enum MenuFlow
{
    /// <summary>Entries side by side, left to right; a long menu wraps into further rows below.</summary>
    Row,
    /// <summary>Entries one above the other, top to bottom; a long menu wraps into further columns to the right.</summary>
    Column,
}

/// <summary>
/// A planned arrangement of a menu.
/// </summary>
/// <param name="Fits">Whether the entries could be arranged inside the area at all.</param>
/// <param name="Detail">A short description for the log: the shape of the arrangement, or why it does not fit.</param>
/// <param name="Flow">The direction the entries run in.</param>
/// <param name="Lines">How many rows (or columns) the entries are wrapped into.</param>
/// <param name="Gap">The space between neighbouring entries that was used.</param>
/// <param name="Boxes">Where each entry goes, in the same order as the entries given to <see cref="MenuLayout.Plan"/> (empty when it does not fit).</param>
internal sealed record MenuPlan(bool Fits, string Detail, MenuFlow Flow, int Lines, float Gap, Box[] Boxes);

/// <summary>
/// Works out a clean arrangement for a menu: the entries in reading order (what a player reaches for first comes first), the same
/// space between neighbours, one common alignment, and the whole group kept where the menu was and inside the usable area. Entry sizes
/// are left as the game designed them; only positions and order change. Pure code with no game or Unity dependency, covered by the
/// test project.
/// </summary>
internal static class MenuLayout
{
    /// <summary>Overlap (in units) that still counts as touching rather than overlapping.</summary>
    private const float OverlapTolerance = 0.5f;

    /// <summary>The main menu's entries in reading order, by the names of the game's menu item types.</summary>
    private static readonly string[] ReadingOrder =
    {
        "Play", "OnlineMenu", "Garage", "TestDrive", "Workshop", "Store", "Mods", "Settings", "Info", "Credits", "Discord", "Quit",
    };

    /// <summary>Where a kind of entry goes in the reading order (unknown kinds come after all known ones).</summary>
    public static int Rank(string kind)
    {
        int index = Array.IndexOf(ReadingOrder, kind);
        return index >= 0 ? index : ReadingOrder.Length;
    }

    /// <summary>
    /// Tells whether the entries are arranged mostly side by side or mostly one above the other, by how far their centres are spread
    /// along each direction. A tie (or a single entry) counts as a column.
    /// </summary>
    public static MenuFlow DetectFlow(IReadOnlyList<Box> boxes)
    {
        if (boxes.Count < 2) return MenuFlow.Column;
        float spreadX = boxes.Max(box => box.CenterX) - boxes.Min(box => box.CenterX);
        float spreadY = boxes.Max(box => box.CenterY) - boxes.Min(box => box.CenterY);
        return spreadX > spreadY ? MenuFlow.Row : MenuFlow.Column;
    }

    /// <summary>The smallest box that holds all the given boxes.</summary>
    public static Box Union(IEnumerable<Box> boxes)
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

    /// <summary>True if any two of the boxes overlap by more than a hair.</summary>
    public static bool AnyOverlap(IReadOnlyList<Box> boxes)
    {
        for (int i = 0; i < boxes.Count; i++)
        for (int j = i + 1; j < boxes.Count; j++)
        {
            Box a = boxes[i], b = boxes[j];
            bool apartX = a.Right <= b.X + OverlapTolerance || b.Right <= a.X + OverlapTolerance;
            bool apartY = a.Top <= b.Y + OverlapTolerance || b.Top <= a.Y + OverlapTolerance;
            if (!apartX && !apartY) return true;
        }
        return false;
    }

    /// <summary>
    /// Plans the arrangement. The entries are put in reading order and laid out in <paramref name="flow"/> direction with
    /// <paramref name="gap"/> between neighbours (a little less if that lets the menu fit in fewer lines); a menu that is too long for one
    /// line wraps into more. The group sits where the menu was (centred on the box that held the entries) and is moved to stay inside
    /// <paramref name="area"/>. Within a row the entries share one centre line; within a column they share one left edge.
    /// </summary>
    /// <param name="entries">The menu's entries and where they are now.</param>
    /// <param name="area">Where the menu may go (the usable part of the screen).</param>
    /// <param name="flow">The direction the entries run in.</param>
    /// <param name="gap">The space between neighbouring entries.</param>
    public static MenuPlan Plan(IReadOnlyList<MenuEntry> entries, Box area, MenuFlow flow, float gap)
    {
        if (entries.Count == 0) return Failure("there are no entries", flow);
        bool row = flow == MenuFlow.Row;
        float Main(Box box) => row ? box.Width : box.Height;
        float Cross(Box box) => row ? box.Height : box.Width;
        float areaMain = row ? area.Width : area.Height, areaCross = row ? area.Height : area.Width;

        int[] order = Enumerable.Range(0, entries.Count)
            .OrderBy(i => Rank(entries[i].Kind))
            .ThenBy(i => row ? entries[i].Box.X : -entries[i].Box.Top)
            .ThenBy(i => i)
            .ToArray();

        foreach (int i in order)
            if (Main(entries[i].Box) > areaMain || Cross(entries[i].Box) > areaCross)
                return Failure($"the '{entries[i].Kind}' entry ({entries[i].Box.Width:0} x {entries[i].Box.Height:0}) is bigger than the usable area ({area.Width:0} x {area.Height:0})", flow);

        // wrap into lines; a slightly smaller gap is used if that saves a line
        List<List<int>> lines = Wrap(order, entries, Main, areaMain, gap);
        float usedGap = gap;
        float tighter = Math.Max(2f, gap / 2f);
        if (lines.Count > 1 && tighter < gap)
        {
            List<List<int>> tighterLines = Wrap(order, entries, Main, areaMain, tighter);
            if (tighterLines.Count < lines.Count)
            {
                lines = tighterLines;
                usedGap = tighter;
            }
        }

        float[] lineCross = lines.Select(line => line.Max(i => Cross(entries[i].Box))).ToArray();
        float groupMain = lines.Max(line => line.Sum(i => Main(entries[i].Box)) + usedGap * (line.Count - 1));
        float groupCross = lineCross.Sum() + usedGap * (lines.Count - 1);
        if (groupCross > areaCross)
            return Failure($"{lines.Count} {(row ? "rows" : "columns")} need {groupCross:0} units but only {areaCross:0} are usable", flow);

        // the group sits where the menu was, kept inside the area
        Box where = Union(entries.Select(entry => entry.Box));
        float groupWidth = row ? groupMain : groupCross, groupHeight = row ? groupCross : groupMain;
        float left = Math.Clamp(where.CenterX - groupWidth / 2f, area.X, area.Right - groupWidth);
        float top = Math.Clamp(where.CenterY + groupHeight / 2f, area.Y + groupHeight, area.Top);

        var boxes = new Box[entries.Count];
        float bandStart = 0f; // distance of the current line from the group's leading cross edge (top for rows, left for columns)
        for (int line = 0; line < lines.Count; line++)
        {
            float along = 0f;
            foreach (int i in lines[line])
            {
                Box original = entries[i].Box;
                if (row)
                {
                    float bandCenter = top - bandStart - lineCross[line] / 2f;
                    boxes[i] = new Box(left + along, bandCenter - original.Height / 2f, original.Width, original.Height);
                }
                else
                {
                    boxes[i] = new Box(left + bandStart, top - along - original.Height, original.Width, original.Height);
                }
                along += Main(original) + usedGap;
            }
            bandStart += lineCross[line] + usedGap;
        }

        string shape = lines.Count == 1
            ? $"{entries.Count} entries in one {(row ? "row" : "column")}"
            : $"{entries.Count} entries in {lines.Count} {(row ? "rows" : "columns")}";
        return new MenuPlan(true, $"{shape}, {usedGap:0} units apart, {groupWidth:0} x {groupHeight:0}", flow, lines.Count, usedGap, boxes);
    }

    /// <summary>Splits the entries (already in order) into lines that each fit the available length.</summary>
    private static List<List<int>> Wrap(int[] order, IReadOnlyList<MenuEntry> entries, Func<Box, float> main, float available, float gap)
    {
        var lines = new List<List<int>>();
        var current = new List<int>();
        float length = 0f;
        foreach (int i in order)
        {
            float size = main(entries[i].Box);
            if (current.Count > 0 && length + gap + size > available)
            {
                lines.Add(current);
                current = new List<int>();
                length = 0f;
            }
            length += (current.Count > 0 ? gap : 0f) + size;
            current.Add(i);
        }
        if (current.Count > 0) lines.Add(current);
        return lines;
    }

    private static MenuPlan Failure(string reason, MenuFlow flow) => new(false, reason, flow, 0, 0f, Array.Empty<Box>());
}

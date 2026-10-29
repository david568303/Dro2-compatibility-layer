namespace CarXModManager.Logic;

/// <summary>How the tiles of a menu are arranged into titled sections (the <c>[PlayMenu] Layout</c> setting).</summary>
internal enum SectionMode
{
    /// <summary>One row with a wider gap between the sections if it fits at nearly full size, otherwise each section in its own row.</summary>
    Auto,
    /// <summary>Each section in its own row, one below the other.</summary>
    Stacked,
    /// <summary>All tiles in one row, with a wider gap between the sections.</summary>
    Line,
}

/// <summary>
/// A planned arrangement of tiles into titled sections.
/// </summary>
/// <param name="Fits">Whether the tiles could be arranged inside the area at a readable size.</param>
/// <param name="Detail">A short description for the log, or why it does not fit.</param>
/// <param name="Mode">The arrangement that was used.</param>
/// <param name="Scale">How much the tiles (and the gaps between them) were shrunk, 1 for none.</param>
/// <param name="Tiles">Where each tile goes, in the same order as the tiles given to <see cref="SectionLayout.Plan"/> (tiles in no section stay <c>default</c>).</param>
/// <param name="Labels">Where each section's title goes, in section order (empty when titles are off).</param>
internal sealed record SectionPlan(bool Fits, string Detail, SectionMode Mode, float Scale, Box[] Tiles, Box[] Labels);

/// <summary>
/// Pure placement rules for a menu made of tiles that belong to titled sections (for example the Play menu's single player modes and
/// multiplayer modes), covered by the test project. The caller supplies where the tiles are now and which section each belongs to; this
/// works out the new boxes: sections in order, the same gap between neighbouring tiles, a wider gap between sections, the whole group
/// centred where the tiles were and kept inside the free area, shrunk uniformly (never enlarged) if it would not fit.
/// </summary>
internal static class SectionLayout
{
    /// <summary>The smallest scale at which an arrangement is still accepted; smaller than this the tiles would be too small to read.</summary>
    public const float MinScale = 0.55f;

    /// <summary>In <see cref="SectionMode.Auto"/>, a single row is used if it fits at least this large.</summary>
    private const float LineIsGoodEnough = 0.9f;

    /// <summary>
    /// The part of <paramref name="usable"/> that is free around the tiles' original region: it is cut back by every obstacle (a header
    /// above, a description below, a panel beside), and always contains the original region. Returns <c>null</c> if an obstacle sits inside
    /// the original region itself (the designer arranged the tiles around it, so they cannot be rearranged safely).
    /// </summary>
    /// <param name="usable">Where anything may go.</param>
    /// <param name="original">The box that holds all the tiles now.</param>
    /// <param name="obstacles">Elements that must not be covered.</param>
    /// <param name="pad">Space to keep between the area and an obstacle.</param>
    public static Box? FreeArea(Box usable, Box original, IReadOnlyList<Box> obstacles, float pad)
    {
        float left = usable.X, right = usable.Right, bottom = usable.Y, top = usable.Top;
        foreach (Box obstacle in obstacles)
        {
            if (!obstacle.HasArea) continue;
            bool sharesRows = obstacle.Y < original.Top && obstacle.Top > original.Y;
            bool sharesColumns = obstacle.X < original.Right && obstacle.Right > original.X;
            if (sharesRows && sharesColumns) return null;
            if (sharesRows)
            {
                if (obstacle.CenterX < original.CenterX) left = Math.Max(left, obstacle.Right + pad);
                else right = Math.Min(right, obstacle.X - pad);
            }
            else if (obstacle.CenterY > original.CenterY) top = Math.Min(top, obstacle.Y - pad);
            else bottom = Math.Max(bottom, obstacle.Top + pad);
        }
        left = Math.Min(left, original.X);
        right = Math.Max(right, original.Right);
        bottom = Math.Min(bottom, original.Y);
        top = Math.Max(top, original.Top);
        return new Box(left, bottom, right - left, top - bottom);
    }

    /// <summary>
    /// Plans the arrangement.
    /// </summary>
    /// <param name="tiles">Where the tiles are now (their sizes are used; their positions only decide where the group is centred).</param>
    /// <param name="sections">For each section, in order, the indexes (into <paramref name="tiles"/>) of its tiles in the order they should appear. Empty sections are skipped.</param>
    /// <param name="area">Where the arrangement may go (see <see cref="FreeArea"/>).</param>
    /// <param name="anchor">The box the group is centred on (the box that holds all the tiles now).</param>
    /// <param name="gap">Space between neighbouring tiles.</param>
    /// <param name="groupGap">Space between sections (between the last tile of one and the first of the next, or between rows when stacked).</param>
    /// <param name="labelHeight">Height of a section's title.</param>
    /// <param name="labelGap">Space between a title and its tiles.</param>
    /// <param name="labels">Whether sections get titles (and so need room for them).</param>
    /// <param name="mode">Which arrangement to use.</param>
    public static SectionPlan Plan(IReadOnlyList<Box> tiles, IReadOnlyList<IReadOnlyList<int>> sections, Box area, Box anchor,
        float gap, float groupGap, float labelHeight, float labelGap, bool labels, SectionMode mode)
    {
        List<IReadOnlyList<int>> groups = sections.Where(section => section.Count > 0).ToList();
        if (groups.Count == 0) return Failure("there are no tiles to arrange", mode);
        float labelLine = labels ? labelHeight + labelGap : 0f;

        // unscaled size of the whole arrangement in each mode: width, height of the tiles (without titles), height of the titles
        (float Width, float Height, float Titles) Measure(SectionMode m)
        {
            if (m == SectionMode.Line)
            {
                float width = groups.Sum(group => group.Sum(i => tiles[i].Width) + gap * (group.Count - 1)) + groupGap * (groups.Count - 1);
                float height = groups.Max(group => group.Max(i => tiles[i].Height));
                return (width, height, labelLine);
            }
            float rowsWidth = groups.Max(group => group.Sum(i => tiles[i].Width) + gap * (group.Count - 1));
            float rowsHeight = groups.Sum(group => group.Max(i => tiles[i].Height)) + groupGap * (groups.Count - 1);
            return (rowsWidth, rowsHeight, labelLine * groups.Count);
        }

        float ScaleFor(SectionMode m)
        {
            (float width, float height, float titles) = Measure(m);
            if (area.Height - titles <= 0f) return 0f;
            return Math.Min(1f, Math.Min(area.Width / width, (area.Height - titles) / height));
        }

        float lineScale = ScaleFor(SectionMode.Line), stackedScale = ScaleFor(SectionMode.Stacked);
        SectionMode chosen = mode != SectionMode.Auto ? mode
            : lineScale >= LineIsGoodEnough || lineScale >= stackedScale ? SectionMode.Line : SectionMode.Stacked;
        float scale = chosen == SectionMode.Line ? lineScale : stackedScale;
        if (scale < MinScale)
            return Failure($"{chosen} would need the tiles shrunk to {scale:P0}, below the readable minimum of {MinScale:P0} (the free area is {area.Width:0} x {area.Height:0} units)", chosen);

        (float unscaledWidth, float unscaledHeight, float titlesHeight) = Measure(chosen);
        float blockWidth = unscaledWidth * scale, blockHeight = unscaledHeight * scale + titlesHeight;
        float left = Math.Clamp(anchor.CenterX - blockWidth / 2f, area.X, Math.Max(area.X, area.Right - blockWidth));
        float top = Math.Clamp(anchor.CenterY + blockHeight / 2f, Math.Min(area.Top, area.Y + blockHeight), area.Top);

        var placed = new Box[tiles.Count];
        var titles = new List<Box>();
        if (chosen == SectionMode.Line)
        {
            float bandHeight = unscaledHeight * scale;
            float bandCenter = top - titlesHeight - bandHeight / 2f;
            float x = left;
            foreach (IReadOnlyList<int> group in groups)
            {
                float groupLeft = x;
                foreach (int i in group)
                {
                    float w = tiles[i].Width * scale, h = tiles[i].Height * scale;
                    placed[i] = new Box(x, bandCenter - h / 2f, w, h);
                    x += w + gap * scale;
                }
                float groupRight = x - gap * scale;
                if (labels) titles.Add(new Box(groupLeft, top - labelHeight, groupRight - groupLeft, labelHeight));
                x = groupRight + groupGap * scale;
            }
        }
        else
        {
            float y = top;
            foreach (IReadOnlyList<int> group in groups)
            {
                float rowWidth = (group.Sum(i => tiles[i].Width) + gap * (group.Count - 1)) * scale;
                if (labels)
                {
                    titles.Add(new Box(left, y - labelHeight, rowWidth, labelHeight));
                    y -= labelLine;
                }
                float rowHeight = group.Max(i => tiles[i].Height) * scale;
                float x = left;
                foreach (int i in group)
                {
                    float w = tiles[i].Width * scale, h = tiles[i].Height * scale;
                    placed[i] = new Box(x, y - rowHeight / 2f - h / 2f, w, h);
                    x += w + gap * scale;
                }
                y -= rowHeight + groupGap * scale;
            }
        }

        string shape = chosen == SectionMode.Line
            ? $"{groups.Sum(group => group.Count)} tiles in one row, {groups.Count} sections"
            : $"{groups.Count} sections, one row each";
        return new SectionPlan(true, $"{shape}, {(scale < 1f ? $"shrunk to {scale:P0}" : "full size")}, {blockWidth:0} x {blockHeight:0} units", chosen, scale, placed, titles.ToArray());
    }

    private static SectionPlan Failure(string reason, SectionMode mode) => new(false, reason, mode, 0f, Array.Empty<Box>(), Array.Empty<Box>());
}

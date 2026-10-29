namespace CarXModManager.Logic;

/// <summary>
/// A rectangle in a UI coordinate space where Y grows upwards (Unity's UI): <c>X</c>, <c>Y</c> is the lower-left corner.
/// Used to decide where new controls go and to check that they would be visible.
/// </summary>
internal readonly record struct Box(float X, float Y, float Width, float Height)
{
    /// <summary>The right edge.</summary>
    public float Right => X + Width;

    /// <summary>The top edge.</summary>
    public float Top => Y + Height;

    /// <summary>The horizontal centre.</summary>
    public float CenterX => X + Width / 2f;

    /// <summary>The vertical centre.</summary>
    public float CenterY => Y + Height / 2f;

    /// <summary>True if the box has a positive width and height.</summary>
    public bool HasArea => Width > 0f && Height > 0f;

    /// <summary>True if <paramref name="inner"/> lies entirely inside this box (touching the edge counts), give or take <paramref name="tolerance"/>.</summary>
    public bool Contains(Box inner, float tolerance = 0.5f) =>
        inner.X >= X - tolerance && inner.Y >= Y - tolerance && inner.Right <= Right + tolerance && inner.Top <= Top + tolerance;

    /// <summary>The same box moved by the given amounts.</summary>
    public Box Moved(float dx, float dy) => this with { X = X + dx, Y = Y + dy };
}

/// <summary>The side of a widget on which a new control is placed.</summary>
internal enum Side
{
    /// <summary>To the right of the widget.</summary>
    Right,
    /// <summary>To the left of the widget.</summary>
    Left,
}

/// <summary>Where new controls go relative to widgets the game already has, and whether that place is usable.</summary>
internal static class ScreenGeometry
{
    /// <summary>
    /// The box for a control of the given size placed next to <paramref name="anchor"/>, centred on the anchor's height.
    /// </summary>
    /// <param name="anchor">The game's widget.</param>
    /// <param name="side">Which side of it.</param>
    /// <param name="width">The new control's width.</param>
    /// <param name="height">The new control's height.</param>
    /// <param name="gap">Space between the two.</param>
    public static Box Beside(Box anchor, Side side, float width, float height, float gap)
    {
        float x = side == Side.Right ? anchor.Right + gap : anchor.X - gap - width;
        return new Box(x, anchor.CenterY - height / 2f, width, height);
    }

    /// <summary>
    /// A box of the given size at the right end of <paramref name="anchor"/>, inside it, centred on its height (used when there is
    /// no room beside the widget).
    /// </summary>
    public static Box InsideRightEnd(Box anchor, float width, float height, float margin) =>
        new(anchor.Right - margin - width, anchor.CenterY - height / 2f, width, height);

    /// <summary>
    /// True if a control at <paramref name="candidate"/> would be fully visible: it has an area, lies inside the
    /// <paramref name="screen"/>, and inside every <paramref name="clippers"/> box (the rectangles of the masks that cut off
    /// whatever is drawn outside them).
    /// </summary>
    public static bool IsVisible(Box candidate, Box screen, IEnumerable<Box> clippers) =>
        candidate.HasArea && screen.Contains(candidate) && clippers.All(clipper => clipper.Contains(candidate));
}

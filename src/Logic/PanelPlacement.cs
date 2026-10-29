namespace CarXModManager.Logic;

/// <summary>Which corner or edge of the screen a panel is positioned from.</summary>
internal enum PanelAnchor
{
    /// <summary>Top-left corner.</summary>
    TopLeft,
    /// <summary>Middle of the top edge.</summary>
    TopCenter,
    /// <summary>Top-right corner.</summary>
    TopRight,
    /// <summary>Bottom-left corner.</summary>
    BottomLeft,
    /// <summary>Middle of the bottom edge.</summary>
    BottomCenter,
    /// <summary>Bottom-right corner.</summary>
    BottomRight,
}

/// <summary>
/// Where a panel sits: an anchor plus an offset in pixels away from that anchor, towards the inside of the screen (for the
/// two centre anchors the horizontal offset is from the middle, positive to the right). All sizes are in the same pixel
/// units. Pure code with no Unity types, covered by the test project.
/// </summary>
/// <param name="Anchor">The corner or edge the offset is measured from.</param>
/// <param name="OffsetX">Horizontal offset in pixels.</param>
/// <param name="OffsetY">Vertical offset in pixels.</param>
internal readonly record struct PanelPlacement(PanelAnchor Anchor, float OffsetX, float OffsetY)
{
    private bool IsLeft => Anchor is PanelAnchor.TopLeft or PanelAnchor.BottomLeft;
    private bool IsRight => Anchor is PanelAnchor.TopRight or PanelAnchor.BottomRight;
    private bool IsTop => Anchor is PanelAnchor.TopLeft or PanelAnchor.TopCenter or PanelAnchor.TopRight;

    /// <summary>The panel's top-left corner for a screen of the given size.</summary>
    public (float X, float Y) Position(float width, float height, float screenWidth, float screenHeight)
    {
        float x = IsLeft ? OffsetX : IsRight ? screenWidth - width - OffsetX : (screenWidth - width) / 2f + OffsetX;
        float y = IsTop ? OffsetY : screenHeight - height - OffsetY;
        return (x, y);
    }

    /// <summary>
    /// A copy moved by (<paramref name="dx"/>, <paramref name="dy"/>) screen pixels (positive = right / down), keeping the
    /// anchor and clamped so the panel stays fully on the screen.
    /// </summary>
    public PanelPlacement MovedBy(float dx, float dy, float width, float height, float screenWidth, float screenHeight)
    {
        PanelPlacement moved = this with { OffsetX = OffsetX + (IsRight ? -dx : dx), OffsetY = OffsetY + (IsTop ? dy : -dy) };
        (float x, float y) = moved.Position(width, height, screenWidth, screenHeight);

        // keep it on screen: clamp the corner position and convert back to offsets
        x = Math.Clamp(x, 0f, Math.Max(0f, screenWidth - width));
        y = Math.Clamp(y, 0f, Math.Max(0f, screenHeight - height));
        float offsetX = IsLeft ? x : IsRight ? screenWidth - width - x : x - (screenWidth - width) / 2f;
        float offsetY = IsTop ? y : screenHeight - height - y;
        return moved with { OffsetX = offsetX, OffsetY = offsetY };
    }
}

using CarXModManager.Logic;
using UnityEngine;

namespace CarXModManager.Overlay;

/// <summary>
/// One floating panel of an integration. The overlay host draws its background, positions it and keeps the game from reacting
/// while the player uses it; the panel only draws its own controls.
/// </summary>
internal interface IOverlayPanel
{
    /// <summary>True while the panel should be on screen (for example, only while a particular game screen is open).</summary>
    bool IsVisible { get; }

    /// <summary>Where the panel sits. Set by the host when the player drags the panel by its title bar; implementations remember it.</summary>
    PanelPlacement Placement { get; set; }

    /// <summary>The panel's size in pixels (at 1080p; the host scales it to the real resolution).</summary>
    Vector2 Size { get; }

    /// <summary>Called every frame while the game runs, visible or not, for work that should not wait for drawing.</summary>
    void Update();

    /// <summary>
    /// Draws the controls inside the panel (an area of <see cref="Size"/> minus a small border). Every <c>Begin</c> must be
    /// matched by an <c>End</c>.
    /// </summary>
    /// <returns><c>true</c> while one of the panel's text boxes has keyboard focus.</returns>
    bool Draw();
}

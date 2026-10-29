namespace CarXModManager.Integrations;

/// <summary>
/// What the Play menu integration has learned about whether the server browser can be opened from the Play menu, for the main menu
/// integration to read: the main menu's own server browser tile is only removed while the Play menu is known (or not yet known) to offer
/// the same thing, so removing a shortcut can never leave the player without a way to the server browser.
/// </summary>
internal static class ServerBrowserAccess
{
    /// <summary>
    /// <c>true</c> if the Play menu was seen with an active multiplayer lobby tile (which opens the server browser), <c>false</c> if it was
    /// seen without one, <c>null</c> if the Play menu has not been seen yet this session.
    /// </summary>
    public static bool? PlayMenuHasLobbyTile { get; set; }
}

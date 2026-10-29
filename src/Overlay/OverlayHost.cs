using CarXModManager.Logic;
using UnityEngine;

namespace CarXModManager.Overlay;

/// <summary>
/// Draws the integrations' panels and ties them to the game's input. Integrations register their <see cref="IOverlayPanel"/>s here;
/// <see cref="OverlayBehaviour"/> calls <see cref="OnGui"/> and <see cref="Update"/> every frame.
/// If drawing keeps failing, the overlay switches itself off for the session so it can never disturb the game.
/// </summary>
internal static class OverlayHost
{
    /// <summary>Space between a panel's edge and its controls, in pixels.</summary>
    private const float Border = 8f;

    /// <summary>Drawing errors after which the whole overlay is switched off until the game restarts.</summary>
    private const int ErrorsBeforeDisabling = 5;

    /// <summary>Height of the title bar at the top of a panel (the part that can be dragged), including the border, in pixels.</summary>
    private const float TitleBarHeight = 8f + 28f;

    private static readonly List<IOverlayPanel> Panels = new();
    private static IOverlayPanel? _dragging;
    private static Vector2 _dragLastMouse;
    private static bool _loggedFirstDraw;
    private static bool _typingInPanel;
    private static int _errors;

    /// <summary>Adds a panel. Panels are drawn in the order they were added.</summary>
    public static void Register(IOverlayPanel panel) => Panels.Add(panel);

    /// <summary>Per-frame work for every panel (from <c>MonoBehaviour.Update</c>).</summary>
    public static void Update()
    {
        if (_errors >= ErrorsBeforeDisabling) return;
        foreach (IOverlayPanel panel in Panels) panel.Update();
    }

    /// <summary>Draws every visible panel and holds the game's input back while the player uses one (from <c>MonoBehaviour.OnGUI</c>).</summary>
    public static void OnGui()
    {
        if (_errors >= ErrorsBeforeDisabling) return;
        FlatTheme.EnsureInit();
        if (!_loggedFirstDraw)
        {
            _loggedFirstDraw = true;
            Plugin.LogSource.LogInfo($"OVERLAY_FIRST_DRAW — IMGUI is running ({(FlatTheme.Ready ? "flat theme" : "plain look")}). Unavailable IMGUI calls so far: {PanelGui.UnavailableSummary}.");
        }

        float scale = Math.Max(0.75f, Screen.height / 1080f);
        float screenWidth = Screen.width / scale, screenHeight = Screen.height / scale;
        Matrix4x4 previousMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
        try
        {
            Event? current = Event.current;
            Vector2 mouse = current != null ? current.mousePosition : default;
            EventType eventType = current != null ? current.type : EventType.Ignore;
            bool clicked = eventType == EventType.MouseDown;
            bool pointerOverPanel = false, typing = false, anyVisible = false;
            if (eventType == EventType.MouseDown) _dragging = null; // a new press: any drag whose release was missed is over

            foreach (IOverlayPanel panel in Panels)
            {
                if (!panel.IsVisible) continue;
                anyVisible = true;
                Rect rect = RectOf(panel, screenWidth, screenHeight);
                if (rect.Contains(mouse)) pointerOverPanel = true;

                // drag by the title bar: press on it, move with the button down, release
                if (eventType == EventType.MouseDown && current!.button == 0 && mouse.y < rect.y + TitleBarHeight && rect.Contains(mouse))
                {
                    _dragging = panel;
                    _dragLastMouse = mouse;
                    current.Use();
                }
                else if (_dragging == panel && eventType == EventType.MouseDrag)
                {
                    // the movement is worked out from the pointer position, which is already in the scaled GUI space the panels use
                    panel.Placement = panel.Placement.MovedBy(mouse.x - _dragLastMouse.x, mouse.y - _dragLastMouse.y, panel.Size.x, panel.Size.y, screenWidth, screenHeight);
                    _dragLastMouse = mouse;
                    rect = RectOf(panel, screenWidth, screenHeight);
                    current!.Use();
                }
                else if (_dragging == panel && eventType == EventType.MouseUp)
                {
                    _dragging = null;
                    current!.Use();
                }
                if (_dragging == panel) pointerOverPanel = true;
                try
                {
                    PanelGui.Background(rect);
                    PanelGui.BeginArea(new Rect(rect.x + Border, rect.y + Border, rect.width - 2f * Border, rect.height - 2f * Border));
                    typing |= panel.Draw();
                    PanelGui.EndArea();
                }
                catch (Exception exception)
                {
                    PanelGui.CloseOpenGroups();
                    ReportError(exception);
                }
            }

            // a click anywhere else ends text entry, so the game does not stay locked after the player moves on
            if (clicked && !pointerOverPanel && typing)
            {
                PanelGui.ClearFocus();
                typing = false;
            }

            _typingInPanel = typing;
            InputGuard.SetHolding(anyVisible && (pointerOverPanel || typing));
        }
        finally
        {
            PanelGui.CloseOpenGroups();
            GUI.matrix = previousMatrix;
        }
    }

    /// <summary>The panel's rectangle on a screen of the given (scaled) size.</summary>
    private static Rect RectOf(IOverlayPanel panel, float screenWidth, float screenHeight)
    {
        (float x, float y) = panel.Placement.Position(panel.Size.x, panel.Size.y, screenWidth, screenHeight);
        return new Rect(x, y, panel.Size.x, panel.Size.y);
    }

    /// <summary>Counts a drawing or update error, logs the first few, and switches the overlay off when there are too many.</summary>
    public static void ReportError(Exception exception)
    {
        _errors++;
        if (_errors <= 3) Plugin.LogSource.LogWarning($"OVERLAY_ERROR — {exception}");
        if (_errors == ErrorsBeforeDisabling)
        {
            Plugin.LogSource.LogError("OVERLAY_DISABLED — too many errors; the integrations' panels are off for this session. The game itself is unaffected.");
            Shutdown();
        }
    }

    /// <summary>Lets the game's input through again. Called when the overlay is destroyed and when the game quits.</summary>
    public static void Shutdown()
    {
        _typingInPanel = false;
        _dragging = null;
        InputGuard.SetHolding(false);
    }

    /// <summary>True while a panel's text box has keyboard focus.</summary>
    public static bool IsTyping => _typingInPanel;
}

using UnityEngine;

namespace CarXModManager.Overlay;

/// <summary>
/// Flat, square, dark "mod menu" styling for the integrations' panels. Everything is built from 1x1 coloured textures and
/// borderless styles, so there are no rounded corners. If any of the styling calls turn out to be stripped from the
/// game build, the whole theme is dropped (logged once as <c>OVERLAY_THEME_UNAVAILABLE</c>) and the plain default IMGUI look
/// is used instead, so the panels still work.
/// </summary>
internal static class FlatTheme
{
    /// <summary>Accent colour: active buttons and the panel's title bar.</summary>
    public static readonly Color Accent = new(1f, 0.35f, 0.21f, 1f);

    /// <summary>Colour of secondary text (counts, hints).</summary>
    public static readonly Color Muted = new(0.62f, 0.62f, 0.68f, 1f);

    private static readonly Color PanelColor = new(0.075f, 0.075f, 0.09f, 0.97f);
    private static readonly Color FieldColor = new(0.045f, 0.045f, 0.055f, 1f);
    private static readonly Color ButtonColor = new(0.17f, 0.17f, 0.21f, 1f);
    private static readonly Color ButtonHoverColor = new(0.24f, 0.24f, 0.30f, 1f);
    private static readonly Color ButtonDownColor = new(0.31f, 0.31f, 0.39f, 1f);
    private static readonly Color AccentHoverColor = new(1f, 0.46f, 0.33f, 1f);
    private static readonly Color AccentDownColor = new(0.85f, 0.27f, 0.15f, 1f);
    private static readonly Color TextColor = new(0.93f, 0.93f, 0.95f, 1f);

    private static bool _tried;

    /// <summary>Panel background.</summary>
    public static GUIStyle? Panel { get; private set; }
    /// <summary>The accent-coloured bar at the top of a panel.</summary>
    public static GUIStyle? Title { get; private set; }
    /// <summary>A normal button.</summary>
    public static GUIStyle? Button { get; private set; }
    /// <summary>A button that is switched on (accent colour).</summary>
    public static GUIStyle? ButtonOn { get; private set; }
    /// <summary>A single-line text box.</summary>
    public static GUIStyle? Field { get; private set; }
    /// <summary>Plain text.</summary>
    public static GUIStyle? Text { get; private set; }
    /// <summary>Secondary (greyed) text.</summary>
    public static GUIStyle? MutedText { get; private set; }

    /// <summary>True when every style was built; false means the plain default look is in use.</summary>
    public static bool Ready => Panel != null && Title != null && Button != null && ButtonOn != null && Field != null && Text != null && MutedText != null;

    /// <summary>Builds the styles on first use. Must be called from inside <c>OnGUI</c> (Unity only allows reading <c>GUI.skin</c> there).</summary>
    public static void EnsureInit()
    {
        if (_tried) return;
        _tried = true;
        try
        {
            GUIStyle label = GUI.skin.label;
            GUIStyle button = GUI.skin.button;
            GUIStyle textField = GUI.skin.textField;

            Texture2D panelTexture = Texture(PanelColor), fieldTexture = Texture(FieldColor);
            Texture2D normal = Texture(ButtonColor), hover = Texture(ButtonHoverColor), down = Texture(ButtonDownColor);
            Texture2D accent = Texture(Accent), accentHover = Texture(AccentHoverColor), accentDown = Texture(AccentDownColor);

            GUIStyle panel = Make(label, panelTexture, panelTexture, panelTexture, TextColor, 0, 0, 0, 0, 0f);
            GUIStyle title = Make(label, accent, accent, accent, TextColor, 10, 10, 4, 4, 24f);
            GUIStyle normalButton = Make(button, normal, hover, down, TextColor, 8, 8, 3, 3, 24f);
            GUIStyle onButton = Make(button, accent, accentHover, accentDown, TextColor, 8, 8, 3, 3, 24f);
            GUIStyle field = Make(textField, fieldTexture, fieldTexture, fieldTexture, TextColor, 8, 8, 4, 4, 26f);
            GUIStyle text = Make(label, null, null, null, TextColor, 2, 2, 1, 1, 0f);
            GUIStyle muted = Make(label, null, null, null, Muted, 2, 2, 1, 1, 0f);

            Panel = panel; Title = title; Button = normalButton; ButtonOn = onButton; Field = field; Text = text; MutedText = muted;
        }
        catch (Exception exception)
        {
            Panel = Title = Button = ButtonOn = Field = Text = MutedText = null;
            Plugin.LogSource.LogWarning($"OVERLAY_THEME_UNAVAILABLE — using the plain default look. {exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>A 1x1 texture of one colour. Kept alive across scene changes and unused-asset clean-ups.</summary>
    private static Texture2D Texture(Color color)
    {
        var texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, color);
        texture.Apply();
        texture.hideFlags = HideFlags.HideAndDontSave;
        return texture;
    }

    /// <summary>Copies <paramref name="template"/> and makes it flat: no border, the given backgrounds and padding, one text colour.</summary>
    private static GUIStyle Make(GUIStyle template, Texture2D? normal, Texture2D? hover, Texture2D? active, Color textColor,
        int padLeft, int padRight, int padTop, int padBottom, float fixedHeight)
    {
        var style = new GUIStyle(template);
        style.border = new RectOffset(0, 0, 0, 0);
        style.margin = new RectOffset(2, 2, 2, 2);
        style.padding = new RectOffset(padLeft, padRight, padTop, padBottom);
        SetState(style.normal, normal, textColor);
        SetState(style.hover, hover, textColor);
        SetState(style.active, active, textColor);
        SetState(style.focused, normal, textColor);
        if (fixedHeight > 0f) style.fixedHeight = fixedHeight;
        return style;
    }

    private static void SetState(GUIStyleState state, Texture2D? background, Color textColor)
    {
        state.background = background!;
        state.textColor = textColor;
    }
}

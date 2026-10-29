using UnityEngine;

namespace CarXModManager;

/// <summary>
/// Flat, square, dark "mod menu" styling. Everything is built from 1x1 coloured textures and borderless styles,
/// so there are no rounded corners. If any of the styling calls turn out to be stripped from this game build the
/// whole theme is dropped (logged once as CARXMM_THEME_UNAVAILABLE) and the plain default look is used instead.
/// </summary>
internal static class Theme
{
    public static readonly Color Accent = new(1f, 0.35f, 0.21f, 1f);
    private static readonly Color PanelBg = new(0.075f, 0.075f, 0.09f, 0.97f);
    private static readonly Color CardBg = new(0.115f, 0.115f, 0.14f, 1f);
    private static readonly Color ButtonBg = new(0.17f, 0.17f, 0.21f, 1f);
    private static readonly Color ButtonHover = new(0.24f, 0.24f, 0.30f, 1f);
    private static readonly Color ButtonDown = new(0.31f, 0.31f, 0.39f, 1f);
    private static readonly Color AccentHover = new(1f, 0.46f, 0.33f, 1f);
    private static readonly Color AccentDown = new(0.85f, 0.27f, 0.15f, 1f);
    private static readonly Color White = new(0.93f, 0.93f, 0.95f, 1f);

    private static bool _tried;

    public static GUIStyle? Panel { get; private set; }
    public static GUIStyle? Title { get; private set; }
    public static GUIStyle? Tab { get; private set; }
    public static GUIStyle? TabActive { get; private set; }
    public static GUIStyle? Card { get; private set; }
    public static GUIStyle? Text { get; private set; }

    public static bool Ready => Panel != null && Title != null && Tab != null && TabActive != null && Card != null && Text != null;

    public static void EnsureInit()
    {
        if (_tried) return;
        _tried = true;
        try
        {
            GUIStyle label = GUI.skin.label;
            GUIStyle button = GUI.skin.button;

            Texture2D panelTex = Tex(PanelBg), cardTex = Tex(CardBg);
            Texture2D btn = Tex(ButtonBg), btnHover = Tex(ButtonHover), btnDown = Tex(ButtonDown);
            Texture2D acc = Tex(Accent), accHover = Tex(AccentHover), accDown = Tex(AccentDown);

            GUIStyle panel = Make(label, panelTex, panelTex, panelTex, 10, 10, 8, 8, 0f);
            GUIStyle title = Make(label, acc, acc, acc, 10, 10, 5, 5, 28f);
            GUIStyle tab = Make(button, btn, btnHover, btnDown, 8, 8, 4, 4, 26f);
            GUIStyle tabActive = Make(button, acc, accHover, accDown, 8, 8, 4, 4, 26f);
            GUIStyle card = Make(label, cardTex, cardTex, cardTex, 8, 8, 6, 6, 0f);
            GUIStyle text = Make(label, null, null, null, 2, 2, 1, 1, 0f);

            Panel = panel; Title = title; Tab = tab; TabActive = tabActive; Card = card; Text = text;
        }
        catch (Exception exception)
        {
            Panel = Title = Tab = TabActive = Card = Text = null;
            Plugin.Logger.LogWarning($"CARXMM_THEME_UNAVAILABLE — using the plain default look. {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static Texture2D Tex(Color color)
    {
        var texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, color);
        texture.Apply();
        texture.hideFlags = HideFlags.HideAndDontSave; // survive scene changes and unused-asset cleanup
        return texture;
    }

    private static GUIStyle Make(GUIStyle template, Texture2D? normal, Texture2D? hover, Texture2D? active,
        int padLeft, int padRight, int padTop, int padBottom, float fixedHeight)
    {
        var style = new GUIStyle(template);
        style.border = new RectOffset(0, 0, 0, 0);
        style.margin = new RectOffset(2, 2, 2, 2);
        style.padding = new RectOffset(padLeft, padRight, padTop, padBottom);
        SetState(style.normal, normal);
        SetState(style.hover, hover);
        SetState(style.active, active);
        if (fixedHeight > 0f) style.fixedHeight = fixedHeight;
        return style;
    }

    private static void SetState(GUIStyleState state, Texture2D? background)
    {
        state.background = background!;
        state.textColor = White;
    }
}

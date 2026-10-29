using CarXModManager.Logic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CarXModManager.Ui;

/// <summary>
/// How one of the game's own buttons looks (its background, tint behaviour and label font), captured so that the buttons the mod
/// manager adds to a screen match the buttons the game has there.
/// </summary>
internal sealed class ButtonLook
{
    /// <summary>The button's background picture, or <c>null</c> for a flat colour.</summary>
    public Sprite? Sprite;
    /// <summary>How the background picture is stretched (simple, sliced frame, ...).</summary>
    public Image.Type ImageType = Image.Type.Simple;
    /// <summary>The background colour.</summary>
    public Color Color = new(0.17f, 0.17f, 0.21f, 1f);
    /// <summary>The background colour of a switched-on button: the normal colour pulled towards the accent colour.</summary>
    public Color OnColor => Color.Lerp(Color, new Color(1f, 0.42f, 0.2f, 1f), 0.7f);
    /// <summary>How the button reacts to hover and press (colour tint, picture swap, ...).</summary>
    public Selectable.Transition Transition = Selectable.Transition.ColorTint;
    /// <summary>The tint colours used when <see cref="Transition"/> is a colour tint; <c>null</c> keeps Unity's defaults.</summary>
    public ColorBlock? Colors;
    /// <summary>The pictures used when <see cref="Transition"/> is a picture swap.</summary>
    public SpriteState? SpriteState;
    /// <summary>The label font, or <c>null</c> for the project default.</summary>
    public TMP_FontAsset? Font;
    /// <summary>The label font's material (outline, glow, ...), or <c>null</c> for the font's own.</summary>
    public Material? FontMaterial;
    /// <summary>The largest label size; labels shrink to fit their button.</summary>
    public float FontSize = 22f;
    /// <summary>The label colour.</summary>
    public Color TextColor = Color.white;
    /// <summary>The height of the button the look was copied from, in the screen's units.</summary>
    public float Height = 36f;
    /// <summary>Where the look came from, for the log ("the 'Find room' button").</summary>
    public string Source = "built-in defaults";
}

/// <summary>A button the mod manager added to a screen: the button, its label and its background, kept together.</summary>
internal sealed class UiButton
{
    private readonly ButtonLook _look;
    private string _text = string.Empty;
    private bool? _on;

    internal UiButton(Button button, TMP_Text label, Image? background, ButtonLook look)
    {
        Button = button;
        Label = label;
        Background = background;
        _look = look;
    }

    /// <summary>The Unity button.</summary>
    public Button Button { get; }

    /// <summary>The text on the button.</summary>
    public TMP_Text Label { get; }

    /// <summary>The button's background picture, if it has one.</summary>
    public Image? Background { get; }

    /// <summary>The object that holds the button (hidden by <see cref="SetVisible"/>).</summary>
    public GameObject Object => Button.gameObject;

    /// <summary>Sets the label text; does nothing if it is already that text (so calling this every frame costs nothing).</summary>
    public void SetText(string text)
    {
        if (text == _text) return;
        _text = text;
        Label.text = text;
    }

    /// <summary>Shows the button in its switched-on look (accent colour) or its normal look.</summary>
    public void SetOn(bool on)
    {
        if (_on == on) return;
        _on = on;
        if (Background != null) Background.color = on ? _look.OnColor : _look.Color;
    }

    /// <summary>Shows or hides the button.</summary>
    public void SetVisible(bool visible)
    {
        if (Object.activeSelf != visible) Object.SetActive(visible);
    }
}

/// <summary>
/// Building blocks for putting controls into the game's own screens. Everything here creates ordinary Unity UI objects (the same
/// Image / Button / TextMeshPro the game's screens are made of) inside the game's own canvas, so the controls scale, sort,
/// click and hide with the screen like the game's own. Nothing is drawn over the game.
/// </summary>
internal static class GameUi
{
    // ---- reading what the game already has --------------------------------------------------------------------------------

    /// <summary>The <see cref="RectTransform"/> of a component's object, or <c>null</c> if it has none (a plain Transform).</summary>
    public static RectTransform? RectOf(Component? component) => component == null ? null : component.gameObject.GetComponent<RectTransform>();

    /// <summary>
    /// The outermost <see cref="RectTransform"/> above <paramref name="start"/> (the root of its canvas): the whole screen, in the
    /// units the screen's controls use. <c>null</c> if <paramref name="start"/> is not part of a UI hierarchy.
    /// </summary>
    public static RectTransform? CanvasRoot(Transform start)
    {
        RectTransform? top = null;
        for (Transform? t = start; t != null; t = t.parent)
        {
            RectTransform? rect = t.gameObject.GetComponent<RectTransform>();
            if (rect != null) top = rect;
        }
        return top;
    }

    /// <summary>The box of <paramref name="target"/> in the coordinates of <paramref name="space"/> (so boxes of different widgets can be compared).</summary>
    public static Box BoxIn(RectTransform space, RectTransform target)
    {
        var corners = new Il2CppStructArray<Vector3>(4L);
        target.GetWorldCorners(corners);
        Vector3 lowerLeft = space.InverseTransformPoint(corners[0]);
        Vector3 upperRight = space.InverseTransformPoint(corners[2]);
        return new Box(Math.Min(lowerLeft.x, upperRight.x), Math.Min(lowerLeft.y, upperRight.y),
            Math.Abs(upperRight.x - lowerLeft.x), Math.Abs(upperRight.y - lowerLeft.y));
    }

    /// <summary>The boxes of the masks above <paramref name="target"/> that cut off whatever is drawn outside them.</summary>
    public static List<Box> ClippersOf(RectTransform space, Transform target)
    {
        var boxes = new List<Box>();
        foreach (RectMask2D mask in target.gameObject.GetComponentsInParent<RectMask2D>(false)) boxes.Add(BoxIn(space, mask.rectTransform));
        foreach (Mask mask in target.gameObject.GetComponentsInParent<Mask>(false)) boxes.Add(BoxIn(space, mask.rectTransform));
        return boxes;
    }

    /// <summary>The first of the given buttons that exists and has a TextMeshPro text (a plain text button to copy the look from), or <c>null</c>.</summary>
    public static Button? FirstWithText(params Button?[] candidates)
    {
        foreach (Button? candidate in candidates)
            if (candidate != null && candidate.gameObject.GetComponentInChildren<TMP_Text>(true) != null) return candidate;
        return null;
    }

    /// <summary>
    /// Copies the look of a button the game already has on the screen: background, tint behaviour and label font. Anything the
    /// button does not have is left at the defaults, so this never fails because a button is plain.
    /// </summary>
    public static ButtonLook CaptureLook(Button reference)
    {
        var look = new ButtonLook { Source = $"the '{reference.gameObject.name}' button" };

        Image? image = reference.targetGraphic != null ? reference.targetGraphic.TryCast<Image>() : null;
        image ??= reference.gameObject.GetComponent<Image>();
        if (image != null)
        {
            look.Sprite = image.sprite;
            look.ImageType = image.type;
            look.Color = image.color;
        }

        look.Transition = reference.transition;
        look.Colors = reference.colors;
        look.SpriteState = reference.spriteState;

        TMP_Text? label = reference.gameObject.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            look.Font = label.font;
            look.FontMaterial = label.fontSharedMaterial;
            look.TextColor = label.color;
            look.FontSize = label.enableAutoSizing ? label.fontSizeMax : label.fontSize;
        }

        RectTransform? rect = RectOf(reference);
        if (rect != null && rect.rect.height > 1f) look.Height = rect.rect.height;
        return look;
    }

    // ---- creating objects ---------------------------------------------------------------------------------------------------

    /// <summary>Creates an empty UI object (a <see cref="RectTransform"/>) as a child of <paramref name="parent"/>, on the parent's layer, at normal scale.</summary>
    public static RectTransform CreateRect(string name, Transform parent)
    {
        var go = new GameObject(name) { layer = parent.gameObject.layer };
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        return rect;
    }

    /// <summary>
    /// Makes <paramref name="rect"/> cover a part of its parent, given as fractions of the parent's width and height (0 to 1,
    /// measured from the lower left) with <paramref name="gap"/> units of space around it. Because the placement is by
    /// fractions, it keeps working if the parent is resized.
    /// </summary>
    public static void Cell(RectTransform rect, float left, float right, float bottom, float top, float gap)
    {
        rect.anchorMin = new Vector2(left, bottom);
        rect.anchorMax = new Vector2(right, top);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(gap / 2f, gap / 2f);
        rect.offsetMax = new Vector2(-gap / 2f, -gap / 2f);
    }

    /// <summary>Makes <paramref name="rect"/> fill its parent, inset by the given amounts.</summary>
    public static void Fill(RectTransform rect, float insetX = 0f, float insetY = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(insetX, insetY);
        rect.offsetMax = new Vector2(-insetX, -insetY);
    }

    /// <summary>
    /// Positions <paramref name="rect"/> (a child of <paramref name="parent"/>) over <paramref name="box"/>, which is given in
    /// the coordinates of <paramref name="space"/>. The object ends up with a centre anchor and pivot, whatever it had before (a
    /// <see cref="RectMemory"/> taken beforehand puts that back).
    /// </summary>
    public static void PlaceOver(RectTransform rect, RectTransform parent, RectTransform space, Box box)
    {
        Vector3 a = parent.InverseTransformPoint(space.TransformPoint(new Vector3(box.X, box.Y, 0f)));
        Vector3 b = parent.InverseTransformPoint(space.TransformPoint(new Vector3(box.Right, box.Top, 0f)));
        float width = Math.Abs(b.x - a.x), height = Math.Abs(b.y - a.y);
        Rect parentRect = parent.rect;
        // the object's own scale multiplies its size, so the size to set is the wanted size divided by it
        Vector3 scale = rect.localScale;
        float scaleX = Math.Abs(scale.x) > 0.0001f ? Math.Abs(scale.x) : 1f, scaleY = Math.Abs(scale.y) > 0.0001f ? Math.Abs(scale.y) : 1f;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(width / scaleX, height / scaleY);
        rect.anchoredPosition = new Vector2(Math.Min(a.x, b.x) + width / 2f - parentRect.center.x, Math.Min(a.y, b.y) + height / 2f - parentRect.center.y);
    }

    /// <summary>Creates a plain coloured rectangle (a background or a dimming layer).</summary>
    public static Image CreateBox(string name, Transform parent, Color color, bool blocksClicks)
    {
        RectTransform rect = CreateRect(name, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = blocksClicks;
        return image;
    }

    /// <summary>Creates a text that is not a button, in the font of <paramref name="look"/>.</summary>
    public static TMP_Text CreateText(string name, Transform parent, ButtonLook look, string text, TextAlignmentOptions alignment, Color? color = null)
    {
        RectTransform rect = CreateRect(name, parent);
        TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        StyleText(label, look, alignment, color ?? look.TextColor);
        label.text = text;
        label.raycastTarget = false;
        return label;
    }

    /// <summary>Creates a button that looks like the one <paramref name="look"/> was copied from. It has no click action yet; see <see cref="OnClick"/>.</summary>
    public static UiButton CreateButton(string name, Transform parent, ButtonLook look, string text)
    {
        RectTransform rect = CreateRect(name, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        if (look.Sprite != null) image.sprite = look.Sprite;
        image.type = look.ImageType;
        image.color = look.Color;

        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        // an Animator-driven button has no Animator on our copy: use the plain colour tint so hovering and pressing still show
        button.transition = look.Transition == Selectable.Transition.Animation ? Selectable.Transition.ColorTint : look.Transition;
        if (look.Colors.HasValue && look.Transition != Selectable.Transition.Animation) button.colors = look.Colors.Value;
        if (look.SpriteState != null) Optional($"copy the picture swap of {look.Source}", () => button.spriteState = look.SpriteState);
        // keep the controls out of the game's keyboard/gamepad navigation: pressing a direction must not jump onto them
        Optional("take the button out of the navigation", () =>
        {
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
        });

        RectTransform labelRect = CreateRect("Label", rect);
        Fill(labelRect, 6f, 2f);
        TextMeshProUGUI label = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
        StyleText(label, look, TextAlignmentOptions.Center, look.TextColor);
        label.text = text;
        label.raycastTarget = false;
        return new UiButton(button, label, image, look);
    }

    /// <summary>Makes a button run <paramref name="action"/> when clicked. Exceptions in the action are logged, not thrown into the game.</summary>
    public static void OnClick(UiButton button, string what, Action action)
    {
        button.Button.onClick.AddListener(new Action(() =>
        {
            try { action(); }
            catch (Exception exception) { Plugin.LogSource.LogError($"UI_CLICK_FAILED — '{what}': {exception}"); }
        }));
    }

    /// <summary>
    /// Makes a plain picture (for example a dimming layer behind a popup) clickable without any visual reaction. Exceptions in the
    /// action are logged, not thrown into the game.
    /// </summary>
    public static void AddClickArea(Image image, string what, Action action)
    {
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.None;
        Optional("take the click area out of the navigation", () =>
        {
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
        });
        button.onClick.AddListener(new Action(() =>
        {
            try { action(); }
            catch (Exception exception) { Plugin.LogSource.LogError($"UI_CLICK_FAILED — '{what}': {exception}"); }
        }));
    }

    private static void StyleText(TMP_Text text, ButtonLook look, TextAlignmentOptions alignment, Color color)
    {
        if (look.Font != null) text.font = look.Font;
        if (look.FontMaterial != null) text.fontSharedMaterial = look.FontMaterial;
        text.color = color;
        text.alignment = alignment;
        text.enableAutoSizing = true;
        text.fontSizeMax = look.FontSize;
        text.fontSizeMin = 10f;
        text.fontSize = look.FontSize;
        Optional("stop the label from wrapping", () => text.textWrappingMode = TextWrappingModes.NoWrap);
        Optional("shorten long labels with dots", () => text.overflowMode = TextOverflowModes.Ellipsis);
    }

    private static readonly HashSet<string> SkippedSteps = new();

    /// <summary>
    /// Runs a step that only polishes the result (it is not needed for the control to work). If this game build does not support
    /// it, that is logged once and the control is built without it.
    /// </summary>
    private static void Optional(string step, Action action)
    {
        try { action(); }
        catch (Exception exception)
        {
            if (SkippedSteps.Add(step))
                Plugin.LogSource.LogWarning($"UI_STEP_SKIPPED — could not {step}; continuing without it. {exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>Destroys an object the mod manager created (does nothing if it is already gone).</summary>
    public static void Destroy(GameObject? gameObject)
    {
        if (gameObject != null) Object.Destroy(gameObject);
    }

    // ---- the dry run ----------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Builds a button and a text in an inactive, throw-away object and checks they hold what was put in, then destroys it. This
    /// proves, before a real screen is touched, that this game build can create and style the Unity UI objects the mod manager needs.
    /// </summary>
    /// <param name="look">The look to try (the real one, so a font that cannot be applied shows up here too).</param>
    /// <returns>Whether it worked, and either what was tried or the step that failed.</returns>
    public static (bool Passed, string Detail) DryRun(ButtonLook look)
    {
        string step = "create an object";
        GameObject? probe = null;
        try
        {
            probe = new GameObject("CarXModManager.DryRun");
            probe.SetActive(false);
            step = "give it a RectTransform";
            RectTransform root = probe.AddComponent<RectTransform>();
            step = "create a button";
            UiButton button = CreateButton("Button", root, look, "probe");
            step = "read the button's text back";
            if (button.Label.text != "probe") return (false, $"the button's text came back as '{button.Label.text}'");
            step = "make the button clickable";
            int clicks = 0;
            OnClick(button, "dry run", () => clicks++);
            step = "create a text";
            TMP_Text text = CreateText("Text", root, look, "probe", TextAlignmentOptions.Left);
            step = "read the text back";
            if (text.text != "probe") return (false, $"the text came back as '{text.text}'");
            step = "place the button";
            Cell(button.Object.GetComponent<RectTransform>(), 0f, 0.5f, 0f, 1f, 4f);
            return (true, $"a button and a text with the look of {look.Source}");
        }
        catch (Exception exception)
        {
            return (false, $"could not {step}: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            GameUi.Destroy(probe);
        }
    }
}

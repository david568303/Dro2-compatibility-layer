using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace CarXModManager;

/// <summary>
/// Thin IMGUI helpers for a game that never uses IMGUI, so IL2CPP may have stripped parts of it
/// (Il2CppInterop reports "Method unstripping failed" or a missing method for those).
/// <list type="bullet">
/// <item>Every call is guarded: the first time one is unavailable it is logged once and from then on skipped,
/// with a fallback where one exists (styled controls fall back to plain ones, Toggle becomes a button, Slider
/// becomes - / + buttons).</item>
/// <item>Every call passes an explicit (empty) options array, because the interop assemblies do not reliably keep
/// GUILayout's "params".</item>
/// <item>Begin/End calls are tracked so a group left open by an exception can be closed at the end of the frame;
/// IMGUI breaks the whole frame ("GUIClips") otherwise.</item>
/// </list>
/// </summary>
internal static class Ui
{
    private static readonly Stack<(char Kind, bool Active)> OpenGroups = new();
    private static readonly HashSet<string> Missing = new();
    private static readonly Color Rule = new(0.5f, 0.5f, 0.5f, 0.6f);
    private static Il2CppReferenceArray<GUILayoutOption>? _none;
    private static Il2CppReferenceArray<GUILayoutOption> None => _none ??= new Il2CppReferenceArray<GUILayoutOption>(0L);

    public static string MissingSummary => Missing.Count == 0 ? "none" : string.Join(", ", Missing.OrderBy(name => name));

    // ---- guarded primitives -------------------------------------------------------------------------------

    private static bool IsUnavailable(Exception exception) =>
        exception is NotSupportedException or MissingMemberException or TypeLoadException or EntryPointNotFoundException;

    private static void MarkMissing(string name, Exception exception)
    {
        if (Missing.Add(name))
            Plugin.Logger.LogWarning($"CARXMM_UI_UNAVAILABLE — {name}: {exception.GetType().Name}: {exception.Message}");
    }

    private static void Run(string name, Action call)
    {
        if (Missing.Contains(name)) return;
        try { call(); }
        catch (Exception exception) when (IsUnavailable(exception)) { MarkMissing(name, exception); }
    }

    private static bool TryRun<T>(string name, Func<T> call, out T result)
    {
        result = default!;
        if (Missing.Contains(name)) return false;
        try { result = call(); return true; }
        catch (Exception exception) when (IsUnavailable(exception)) { MarkMissing(name, exception); return false; }
    }

    /// <summary>Runs a call that needs the theme; false means "use the plain version instead".</summary>
    private static bool Styled(string name, Action call)
    {
        if (!Theme.Ready || Missing.Contains(name)) return false;
        try { call(); return true; }
        catch (Exception exception) when (IsUnavailable(exception)) { MarkMissing(name, exception); return false; }
    }

    // ---- controls -----------------------------------------------------------------------------------------

    public static void Label(string text)
    {
        if (Styled("LabelStyled", () => GUILayout.Label(text, Theme.Text!, None))) return;
        Run("Label", () => GUILayout.Label(text, None));
    }

    public static void Label(string text, Color color)
    {
        Color previous = GUI.contentColor;
        GUI.contentColor = color;
        try { Label(text); }
        finally { GUI.contentColor = previous; }
    }

    /// <summary>The big accent-coloured bar at the top of the window.</summary>
    public static void Title(string text)
    {
        if (Styled("TitleStyled", () => GUILayout.Label(text, Theme.Title!, None))) return;
        Label(text);
    }

    public static bool Button(string text, bool active = false)
    {
        bool clicked = false;
        if (Styled("ButtonStyled", () => clicked = GUILayout.Button(text, active ? Theme.TabActive! : Theme.Tab!, None))) return clicked;
        return TryRun("Button", () => GUILayout.Button(text, None), out bool plain) && plain;
    }

    public static bool Toggle(bool value, string text)
    {
        if (Theme.Ready) return Button($"{text}: {(value ? "ON" : "OFF")}", value) ? !value : value;
        if (TryRun("Toggle", () => GUILayout.Toggle(value, text, None), out bool result)) return result;
        return Button($"[{(value ? "x" : " ")}] {text}") ? !value : value;
    }

    public static float Slider(float value, float min, float max)
    {
        if (!Theme.Ready && TryRun("Slider", () => GUILayout.HorizontalSlider(value, min, max, None), out float result)) return result;
        float step = (max - min) / 10f;
        BeginHorizontal();
        if (Button("-")) value -= step;
        if (Button("+")) value += step;
        EndHorizontal();
        return Mathf.Min(max, Mathf.Max(min, value));
    }

    public static void Space(float pixels) => Run("Space", () => GUILayout.Space(pixels));
    public static void Flex() => Run("Flex", () => GUILayout.FlexibleSpace());

    // ---- groups -------------------------------------------------------------------------------------------

    private static void BeginGroup(char kind, string name, Action begin)
    {
        bool active = false;
        if (!Missing.Contains(name))
        {
            try { begin(); active = true; }
            catch (Exception exception) when (IsUnavailable(exception)) { MarkMissing(name, exception); }
        }
        OpenGroups.Push((kind, active));
    }

    private static void EndGroup(char kind, string name, Action end)
    {
        bool active = true;
        if (OpenGroups.Count > 0 && OpenGroups.Peek().Kind == kind) active = OpenGroups.Pop().Active;
        if (active) Run(name, end);
    }

    public static Vector2 BeginScroll(Vector2 position)
    {
        Vector2 result = position;
        BeginGroup('S', "BeginScroll", () => result = GUILayout.BeginScrollView(position, None));
        return result;
    }

    public static void EndScroll() => EndGroup('S', "EndScroll", () => GUILayout.EndScrollView());

    public static void BeginVertical() => BeginGroup('V', "BeginVertical", () => GUILayout.BeginVertical(None));
    public static void EndVertical() => EndGroup('V', "EndVertical", () => GUILayout.EndVertical());

    public static void BeginHorizontal() => BeginGroup('H', "BeginHorizontal", () => GUILayout.BeginHorizontal(None));
    public static void EndHorizontal() => EndGroup('H', "EndHorizontal", () => GUILayout.EndHorizontal());

    public static void BeginArea(Rect area) => BeginGroup('A', "BeginArea", () => GUILayout.BeginArea(area));
    public static void EndArea() => EndGroup('A', "EndArea", () => GUILayout.EndArea());

    /// <summary>The window: a flat dark panel when themed, otherwise two default boxes behind a plain area.</summary>
    public static void BeginPanel(Rect area)
    {
        if (Theme.Ready && !Missing.Contains("BeginPanel"))
        {
            try
            {
                GUILayout.BeginArea(area, Theme.Panel!);
                OpenGroups.Push(('A', true));
                return;
            }
            catch (Exception exception) when (IsUnavailable(exception)) { MarkMissing("BeginPanel", exception); }
        }
        Run("Box", () => { GUI.Box(area, string.Empty); GUI.Box(area, string.Empty); });
        BeginArea(new Rect(area.x + 12f, area.y + 10f, area.width - 24f, area.height - 20f));
    }

    public static void EndPanel() => EndArea();

    /// <summary>A grouped block of rows: a flat lighter card when themed, otherwise a rule above a plain group.</summary>
    public static void BeginCard()
    {
        Space(2f);
        if (Theme.Ready && !Missing.Contains("BeginCard"))
        {
            try
            {
                GUILayout.BeginVertical(Theme.Card!, None);
                OpenGroups.Push(('V', true));
                return;
            }
            catch (Exception exception) when (IsUnavailable(exception)) { MarkMissing("BeginCard", exception); }
        }
        Label("--------------------------------------------------------------", Rule);
        BeginVertical();
    }

    public static void EndCard() => EndVertical();

    /// <summary>Closes every group still open. Call from a finally block around a whole frame.</summary>
    public static void Unwind()
    {
        while (OpenGroups.Count > 0)
        {
            (char kind, bool active) = OpenGroups.Pop();
            if (!active) continue;
            try
            {
                switch (kind)
                {
                    case 'S': GUILayout.EndScrollView(); break;
                    case 'V': GUILayout.EndVertical(); break;
                    case 'H': GUILayout.EndHorizontal(); break;
                    default: GUILayout.EndArea(); break;
                }
            }
            catch { /* already inconsistent; keep closing the rest */ }
        }
    }

    /// <summary>
    /// Exercises every primitive once, off-screen, so unavailable ones are found and logged up front instead of
    /// when the user first opens a tab. Safe to call repeatedly.
    /// </summary>
    public static void Probe()
    {
        Theme.EnsureInit();
        BeginPanel(new Rect(-2000f, -2000f, 10f, 10f));
        Title("probe");
        Label("probe");
        Button("probe");
        Button("probe", true);
        Toggle(false, "probe");
        Slider(0f, 0f, 1f);
        Space(1f);
        Flex();
        BeginScroll(default);
        BeginCard();
        BeginHorizontal();
        EndHorizontal();
        EndCard();
        BeginVertical();
        EndVertical();
        EndScroll();
        EndPanel();
    }
}

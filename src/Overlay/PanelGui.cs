using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace CarXModManager.Overlay;

/// <summary>
/// Thin IMGUI helpers for a game that never uses IMGUI, so IL2CPP may have stripped parts of it (Il2CppInterop then
/// reports "Method unstripping failed" or a missing method).
/// <list type="bullet">
/// <item>Every call is guarded: the first time one is unavailable it is logged once (<c>OVERLAY_IMGUI_UNAVAILABLE</c>) and
/// skipped from then on, with a fallback where one exists (styled controls fall back to plain ones, a toggle becomes a
/// button).</item>
/// <item>Every call passes an explicit options array, because the interop assemblies do not reliably keep GUILayout's
/// <c>params</c>.</item>
/// <item>Begin/End calls are tracked so a group left open by an exception can be closed at the end of the frame (IMGUI
/// breaks the whole frame otherwise).</item>
/// </list>
/// </summary>
internal static class PanelGui
{
    private static readonly Stack<(char Kind, bool Active)> OpenGroups = new();
    private static readonly HashSet<string> UnavailableCalls = new();
    private static Il2CppReferenceArray<GUILayoutOption>? _noLayoutOptions;
    private static Il2CppReferenceArray<GUILayoutOption> NoLayoutOptions => _noLayoutOptions ??= new Il2CppReferenceArray<GUILayoutOption>(0L);

    /// <summary>The IMGUI calls found to be unavailable, for the log; "none" when everything works.</summary>
    public static string UnavailableSummary => UnavailableCalls.Count == 0 ? "none" : string.Join(", ", UnavailableCalls.OrderBy(name => name));

    // ---- guarded primitives -------------------------------------------------------------------------------------------

    /// <summary>True for the exceptions IL2CPP stripping or an interop mismatch produces, as opposed to a real bug.</summary>
    private static bool IsStrippedMethodError(Exception exception) =>
        exception is NotSupportedException or MissingMemberException or TypeLoadException or EntryPointNotFoundException;

    private static void MarkUnavailable(string name, Exception exception)
    {
        if (UnavailableCalls.Add(name))
            Plugin.LogSource.LogWarning($"OVERLAY_IMGUI_UNAVAILABLE — {name}: {exception.GetType().Name}: {exception.Message}");
    }

    /// <summary>Runs a call that returns nothing; skipped if it was found to be unavailable.</summary>
    private static void TryCall(string name, Action call)
    {
        if (UnavailableCalls.Contains(name)) return;
        try { call(); }
        catch (Exception exception) when (IsStrippedMethodError(exception)) { MarkUnavailable(name, exception); }
    }

    /// <summary>Runs a call that returns a value. False means it is unavailable (the caller should use its fallback).</summary>
    private static bool TryCall<T>(string name, Func<T> call, out T result)
    {
        result = default!;
        if (UnavailableCalls.Contains(name)) return false;
        try { result = call(); return true; }
        catch (Exception exception) when (IsStrippedMethodError(exception)) { MarkUnavailable(name, exception); return false; }
    }

    /// <summary>Runs a call that needs the flat theme; false means "use the plain version instead".</summary>
    private static bool TryStyled(string name, Action call)
    {
        if (!FlatTheme.Ready || UnavailableCalls.Contains(name)) return false;
        try { call(); return true; }
        catch (Exception exception) when (IsStrippedMethodError(exception)) { MarkUnavailable(name, exception); return false; }
    }

    private static Il2CppReferenceArray<GUILayoutOption> Width(float pixels) =>
        pixels > 0f ? new Il2CppReferenceArray<GUILayoutOption>(new[] { GUILayout.Width(pixels) }) : NoLayoutOptions;

    private static Il2CppReferenceArray<GUILayoutOption> Height(float pixels) =>
        pixels > 0f ? new Il2CppReferenceArray<GUILayoutOption>(new[] { GUILayout.Height(pixels) }) : NoLayoutOptions;

    // ---- controls -----------------------------------------------------------------------------------------------------

    /// <summary>Plain text.</summary>
    public static void Label(string text)
    {
        if (TryStyled("LabelStyled", () => GUILayout.Label(text, FlatTheme.Text!, NoLayoutOptions))) return;
        TryCall("Label", () => GUILayout.Label(text, NoLayoutOptions));
    }

    /// <summary>Secondary (greyed) text, for counts and hints.</summary>
    public static void MutedLabel(string text)
    {
        if (TryStyled("MutedLabelStyled", () => GUILayout.Label(text, FlatTheme.MutedText!, NoLayoutOptions))) return;
        Label(text);
    }

    /// <summary>The accent-coloured bar at the top of a panel.</summary>
    public static void Title(string text)
    {
        if (TryStyled("TitleStyled", () => GUILayout.Label(text, FlatTheme.Title!, NoLayoutOptions))) return;
        Label(text);
    }

    /// <summary>A button. Returns true on the frame it is clicked.</summary>
    /// <param name="text">The button's label.</param>
    /// <param name="on">Draws the button in the accent colour (a switched-on state).</param>
    /// <param name="width">Fixed width in pixels, or 0 to share the row's width.</param>
    public static bool Button(string text, bool on = false, float width = 0f)
    {
        bool clicked = false;
        if (TryStyled("ButtonStyled", () => clicked = GUILayout.Button(text, on ? FlatTheme.ButtonOn! : FlatTheme.Button!, Width(width)))) return clicked;
        return TryCall("Button", () => GUILayout.Button(text, Width(width)), out bool plain) && plain;
    }

    /// <summary>A switch shown as a button reading "name: ON" or "name: OFF". Returns the new value.</summary>
    /// <param name="value">The current state.</param>
    /// <param name="text">The switch's name.</param>
    /// <param name="width">Fixed width in pixels, or 0 to share the row's width.</param>
    public static bool Toggle(bool value, string text, float width = 0f) =>
        Button($"{text}: {(value ? "ON" : "OFF")}", value, width) ? !value : value;

    /// <summary>
    /// A single-line text box. <paramref name="controlName"/> lets the caller ask whether this box has keyboard focus
    /// (<see cref="IsFocused"/>). Returns the (possibly edited) text.
    /// </summary>
    public static string TextField(string value, string controlName)
    {
        TryCall("SetNextControlName", () => GUI.SetNextControlName(controlName));
        string edited = value;
        if (TryStyled("TextFieldStyled", () => edited = GUILayout.TextField(value, FlatTheme.Field!, NoLayoutOptions))) return edited;
        return TryCall("TextField", () => GUILayout.TextField(value, NoLayoutOptions), out string plain) ? plain : value;
    }

    /// <summary>True if the text box with this control name currently has keyboard focus.</summary>
    public static bool IsFocused(string controlName) =>
        TryCall("GetNameOfFocusedControl", () => GUI.GetNameOfFocusedControl(), out string name) && name == controlName;

    /// <summary>Takes keyboard focus away from whatever IMGUI text box has it.</summary>
    public static void ClearFocus() => TryCall("FocusControl", () => GUI.FocusControl(string.Empty));

    /// <summary>Empty space of the given height.</summary>
    public static void Space(float pixels) => TryCall("Space", () => GUILayout.Space(pixels));

    /// <summary>Space that pushes the following controls to the right (or bottom) edge of the current group.</summary>
    public static void Flexible() => TryCall("Flexible", () => GUILayout.FlexibleSpace());

    // ---- groups -------------------------------------------------------------------------------------------------------

    private static void BeginGroup(char kind, string name, Action begin)
    {
        bool active = false;
        if (!UnavailableCalls.Contains(name))
        {
            try { begin(); active = true; }
            catch (Exception exception) when (IsStrippedMethodError(exception)) { MarkUnavailable(name, exception); }
        }
        OpenGroups.Push((kind, active));
    }

    private static void EndGroup(char kind, string name, Action end)
    {
        bool active = true;
        if (OpenGroups.Count > 0 && OpenGroups.Peek().Kind == kind) active = OpenGroups.Pop().Active;
        if (active) TryCall(name, end);
    }

    /// <summary>Starts a vertical stack of controls.</summary>
    public static void BeginVertical() => BeginGroup('V', "BeginVertical", () => GUILayout.BeginVertical(NoLayoutOptions));
    /// <summary>Ends the stack started by <see cref="BeginVertical"/>.</summary>
    public static void EndVertical() => EndGroup('V', "EndVertical", () => GUILayout.EndVertical());

    /// <summary>Starts a row of controls.</summary>
    public static void BeginHorizontal() => BeginGroup('H', "BeginHorizontal", () => GUILayout.BeginHorizontal(NoLayoutOptions));
    /// <summary>Ends the row started by <see cref="BeginHorizontal"/>.</summary>
    public static void EndHorizontal() => EndGroup('H', "EndHorizontal", () => GUILayout.EndHorizontal());

    /// <summary>Starts a screen-space area to lay controls out in.</summary>
    public static void BeginArea(Rect area) => BeginGroup('A', "BeginArea", () => GUILayout.BeginArea(area));
    /// <summary>Ends the area started by <see cref="BeginArea"/>.</summary>
    public static void EndArea() => EndGroup('A', "EndArea", () => GUILayout.EndArea());

    /// <summary>Starts a scrollable region of fixed height. Returns the new scroll position.</summary>
    /// <param name="position">The current scroll position (keep it between frames).</param>
    /// <param name="height">The region's height in pixels.</param>
    public static Vector2 BeginScroll(Vector2 position, float height)
    {
        Vector2 result = position;
        BeginGroup('S', "BeginScroll", () => result = GUILayout.BeginScrollView(position, Height(height)));
        return result;
    }
    /// <summary>Ends the region started by <see cref="BeginScroll"/>.</summary>
    public static void EndScroll() => EndGroup('S', "EndScroll", () => GUILayout.EndScrollView());

    /// <summary>Draws a solid background behind a rectangle, in the panel colour (plain dark box if the theme is unavailable).</summary>
    public static void Background(Rect area)
    {
        if (TryStyled("BackgroundStyled", () => GUI.Box(area, GUIContent.none, FlatTheme.Panel!))) return;
        TryCall("Background", () => GUI.Box(area, string.Empty));
    }

    /// <summary>Closes any group still open after an exception, so IMGUI is never left unbalanced.</summary>
    public static void CloseOpenGroups()
    {
        while (OpenGroups.Count > 0)
        {
            (char kind, bool active) = OpenGroups.Pop();
            if (!active) continue;
            switch (kind)
            {
                case 'V': TryCall("EndVertical", () => GUILayout.EndVertical()); break;
                case 'H': TryCall("EndHorizontal", () => GUILayout.EndHorizontal()); break;
                case 'A': TryCall("EndArea", () => GUILayout.EndArea()); break;
                case 'S': TryCall("EndScroll", () => GUILayout.EndScrollView()); break;
            }
        }
    }
}

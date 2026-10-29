using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace CarXModManager;

/// <summary>
/// Thin IMGUI helpers. The IL2CPP interop assemblies do not reliably keep GUILayout's "params" arrays,
/// so every call passes an explicit (empty) options array. That compiles whether or not params survived.
/// Begin/End calls are tracked so an exception half-way through a frame can be unwound cleanly: IMGUI
/// reports "more GUIClips pushed than popped" and breaks the whole frame if a group is left open.
/// </summary>
internal static class Ui
{
    private static readonly Stack<char> OpenGroups = new();
    private static readonly Color Rule = new(0.5f, 0.5f, 0.5f, 0.6f);
    private static Il2CppReferenceArray<GUILayoutOption>? _none;
    private static Il2CppReferenceArray<GUILayoutOption> None => _none ??= new Il2CppReferenceArray<GUILayoutOption>(0L);

    public static void Label(string text) => GUILayout.Label(text, None);

    public static void Label(string text, Color color)
    {
        Color previous = GUI.contentColor;
        GUI.contentColor = color;
        try { GUILayout.Label(text, None); }
        finally { GUI.contentColor = previous; }
    }

    public static bool Button(string text) => GUILayout.Button(text, None);
    public static bool Toggle(bool value, string text) => GUILayout.Toggle(value, text, None);
    public static float Slider(float value, float min, float max) => GUILayout.HorizontalSlider(value, min, max, None);
    public static void Space(float pixels) => GUILayout.Space(pixels);
    public static void Flex() => GUILayout.FlexibleSpace();

    public static Vector2 BeginScroll(Vector2 position)
    {
        Vector2 result = GUILayout.BeginScrollView(position, None);
        OpenGroups.Push('S');
        return result;
    }

    public static void EndScroll() { Pop('S'); GUILayout.EndScrollView(); }

    public static void BeginVertical() { GUILayout.BeginVertical(None); OpenGroups.Push('V'); }
    public static void EndVertical() { Pop('V'); GUILayout.EndVertical(); }

    public static void BeginHorizontal() { GUILayout.BeginHorizontal(None); OpenGroups.Push('H'); }
    public static void EndHorizontal() { Pop('H'); GUILayout.EndHorizontal(); }

    public static void BeginArea(Rect area) { GUILayout.BeginArea(area); OpenGroups.Push('A'); }
    public static void EndArea() { Pop('A'); GUILayout.EndArea(); }

    /// <summary>A vertical group with a dim rule above it. (GUILayout's styled "box" overloads take a GUIStyle, which we avoid.)</summary>
    public static void BeginBox()
    {
        Space(4f);
        Label("--------------------------------------------------------------", Rule);
        BeginVertical();
    }

    /// <summary>Closes every group still open. Call from a finally block around a whole frame.</summary>
    public static void Unwind()
    {
        while (OpenGroups.Count > 0)
        {
            char kind = OpenGroups.Pop();
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

    private static void Pop(char kind)
    {
        if (OpenGroups.Count > 0 && OpenGroups.Peek() == kind) OpenGroups.Pop();
    }
}

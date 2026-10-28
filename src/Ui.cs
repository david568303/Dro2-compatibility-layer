using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace CarXModManager;

/// <summary>
/// Thin IMGUI helpers. The IL2CPP interop assemblies do not reliably keep GUILayout's "params" arrays,
/// so every call passes an explicit (empty) options array. That compiles whether or not params survived.
/// </summary>
internal static class Ui
{
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
    public static Vector2 BeginScroll(Vector2 position) => GUILayout.BeginScrollView(position, None);
    public static void EndScroll() => GUILayout.EndScrollView();
    public static void BeginVertical() => GUILayout.BeginVertical(None);
    public static void BeginBox() => GUILayout.BeginVertical("box", None);
    public static void EndVertical() => GUILayout.EndVertical();
    public static void BeginHorizontal() => GUILayout.BeginHorizontal(None);
    public static void EndHorizontal() => GUILayout.EndHorizontal();
    public static void Space(float pixels) => GUILayout.Space(pixels);
    public static void Flex() => GUILayout.FlexibleSpace();
}

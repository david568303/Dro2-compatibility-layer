using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace CarXModManager;

/// <summary>
/// The few IMGUI calls the status line needs. The game never uses IMGUI, so IL2CPP may have stripped parts of it
/// (Il2CppInterop then throws "Method unstripping failed"): each call is guarded, logged once when unavailable,
/// and skipped afterwards. Every call passes an explicit (empty) options array because the interop assemblies do
/// not reliably keep GUILayout's "params".
/// </summary>
internal static class Ui
{
    private static readonly HashSet<string> Missing = new();
    private static int _openAreas;
    private static Il2CppReferenceArray<GUILayoutOption>? _none;
    private static Il2CppReferenceArray<GUILayoutOption> None => _none ??= new Il2CppReferenceArray<GUILayoutOption>(0L);

    private static bool IsUnavailable(Exception exception) =>
        exception is NotSupportedException or MissingMemberException or TypeLoadException or EntryPointNotFoundException;

    private static bool Run(string name, Action call)
    {
        if (Missing.Contains(name)) return false;
        try { call(); return true; }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            if (Missing.Add(name))
                Plugin.Logger.LogWarning($"CARXMM_UI_UNAVAILABLE — {name}: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    public static void BeginArea(Rect area)
    {
        if (Run("BeginArea", () => GUILayout.BeginArea(area))) _openAreas++;
    }

    public static void EndArea()
    {
        if (_openAreas == 0) return;
        _openAreas--;
        Run("EndArea", () => GUILayout.EndArea());
    }

    public static void Label(string text, Color color)
    {
        Color previous = GUI.contentColor;
        GUI.contentColor = color;
        try { Run("Label", () => GUILayout.Label(text, None)); }
        finally { GUI.contentColor = previous; }
    }

    /// <summary>Closes any area still open after an exception, so IMGUI is never left unbalanced.</summary>
    public static void Unwind()
    {
        while (_openAreas > 0) EndArea();
    }
}

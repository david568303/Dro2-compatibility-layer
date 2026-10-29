using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace CarXModManager;

/// <summary>
/// The few IMGUI calls the status line needs, each wrapped so a missing one cannot break the game. The game never uses
/// IMGUI, so IL2CPP may have stripped parts of it (Il2CppInterop then throws "Method unstripping failed"): a call that
/// turns out to be unavailable is logged once and skipped from then on. Every layout call passes an explicit (empty)
/// options array because the interop assemblies do not reliably keep GUILayout's <c>params</c>.
/// </summary>
internal static class Imgui
{
    /// <summary>Names of the calls found to be unavailable; they are not tried again.</summary>
    private static readonly HashSet<string> UnavailableCalls = new();

    /// <summary>How many <see cref="BeginArea"/> calls have not been matched by an <see cref="EndArea"/> yet.</summary>
    private static int _openAreas;

    private static Il2CppReferenceArray<GUILayoutOption>? _noLayoutOptions;
    private static Il2CppReferenceArray<GUILayoutOption> NoLayoutOptions => _noLayoutOptions ??= new Il2CppReferenceArray<GUILayoutOption>(0L);

    /// <summary>True for the exceptions IL2CPP stripping or an interop mismatch produces, as opposed to a real bug.</summary>
    private static bool IsStrippedMethodError(Exception exception) =>
        exception is NotSupportedException or MissingMemberException or TypeLoadException or EntryPointNotFoundException;

    /// <summary>Runs one IMGUI call. Returns false (and remembers it) when the call is unavailable in this game build.</summary>
    /// <param name="name">Name used in the log and to remember that this call is unavailable.</param>
    /// <param name="call">The IMGUI call to run.</param>
    private static bool TryCall(string name, Action call)
    {
        if (UnavailableCalls.Contains(name)) return false;
        try { call(); return true; }
        catch (Exception exception) when (IsStrippedMethodError(exception))
        {
            if (UnavailableCalls.Add(name))
                Plugin.LogSource.LogWarning($"IMGUI_CALL_UNAVAILABLE — {name}: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    /// <summary>Starts a screen-space area to draw into. Every call must be followed by <see cref="EndArea"/>.</summary>
    public static void BeginArea(Rect area)
    {
        if (TryCall("BeginArea", () => GUILayout.BeginArea(area))) _openAreas++;
    }

    /// <summary>Ends the area started by the last <see cref="BeginArea"/>. Does nothing if none is open.</summary>
    public static void EndArea()
    {
        if (_openAreas == 0) return;
        _openAreas--;
        TryCall("EndArea", () => GUILayout.EndArea());
    }

    /// <summary>Draws text (Unity rich text allowed) in the given colour inside the current area.</summary>
    public static void Label(string text, Color color)
    {
        Color previous = GUI.contentColor;
        GUI.contentColor = color;
        try { TryCall("Label", () => GUILayout.Label(text, NoLayoutOptions)); }
        finally { GUI.contentColor = previous; }
    }

    /// <summary>Closes any area still open after an exception, so IMGUI is never left unbalanced.</summary>
    public static void CloseOpenAreas()
    {
        while (_openAreas > 0) EndArea();
    }
}

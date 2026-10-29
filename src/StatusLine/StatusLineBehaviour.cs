using UnityEngine;

namespace CarXModManager;

/// <summary>
/// The only Unity component the plugin injects. Unity calls <see cref="OnGUI"/> every frame, which draws the status
/// line. It holds no state of its own (that lives in <see cref="StatusLine"/>) and is not part of the public API; it is
/// public only because IL2CPP injection needs a public component type.
/// </summary>
public sealed class StatusLineBehaviour : MonoBehaviour
{
    /// <summary>Required by Il2CppInterop for injected components.</summary>
    public StatusLineBehaviour(IntPtr pointer) : base(pointer) { }

    /// <summary>Unity's IMGUI callback. Drawing errors are counted and logged, never allowed to reach the game.</summary>
    public void OnGUI()
    {
        try { StatusLine.Draw(); }
        catch (Exception exception) { StatusLine.ReportDrawError(exception); }
    }
}

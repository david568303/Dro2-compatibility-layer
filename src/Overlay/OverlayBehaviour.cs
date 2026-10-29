using UnityEngine;

namespace CarXModManager.Overlay;

/// <summary>
/// The Unity component that lets the overlay run: Unity calls <see cref="Update"/> and <see cref="OnGUI"/> every frame.
/// It holds no state (everything lives in <see cref="OverlayHost"/>). It is not part of any API; it is public only
/// because IL2CPP injection needs a public component type.
/// </summary>
public sealed class OverlayBehaviour : MonoBehaviour
{
    /// <summary>Required by Il2CppInterop for injected components.</summary>
    public OverlayBehaviour(IntPtr pointer) : base(pointer) { }

    /// <summary>Unity's per-frame callback.</summary>
    public void Update()
    {
        try { OverlayHost.Update(); }
        catch (Exception exception) { OverlayHost.ReportError(exception); }
    }

    /// <summary>Unity's IMGUI callback (runs several times per frame, once per IMGUI event).</summary>
    public void OnGUI()
    {
        try { OverlayHost.OnGui(); }
        catch (Exception exception) { OverlayHost.ReportError(exception); }
    }

    /// <summary>Releases the game's input if this component is destroyed.</summary>
    public void OnDestroy() => OverlayHost.Shutdown();

    /// <summary>Releases the game's input when the game quits.</summary>
    public void OnApplicationQuit() => OverlayHost.Shutdown();
}

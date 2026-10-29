using UnityEngine;

namespace CarXModManager;

/// <summary>
/// The only injected Unity component. It holds no state: everything lives in <see cref="OverlayController"/>,
/// so Il2CppInterop never has to marshal managed fields.
/// </summary>
public sealed class OverlayBehaviour : MonoBehaviour
{
    public OverlayBehaviour(IntPtr pointer) : base(pointer) { }

    public void Update()
    {
        try { OverlayController.Tick(); }
        catch (Exception exception) { OverlayController.ReportFault("Update", exception); }
    }

    public void LateUpdate()
    {
        try { OverlayController.EnforceCursor(); }
        catch (Exception exception) { OverlayController.ReportFault("LateUpdate", exception); }
    }

    public void OnGUI()
    {
        try { OverlayController.Draw(); }
        catch (Exception exception) { OverlayController.ReportFault("OnGUI", exception); }
    }

    public void OnDestroy() => OverlayController.Shutdown();
    public void OnApplicationQuit() => OverlayController.Shutdown();
}

using UnityEngine;

namespace CarXModManager;

/// <summary>The only injected Unity component. It holds no state; everything lives in <see cref="StatusLine"/>.</summary>
public sealed class StatusBehaviour : MonoBehaviour
{
    public StatusBehaviour(IntPtr pointer) : base(pointer) { }

    public void OnGUI()
    {
        try { StatusLine.Draw(); }
        catch (Exception exception) { StatusLine.ReportFault(exception); }
    }
}

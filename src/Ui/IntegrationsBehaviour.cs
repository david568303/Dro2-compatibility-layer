using CarXModManager.Integrations;
using UnityEngine;

namespace CarXModManager.Ui;

/// <summary>
/// The Unity component that lets the integrations run: Unity calls <see cref="Update"/> every frame, and <see cref="OnGUI"/> delivers the
/// keyboard and mouse events that end or edit a search being typed. It draws nothing and holds no state (everything lives in
/// <see cref="IntegrationHost"/> and <see cref="TextCapture"/>). It is not part of any API; it is public only because IL2CPP
/// injection needs a public component type.
/// </summary>
public sealed class IntegrationsBehaviour : MonoBehaviour
{
    /// <summary>Required by Il2CppInterop for injected components.</summary>
    public IntegrationsBehaviour(IntPtr pointer) : base(pointer) { }

    /// <summary>Unity's per-frame callback.</summary>
    public void Update()
    {
        try { IntegrationHost.Update(); }
        catch (Exception exception) { Plugin.LogSource.LogError($"INTEGRATIONS_ERROR — Update: {exception}"); }
    }

    /// <summary>Unity's IMGUI callback (runs once per IMGUI event). Used only to read key presses and clicks while a search is being typed.</summary>
    public void OnGUI()
    {
        if (!TextCapture.IsActive) return;
        try
        {
            Event? current = Event.current;
            if (current == null) return;
            if (current.type == EventType.KeyDown) TextCapture.OnKeyDown(current);
            else if (current.type == EventType.MouseDown) TextCapture.OnMouseDown();
        }
        catch (Exception exception)
        {
            Plugin.LogSource.LogError($"INTEGRATIONS_ERROR — OnGUI: {exception}");
            TextCapture.End();
        }
    }

    /// <summary>Lets the game's input through again if this component is destroyed.</summary>
    public void OnDestroy() => IntegrationHost.Shutdown();

    /// <summary>Lets the game's input through again when the game quits.</summary>
    public void OnApplicationQuit() => IntegrationHost.Shutdown();
}

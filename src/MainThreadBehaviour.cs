using UnityEngine;

namespace CarXModManager;

/// <summary>Injected component that drains <see cref="Api.MainThread"/> once per frame.</summary>
public sealed class MainThreadBehaviour : MonoBehaviour
{
    public MainThreadBehaviour(IntPtr pointer) : base(pointer) { }

    public void Update() => Api.MainThread.Drain();
}

using System.Collections.Concurrent;

namespace CarXModManager.Api;

/// <summary>
/// Runs work on Unity's main thread. Unity and Il2Cpp objects may only be touched there, so a mod that does heavy
/// work on a background thread posts the Unity-facing part back with <see cref="Post"/>.
/// </summary>
public static class MainThread
{
    private const int MaxPerFrame = 16;
    private static readonly ConcurrentQueue<(string Name, Action Work)> Queue = new();

    /// <summary>Queues <paramref name="work"/> for the next frame. Safe to call from any thread.</summary>
    /// <param name="name">Shown in the error log if the work throws.</param>
    public static void Post(string name, Action work) => Queue.Enqueue((name, work));

    internal static void Drain()
    {
        for (int i = 0; i < MaxPerFrame && Queue.TryDequeue(out var item); i++)
        {
            try { item.Work(); }
            catch (Exception exception) { Plugin.Logger.LogError($"MAINTHREAD_WORK_FAILED — {item.Name}: {exception}"); }
        }
    }
}

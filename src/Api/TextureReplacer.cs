using System.Diagnostics;
using BepInEx.Logging;
using UnityEngine;

namespace CarXModManager.Api;

/// <summary>
/// "Give the game a different version of this texture, before it ever uses the original."
///
/// Call <see cref="Replace"/> from a prefix on the game method that receives the texture and assign the result back
/// to the argument. The processing runs there and then, synchronously, so the game never shows the original for even
/// a frame (processing in the background and swapping later makes the original flash on screen first).
///
/// Textures with Read/Write disabled, the usual case for textures the game loads itself, are copied on the GPU first,
/// and the game is given the copy; the original is left alone. Results are remembered per original texture, so the
/// several calls the game makes with the same texture all get the same replacement and the work is done once.
/// Replacements are destroyed oldest-first beyond <c>maxKeptCopies</c>, so reopening previews does not leak.
/// All of this must run on Unity's main thread.
/// </summary>
public sealed class TextureReplacer
{
    /// <summary>Turns tightly packed RGBA pixels (row 0 first) into new pixels of the same size.</summary>
    public delegate byte[] Processor(byte[] rgba, int width, int height);

    private readonly ManualLogSource _log;
    private readonly string _tag;
    private readonly Processor _processor;
    private readonly int _maxKeptCopies;

    private readonly object _gate = new();
    private readonly HashSet<int> _ours = new();           // ids of textures that are replacements, or were processed in place
    private readonly HashSet<int> _failed = new();         // originals that could not be processed; not retried
    private readonly Dictionary<int, Texture2D> _replacements = new();
    private readonly Queue<(int OriginalId, Texture2D Copy)> _copies = new();

    /// <param name="log">The calling mod's log source; failures are logged as Errors under it.</param>
    /// <param name="tag">Prefix of the log lines, e.g. "MAPMANAGER_PREVIEW".</param>
    public TextureReplacer(ManualLogSource log, string tag, Processor processor, int maxKeptCopies = 6)
    {
        _log = log;
        _tag = tag;
        _processor = processor;
        _maxKeptCopies = Math.Max(1, maxKeptCopies);
    }

    /// <summary>True if <paramref name="texture"/> is one this replacer produced (or already processed in place).</summary>
    public bool IsOurs(Texture2D texture)
    {
        if (texture == null) return false;
        lock (_gate) return _ours.Contains(texture.GetInstanceID());
    }

    /// <summary>The texture the game should use instead of <paramref name="original"/>; the original itself if it cannot be replaced.</summary>
    public Texture2D Replace(Texture2D original)
    {
        if (original == null) return original!;
        int id = original.GetInstanceID();
        lock (_gate)
        {
            if (_ours.Contains(id) || _failed.Contains(id)) return original;
            if (_replacements.TryGetValue(id, out Texture2D? existing)) return existing;
        }

        var total = Stopwatch.StartNew();
        Texture2D? copy = null;
        try
        {
            Texture2D target = original;
            if (!original.isReadable)
            {
                if (!TextureTools.TryCopyToReadable(original, out copy, out string? copyProblem) || copy == null)
                    return Fail(id, $"could not make a readable copy: {copyProblem}", original);
                target = copy;
            }
            long copyMs = total.ElapsedMilliseconds;

            if (!TextureTools.TryRead(target, out byte[] rgba, out int width, out int height, out string? readProblem))
                return Fail(id, readProblem ?? "could not read the texture", original, copy);

            byte[] result = _processor(rgba, width, height);
            if (result.Length != rgba.Length)
                return Fail(id, "the processor returned a different number of pixels", original, copy);
            long processMs = total.ElapsedMilliseconds - copyMs;

            if (!TextureTools.TryWriteInPlace(target, result, width, height, out string? writeProblem))
                return Fail(id, writeProblem ?? "could not write the result", original, copy);

            if (copy != null) Remember(id, copy);
            else lock (_gate) _ours.Add(id);
            _log.LogInfo($"{_tag}_REPLACED — {width}x{height}, {(copy != null ? "GPU copy" : "in place")}; copy {copyMs} ms, processing {processMs} ms, total {total.ElapsedMilliseconds} ms.");
            return target;
        }
        catch (Exception exception)
        {
            return Fail(id, exception.ToString(), original, copy);
        }
    }

    private Texture2D Fail(int id, string problem, Texture2D original, Texture2D? copy = null)
    {
        lock (_gate) _failed.Add(id);
        Destroy(copy);
        _log.LogError($"{_tag}_FAILED — {problem}");
        return original;
    }

    private void Remember(int originalId, Texture2D copy)
    {
        var expired = new List<Texture2D>();
        lock (_gate)
        {
            _replacements[originalId] = copy;
            _ours.Add(copy.GetInstanceID());
            _copies.Enqueue((originalId, copy));
            while (_copies.Count > _maxKeptCopies)
            {
                var (oldId, oldCopy) = _copies.Dequeue();
                _replacements.Remove(oldId);
                _ours.Remove(oldCopy.GetInstanceID());
                expired.Add(oldCopy);
            }
        }
        foreach (Texture2D old in expired) Destroy(old);
    }

    private void Destroy(Texture2D? texture)
    {
        if (texture == null) return;
        try { UnityEngine.Object.Destroy(texture); }
        catch (Exception exception) { _log.LogDebug($"{_tag}: could not destroy a texture copy: {exception.Message}"); }
    }
}

namespace CarXModManager.Logic;

/// <summary>
/// Which parts of the mod manager are not working right now (an integration that could not be installed, a screen whose checks
/// failed, an integration that stopped after errors), for the status line to show. Everything is also in the log; this is what
/// makes a problem visible in the game without opening it. Pure code, covered by the test project.
/// </summary>
internal sealed class ProblemList
{
    private readonly SortedDictionary<string, string> _problems = new(StringComparer.Ordinal);

    /// <summary>Changes whenever the list changes, so a cached text can tell it is out of date.</summary>
    public int Version { get; private set; }

    /// <summary>True if anything is not working.</summary>
    public bool Any => _problems.Count > 0;

    /// <summary>Records that <paramref name="name"/> is not working. Reporting the same reason again changes nothing.</summary>
    /// <param name="name">The part, e.g. <c>ServerBrowser</c>.</param>
    /// <param name="reason">Short reason, for the log and tests; the status line shows only the name.</param>
    public void Report(string name, string reason)
    {
        if (_problems.TryGetValue(name, out string? known) && known == reason) return;
        _problems[name] = reason;
        Version++;
    }

    /// <summary>Records that <paramref name="name"/> works (again). Does nothing if it was not listed.</summary>
    public void Clear(string name)
    {
        if (_problems.Remove(name)) Version++;
    }

    /// <summary>The reason recorded for <paramref name="name"/>, or <c>null</c> if it is not listed.</summary>
    public string? ReasonFor(string name) => _problems.GetValueOrDefault(name);

    /// <summary>The note for the status line, e.g. <c>mod manager: ServerBrowser, TrackPicker not working (see log)</c>; empty when everything works.</summary>
    public string Describe() => _problems.Count == 0 ? string.Empty : $"mod manager: {string.Join(", ", _problems.Keys)} not working (see log)";
}

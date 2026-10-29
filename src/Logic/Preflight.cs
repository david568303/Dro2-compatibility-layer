namespace CarXModManager.Logic;

/// <summary>The outcome of one check on a game screen.</summary>
/// <param name="Name">What was checked, in words a player or mod author can read ("the room list", "a button to copy the look from").</param>
/// <param name="Passed">Whether it held.</param>
/// <param name="Detail">What was found (also given for passed checks, e.g. the size of the list) or why it failed.</param>
internal readonly record struct CheckResult(string Name, bool Passed, string Detail);

/// <summary>
/// The checks run on one of the game's screens before the mod manager changes it. Every fact the change depends on is recorded
/// here (the widgets it copies from must exist, the place for the new controls must be on screen and not clipped, ...), and the
/// screen is only changed if all of them hold. If any fails, the screen is left exactly as the game made it and
/// <see cref="Describe"/> gives the one log line that says what was missing, so a game update that moves things shows up in
/// the log straight away instead of as a half-broken screen.
/// </summary>
internal sealed class Preflight
{
    private readonly List<CheckResult> _results = new();

    /// <summary>Every check recorded so far, in order.</summary>
    public IReadOnlyList<CheckResult> Results => _results;

    /// <summary>True if every recorded check passed (also true while nothing has been checked).</summary>
    public bool Passed => _results.All(result => result.Passed);

    /// <summary>How many recorded checks failed.</summary>
    public int FailedCount => _results.Count(result => !result.Passed);

    /// <summary>Records a check whose outcome is already known. Returns <paramref name="passed"/>, so checks can be chained.</summary>
    /// <param name="name">What was checked.</param>
    /// <param name="passed">Whether it held.</param>
    /// <param name="detail">What was found, or why it failed.</param>
    public bool Check(string name, bool passed, string detail = "")
    {
        _results.Add(new CheckResult(name, passed, detail));
        return passed;
    }

    /// <summary>
    /// Runs a check that reads the game (which may throw if an object is gone or a member was removed). An exception counts
    /// as a failed check whose detail is the exception, instead of escaping.
    /// </summary>
    /// <param name="name">What is checked.</param>
    /// <param name="test">Returns whether it holds and a description of what was found.</param>
    public bool Check(string name, Func<(bool Passed, string Detail)> test)
    {
        try
        {
            (bool passed, string detail) = test();
            return Check(name, passed, detail);
        }
        catch (Exception exception)
        {
            return Check(name, false, $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>
    /// One line for the log. When everything passed: how many checks and what they found. Otherwise: which checks failed and
    /// why, then the ones that passed.
    /// </summary>
    public string Describe()
    {
        if (_results.Count == 0) return "no checks were run";
        string Format(CheckResult result) => result.Detail.Length == 0 ? result.Name : $"{result.Name} ({result.Detail})";
        if (Passed) return $"{_results.Count} of {_results.Count} checks passed: {string.Join("; ", _results.Select(Format))}";

        string failed = string.Join("; ", _results.Where(result => !result.Passed).Select(Format));
        string passed = string.Join(", ", _results.Where(result => result.Passed).Select(result => result.Name));
        return $"{FailedCount} of {_results.Count} checks failed: {failed}" + (passed.Length > 0 ? $". Passed: {passed}." : ".");
    }
}

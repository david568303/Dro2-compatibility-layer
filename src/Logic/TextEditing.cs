namespace CarXModManager.Logic;

/// <summary>
/// The editing rules of the panels' text boxes: what a typed character does, and what Backspace does. Pure code with no game
/// or Unity dependency, covered by the test project.
/// </summary>
internal static class TextEditing
{
    /// <summary>The longest text a box accepts.</summary>
    public const int MaxLength = 48;

    /// <summary>True for a character that is text (a letter, digit, space, symbol), false for control characters such as Backspace, Enter or Delete.</summary>
    public static bool IsPrintable(char c) => !char.IsControl(c) && !char.IsSurrogate(c);

    /// <summary>The text with <paramref name="c"/> added at the end, or unchanged if <paramref name="c"/> is not printable or the box is full.</summary>
    public static string Insert(string text, char c, int maxLength = MaxLength) =>
        IsPrintable(c) && text.Length < maxLength ? text + c : text;

    /// <summary>The text without its last character (unchanged if empty).</summary>
    public static string Backspace(string text) => text.Length == 0 ? text : text[..^1];
}

/// <summary>Where a keyboard event comes from. The panels listen to two sources because either may be missing in a given game build.</summary>
internal enum KeySource
{
    /// <summary>No event has been seen yet.</summary>
    None,
    /// <summary>The IMGUI event queue (<c>Event.current</c> in <c>OnGUI</c>).</summary>
    Imgui,
    /// <summary>The Input System package's keyboard device.</summary>
    InputSystem,
}

/// <summary>
/// Settles which keyboard source to believe for one kind of key. Both sources normally report every key press, so using both
/// would type each letter twice: the first source to report is chosen and the other is ignored from then on. If only one source
/// works in this game, it is the one that reports, so it is chosen. Pure code, covered by the test project.
/// </summary>
internal sealed class KeySourceLock
{
    /// <summary>The chosen source, or <see cref="KeySource.None"/> while neither has reported.</summary>
    public KeySource Owner { get; private set; }

    /// <summary>Reports an event from <paramref name="source"/>. Returns true if it should be used (that source is, or has just become, the chosen one).</summary>
    public bool Accept(KeySource source)
    {
        if (Owner == KeySource.None) Owner = source;
        return Owner == source;
    }
}

/// <summary>
/// Key repeat for a held key (Backspace): one action when the key goes down, then, after a delay, one every interval. If the
/// game stalls for a moment, at most a few catch-up repeats are given at once instead of a burst.
/// Pure code (time is passed in), so it is covered by the test project.
/// </summary>
internal sealed class KeyRepeat
{
    private const int MaxPerUpdate = 4;

    private readonly float _delaySeconds;
    private readonly float _intervalSeconds;
    private bool _held;
    private float _nextRepeatAt;

    /// <summary>Creates a repeater.</summary>
    /// <param name="delaySeconds">How long the key must be held before it starts repeating.</param>
    /// <param name="intervalSeconds">Time between repeats once repeating.</param>
    public KeyRepeat(float delaySeconds = 0.45f, float intervalSeconds = 0.04f)
    {
        _delaySeconds = delaySeconds;
        _intervalSeconds = intervalSeconds;
    }

    /// <summary>Forgets that the key was held, for example when the text box loses focus.</summary>
    public void Reset() => _held = false;

    /// <summary>Call every frame. Returns how many times the key's action should happen now.</summary>
    /// <param name="isDown">Whether the key is held down this frame.</param>
    /// <param name="now">The current time in seconds.</param>
    public int Update(bool isDown, float now)
    {
        if (!isDown)
        {
            _held = false;
            return 0;
        }
        if (!_held)
        {
            _held = true;
            _nextRepeatAt = now + _delaySeconds;
            return 1;
        }
        int repeats = 0;
        while (now >= _nextRepeatAt && repeats < MaxPerUpdate)
        {
            repeats++;
            _nextRepeatAt += _intervalSeconds;
        }
        if (now >= _nextRepeatAt) _nextRepeatAt = now + _intervalSeconds; // after a long stall, resume at the normal pace
        return repeats;
    }
}

using BepInEx.Logging;

namespace CarXModManager;

/// <summary>Keeps the most recent BepInEx log lines so the overlay can show them.</summary>
internal sealed class LogBuffer : ILogListener
{
    private const int MaxLines = 400;
    public static readonly LogBuffer Instance = new();

    private readonly object _gate = new();
    private readonly Queue<string> _lines = new();

    /// <summary>Bumps whenever a line is added so the UI knows when to rebuild its text.</summary>
    public int Version { get; private set; }

    public LogLevel LogLevelFilter => LogLevel.All;

    public static void Install() => BepInEx.Logging.Logger.Listeners.Add(Instance);

    public void LogEvent(object sender, LogEventArgs eventArgs)
    {
        if ((eventArgs.Level & LogLevel.Debug) != 0) return;
        string line = $"[{eventArgs.Level,-7}:{eventArgs.Source.SourceName}] {eventArgs.Data}";
        lock (_gate)
        {
            _lines.Enqueue(line);
            while (_lines.Count > MaxLines) _lines.Dequeue();
            Version++;
        }
    }

    public string[] Snapshot(int last)
    {
        lock (_gate) return _lines.Skip(Math.Max(0, _lines.Count - last)).ToArray();
    }

    public void Dispose() { }
}

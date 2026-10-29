using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace CarXModManager;

/// <summary>
/// Keeps track of which mods loaded, which of those have logged an error, and which failed to load at all, and turns
/// that into the status line's text. It works by listening to BepInEx's log, so a mod needs no code of its own to
/// appear in the status line: it only has to log errors the usual way (see docs/API.md, "Logging conventions").
/// The layer itself is not counted as a mod.
/// <list type="bullet">
/// <item><b>With errors:</b> a loaded mod whose own log source (its plugin name) has logged an Error or Fatal line.
/// Warnings do not count. Errors logged under some other source name cannot be tied to a mod and are ignored.</item>
/// <item><b>Failed to load:</b> BepInEx logged "Error loading [X]" or "Could not load [X]" for it.</item>
/// </list>
/// Mods that loaded before this plugin are not seen by the live listener, so <c>LogOutput.log</c> is read once as well
/// (<see cref="ImportErrorsFromLogFile"/>).
/// </summary>
internal sealed class ModStatusTracker : ILogListener
{
    /// <summary>The single tracker. BepInEx's listener list and the status line both use this instance.</summary>
    public static readonly ModStatusTracker Instance = new();

    /// <summary>A written log line of level Error or Fatal: "[Error  : Source Name] message". Group 1 = source, group 2 = message.</summary>
    private static readonly Regex ErrorLogLinePattern = new(@"^\[(?:Error|Fatal)\s*:\s*(.+?)\]\s?(.*)$", RegexOptions.Compiled);

    /// <summary>BepInEx's own message when a plugin cannot be loaded. Group 1 = the plugin's name.</summary>
    private static readonly Regex LoadFailurePattern = new(@"^(?:Error loading|Could not load) \[(.+?)\]", RegexOptions.Compiled);

    /// <summary>Colour of the error counts in the status line (Unity rich text).</summary>
    private const string ErrorColorHex = "#FF4D4D";

    private readonly object _gate = new();
    private readonly HashSet<string> _modsWithErrors = new();
    private readonly HashSet<string> _modsFailedToLoad = new();

    /// <summary>Counts every change to the two sets above, so the text is only rebuilt when something changed.</summary>
    private int _changeCount;
    private int _textChangeCount = -1;
    private int _textPluginCount = -1;
    private string _text = string.Empty;
    private string _plainText = string.Empty;

    /// <summary>Listen to every log level; the filtering to Error and Fatal happens in <see cref="LogEvent"/>.</summary>
    public LogLevel LogLevelFilter => LogLevel.All;

    /// <summary>Starts listening to BepInEx's log. Call once, early in the plugin's <c>Load</c>.</summary>
    public static void StartListening() => BepInEx.Logging.Logger.Listeners.Add(Instance);

    /// <summary>Called by BepInEx for every log message; remembers the source of every Error or Fatal one.</summary>
    public void LogEvent(object sender, LogEventArgs eventArgs)
    {
        if ((eventArgs.Level & (LogLevel.Error | LogLevel.Fatal)) == 0) return;
        RecordError(eventArgs.Source.SourceName, eventArgs.Data?.ToString() ?? string.Empty);
    }

    /// <summary>Required by <see cref="ILogListener"/>; there is nothing to release.</summary>
    public void Dispose() { }

    /// <summary>
    /// Reads the errors already written to <c>LogOutput.log</c>, which covers mods that loaded (and logged) before this
    /// plugin started listening. Safe to call late and more than once: recording the same error again changes nothing.
    /// </summary>
    public void ImportErrorsFromLogFile()
    {
        try
        {
            string path = Path.Combine(Paths.BepInExRootPath, "LogOutput.log");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                Match match = ErrorLogLinePattern.Match(line);
                if (match.Success) RecordError(match.Groups[1].Value.Trim(), match.Groups[2].Value);
            }
        }
        catch (Exception exception)
        {
            Plugin.LogSource.LogDebug($"Could not read LogOutput.log for earlier errors: {exception.Message}");
        }
    }

    /// <summary>
    /// Remembers that <paramref name="source"/> logged an error. Messages from BepInEx itself are checked for a load
    /// failure instead, because those name the failed plugin in the message rather than in the source.
    /// </summary>
    private void RecordError(string source, string message)
    {
        lock (_gate)
        {
            if (source == "BepInEx")
            {
                Match failure = LoadFailurePattern.Match(message);
                if (failure.Success && _modsFailedToLoad.Add(failure.Groups[1].Value)) _changeCount++;
                return;
            }
            if (_modsWithErrors.Add(source)) _changeCount++;
        }
    }

    /// <summary>
    /// The status line's text, for example <c>4 mods loaded, 3 with errors</c>. Rebuilt only when an error was recorded
    /// or the number of loaded plugins changed.
    /// </summary>
    /// <param name="plainText">The same text without colour tags, for drawing the drop shadow.</param>
    /// <returns>The text with the error counts wrapped in Unity rich-text colour tags.</returns>
    public string GetStatusText(out string plainText)
    {
        int pluginCount = IL2CPPChainloader.Instance.Plugins.Count;
        lock (_gate)
        {
            if (_textChangeCount != _changeCount || _textPluginCount != pluginCount)
            {
                _textChangeCount = _changeCount;
                _textPluginCount = pluginCount;
                RebuildText();
            }
            plainText = _plainText;
            return _text;
        }
    }

    /// <summary>Builds <see cref="_text"/> and <see cref="_plainText"/> from the loaded plugin list and the two error sets.</summary>
    private void RebuildText()
    {
        var loaded = IL2CPPChainloader.Instance.Plugins.Values
            .Where(plugin => plugin.Metadata.GUID != Plugin.PluginGuid && !_modsFailedToLoad.Contains(plugin.Metadata.Name))
            .ToList();
        int withErrors = loaded.Count(plugin => _modsWithErrors.Contains(plugin.Metadata.Name));
        int failed = _modsFailedToLoad.Count(name => name != Plugin.PluginName);

        string loadedPart = $"{loaded.Count} mod{(loaded.Count == 1 ? string.Empty : "s")} loaded";
        string errorsPart = withErrors > 0 ? $"{withErrors} with error{(withErrors == 1 ? string.Empty : "s")}" : string.Empty;
        string failedPart = failed > 0 ? $"{failed} failed to load" : string.Empty;

        _plainText = loadedPart
            + (errorsPart.Length > 0 ? ", " + errorsPart : string.Empty)
            + (failedPart.Length > 0 ? ", " + failedPart : string.Empty);
        _text = loadedPart
            + (errorsPart.Length > 0 ? $", <color={ErrorColorHex}>{errorsPart}</color>" : string.Empty)
            + (failedPart.Length > 0 ? $", <color={ErrorColorHex}>{failedPart}</color>" : string.Empty);
    }
}

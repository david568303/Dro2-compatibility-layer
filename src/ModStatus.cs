using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace CarXModManager;

/// <summary>
/// Works out how many mods loaded, how many of those have logged an error, and how many failed to load at all.
/// The manager itself is not counted.
/// <list type="bullet">
/// <item>"With errors": a loaded mod whose own log source (its plugin name) has logged an Error or Fatal line.
/// Warnings do not count. Errors logged under some other source name cannot be tied to a mod and are ignored.</item>
/// <item>"Failed to load": BepInEx logged "Error loading [X]" or "Could not load [X]" for it.</item>
/// </list>
/// Mods that loaded before this one are not seen by the live log listener, so LogOutput.log is read once as well.
/// </summary>
internal sealed class ModStatus : ILogListener
{
    public static readonly ModStatus Instance = new();

    private static readonly Regex LogLine = new(@"^\[(?:Error|Fatal)\s*:\s*(.+?)\]\s?(.*)$", RegexOptions.Compiled);
    private static readonly Regex LoadFailure = new(@"^(?:Error loading|Could not load) \[(.+?)\]", RegexOptions.Compiled);
    private const string Red = "#FF4D4D";

    private readonly object _gate = new();
    private readonly HashSet<string> _errored = new();
    private readonly HashSet<string> _failed = new();
    private int _version;
    private int _cachedVersion = -1;
    private int _cachedPluginCount = -1;
    private string _text = string.Empty;
    private string _plain = string.Empty;

    public LogLevel LogLevelFilter => LogLevel.All;

    public static void Install() => BepInEx.Logging.Logger.Listeners.Add(Instance);

    public void LogEvent(object sender, LogEventArgs eventArgs)
    {
        if ((eventArgs.Level & (LogLevel.Error | LogLevel.Fatal)) == 0) return;
        Record(eventArgs.Source.SourceName, eventArgs.Data?.ToString() ?? string.Empty);
    }

    public void Dispose() { }

    /// <summary>Reads the errors already written to LogOutput.log. Safe to call late; recording is idempotent.</summary>
    public void ImportExistingLog()
    {
        try
        {
            string path = Path.Combine(Paths.BepInExRootPath, "LogOutput.log");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                Match match = LogLine.Match(line);
                if (match.Success) Record(match.Groups[1].Value.Trim(), match.Groups[2].Value);
            }
        }
        catch (Exception exception)
        {
            Plugin.Logger.LogDebug($"Could not read LogOutput.log for earlier errors: {exception.Message}");
        }
    }

    private void Record(string source, string message)
    {
        lock (_gate)
        {
            if (source == "BepInEx")
            {
                Match failure = LoadFailure.Match(message);
                if (failure.Success && _failed.Add(failure.Groups[1].Value)) _version++;
                return;
            }
            if (_errored.Add(source)) _version++;
        }
    }

    /// <summary>The line to draw. <paramref name="plain"/> is the same text without colour tags, for the drop shadow.</summary>
    public string GetText(out string plain)
    {
        int pluginCount = IL2CPPChainloader.Instance.Plugins.Count;
        lock (_gate)
        {
            if (_cachedVersion != _version || _cachedPluginCount != pluginCount)
            {
                _cachedVersion = _version;
                _cachedPluginCount = pluginCount;
                Rebuild();
            }
            plain = _plain;
            return _text;
        }
    }

    private void Rebuild()
    {
        var loaded = IL2CPPChainloader.Instance.Plugins.Values
            .Where(plugin => plugin.Metadata.GUID != Plugin.PluginGuid && !_failed.Contains(plugin.Metadata.Name))
            .ToList();
        int withErrors = loaded.Count(plugin => _errored.Contains(plugin.Metadata.Name));
        int failed = _failed.Count(name => name != "CarX Mod Manager");

        string loadedPart = $"{loaded.Count} mod{(loaded.Count == 1 ? string.Empty : "s")} loaded";
        string errorsPart = withErrors > 0 ? $"{withErrors} with error{(withErrors == 1 ? string.Empty : "s")}" : string.Empty;
        string failedPart = failed > 0 ? $"{failed} failed to load" : string.Empty;

        _plain = loadedPart
            + (errorsPart.Length > 0 ? ", " + errorsPart : string.Empty)
            + (failedPart.Length > 0 ? ", " + failedPart : string.Empty);
        _text = loadedPart
            + (errorsPart.Length > 0 ? $", <color={Red}>{errorsPart}</color>" : string.Empty)
            + (failedPart.Length > 0 ? $", <color={Red}>{failedPart}</color>" : string.Empty);
    }
}

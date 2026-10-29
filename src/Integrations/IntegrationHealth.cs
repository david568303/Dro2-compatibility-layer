using CarXModManager.Logic;

namespace CarXModManager.Integrations;

/// <summary>
/// Whether each integration works, as the status line shows it. The mod manager is not counted as a mod in the status line, so its own
/// errors would otherwise be visible only in the log; an integration that cannot be installed, whose screen checks failed or that
/// stopped after errors is reported here and the line shows it in red. Cleared again when the integration works.
/// </summary>
internal static class IntegrationHealth
{
    private static readonly ProblemList Problems = new();
    private static readonly object Gate = new();

    /// <summary>Changes whenever the list changes.</summary>
    public static int Version { get { lock (Gate) return Problems.Version; } }

    /// <summary>Records that an integration (or a part of it) is not working.</summary>
    public static void Report(string integration, string reason)
    {
        lock (Gate) Problems.Report(integration, reason);
    }

    /// <summary>Records that an integration works.</summary>
    public static void Clear(string integration)
    {
        lock (Gate) Problems.Clear(integration);
    }

    /// <summary>The note for the status line, or empty when everything works.</summary>
    public static string Describe()
    {
        lock (Gate) return Problems.Describe();
    }
}

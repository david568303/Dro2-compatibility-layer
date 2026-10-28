namespace CarXModManager.Api;

/// <summary>How much a mod changes what other players or the server can observe.</summary>
public enum ModImpact
{
    /// <summary>The mod did not declare anything; treated as possibly affecting gameplay.</summary>
    Unknown = 0,
    /// <summary>Local-only visuals, audio or UI.</summary>
    Cosmetic = 1,
    /// <summary>Changes physics, stats, currency, network data or anything else that is not local-only.</summary>
    Gameplay = 2,
}

/// <summary>Put on the plugin class (the one with [BepInPlugin]) to declare its <see cref="ModImpact"/>.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ModImpactAttribute : Attribute
{
    public ModImpactAttribute(ModImpact impact) => Impact = impact;
    public ModImpact Impact { get; }
}

internal sealed class PanelRegistration
{
    public PanelRegistration(string modGuid, string title, Action draw)
    {
        ModGuid = modGuid;
        Title = title;
        Draw = draw;
    }

    public string ModGuid { get; }
    public string Title { get; }
    public Action Draw { get; }
    public int Errors;
    public bool Isolated;
}

/// <summary>
/// The surface other mods program against. Keep this small and stable: when the game updates, only
/// the manager internals should need changes, not every mod.
/// </summary>
public static class ModManagerApi
{
    public const int ApiVersion = 1;

    private static readonly object Gate = new();
    private static readonly List<PanelRegistration> PanelList = new();

    /// <summary>True while the overlay is open. Mods should ignore their own hotkeys while it is.</summary>
    public static bool IsOverlayOpen => OverlayController.Open;

    /// <summary>
    /// Adds a panel to the overlay's Panels tab. <paramref name="draw"/> runs inside OnGUI and may use GUILayout.
    /// If it throws three times the manager isolates the panel instead of letting it spam or break the overlay.
    /// Registering the same guid and title again replaces the previous panel.
    /// </summary>
    public static void RegisterPanel(string modGuid, string title, Action draw)
    {
        if (string.IsNullOrWhiteSpace(modGuid)) throw new ArgumentException("modGuid is required", nameof(modGuid));
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("title is required", nameof(title));
        ArgumentNullException.ThrowIfNull(draw);
        lock (Gate)
        {
            PanelList.RemoveAll(p => p.ModGuid == modGuid && p.Title == title);
            PanelList.Add(new PanelRegistration(modGuid, title, draw));
        }
    }

    public static void UnregisterPanels(string modGuid)
    {
        lock (Gate) PanelList.RemoveAll(p => p.ModGuid == modGuid);
    }

    internal static PanelRegistration[] SnapshotPanels()
    {
        lock (Gate) return PanelList.ToArray();
    }

    internal static bool HasPanel(string modGuid)
    {
        lock (Gate) return PanelList.Any(p => p.ModGuid == modGuid);
    }
}

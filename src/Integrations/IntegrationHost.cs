using BepInEx.Configuration;
using BepInEx.Logging;
using CarXModManager.Integrations.ServerBrowser;
using CarXModManager.Integrations.TrackPicker;
using CarXModManager.Ui;
using HarmonyLib;

namespace CarXModManager.Integrations;

/// <summary>
/// Installs the mod manager's integrations with the game's own screens (the server browser, the map picker) and runs them. Each one is
/// independent, has its own config section with an <c>Enabled</c> switch, and fails on its own: a hook that cannot be created,
/// a game type that no longer exists, a screen that does not have the structure the integration expects, or an error while running
/// is logged and leaves the game's screen alone, and the other integrations are not affected.
/// <c>[Integrations] Enabled = false</c> turns all of them off at once.
/// </summary>
internal static class IntegrationHost
{
    /// <summary>Errors in one integration's per-frame work after which it is no longer run this session.</summary>
    private const int ErrorsBeforeStandingDown = 5;

    /// <summary>
    /// The integrations, as factories: creating one loads the game types it uses, which can fail after a game update, so
    /// each is created inside its own error handling.
    /// </summary>
    private static readonly (string Name, Func<IIntegration> Create)[] Available =
    {
        ("ServerBrowser", () => new ServerBrowserIntegration()),
        ("TrackPicker", () => new TrackPickerIntegration()),
    };

    private static readonly List<IIntegration> Running = new();
    private static readonly Dictionary<string, int> Errors = new();

    /// <summary>
    /// Binds every integration's settings and creates the hooks of the enabled ones.
    /// </summary>
    /// <param name="config">The mod manager's config file (<c>BepInEx/config/carx.modmanager.cfg</c>).</param>
    /// <param name="log">The mod manager's log source.</param>
    /// <returns><c>true</c> if at least one integration installed, so the component that runs them is needed.</returns>
    public static bool Install(ConfigFile config, ManualLogSource log)
    {
        ConfigEntry<bool> enabled = config.Bind("Integrations", "Enabled", true,
            "Master switch for the integrations with the game's screens (server browser, map picker). false = the game's normal screens.");
        InputGuard.Enabled = config.Bind("Integrations", "BlockGameInput", true,
            "While you type into one of the added search buttons, hold the game's own input back so the keys do not also press the game's hotkeys. Turn off only if this causes trouble.").Value;

        var summary = new List<string>();
        var integrations = new List<IIntegration>();
        foreach ((string name, Func<IIntegration> create) in Available)
        {
            try
            {
                IIntegration integration = create();
                integration.BindConfig(config);
                integrations.Add(integration);
            }
            catch (Exception exception)
            {
                log.LogError($"INTEGRATION_FAILED — {name} could not be created: {exception}");
                IntegrationHealth.Report(name, "could not be created");
                summary.Add($"{name}: FAILED");
            }
        }

        if (!enabled.Value)
        {
            log.LogInfo("INTEGRATIONS_DISABLED — [Integrations] Enabled is false; none was installed.");
            return false;
        }

        var harmony = new Harmony(Plugin.PluginGuid + ".integrations");
        foreach (IIntegration integration in integrations)
        {
            if (!integration.IsEnabled)
            {
                summary.Add($"{integration.Name}: off");
                continue;
            }
            bool installed;
            try { installed = integration.Install(harmony, log); }
            catch (Exception exception)
            {
                log.LogError($"INTEGRATION_FAILED — {integration.Name}: {exception}");
                installed = false;
            }
            if (installed) Running.Add(integration);
            else IntegrationHealth.Report(integration.Name, "could not be installed");
            summary.Add($"{integration.Name}: {(installed ? "on" : "FAILED")}");
        }

        log.LogInfo($"INTEGRATIONS_LOADED — {string.Join(", ", summary)}.");
        return Running.Count > 0;
    }

    /// <summary>Runs every installed integration's per-frame work (from the host component's <c>Update</c>). One that keeps failing is stood down; the others go on.</summary>
    public static void Update()
    {
        TextCapture.Update();
        foreach (IIntegration integration in Running)
        {
            if (Errors.GetValueOrDefault(integration.Name) >= ErrorsBeforeStandingDown) continue;
            try { integration.Update(); }
            catch (Exception exception) { ReportError(integration.Name, "Update", exception); }
        }
    }

    /// <summary>Counts an error in an integration's per-frame work, logs the first few, and stands the integration down when there are too many.</summary>
    public static void ReportError(string integration, string where, Exception exception)
    {
        int count = Errors.GetValueOrDefault(integration) + 1;
        Errors[integration] = count;
        if (count <= 3) Plugin.LogSource.LogError($"{integration.ToUpperInvariant()}_ERROR — {where}: {exception}");
        if (count == ErrorsBeforeStandingDown)
        {
            Plugin.LogSource.LogError($"{integration.ToUpperInvariant()}_STOOD_DOWN — too many errors; {integration} is off until the game restarts. The game itself is unaffected.");
            IntegrationHealth.Report(integration, "stopped after errors");
        }
    }

    /// <summary>Ends anything still running and lets the game's input through again. Called when the host component is destroyed and when the game quits.</summary>
    public static void Shutdown()
    {
        foreach (IIntegration integration in Running)
        {
            try { integration.Shutdown(); }
            catch (Exception exception) { Plugin.LogSource.LogWarning($"INTEGRATION_SHUTDOWN_FAILED — {integration.Name}: {exception.Message}"); }
        }
        TextCapture.Shutdown();
        InputGuard.SetHolding(false);
    }
}

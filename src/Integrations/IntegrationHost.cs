using BepInEx.Configuration;
using BepInEx.Logging;
using CarXModManager.Integrations.ServerBrowser;
using CarXModManager.Integrations.TrackPicker;
using CarXModManager.Overlay;
using HarmonyLib;

namespace CarXModManager.Integrations;

/// <summary>
/// Installs the mod manager's integrations with the game's own screens (the server browser, the map picker). Each one is
/// independent, has its own config section with an <c>Enabled</c> switch, and fails on its own: a hook that cannot be created
/// or an error while running is logged and leaves the game's screen alone. <c>[Integrations] Enabled = false</c> turns all of
/// them off at once.
/// </summary>
internal static class IntegrationHost
{
    /// <summary>
    /// Binds every integration's settings, creates the hooks of the enabled ones and registers their panels.
    /// </summary>
    /// <param name="config">The mod manager's config file (<c>BepInEx/config/carx.modmanager.cfg</c>).</param>
    /// <param name="log">The mod manager's log source.</param>
    /// <returns><c>true</c> if at least one integration registered a panel, so the overlay component is needed.</returns>
    public static bool Install(ConfigFile config, ManualLogSource log)
    {
        ConfigEntry<bool> enabled = config.Bind("Integrations", "Enabled", true,
            "Master switch for the integrations with the game's screens (server browser, map picker). false = the game's normal screens.");
        InputGuard.Enabled = config.Bind("Integrations", "BlockGameInput", true,
            "While the pointer is over one of the integrations' panels or you are typing in one, hold the game's own input back so keys and clicks do not also reach the game. Turn off only if this causes trouble.").Value;

        IIntegration[] integrations = { new ServerBrowserIntegration(), new TrackPickerIntegration() };
        foreach (IIntegration integration in integrations) integration.BindConfig(config);

        if (!enabled.Value)
        {
            log.LogInfo("INTEGRATIONS_DISABLED — [Integrations] Enabled is false; none was installed.");
            return false;
        }

        var harmony = new Harmony(Plugin.PluginGuid + ".integrations");
        var summary = new List<string>();
        bool anyInstalled = false;
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
            anyInstalled |= installed;
            summary.Add($"{integration.Name}: {(installed ? "on" : "FAILED")}");
        }

        log.LogInfo($"INTEGRATIONS_LOADED — {string.Join(", ", summary)}.");
        return anyInstalled;
    }
}

using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace CarXModManager.Integrations;

/// <summary>
/// One independent quality-of-life improvement. Every integration has its own config section with an <c>Enabled</c> switch,
/// so a player can turn any one off without losing the others, and one integration failing never affects the rest.
/// </summary>
internal interface IIntegration
{
    /// <summary>Short name; also the config section name, e.g. <c>ServerBrowser</c>.</summary>
    string Name { get; }

    /// <summary>The value of the integration's <c>Enabled</c> setting. Only valid after <see cref="BindConfig"/>.</summary>
    bool IsEnabled { get; }

    /// <summary>Creates the integration's config entries (including <c>Enabled</c>). Called for every integration, enabled or not.</summary>
    void BindConfig(ConfigFile config);

    /// <summary>The hooks the integration wants on game methods. Also read by <c>tools/HookCheck</c> to verify them against the game's assemblies.</summary>
    IReadOnlyList<HookSpec> Hooks { get; }

    /// <summary>
    /// Creates the integration's hooks. Only called when the integration is enabled.
    /// Returns <c>true</c> if the integration is usable; <c>false</c> if its required hooks could not be created (already logged).
    /// </summary>
    bool Install(Harmony harmony, ManualLogSource log);

    /// <summary>
    /// Called every frame while the game runs (only for integrations that installed). The integration adds its controls to the game's
    /// screens from here (after the screen has settled) and keeps them up to date. Exceptions are caught and counted by the host.
    /// </summary>
    void Update();

    /// <summary>Called when the game quits or the mod manager unloads: end anything still running (a search being typed, ...).</summary>
    void Shutdown();
}

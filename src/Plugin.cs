using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Object = UnityEngine.Object;

namespace CarXModManager;

/// <summary>
/// The compatibility layer's BepInEx plugin. It shows the always-on status line ("N mods loaded") and carries the
/// shared API other mods build on (<see cref="Api.GameHooks"/>).
/// A mod that uses the API declares <c>[BepInDependency(CarXModManager.Plugin.PluginGuid)]</c> so BepInEx loads this
/// plugin first. See docs/API.md in the repository.
/// </summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BasePlugin
{
    /// <summary>The plugin's BepInEx GUID. Use it in <c>[BepInDependency]</c> to require this plugin.</summary>
    public const string PluginGuid = "carx.modmanager";

    /// <summary>
    /// The plugin's display name. BepInEx also uses it as the log source name, which is how the status line tells
    /// this plugin's messages from other mods' messages.
    /// </summary>
    public const string PluginName = "CarX Mod Manager";

    /// <summary>
    /// The plugin's version. Dev builds do not bump it, so it does not identify a build; use <see cref="BuildId"/>.
    /// </summary>
    public const string PluginVersion = "0.1.0";

    /// <summary>
    /// Short commit hash this build was made from (for example <c>79e59c7</c>), or <c>local</c> for a build made
    /// outside CI. It is also written to the log at startup, so a log file says exactly which build produced it.
    /// </summary>
    public static readonly string BuildId = ReadBuildId();

    /// <summary>Reads the commit hash that the build stamped into the assembly's informational version ("0.1.0+hash").</summary>
    private static string ReadBuildId()
    {
        string? info = typeof(Plugin).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion;
        int plus = info?.IndexOf('+') ?? -1;
        return info != null && plus >= 0 && info.Length > plus + 1
            ? info.Substring(plus + 1, Math.Min(7, info.Length - plus - 1))
            : "local";
    }

    /// <summary>This plugin's own log source, shown as [CarX Mod Manager] in LogOutput.log.</summary>
    internal static ManualLogSource LogSource = null!;

    /// <summary>The "Display / ShowStatusLine" setting in <c>BepInEx/config/carx.modmanager.cfg</c>.</summary>
    internal static ConfigEntry<bool> ShowStatusLine = null!;

    private StatusLineBehaviour? _statusLine;

    /// <summary>Called by BepInEx at startup: binds the setting, starts watching the log and adds the drawing component.</summary>
    public override void Load()
    {
        LogSource = Log;
        ShowStatusLine = Config.Bind("Display", "ShowStatusLine", true,
            "Show the \"N mods loaded\" line in the bottom-left corner of the screen.");

        ModStatusTracker.StartListening();
        _statusLine = AddComponent<StatusLineBehaviour>();
        Log.LogInfo($"COMPATLAYER_LOADED — dev build {BuildId}.");
    }

    /// <summary>Called by BepInEx when the plugin is unloaded: removes the drawing component.</summary>
    public override bool Unload()
    {
        if (_statusLine != null)
        {
            Object.Destroy(_statusLine);
            _statusLine = null;
        }
        return true;
    }
}

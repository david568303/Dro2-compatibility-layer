using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Object = UnityEngine.Object;

namespace CarXModManager;

[BepInPlugin(PluginGuid, "CarX Mod Manager", PluginVersion)]
public sealed class Plugin : BasePlugin
{
    public const string PluginGuid = "carx.modmanager";
    public const string PluginVersion = "0.1.0";

    /// <summary>Short commit hash of the build ("local" for a build made outside CI). Versions are not bumped for dev builds.</summary>
    public static readonly string BuildId = ReadBuildId();

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

    internal static ManualLogSource Logger = null!;
    internal static ConfigEntry<bool> ShowStatusLine = null!;
    private StatusBehaviour? _status;

    public override void Load()
    {
        Logger = Log;
        ShowStatusLine = Config.Bind("Display", "ShowStatusLine", true,
            "Show the \"N mods loaded\" line in the bottom-left corner of the screen.");

        ModStatus.Install();
        _status = AddComponent<StatusBehaviour>();
        Log.LogInfo($"CARXMM_LOADED — dev build {BuildId}.");
    }

    public override bool Unload()
    {
        if (_status != null)
        {
            Object.Destroy(_status);
            _status = null;
        }
        return true;
    }
}

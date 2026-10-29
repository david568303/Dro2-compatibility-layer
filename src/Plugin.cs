using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using CarXModManager.Api;
using Object = UnityEngine.Object;

namespace CarXModManager;

[BepInPlugin(PluginGuid, "CarX Mod Manager", PluginVersion)]
[ModImpact(ModImpact.Cosmetic)]
public sealed class Plugin : BasePlugin
{
    public const string PluginGuid = "carx.modmanager";
    public const string PluginVersion = "0.2.0";

    internal static ManualLogSource Logger = null!;
    // A key name rather than ConfigEntry<Key>: the name is resolved against the game's real Key enum at runtime,
    // so no enum number is baked into this DLL.
    internal static ConfigEntry<string> ToggleKey = null!;
    internal static ConfigEntry<bool> OnlineSafeMode = null!;
    internal static ConfigEntry<float> UiScale = null!;
    internal static ConfigEntry<bool> BlockGameMouse = null!;
    private OverlayBehaviour? _overlay;

    public override void Load()
    {
        Logger = Log;
        ToggleKey = Config.Bind("Overlay", "ToggleKey", "Delete",
            "Opens and closes the mod manager overlay. A name from UnityEngine.InputSystem.Key, e.g. Delete, Insert, F10. Avoid Home: ReShade uses it for its own overlay.");
        OnlineSafeMode = Config.Bind("Safety", "OnlineSafeMode", true,
            "Flag mods that are not declared cosmetic (ModImpact attribute) so they can be kept out of online play. v0.1 only warns; it does not block yet.");
        BlockGameMouse = Config.Bind("Overlay", "BlockGameMouse", true,
            "While the overlay is open, stop the game from seeing the mouse (disables the Input System mouse device). Turn off if the menu stops responding to clicks.");
        UiScale = Config.Bind("Overlay", "UiScale", 1f,
            new ConfigDescription("Extra scale on top of the automatic 1080p-relative scaling.", new AcceptableValueRange<float>(0.5f, 2.5f)));

        LogBuffer.Install();
        if (string.Equals(ToggleKey.Value.Trim(), "Home", StringComparison.OrdinalIgnoreCase))
            Log.LogWarning("CARXMM_KEY_WARNING — Home is ReShade's default overlay key; pick another ToggleKey in carx.modmanager.cfg.");

        _overlay = AddComponent<OverlayBehaviour>();
        Log.LogInfo($"CARXMM_LOADED — v{PluginVersion}, API v{ModManagerApi.ApiVersion}; press {ToggleKey.Value} to open the overlay.");
    }

    public override bool Unload()
    {
        OverlayController.Shutdown();
        if (_overlay != null)
        {
            Object.Destroy(_overlay);
            _overlay = null;
        }
        return true;
    }
}

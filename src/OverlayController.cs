using CarXModManager.Api;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarXModManager;

/// <summary>Overlay logic and drawing. Drawn with IMGUI so it renders identically on DX11 and DX12 and sits under ReShade.</summary>
internal static class OverlayController
{
    private static readonly string[] TabNames = { "Mods", "Panels", "Conflicts", "Log", "Settings" };
    private static readonly HashSet<string> ExpandedPanels = new();
    private static readonly Color Dim = new(0.7f, 0.7f, 0.7f, 1f);
    private static readonly Color Warn = new(1f, 0.8f, 0.3f, 1f);
    private static readonly Color Bad = new(1f, 0.45f, 0.4f, 1f);
    private static readonly Color Good = new(0.5f, 0.9f, 0.5f, 1f);

    private static int _tab;
    private static Vector2 _modsScroll, _panelsScroll, _conflictsScroll, _logScroll, _settingsScroll;
    private static List<ModEntry> _mods = new();
    private static List<PatchConflict> _conflicts = new();
    private static string _logText = string.Empty;
    private static int _logVersion = -1;
    private static bool _onGuiLogged;
    private static bool _hotkeyWarned;
    private static bool _stopped;
    private static int _faults;
    private static CursorLockMode _previousLock;
    private static bool _previousVisible;

    public static bool Open { get; private set; }

    public static void Tick()
    {
        if (_stopped) return;
        if (HotkeyPressed()) SetOpen(!Open);
    }

    public static void EnforceCursor()
    {
        if (!Open) return;
        // The game re-locks the cursor on its own schedule, so claim it every frame while we are open.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public static void Shutdown() => SetOpen(false);

    public static void ReportFault(string where, Exception exception)
    {
        _faults++;
        if (_faults <= 3) Plugin.Logger.LogWarning($"CARXMM_FAULT — {where}: {exception}");
        if (_faults >= 10 && !_stopped)
        {
            _stopped = true;
            SetOpen(false);
            Plugin.Logger.LogError("CARXMM_DISABLED — too many overlay errors; the overlay is off for this session. Mods are unaffected.");
        }
    }

    private static bool HotkeyPressed()
    {
        try
        {
            Keyboard? keyboard = Keyboard.current;
            Key key = Plugin.ToggleKey.Value;
            return keyboard != null && key != Key.None && keyboard[key].wasPressedThisFrame;
        }
        catch (Exception exception)
        {
            if (!_hotkeyWarned)
            {
                _hotkeyWarned = true;
                Plugin.Logger.LogWarning($"CARXMM_HOTKEY_UNAVAILABLE — {exception.Message}");
            }
            return false;
        }
    }

    private static void SetOpen(bool open)
    {
        if (open == Open) return;
        Open = open;
        if (open)
        {
            _previousLock = Cursor.lockState;
            _previousVisible = Cursor.visible;
            Rescan();
        }
        else
        {
            Cursor.lockState = _previousLock;
            Cursor.visible = _previousVisible;
        }
        Plugin.Logger.LogInfo(open ? "CARXMM_OPEN" : "CARXMM_CLOSED");
    }

    private static void Rescan()
    {
        _mods = ModCatalog.Scan();
        _conflicts = PatchConflicts.Scan();
    }

    public static void Draw()
    {
        if (!_onGuiLogged)
        {
            _onGuiLogged = true;
            Plugin.Logger.LogInfo($"CARXMM_ONGUI_FIRED — IMGUI is running. Graphics API: {SystemInfo.graphicsDeviceType}; Unity {Application.unityVersion}; game {Application.version}.");
        }
        if (!Open || _stopped) return;

        float scale = Mathf.Max(0.5f, Plugin.UiScale.Value) * (Screen.height / 1080f);
        Matrix4x4 previous = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
        try { DrawWindow(Screen.width / scale, Screen.height / scale); }
        finally { GUI.matrix = previous; }
    }

    private static void DrawWindow(float width, float height)
    {
        float w = Mathf.Min(980f, width - 40f);
        float h = Mathf.Min(640f, height - 40f);
        var window = new Rect((width - w) / 2f, (height - h) / 2f, w, h);
        GUI.Box(window, string.Empty);
        GUI.Box(window, string.Empty); // second pass so the text behind stays readable

        GUILayout.BeginArea(new Rect(window.x + 12f, window.y + 10f, window.width - 24f, window.height - 20f));
        Ui.Label($"CarX Mod Manager v{Plugin.PluginVersion}   |   {Plugin.ToggleKey.Value} closes", Dim);

        Ui.BeginHorizontal();
        for (int i = 0; i < TabNames.Length; i++)
            if (Ui.Button(i == _tab ? $"[ {TabNames[i]} ]" : TabNames[i])) _tab = i;
        Ui.EndHorizontal();
        Ui.Space(6f);

        switch (_tab)
        {
            case 0: DrawMods(); break;
            case 1: DrawPanels(); break;
            case 2: DrawConflicts(); break;
            case 3: DrawLog(); break;
            default: DrawSettings(); break;
        }
        GUILayout.EndArea();
    }

    private static void DrawMods()
    {
        _modsScroll = Ui.BeginScroll(_modsScroll);
        Ui.Label("v0.1 lists loaded mods. Turning mods on/off from here needs the preloader step (next milestone).", Dim);
        foreach (ModEntry mod in _mods)
        {
            Ui.BeginBox();
            Ui.BeginHorizontal();
            Ui.Label($"{mod.Name}  v{mod.Version}");
            Ui.Flex();
            (string tag, Color color) = mod.Impact switch
            {
                ModImpact.Cosmetic => ("cosmetic", Good),
                ModImpact.Gameplay => ("gameplay", Bad),
                _ => ("undeclared", Warn),
            };
            Ui.Label(tag, color);
            Ui.EndHorizontal();
            Ui.Label($"{mod.Guid}   {mod.File}" + (mod.HasPanel ? "   [has panel]" : string.Empty), Dim);
            if (mod.Dependencies.Length > 0) Ui.Label($"needs: {mod.Dependencies}", Dim);
            if (Plugin.OnlineSafeMode.Value && mod.Impact != ModImpact.Cosmetic)
                Ui.Label("online-safe: not declared cosmetic, so it would be kept out of online play", Warn);
            Ui.EndVertical();
        }
        Ui.EndScroll();
    }

    private static void DrawPanels()
    {
        _panelsScroll = Ui.BeginScroll(_panelsScroll);
        PanelRegistration[] panels = ModManagerApi.SnapshotPanels();
        if (panels.Length == 0)
            Ui.Label("No mod has registered a panel. Mods call ModManagerApi.RegisterPanel(guid, title, draw).", Dim);
        foreach (PanelRegistration panel in panels)
        {
            string key = panel.ModGuid + "/" + panel.Title;
            bool expanded = ExpandedPanels.Contains(key);
            Ui.BeginBox();
            Ui.BeginHorizontal();
            bool now = Ui.Toggle(expanded, $"{panel.Title}  ({panel.ModGuid})");
            if (now != expanded) { if (now) ExpandedPanels.Add(key); else ExpandedPanels.Remove(key); }
            if (panel.Isolated && Ui.Button("Re-enable")) { panel.Isolated = false; panel.Errors = 0; }
            Ui.EndHorizontal();
            if (now) DrawPanelBody(panel);
            Ui.EndVertical();
        }
        Ui.EndScroll();
    }

    private static void DrawPanelBody(PanelRegistration panel)
    {
        if (panel.Isolated)
        {
            Ui.Label("Isolated after repeated errors; the rest of the manager keeps working.", Bad);
            return;
        }
        Ui.BeginVertical();
        try { panel.Draw(); }
        catch (Exception exception)
        {
            panel.Errors++;
            if (panel.Errors == 1) Plugin.Logger.LogWarning($"CARXMM_PANEL_ERROR — {panel.ModGuid}/{panel.Title}: {exception}");
            if (panel.Errors >= 3)
            {
                panel.Isolated = true;
                Plugin.Logger.LogWarning($"CARXMM_PANEL_ISOLATED — {panel.ModGuid}/{panel.Title}");
            }
        }
        finally { Ui.EndVertical(); }
    }

    private static void DrawConflicts()
    {
        Ui.BeginHorizontal();
        if (Ui.Button("Rescan")) Rescan();
        Ui.Label($"{_conflicts.Count} method(s) patched by more than one mod", Dim);
        Ui.EndHorizontal();
        _conflictsScroll = Ui.BeginScroll(_conflictsScroll);
        if (_conflicts.Count == 0)
            Ui.Label("No Harmony conflicts found. Hooks installed by other means are not visible here.", Good);
        foreach (PatchConflict conflict in _conflicts)
        {
            Ui.BeginBox();
            Ui.Label(conflict.Method);
            Ui.Label("owners: " + string.Join(", ", conflict.Owners), Dim);
            if (conflict.CanSkipOriginal)
                Ui.Label("a prefix can skip the original method: the other mods may never run", Bad);
            Ui.EndVertical();
        }
        Ui.EndScroll();
    }

    private static void DrawLog()
    {
        if (_logVersion != LogBuffer.Instance.Version)
        {
            _logVersion = LogBuffer.Instance.Version;
            _logText = string.Join("\n", LogBuffer.Instance.Snapshot(120));
            _logScroll.y = 1000000f; // stick to the newest line
        }
        _logScroll = Ui.BeginScroll(_logScroll);
        Ui.Label(_logText);
        Ui.EndScroll();
    }

    private static void DrawSettings()
    {
        _settingsScroll = Ui.BeginScroll(_settingsScroll);
        Plugin.OnlineSafeMode.Value = Ui.Toggle(Plugin.OnlineSafeMode.Value, "Online-safe mode (flag mods not declared cosmetic)");
        Ui.Label($"UI scale: {Plugin.UiScale.Value:0.00}");
        float scale = Ui.Slider(Plugin.UiScale.Value, 0.5f, 2.5f);
        if (!Mathf.Approximately(scale, Plugin.UiScale.Value)) Plugin.UiScale.Value = scale;
        Ui.Space(8f);
        Ui.Label($"Toggle key: {Plugin.ToggleKey.Value}  (edit BepInEx/config/{Plugin.PluginGuid}.cfg)", Dim);
        Ui.Label($"Graphics API: {SystemInfo.graphicsDeviceType}", Dim);
        Ui.Label($"Unity {Application.unityVersion}   game {Application.version}", Dim);
        Ui.Label($"Manager v{Plugin.PluginVersion}   mod API v{ModManagerApi.ApiVersion}", Dim);
        Ui.EndScroll();
    }
}

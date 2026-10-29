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
    private static int _probeFrames;
    private static bool _hotkeyWarned;
    private static string? _keyName;
    private static Key _key;
    private static bool _keyValid;
    private static bool _stopped;
    private static int _faults;
    private static CursorLockMode _previousLock;
    private static bool _previousVisible;
    private static bool _rebinding;
    private static Mouse? _capturedMouse;
    private static bool _mouseWarned;
    private static Key[]? _bindableKeys;

    public static bool Open { get; private set; }

    public static void Tick()
    {
        if (_stopped) return;
        if (_rebinding) { PollRebind(); return; }
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
            if (keyboard == null || !TryGetToggleKey(out Key key)) return false;
            return keyboard[key].wasPressedThisFrame;
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

    private static bool TryGetToggleKey(out Key key)
    {
        string name = Plugin.ToggleKey.Value.Trim();
        if (name != _keyName)
        {
            _keyName = name;
            // (int)0 is Key.None. Checked by value so the DLL never depends on the enum's member numbers.
            _keyValid = Enum.TryParse(name, true, out _key) && (int)_key != 0;
            if (!_keyValid) Plugin.Logger.LogWarning($"CARXMM_KEY_INVALID — '{name}' is not a key name; the overlay cannot be opened until ToggleKey is fixed.");
        }
        key = _key;
        return _keyValid;
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
            if (Plugin.BlockGameMouse.Value) CaptureMouse(true);
        }
        else
        {
            _rebinding = false;
            CaptureMouse(false);
            Cursor.lockState = _previousLock;
            Cursor.visible = _previousVisible;
        }
        Plugin.Logger.LogInfo(open ? "CARXMM_OPEN" : "CARXMM_CLOSED");
    }

    /// <summary>
    /// "Hijacks" the mouse: while the overlay is open the game's Mouse device is disabled in the Input System, so
    /// clicks, movement and scrolling no longer reach the game. IMGUI keeps working because it is fed by the native
    /// event queue, not by that device. Always re-enabled on close, on Unload and on game exit.
    /// </summary>
    private static void CaptureMouse(bool capture)
    {
        try
        {
            if (capture)
            {
                Mouse? mouse = Mouse.current;
                if (mouse == null || _capturedMouse != null) return;
                try { InputSystem.DisableDevice(mouse, false); }
                catch (MissingMethodException) { InputSystem.DisableDevice(mouse); }
                _capturedMouse = mouse;
            }
            else if (_capturedMouse != null)
            {
                InputSystem.EnableDevice(_capturedMouse);
                _capturedMouse = null;
            }
        }
        catch (Exception exception)
        {
            if (_mouseWarned) return;
            _mouseWarned = true;
            Plugin.Logger.LogWarning($"CARXMM_MOUSE_CAPTURE_FAILED — the game will still see the mouse. {exception.GetType().Name}: {exception.Message}");
        }
    }

    public static void BeginRebind() => _rebinding = true;

    private static void PollRebind()
    {
        try
        {
            Keyboard? keyboard = Keyboard.current;
            if (keyboard == null) return;
            _bindableKeys ??= Enum.GetValues(typeof(Key)).Cast<Key>()
                .Where(key => (int)key != 0 && key.ToString() != "IMESelected")
                .Distinct()
                .ToArray();
            foreach (Key key in _bindableKeys)
            {
                if (!keyboard[key].wasPressedThisFrame) continue;
                _rebinding = false;
                if (key.ToString() == "Escape") return; // cancel
                Plugin.ToggleKey.Value = key.ToString();
                Plugin.Logger.LogInfo($"CARXMM_KEY_BOUND — toggle key is now {KeyLabel()}");
                return;
            }
        }
        catch (Exception exception)
        {
            _rebinding = false;
            Plugin.Logger.LogWarning($"CARXMM_REBIND_FAILED — {exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>
    /// The configured key as both its Input System name and the label the current keyboard layout gives it, so
    /// "Delete" vs "Supr" is never guesswork (e.g. Key.Delete vs the numpad's Key.NumpadPeriod).
    /// </summary>
    private static string KeyLabel()
    {
        string name = Plugin.ToggleKey.Value.Trim();
        try
        {
            Keyboard? keyboard = Keyboard.current;
            if (keyboard != null && TryGetToggleKey(out Key key))
            {
                string shown = keyboard[key].displayName;
                if (!string.IsNullOrWhiteSpace(shown) && !string.Equals(shown, name, StringComparison.OrdinalIgnoreCase))
                    return $"{name} (your keyboard calls it \"{shown}\")";
            }
        }
        catch { /* label only */ }
        return name;
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
        if (_probeFrames < 3)
        {
            // Three passes so both the Layout and Repaint events see the same controls.
            _probeFrames++;
            try { Ui.Probe(); }
            finally { Ui.Unwind(); }
            if (_probeFrames == 3)
                Plugin.Logger.LogInfo($"CARXMM_UI_PROBE — IMGUI primitives unavailable in this game build: {Ui.MissingSummary}.");
        }
        if (!Open || _stopped) return;

        float scale = Mathf.Max(0.5f, Plugin.UiScale.Value) * (Screen.height / 1080f);
        Matrix4x4 previous = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
        try { DrawWindow(Screen.width / scale, Screen.height / scale); }
        finally
        {
            Ui.Unwind(); // an exception mid-frame must not leave IMGUI groups open
            GUI.matrix = previous;
        }
    }

    private static void DrawWindow(float width, float height)
    {
        float w = Mathf.Min(960f, width - 40f);
        float h = Mathf.Min(620f, height - 40f);
        Ui.BeginPanel(new Rect((width - w) / 2f, (height - h) / 2f, w, h));

        Ui.Title($"CARX MOD MANAGER   v{Plugin.PluginVersion}");
        Ui.BeginHorizontal();
        for (int i = 0; i < TabNames.Length; i++)
            if (Ui.Button(TabNames[i], i == _tab)) _tab = i;
        Ui.EndHorizontal();
        Ui.Space(4f);

        switch (_tab)
        {
            case 0: DrawMods(); break;
            case 1: DrawPanels(); break;
            case 2: DrawConflicts(); break;
            case 3: DrawLog(); break;
            default: DrawSettings(); break;
        }

        Ui.Space(2f);
        Ui.Label($"{Plugin.ToggleKey.Value} closes   |   mouse is captured while this is open", Dim);
        Ui.EndPanel();
    }

    private static void DrawMods()
    {
        _modsScroll = Ui.BeginScroll(_modsScroll);
        Ui.Label("v0.1 lists loaded mods. Turning mods on/off from here needs the preloader step (next milestone).", Dim);
        foreach (ModEntry mod in _mods)
        {
            Ui.BeginCard();
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
            Ui.EndCard();
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
            Ui.BeginCard();
            Ui.BeginHorizontal();
            bool now = Ui.Toggle(expanded, $"{panel.Title}  ({panel.ModGuid})");
            if (now != expanded) { if (now) ExpandedPanels.Add(key); else ExpandedPanels.Remove(key); }
            if (panel.Isolated && Ui.Button("Re-enable")) { panel.Isolated = false; panel.Errors = 0; }
            Ui.EndHorizontal();
            if (now) DrawPanelBody(panel);
            Ui.EndCard();
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
            Ui.BeginCard();
            Ui.Label(conflict.Method);
            Ui.Label("owners: " + string.Join(", ", conflict.Owners), Dim);
            if (conflict.CanSkipOriginal)
                Ui.Label("a prefix can skip the original method: the other mods may never run", Bad);
            Ui.EndCard();
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

        Ui.BeginCard();
        Ui.Label("Menu key");
        Ui.Label(_rebinding ? "Press the new key now...  (Esc cancels)" : $"Toggle key: {KeyLabel()}", _rebinding ? Warn : Dim);
        if (!_rebinding && Ui.Button("Change key")) BeginRebind();
        Ui.EndCard();

        Ui.BeginCard();
        Ui.Label("Behaviour");
        Plugin.BlockGameMouse.Value = Ui.Toggle(Plugin.BlockGameMouse.Value, "Capture mouse while open");
        Plugin.OnlineSafeMode.Value = Ui.Toggle(Plugin.OnlineSafeMode.Value, "Online-safe mode");
        Ui.Label($"UI scale: {Plugin.UiScale.Value:0.00}", Dim);
        float scale = Ui.Slider(Plugin.UiScale.Value, 0.5f, 2.5f);
        if (!Mathf.Approximately(scale, Plugin.UiScale.Value)) Plugin.UiScale.Value = scale;
        Ui.EndCard();

        Ui.BeginCard();
        Ui.Label("About");
        Ui.Label($"Graphics API: {SystemInfo.graphicsDeviceType}", Dim);
        Ui.Label($"Unity {Application.unityVersion}   game {Application.version}", Dim);
        Ui.Label($"Manager v{Plugin.PluginVersion}   mod API v{ModManagerApi.ApiVersion}", Dim);
        Ui.Label($"IMGUI primitives unavailable in this build: {Ui.MissingSummary}", Dim);
        Ui.Label($"Theme: {(Theme.Ready ? "flat" : "plain (styling unavailable)")}", Dim);
        Ui.EndCard();

        Ui.EndScroll();
    }
}

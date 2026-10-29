using CarXModManager.Logic;
using UnityEngine;

namespace CarXModManager.Ui;

/// <summary>
/// Typing into the mod manager's search buttons. A search button is an ordinary button of the game's screen that shows the text
/// being typed; clicking it starts a <em>capture</em>: typed characters edit the text, Backspace erases, Delete clears, Enter or
/// Escape or a click anywhere else ends it. While a capture runs, the game's own input is held back (see <see cref="InputGuard"/>)
/// so the keys typed do not also press the game's hotkeys.
/// <para>
/// This build of the game has no usable IMGUI text field, so the keys are read directly. Two sources report them: the Input System
/// package's keyboard (<see cref="InputSystemKeyboard"/>) and IMGUI key events (the host component forwards them to
/// <see cref="OnKeyDown"/>). Both normally report every key press, so for each kind of key the first source to report is used and
/// the other is ignored (<see cref="KeySourceLock"/>); which one it was is logged once as <c>TEXTINPUT_SOURCE</c>.
/// </para>
/// </summary>
internal static class TextCapture
{
    private static readonly KeySourceLock CharacterSource = new(), BackspaceSource = new();
    private static readonly KeyRepeat BackspaceRepeat = new();

    private static object? _owner;
    private static Func<string>? _getText;
    private static Action<string>? _setText;
    private static Action? _ended;
    private static bool _characterSourceLogged;
    private static int _beganInFrame = -1;

    /// <summary>True while a search button is capturing the keyboard.</summary>
    public static bool IsActive => _owner != null;

    /// <summary>True while the given search button is the one capturing the keyboard.</summary>
    public static bool IsActiveFor(object owner) => _owner != null && ReferenceEquals(_owner, owner);

    /// <summary>True for half of every second: the text cursor of the capturing button blinks with it.</summary>
    public static bool CaretVisible => ((int)(Time.unscaledTime * 2f) & 1) == 0;

    /// <summary>Starts capturing the keyboard for a search button. A capture that is already running ends first.</summary>
    /// <param name="owner">Identifies the button (any object; compared by reference).</param>
    /// <param name="getText">Reads the text being edited.</param>
    /// <param name="setText">Stores the edited text.</param>
    /// <param name="ended">Called when the capture ends, for whatever reason, so the button can show it is idle again.</param>
    public static void Begin(object owner, Func<string> getText, Action<string> setText, Action? ended = null)
    {
        if (_owner != null) End();
        _owner = owner;
        _getText = getText;
        _setText = setText;
        _ended = ended;
        _beganInFrame = Time.frameCount;
        BackspaceRepeat.Reset();
        InputSystemKeyboard.DiscardTyped();
        InputSystemKeyboard.Listen();
        InputGuard.SetHolding(true);
    }

    /// <summary>Ends the capture, if one is running: the keyboard goes back to the game.</summary>
    public static void End()
    {
        if (_owner == null) return;
        Action? ended = _ended;
        _owner = null;
        _getText = null;
        _setText = null;
        _ended = null;
        BackspaceRepeat.Reset();
        InputGuard.SetHolding(false);
        try { ended?.Invoke(); }
        catch (Exception exception) { Plugin.LogSource.LogError($"TEXTINPUT_ERROR — while ending the capture: {exception}"); }
    }

    /// <summary>Per-frame work: applies the characters and editing keys reported by the Input System. Call once a frame.</summary>
    public static void Update()
    {
        if (_owner == null)
        {
            InputSystemKeyboard.DiscardTyped();
            return;
        }
        try
        {
            InputSystemKeyboard.Listen();
            while (InputSystemKeyboard.TryTakeCharacter(out char character)) Type(KeySource.InputSystem, character);

            EditingKeys keys = InputSystemKeyboard.ReadEditingKeys();
            // a click that began the capture also went down this frame (a quick click is pressed and released within one frame): not a click elsewhere
            bool clickedElsewhere = Time.frameCount != _beganInFrame && InputSystemKeyboard.MousePressedThisFrame();
            if (keys.EnterPressed || keys.EscapePressed || clickedElsewhere)
            {
                End();
                return;
            }
            if (keys.DeletePressed) SetText(string.Empty);
            int erase = BackspaceRepeat.Update(keys.BackspaceHeld, Time.unscaledTime);
            for (int i = 0; i < erase; i++) EraseLast(KeySource.InputSystem);
        }
        catch (Exception exception)
        {
            Plugin.LogSource.LogError($"TEXTINPUT_ERROR — {exception}");
            End();
        }
    }

    /// <summary>Handles an IMGUI key-down event (forwarded by the host component, which runs in <c>OnGUI</c>).</summary>
    public static void OnKeyDown(Event keyEvent)
    {
        if (_owner == null) return;
        try
        {
            KeyCode key = keyEvent.keyCode;
            if (key is KeyCode.Return or KeyCode.KeypadEnter or KeyCode.Escape)
            {
                End();
                keyEvent.Use();
            }
            else if (key == KeyCode.Delete)
            {
                SetText(string.Empty);
                keyEvent.Use();
            }
            else if (key == KeyCode.Backspace)
            {
                EraseLast(KeySource.Imgui);
                keyEvent.Use();
            }
            else if (keyEvent.character != '\0')
            {
                Type(KeySource.Imgui, keyEvent.character);
                keyEvent.Use();
            }
        }
        catch (Exception exception)
        {
            Plugin.LogSource.LogError($"TEXTINPUT_ERROR — {exception}");
            End();
        }
    }

    /// <summary>Handles an IMGUI mouse-down event anywhere on screen: a click ends the capture (a click on the capturing button itself starts it again).</summary>
    public static void OnMouseDown()
    {
        if (Time.frameCount != _beganInFrame) End();
    }

    /// <summary>Ends any capture and stops listening (when the mod manager shuts down).</summary>
    public static void Shutdown()
    {
        End();
        InputSystemKeyboard.StopListening();
    }

    private static void Type(KeySource source, char character)
    {
        if (!TextEditing.IsPrintable(character) || !CharacterSource.Accept(source)) return;
        if (!_characterSourceLogged)
        {
            _characterSourceLogged = true;
            Plugin.LogSource.LogInfo($"TEXTINPUT_SOURCE — typing into the search buttons works through {(source == KeySource.InputSystem ? "the Input System" : "IMGUI key events")}.");
        }
        SetText(TextEditing.Insert(_getText!(), character));
    }

    private static void EraseLast(KeySource source)
    {
        if (BackspaceSource.Accept(source)) SetText(TextEditing.Backspace(_getText!()));
    }

    private static void SetText(string text)
    {
        if (_setText != null && text != _getText!()) _setText(text);
    }
}

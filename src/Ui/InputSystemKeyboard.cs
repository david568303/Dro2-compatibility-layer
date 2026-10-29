using System.Collections.Concurrent;
using UnityEngine.InputSystem;

namespace CarXModManager.Ui;

/// <summary>What the editing keys of the keyboard are doing this frame.</summary>
/// <param name="BackspaceHeld">Backspace is down.</param>
/// <param name="DeletePressed">Delete went down this frame.</param>
/// <param name="EnterPressed">Enter (either one) went down this frame.</param>
/// <param name="EscapePressed">Escape went down this frame.</param>
internal readonly record struct EditingKeys(bool BackspaceHeld, bool DeletePressed, bool EnterPressed, bool EscapePressed);

/// <summary>
/// The Input System package's view of the keyboard and mouse, which the game uses for its own input. The mod manager's search
/// buttons read typed characters from its <c>onTextInput</c> event (the characters the player actually typed, with the right
/// keyboard layout, Shift and AltGr applied) and the editing keys by polling. If the Input System cannot be used in this game
/// build, that is logged once as <c>TEXTINPUT_INPUTSYSTEM_UNAVAILABLE</c> and the other source (IMGUI key events, see
/// <see cref="TextCapture"/>) is used alone.
/// </summary>
internal static class InputSystemKeyboard
{
    private static readonly ConcurrentQueue<char> Typed = new();
    private static Il2CppSystem.Action<char>? _handler;
    private static IntPtr _subscribedKeyboard;
    private static bool _unavailable;

    /// <summary>Takes the next typed character that has arrived, if any.</summary>
    public static bool TryTakeCharacter(out char character) => Typed.TryDequeue(out character);

    /// <summary>Throws away characters that arrived while nobody was typing.</summary>
    public static void DiscardTyped()
    {
        while (Typed.TryDequeue(out _)) { }
    }

    /// <summary>
    /// Starts listening for typed characters on the current keyboard (and again if the keyboard device is replaced).
    /// Safe to call every frame; it only does work when the keyboard changed.
    /// </summary>
    public static void Listen()
    {
        if (_unavailable) return;
        try
        {
            Keyboard? keyboard = Keyboard.current;
            if (keyboard == null || keyboard.Pointer == _subscribedKeyboard) return;
            _handler ??= new Action<char>(character => Typed.Enqueue(character));
            keyboard.add_onTextInput(_handler);
            _subscribedKeyboard = keyboard.Pointer;
        }
        catch (Exception exception)
        {
            _unavailable = true;
            Plugin.LogSource.LogWarning($"TEXTINPUT_INPUTSYSTEM_UNAVAILABLE — typed characters cannot be read from the Input System ({exception.GetType().Name}: {exception.Message}); IMGUI key events are used instead.");
        }
    }

    /// <summary>Stops listening (when the mod manager shuts down).</summary>
    public static void StopListening()
    {
        if (_unavailable || _handler == null || _subscribedKeyboard == IntPtr.Zero) return;
        try { Keyboard.current?.remove_onTextInput(_handler); }
        catch (Exception) { /* the game is closing; nothing more to undo */ }
        _subscribedKeyboard = IntPtr.Zero;
    }

    /// <summary>The state of Backspace, Delete, Enter and Escape this frame (all false when there is no keyboard).</summary>
    public static EditingKeys ReadEditingKeys()
    {
        if (_unavailable) return default;
        try
        {
            Keyboard? keyboard = Keyboard.current;
            if (keyboard == null) return default;
            return new EditingKeys(
                keyboard.backspaceKey.isPressed,
                keyboard.deleteKey.wasPressedThisFrame,
                keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame,
                keyboard.escapeKey.wasPressedThisFrame);
        }
        catch (Exception exception)
        {
            _unavailable = true;
            Plugin.LogSource.LogWarning($"TEXTINPUT_INPUTSYSTEM_UNAVAILABLE — the keyboard cannot be polled ({exception.GetType().Name}: {exception.Message}); IMGUI key events are used instead.");
            return default;
        }
    }

    /// <summary>True on the frame a mouse button went down (any button), false if there is no mouse or it cannot be read.</summary>
    public static bool MousePressedThisFrame()
    {
        if (_unavailable) return false;
        try
        {
            Mouse? mouse = Mouse.current;
            return mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame);
        }
        catch (Exception)
        {
            return false;
        }
    }
}

using CarX.Game.DR3Console.Contexts.Common.Abstract;
using CarX.Game.DR3Console.DR3.Scripts.States.Contexts.Common;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace CarXModManager.Ui;

/// <summary>
/// Keeps the game from reacting while the player types into one of the mod manager's search buttons: without this, typing
/// would also press the game's hotkeys (Backspace goes "back", letters trigger actions).
/// <para>
/// It uses the game's own input lock (<c>InputLocker.LockInput</c>, the same mechanism the game uses during screen
/// transitions), so the game keeps its state consistent. If that component cannot be found, it falls back to switching the
/// current <c>EventSystem</c> off. The hold is released as soon as the player stops using the panel, and always on
/// shutdown.
/// </para>
/// </summary>
internal static class InputGuard
{
    private const string LockName = "CarX Mod Manager";

    private static InputLocker? _locker;
    private static IInputLocker.LockInputHandle? _handle;
    private static EventSystem? _disabledEventSystem;
    private static bool _failureLogged;
    private static int _transitionsLogged;

    /// <summary>
    /// The "[Integrations] BlockGameInput" setting. When false the game's input is never held back (typing then also
    /// presses the game's hotkeys), for the case that the hold causes trouble on some screen.
    /// </summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>True while the game's input is being held back.</summary>
    public static bool IsHolding => _handle != null || _disabledEventSystem != null;

    /// <summary>Holds the game's input back (<c>true</c>) or lets it through again (<c>false</c>). Does nothing if already in that state.</summary>
    public static void SetHolding(bool hold)
    {
        if (hold && !Enabled) return;
        if (hold == IsHolding) return;
        if (hold) Acquire(); else Release();
    }

    private static void Acquire()
    {
        try
        {
            InputLocker? locker = FindLocker();
            if (locker != null)
            {
                _handle = locker.LockInput(LockName);
                LogTransition("INPUT_HELD — the game's input is held back while typing (the game's input lock).");
                return;
            }
        }
        catch (Exception exception)
        {
            LogFailureOnce($"INPUT_LOCK_FAILED — the game's input lock could not be used ({exception.GetType().Name}: {exception.Message}); falling back to the EventSystem.");
        }

        try
        {
            EventSystem? eventSystem = EventSystem.current;
            if (eventSystem != null && eventSystem.enabled)
            {
                eventSystem.enabled = false;
                _disabledEventSystem = eventSystem;
                LogTransition("INPUT_HELD — the game's input is held back while typing (EventSystem fallback).");
            }
        }
        catch (Exception exception)
        {
            LogFailureOnce($"INPUT_LOCK_FAILED — neither the input lock nor the EventSystem could be used: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static void Release()
    {
        LogTransition("INPUT_RELEASED — the game's input is back.");
        IInputLocker.LockInputHandle? handle = _handle;
        _handle = null;
        if (handle != null)
        {
            try { handle.Dispose(); }
            catch (Exception exception) { LogFailureOnce($"INPUT_UNLOCK_FAILED — {exception.GetType().Name}: {exception.Message}"); }
        }

        EventSystem? eventSystem = _disabledEventSystem;
        _disabledEventSystem = null;
        if (eventSystem != null)
        {
            try { eventSystem.enabled = true; }
            catch (Exception exception) { LogFailureOnce($"INPUT_UNLOCK_FAILED — {exception.GetType().Name}: {exception.Message}"); }
        }
    }

    /// <summary>The game's input locker (one per game), found once and found again if the scene change destroyed it.</summary>
    private static InputLocker? FindLocker()
    {
        if (_locker == null) _locker = Object.FindObjectOfType<InputLocker>();
        return _locker;
    }

    /// <summary>Logs the first few hold/release changes, enough to see in the log that the mechanism works without flooding it.</summary>
    private static void LogTransition(string message)
    {
        if (_transitionsLogged >= 8) return;
        _transitionsLogged++;
        Plugin.LogSource.LogInfo(message);
    }

    private static void LogFailureOnce(string message)
    {
        if (_failureLogged) return;
        _failureLogged = true;
        Plugin.LogSource.LogWarning(message);
    }
}

namespace UnityEngine.InputSystem
{
    // Member numbers are deliberately not modelled: the plugin resolves key names against the real enum at runtime.
    public enum Key { None = 0 }

    public abstract class InputControl { public string displayName => ""; }

    public class InputDevice : InputControl { }

    public class Keyboard : InputDevice
    {
        public static Keyboard? current => null;
        public Controls.KeyControl this[Key key] => throw new NotSupportedException();
    }

    public class Mouse : InputDevice
    {
        public static Mouse? current => null;
    }

    public static class InputSystem
    {
        public static void DisableDevice(InputDevice device, bool keepSendingEvents) { }
        public static void DisableDevice(InputDevice device) { }
        public static void EnableDevice(InputDevice device) { }
    }
}

namespace UnityEngine.InputSystem.Controls
{
    public class ButtonControl : InputControl { public bool wasPressedThisFrame => false; }
    public class KeyControl : ButtonControl { }
}

namespace UnityEngine.InputSystem
{
    // Member numbers are deliberately not modelled: the plugin resolves key names against the real enum at runtime.
    public enum Key { None = 0 }

    public class Keyboard
    {
        public static Keyboard? current => null;
        public Controls.KeyControl this[Key key] => throw new NotSupportedException();
    }
}

namespace UnityEngine.InputSystem.Controls
{
    public class ButtonControl { public bool wasPressedThisFrame => false; }
    public class KeyControl : ButtonControl { }
}

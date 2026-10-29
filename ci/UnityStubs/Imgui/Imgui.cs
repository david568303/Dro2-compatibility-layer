using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace UnityEngine
{
    public class GUILayoutOption : Il2CppObjectBase { public GUILayoutOption(IntPtr pointer) : base(pointer) { } }

    public static class GUI
    {
        public static Matrix4x4 matrix { get; set; }
        public static Color contentColor { get; set; }
    }

    public static class GUILayout
    {
        public static void Label(string text, Il2CppReferenceArray<GUILayoutOption> options) { }
        public static void BeginArea(Rect screenRect) { }
        public static void EndArea() { }
    }
}

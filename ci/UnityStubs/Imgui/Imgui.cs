using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace UnityEngine
{
    public class GUILayoutOption : Il2CppObjectBase { public GUILayoutOption(IntPtr pointer) : base(pointer) { } }

    public static class GUI
    {
        public static Matrix4x4 matrix { get; set; }
        public static Color contentColor { get; set; }
        public static void Box(Rect position, string text) { }
    }

    public static class GUILayout
    {
        public static void Label(string text, Il2CppReferenceArray<GUILayoutOption> options) { }
        public static bool Button(string text, Il2CppReferenceArray<GUILayoutOption> options) => false;
        public static bool Toggle(bool value, string text, Il2CppReferenceArray<GUILayoutOption> options) => value;
        public static float HorizontalSlider(float value, float leftValue, float rightValue, Il2CppReferenceArray<GUILayoutOption> options) => value;
        public static Vector2 BeginScrollView(Vector2 scrollPosition, Il2CppReferenceArray<GUILayoutOption> options) => scrollPosition;
        public static void EndScrollView() { }
        public static void BeginVertical(Il2CppReferenceArray<GUILayoutOption> options) { }
        public static void BeginVertical(string style, Il2CppReferenceArray<GUILayoutOption> options) { }
        public static void EndVertical() { }
        public static void BeginHorizontal(Il2CppReferenceArray<GUILayoutOption> options) { }
        public static void EndHorizontal() { }
        public static void Space(float pixels) { }
        public static void FlexibleSpace() { }
        public static void BeginArea(Rect screenRect) { }
        public static void EndArea() { }
    }
}

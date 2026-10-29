using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace UnityEngine
{
    public class GUILayoutOption : Il2CppObjectBase { public GUILayoutOption(IntPtr pointer) : base(pointer) { } }

    public class RectOffset : Il2CppObjectBase
    {
        public RectOffset(IntPtr pointer) : base(pointer) { }
        public RectOffset() : base(IntPtr.Zero) { }
        public RectOffset(int left, int right, int top, int bottom) : base(IntPtr.Zero) { }
    }

    public class GUIStyleState : Il2CppObjectBase
    {
        public GUIStyleState(IntPtr pointer) : base(pointer) { }
        public Texture2D background { get; set; } = null!;
        public Color textColor { get; set; }
    }

    public class GUIStyle : Il2CppObjectBase
    {
        public GUIStyle(IntPtr pointer) : base(pointer) { }
        public GUIStyle(GUIStyle other) : base(IntPtr.Zero) { }
        public GUIStyleState normal { get; set; } = null!;
        public GUIStyleState hover { get; set; } = null!;
        public GUIStyleState active { get; set; } = null!;
        public RectOffset border { get; set; } = null!;
        public RectOffset margin { get; set; } = null!;
        public RectOffset padding { get; set; } = null!;
        public float fixedHeight { get; set; }
    }

    public class GUISkin : Il2CppObjectBase
    {
        public GUISkin(IntPtr pointer) : base(pointer) { }
        public GUIStyle label { get; set; } = null!;
        public GUIStyle button { get; set; } = null!;
    }

    public static class GUI
    {
        public static Matrix4x4 matrix { get; set; }
        public static GUISkin skin { get; set; } = null!;
        public static Color contentColor { get; set; }
        public static void Box(Rect position, string text) { }
    }

    public static class GUILayout
    {
        public static void Label(string text, Il2CppReferenceArray<GUILayoutOption> options) { }
        public static void Label(string text, GUIStyle style, Il2CppReferenceArray<GUILayoutOption> options) { }
        public static bool Button(string text, GUIStyle style, Il2CppReferenceArray<GUILayoutOption> options) => false;
        public static void BeginVertical(GUIStyle style, Il2CppReferenceArray<GUILayoutOption> options) { }
        public static void BeginArea(Rect screenRect, GUIStyle style) { }
        public static bool Button(string text, Il2CppReferenceArray<GUILayoutOption> options) => false;
        public static bool Toggle(bool value, string text, Il2CppReferenceArray<GUILayoutOption> options) => value;
        public static float HorizontalSlider(float value, float leftValue, float rightValue, Il2CppReferenceArray<GUILayoutOption> options) => value;
        public static Vector2 BeginScrollView(Vector2 scrollPosition, Il2CppReferenceArray<GUILayoutOption> options) => scrollPosition;
        public static void EndScrollView() { }
        public static void BeginVertical(Il2CppReferenceArray<GUILayoutOption> options) { }
        public static void EndVertical() { }
        public static void BeginHorizontal(Il2CppReferenceArray<GUILayoutOption> options) { }
        public static void EndHorizontal() { }
        public static void Space(float pixels) { }
        public static void FlexibleSpace() { }
        public static void BeginArea(Rect screenRect) { }
        public static void EndArea() { }
    }
}

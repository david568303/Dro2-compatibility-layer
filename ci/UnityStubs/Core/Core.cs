using Il2CppInterop.Runtime.InteropTypes;

namespace UnityEngine
{
    public struct Rect
    {
        private float m_XMin, m_YMin, m_Width, m_Height;
        public Rect(float x, float y, float width, float height) { m_XMin = x; m_YMin = y; m_Width = width; m_Height = height; }
        public float x { get => m_XMin; set => m_XMin = value; }
        public float y { get => m_YMin; set => m_YMin = value; }
        public float width { get => m_Width; set => m_Width = value; }
        public float height { get => m_Height; set => m_Height = value; }
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }

    public struct Vector2 { public float x, y; }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => default;
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public static Quaternion identity => default;
    }

    public struct Matrix4x4
    {
        public static Matrix4x4 TRS(Vector3 pos, Quaternion q, Vector3 s) => default;
    }

    public static class Mathf
    {
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static bool Approximately(float a, float b) => a == b;
    }

    public enum CursorLockMode { None = 0, Locked = 1, Confined = 2 }

    public static class Cursor
    {
        public static CursorLockMode lockState { get; set; }
        public static bool visible { get; set; }
    }

    public static class Screen
    {
        public static int width => 0;
        public static int height => 0;
    }

    public static class Application
    {
        public static string unityVersion => "";
        public static string version => "";
    }

    public static class SystemInfo
    {
        public static Rendering.GraphicsDeviceType graphicsDeviceType => default;
    }

    public class Object : Il2CppObjectBase
    {
        public Object(IntPtr pointer) : base(pointer) { }
        public static void Destroy(Object obj) { }
        public HideFlags hideFlags { get; set; }
    }

    public enum HideFlags
    {
        None = 0, HideInHierarchy = 1, HideInInspector = 2, DontSaveInEditor = 4, NotEditable = 8,
        DontSaveInBuild = 16, DontUnloadUnusedAsset = 32, DontSave = 52, HideAndDontSave = 61,
    }

    public class Texture : Object { public Texture(IntPtr pointer) : base(pointer) { } }

    public class Texture2D : Texture
    {
        public Texture2D(int width, int height) : base(IntPtr.Zero) { }
        public void SetPixel(int x, int y, Color color) { }
        public void Apply() { }
    }

    public class Component : Object { public Component(IntPtr pointer) : base(pointer) { } }
    public class Behaviour : Component { public Behaviour(IntPtr pointer) : base(pointer) { } }
    public class MonoBehaviour : Behaviour { public MonoBehaviour(IntPtr pointer) : base(pointer) { } }
}

namespace UnityEngine.Rendering
{
    public enum GraphicsDeviceType { Direct3D11 = 2, Direct3D12 = 18 }
}

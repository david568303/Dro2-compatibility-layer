using UnityEngine;

namespace CarXModManager;

/// <summary>
/// Draws the always-on line in the bottom-left corner, e.g. "4 mods loaded, 3 with errors" (errors in red).
/// Drawn with IMGUI inside the engine, so it works on DX11 and DX12 alike and sits under ReShade.
/// </summary>
internal static class StatusLine
{
    private static readonly Color Text = new(0.93f, 0.93f, 0.95f, 1f);
    private static readonly Color Shadow = new(0f, 0f, 0f, 0.85f);
    private static bool _started;
    private static int _faults;

    public static void ReportFault(Exception exception)
    {
        _faults++;
        if (_faults <= 3) Plugin.Logger.LogWarning($"CARXMM_FAULT — {exception}");
        if (_faults == 5) Plugin.Logger.LogError("CARXMM_DISABLED — too many drawing errors; the status line is off for this session. Mods are unaffected.");
    }

    public static void Draw()
    {
        if (!_started)
        {
            _started = true;
            Plugin.Logger.LogInfo($"CARXMM_ONGUI_FIRED — IMGUI is running. Graphics API: {SystemInfo.graphicsDeviceType}; Unity {Application.unityVersion}; game {Application.version}.");
            ModStatus.Instance.ImportExistingLog();
        }
        if (!Plugin.ShowStatusLine.Value || _faults >= 5) return;

        string text = ModStatus.Instance.GetText(out string plain);
        float scale = Math.Max(0.75f, Screen.height / 1080f);
        Matrix4x4 previous = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
        try
        {
            float top = Screen.height / scale - 30f;
            Ui.BeginArea(new Rect(11f, top + 1f, 720f, 26f));
            Ui.Label(plain, Shadow);
            Ui.EndArea();
            Ui.BeginArea(new Rect(10f, top, 720f, 26f));
            Ui.Label(text, Text);
            Ui.EndArea();
        }
        finally
        {
            Ui.Unwind();
            GUI.matrix = previous;
        }
    }
}

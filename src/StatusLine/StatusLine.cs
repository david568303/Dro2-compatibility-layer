using UnityEngine;

namespace CarXModManager;

/// <summary>
/// Draws the always-on line in the bottom-left corner, e.g. "4 mods loaded, 3 with errors" (the error counts in red).
/// It is drawn with IMGUI inside the engine, so it works on DX11 and DX12 alike and sits under ReShade.
/// If drawing keeps failing, the line switches itself off for the session so it can never disturb the game.
/// </summary>
internal static class StatusLine
{
    /// <summary>Drawing errors that are logged in full; later ones are counted silently.</summary>
    private const int MaxLoggedDrawErrors = 3;

    /// <summary>After this many drawing errors the line is switched off until the game restarts.</summary>
    private const int DrawErrorsBeforeDisabling = 5;

    private static readonly Color TextColor = new(0.93f, 0.93f, 0.95f, 1f);
    private static readonly Color ShadowColor = new(0f, 0f, 0f, 0.85f);
    private static bool _firstDrawLogged;
    private static int _drawErrors;

    /// <summary>Counts a drawing error, logs the first few, and disables the line when there are too many.</summary>
    public static void ReportDrawError(Exception exception)
    {
        _drawErrors++;
        if (_drawErrors <= MaxLoggedDrawErrors) Plugin.LogSource.LogWarning($"STATUSLINE_DRAW_ERROR — {exception}");
        if (_drawErrors == DrawErrorsBeforeDisabling)
            Plugin.LogSource.LogError("STATUSLINE_DISABLED — too many drawing errors; the status line is off for this session. Mods are unaffected.");
    }

    /// <summary>
    /// Draws the line once. Called every frame from <see cref="StatusLineBehaviour.OnGUI"/>. The first call also logs
    /// which graphics API and Unity version are in use, and imports the errors mods logged before this plugin started.
    /// </summary>
    public static void Draw()
    {
        if (!_firstDrawLogged)
        {
            _firstDrawLogged = true;
            Plugin.LogSource.LogInfo($"STATUSLINE_FIRST_DRAW — IMGUI is running. Graphics API: {SystemInfo.graphicsDeviceType}; Unity {Application.unityVersion}; game {Application.version}.");
            ModStatusTracker.Instance.ImportErrorsFromLogFile();
        }
        if (!Plugin.ShowStatusLine.Value || _drawErrors >= DrawErrorsBeforeDisabling) return;

        string text = ModStatusTracker.Instance.GetStatusText(out string plainText);
        float scale = Math.Max(0.75f, Screen.height / 1080f);
        Matrix4x4 previousMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
        try
        {
            float top = Screen.height / scale - 30f;
            // the dark copy, one pixel down and right, is the drop shadow that keeps the text readable on any background
            Imgui.BeginArea(new Rect(11f, top + 1f, 720f, 26f));
            Imgui.Label(plainText, ShadowColor);
            Imgui.EndArea();
            Imgui.BeginArea(new Rect(10f, top, 720f, 26f));
            Imgui.Label(text, TextColor);
            Imgui.EndArea();
        }
        finally
        {
            Imgui.CloseOpenAreas();
            GUI.matrix = previousMatrix;
        }
    }
}

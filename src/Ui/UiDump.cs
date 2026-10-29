using BepInEx;
using CarXModManager.Logic;
using UnityEngine;

namespace CarXModManager.Ui;

/// <summary>
/// Writes the layout of a part of the game's UI (every object with its size, position and components) to a text file next to
/// <c>LogOutput.log</c>. This is how the mod manager's controls are placed: the file shows what is where on the game's screen, so a
/// control can be put in a free spot, and it is the first thing to look at when a control ends up somewhere unexpected.
/// </summary>
internal static class UiDump
{
    /// <summary>
    /// Writes the dump for <paramref name="root"/> and its children to <c>BepInEx/carx.modmanager.ui-NAME.txt</c> (replacing the previous one).
    /// Never throws.
    /// </summary>
    /// <param name="name">Which screen it is ("serverbrowser").</param>
    /// <param name="root">The object to start from.</param>
    /// <param name="maxDepth">How many levels of children to follow.</param>
    /// <param name="maxLines">Upper limit on the file's length.</param>
    /// <returns>A short description for the log: the file written, or why nothing was.</returns>
    public static string Write(string name, Transform root, int maxDepth = 8, int maxLines = 900)
    {
        try
        {
            RectTransform? canvas = GameUi.CanvasRoot(root);
            var lines = new List<string>
            {
                $"# {name}: UI layout written by CarX Mod Manager {Plugin.BuildId}",
                "# Each line: name [active?] box=(x,y,width,height) in the units of the screen's root (the lower left of the root is not necessarily 0,0), anchors, components.",
            };
            Walk(root, canvas, 0, maxDepth, maxLines, lines);
            string path = Path.Combine(Paths.BepInExRootPath, $"carx.modmanager.ui-{name}.txt");
            File.WriteAllLines(path, lines);
            return $"{lines.Count} lines written to {path}";
        }
        catch (Exception exception)
        {
            return $"could not be written ({exception.GetType().Name}: {exception.Message})";
        }
    }

    private static string PathFromRoot(Transform node)
    {
        var names = new List<string>();
        for (Transform? t = node; t != null; t = t.parent) names.Add(t.gameObject.name);
        names.Reverse();
        return string.Join("/", names);
    }

    private static void Walk(Transform node, RectTransform? canvas, int depth, int maxDepth, int maxLines, List<string> lines)
    {
        if (lines.Count >= maxLines) return;
        lines.Add(new string(' ', depth * 2) + Describe(node, canvas));
        if (depth >= maxDepth)
        {
            if (node.childCount > 0) lines.Add(new string(' ', (depth + 1) * 2) + $"... {node.childCount} more below");
            return;
        }
        for (int i = 0; i < node.childCount; i++)
        {
            if (lines.Count >= maxLines)
            {
                lines.Add("... cut off at the line limit");
                return;
            }
            Walk(node.GetChild(i), canvas, depth + 1, maxDepth, maxLines, lines);
        }
    }

    private static string Describe(Transform node, RectTransform? canvas)
    {
        string text = $"{node.gameObject.name} [{(node.gameObject.activeSelf ? "on" : "off")}]";
        RectTransform? rect = node.gameObject.GetComponent<RectTransform>();
        if (rect != null)
        {
            if (canvas != null)
            {
                Box box = GameUi.BoxIn(canvas, rect);
                text += $" box=({box.X:0},{box.Y:0},{box.Width:0},{box.Height:0})";
            }
            text += $" anchors=({rect.anchorMin.x:0.##},{rect.anchorMin.y:0.##})-({rect.anchorMax.x:0.##},{rect.anchorMax.y:0.##})";
        }
        text += " " + string.Join(",", ComponentNames(node));
        return text;
    }

    private static IEnumerable<string> ComponentNames(Transform node)
    {
        var names = new List<string>();
        foreach (Component component in node.gameObject.GetComponents<Component>())
        {
            if (component == null) { names.Add("(missing)"); continue; }
            string typeName;
            try { typeName = component.GetIl2CppType().Name; }
            catch (Exception) { typeName = "?"; }
            if (typeName != "RectTransform" && typeName != "CanvasRenderer") names.Add(typeName);
        }
        return names;
    }
}

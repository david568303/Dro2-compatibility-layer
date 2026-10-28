using System.Reflection;
using BepInEx.Unity.IL2CPP;
using CarXModManager.Api;

namespace CarXModManager;

internal sealed record ModEntry(
    string Guid,
    string Name,
    string Version,
    string File,
    string Dependencies,
    ModImpact Impact,
    bool HasPanel);

/// <summary>Lists every plugin the BepInEx chainloader loaded, whether or not it knows about the manager.</summary>
internal static class ModCatalog
{
    public static List<ModEntry> Scan()
    {
        var list = new List<ModEntry>();
        foreach (var info in IL2CPPChainloader.Instance.Plugins.Values)
        {
            ModImpact impact = ModImpact.Unknown;
            try { impact = info.Instance?.GetType().GetCustomAttribute<ModImpactAttribute>()?.Impact ?? ModImpact.Unknown; }
            catch { /* attribute from a different copy of the API: leave Unknown */ }

            list.Add(new ModEntry(
                info.Metadata.GUID,
                info.Metadata.Name,
                info.Metadata.Version.ToString(),
                Path.GetFileName(info.Location ?? string.Empty),
                string.Join(", ", info.Dependencies.Select(d => d.DependencyGUID)),
                impact,
                ModManagerApi.HasPanel(info.Metadata.GUID)));
        }
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }
}

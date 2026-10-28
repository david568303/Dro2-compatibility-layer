using System.Reflection;
using HarmonyLib;

namespace CarXModManager;

internal sealed record PatchConflict(string Method, string[] Owners, bool CanSkipOriginal);

/// <summary>
/// Finds methods patched by more than one Harmony owner. Only Harmony patches are visible; hooks
/// installed some other way (native detours, other tools) cannot be seen from here.
/// </summary>
internal static class PatchConflicts
{
    private static readonly string[] IgnoredOwnerPrefixes = { "BepInEx", "Il2CppInterop", "MonoMod" };

    public static List<PatchConflict> Scan()
    {
        var result = new List<PatchConflict>();
        foreach (MethodBase method in Harmony.GetAllPatchedMethods().ToList())
        {
            try
            {
                Patches? info = Harmony.GetPatchInfo(method);
                if (info == null) continue;
                string[] owners = info.Owners
                    .Where(owner => !IgnoredOwnerPrefixes.Any(prefix => owner.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    .Distinct()
                    .ToArray();
                if (owners.Length < 2) continue;
                // A prefix returning bool can skip the original method and every lower-priority prefix.
                bool canSkip = info.Prefixes.Any(patch => patch.PatchMethod.ReturnType == typeof(bool));
                result.Add(new PatchConflict($"{method.DeclaringType?.FullName}.{method.Name}", owners, canSkip));
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogDebug($"Patch scan skipped {method}: {exception.Message}");
            }
        }
        result.Sort((a, b) =>
        {
            int bySeverity = b.CanSkipOriginal.CompareTo(a.CanSkipOriginal);
            return bySeverity != 0 ? bySeverity : string.Compare(a.Method, b.Method, StringComparison.Ordinal);
        });
        return result;
    }
}

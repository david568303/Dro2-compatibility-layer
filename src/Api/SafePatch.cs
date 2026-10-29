using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace CarXModManager.Api;

/// <summary>
/// Patching game methods without a compile-time dependency on the game's types: the target is found by name at run
/// time, and every way it can go wrong (type renamed, method changed, patch refused) is logged as an Error under the
/// calling mod's own log source. That makes the mod show up in the status line as "with errors" instead of failing
/// silently, and it is how a game update that moves a method becomes visible.
/// </summary>
public static class SafePatch
{
    /// <param name="harmony">The calling mod's Harmony instance.</param>
    /// <param name="log">The calling mod's log source (its plugin's Log).</param>
    /// <param name="typeName">Full name of the game type, e.g. "Namespace.ClassName".</param>
    /// <param name="assemblyName">Interop assembly to load if the type is not found yet, e.g. "CarX.DR3.Assembly".</param>
    /// <param name="methodName">Method to patch.</param>
    /// <param name="parameterCheck">Picks the right overload; the patch is applied only if exactly one method matches.</param>
    /// <param name="prefix">Static patch method, or null.</param>
    /// <param name="postfix">Static patch method, or null.</param>
    public static bool TryPatch(Harmony harmony, ManualLogSource log, string typeName, string assemblyName, string methodName,
        Func<ParameterInfo[], bool>? parameterCheck, MethodInfo? prefix, MethodInfo? postfix)
    {
        try
        {
            Type? type = AccessTools.TypeByName(typeName);
            if (type == null)
            {
                try { Assembly.Load(assemblyName); } catch { /* reported below */ }
                type = AccessTools.TypeByName(typeName);
            }
            if (type == null)
            {
                log.LogError($"PATCH_TARGET_MISSING — type {typeName} was not found in {assemblyName}. The game was probably updated.");
                return false;
            }

            var candidates = type
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(method => method.Name == methodName && (parameterCheck?.Invoke(method.GetParameters()) ?? true))
                .ToList();
            if (candidates.Count != 1)
            {
                log.LogError($"PATCH_TARGET_UNCLEAR — {typeName}.{methodName}: expected exactly one matching method, found {candidates.Count}. The game was probably updated.");
                return false;
            }

            harmony.Patch(candidates[0],
                prefix: prefix == null ? null : new HarmonyMethod(prefix),
                postfix: postfix == null ? null : new HarmonyMethod(postfix));
            log.LogInfo($"PATCHED — {typeName}.{methodName}");
            return true;
        }
        catch (Exception exception)
        {
            log.LogError($"PATCH_FAILED — {typeName}.{methodName}: {exception}");
            return false;
        }
    }
}

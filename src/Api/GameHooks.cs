using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace CarXModManager.Api;

/// <summary>
/// Creating hooks on game methods (code that runs before or after a game method) without a compile-time dependency on the game's types: the target is found by name at run
/// time, and every way it can go wrong (type renamed, method changed, hook refused) is logged as an Error under the
/// calling mod's own log source. That makes the mod show up in the status line as "with errors" instead of failing
/// silently, and it is how a game update that moves a method becomes visible.
/// </summary>
public static class GameHooks
{
    /// <param name="harmony">The calling mod's Harmony instance.</param>
    /// <param name="log">The calling mod's log source (its plugin's Log).</param>
    /// <param name="typeName">Full name of the game type, e.g. "Namespace.ClassName".</param>
    /// <param name="assemblyName">Interop assembly to load if the type is not found yet, e.g. "CarX.DR3.Assembly".</param>
    /// <param name="methodName">Method to patch.</param>
    /// <param name="parameterCheck">Picks the right overload; the hook is created only if exactly one method matches.</param>
    /// <param name="prefix">Static method that runs before the game method (it may change the arguments), or null.</param>
    /// <param name="postfix">Static method that runs after the game method, or null.</param>
    public static bool Create(Harmony harmony, ManualLogSource log, string typeName, string assemblyName, string methodName,
        Func<ParameterInfo[], bool>? parameterCheck, MethodInfo? prefix, MethodInfo? postfix)
    {
        try
        {
            Type? type = null;
            try { type = Assembly.Load(assemblyName).GetType(typeName, throwOnError: false); }
            catch { /* fall back to a search of every loaded assembly */ }
            type ??= AccessTools.TypeByName(typeName);
            if (type == null)
            {
                log.LogError($"HOOK_TARGET_MISSING — type {typeName} was not found in {assemblyName}. The game was probably updated.");
                return false;
            }

            var candidates = type
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(method => method.Name == methodName && (parameterCheck?.Invoke(method.GetParameters()) ?? true))
                .ToList();
            if (candidates.Count != 1)
            {
                log.LogError($"HOOK_TARGET_UNCLEAR — {typeName}.{methodName}: expected exactly one matching method, found {candidates.Count}. The game was probably updated.");
                return false;
            }

            harmony.Patch(candidates[0],
                prefix: prefix == null ? null : new HarmonyMethod(prefix),
                postfix: postfix == null ? null : new HarmonyMethod(postfix));
            log.LogInfo($"HOOK_CREATED — {typeName}.{methodName}");
            return true;
        }
        catch (Exception exception)
        {
            log.LogError($"HOOK_FAILED — {typeName}.{methodName}: {exception}");
            return false;
        }
    }
}

using System.Reflection;
using BepInEx.Logging;
using CarXModManager.Api;
using HarmonyLib;

namespace CarXModManager.Integrations;

/// <summary>
/// Describes one hook an integration wants on a game method: which method, and which of the integration's own static methods run
/// before and after it. Kept as data (not buried in code) so the same description can be checked against the game's real
/// assemblies by <c>tools/HookCheck</c> before a build is released.
/// </summary>
/// <param name="TypeName">Full name of the game class, e.g. <c>CarX.Game...UILobbyRoomBrowserContext</c>.</param>
/// <param name="AssemblyName">The game assembly that holds the class, without <c>.dll</c>.</param>
/// <param name="MethodName">Name of the game method.</param>
/// <param name="SelectOverload">Picks the overload by its parameters; exactly one method must match.</param>
/// <param name="PrefixName">Name of the integration's static method that runs before the game method, or <c>null</c>.</param>
/// <param name="PostfixName">Name of the integration's static method that runs after the game method, or <c>null</c>.</param>
/// <param name="Required">If true, the integration counts as failed when this hook cannot be created.</param>
internal sealed record HookSpec(
    string TypeName,
    string AssemblyName,
    string MethodName,
    Func<ParameterInfo[], bool> SelectOverload,
    string? PrefixName = null,
    string? PostfixName = null,
    bool Required = true);

/// <summary>Creates the hooks an integration declared, through the compatibility layer's <see cref="GameHooks"/>.</summary>
internal static class HookInstaller
{
    private const BindingFlags HookMethodFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    /// <summary>
    /// Creates every hook in <paramref name="specs"/>. A hook that cannot be created is logged by <see cref="GameHooks"/>
    /// (or here, if the integration's own method is missing) and does not stop the others.
    /// </summary>
    /// <param name="harmony">The mod manager's Harmony instance for integrations.</param>
    /// <param name="log">The mod manager's log source; failures are logged there.</param>
    /// <param name="owner">The class that holds the static prefix and postfix methods.</param>
    /// <param name="specs">The hooks to create.</param>
    /// <returns><c>true</c> if every <see cref="HookSpec.Required"/> hook was created.</returns>
    public static bool Install(Harmony harmony, ManualLogSource log, Type owner, IEnumerable<HookSpec> specs)
    {
        bool allRequiredCreated = true;
        foreach (HookSpec spec in specs)
        {
            MethodInfo? prefix = Find(owner, spec.PrefixName, log, spec);
            MethodInfo? postfix = Find(owner, spec.PostfixName, log, spec);
            bool missingOwnMethod = (spec.PrefixName != null && prefix == null) || (spec.PostfixName != null && postfix == null);
            bool created = !missingOwnMethod
                && GameHooks.Create(harmony, log, spec.TypeName, spec.AssemblyName, spec.MethodName, spec.SelectOverload, prefix, postfix);
            if (!created && spec.Required) allRequiredCreated = false;
        }
        return allRequiredCreated;
    }

    private static MethodInfo? Find(Type owner, string? name, ManualLogSource log, HookSpec spec)
    {
        if (name == null) return null;
        MethodInfo? method = owner.GetMethod(name, HookMethodFlags);
        if (method == null)
            log.LogError($"HOOK_OWN_METHOD_MISSING — {owner.Name}.{name} (for {spec.TypeName}.{spec.MethodName}) does not exist; this is a bug in the mod manager.");
        return method;
    }
}

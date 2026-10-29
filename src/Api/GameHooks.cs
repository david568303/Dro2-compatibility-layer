using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace CarXModManager.Api;

/// <summary>
/// Creating hooks on the game's methods: your code runs before (a <i>prefix</i>) or after (a <i>postfix</i>) a game
/// method, and can read or change its arguments and result.
/// <para>
/// The game method is found <b>by name at run time</b>, so your mod needs no compile-time reference to the game's
/// types. Everything that can go wrong (the type was renamed, the method changed, the hook was refused) is logged as an
/// <b>Error under your mod's own log source</b>. That is deliberate: your mod then shows up in the status line as
/// "with errors" and a game update that moves a method is noticed immediately, instead of the mod silently doing
/// nothing.
/// </para>
/// <para>See docs/API.md for a full explanation, the Harmony argument names and the pitfalls.</para>
/// </summary>
public static class GameHooks
{
    /// <summary>
    /// Creates a hook on one game method. Returns <c>true</c> if the hook was created and <c>false</c> if it was not
    /// (the reason is logged as an Error; nothing is thrown).
    /// </summary>
    /// <remarks>
    /// <c>true</c> means the hook is installed. It does <b>not</b> prove the game ever calls that method: the IL2CPP
    /// compiler may have inlined a small method into its callers, in which case it never runs. If a hook never fires,
    /// hook the caller instead (or in addition).
    /// </remarks>
    /// <param name="harmony">Your mod's own Harmony instance, e.g. <c>new Harmony("yourname.yourmod")</c>.</param>
    /// <param name="log">
    /// Your mod's log source (in a <c>BasePlugin</c> that is <c>Log</c>). Failures are logged here, so they are
    /// attributed to your mod.
    /// </param>
    /// <param name="typeName">
    /// Full name of the game class including its namespace, e.g. <c>"CarX.Game.SomeNamespace.SomeClass"</c>.
    /// </param>
    /// <param name="assemblyName">
    /// Name of the game's interop assembly that holds the class, without <c>.dll</c> (the game's own code is in
    /// <c>"CarX.DR3.Assembly"</c>). It is searched first; if the class is not there, every loaded assembly is searched.
    /// </param>
    /// <param name="methodName">Name of the method to hook.</param>
    /// <param name="selectOverload">
    /// Picks the right method when the name is not unique: it receives each candidate's parameters and returns
    /// <c>true</c> for the one you want, e.g. <c>p => p.Length == 2 &amp;&amp; p[0].ParameterType == typeof(string)</c>.
    /// The hook is created only if <b>exactly one</b> method matches. <c>null</c> means the name alone must be unique.
    /// </param>
    /// <param name="prefix">
    /// A <c>static</c> method that runs <b>before</b> the game method, or <c>null</c>. It may take the game method's
    /// arguments by position (<c>__0</c>, <c>__1</c>, ...), as <c>ref</c> to change what the game receives, and
    /// <c>__instance</c> for the object the method is called on. If it returns <c>bool</c>, returning
    /// <c>false</c> skips the game method.
    /// </param>
    /// <param name="postfix">
    /// A <c>static</c> method that runs <b>after</b> the game method, or <c>null</c>. It may take the same arguments and
    /// <c>__result</c> (use <c>ref</c> to change the result).
    /// </param>
    /// <example>
    /// A hook that changes the first argument of a game method before the game sees it:
    /// <code>
    /// bool hooked = GameHooks.Create(harmony, Log,
    ///     typeName: "CarX.Game.SomeNamespace.SomeClass",
    ///     assemblyName: "CarX.DR3.Assembly",
    ///     methodName: "Load",
    ///     selectOverload: p => p.Length == 2 &amp;&amp; p[0].ParameterType == typeof(string),
    ///     prefix: typeof(MyMod).GetMethod(nameof(LoadPrefix), BindingFlags.NonPublic | BindingFlags.Static));
    ///
    /// private static void LoadPrefix(ref string __0)
    /// {
    ///     try { __0 = __0.Trim(); }
    ///     catch (Exception exception) { MyMod.Logger.LogError($"MYMOD_LOAD_HOOK_FAILED — {exception}"); }
    /// }
    /// </code>
    /// </example>
    public static bool Create(Harmony harmony, ManualLogSource log, string typeName, string assemblyName, string methodName,
        Func<ParameterInfo[], bool>? selectOverload = null, MethodInfo? prefix = null, MethodInfo? postfix = null)
    {
        try
        {
            if (prefix == null && postfix == null)
            {
                log.LogError($"HOOK_NOTHING_TO_RUN — {typeName}.{methodName}: neither a prefix nor a postfix was given, so there is nothing to hook.");
                return false;
            }

            Type? type = null;
            try { type = Assembly.Load(assemblyName).GetType(typeName, throwOnError: false); }
            catch { /* the assembly could not be loaded by name: fall back to a search of every loaded assembly */ }
            type ??= AccessTools.TypeByName(typeName);
            if (type == null)
            {
                log.LogError($"HOOK_TARGET_MISSING — type {typeName} was not found in {assemblyName}. Check the type name; if it used to work, the game was probably updated.");
                return false;
            }

            var candidates = type
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(method => method.Name == methodName && (selectOverload?.Invoke(method.GetParameters()) ?? true))
                .ToList();
            if (candidates.Count != 1)
            {
                string advice = candidates.Count == 0
                    ? "no method has that name (or selectOverload accepted none of them). Check the method name; if it used to work, the game was probably updated."
                    : "several methods match. Pass a selectOverload that accepts exactly one of them.";
                log.LogError($"HOOK_TARGET_UNCLEAR — {typeName}.{methodName}: expected exactly one matching method, found {candidates.Count}: {advice}");
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

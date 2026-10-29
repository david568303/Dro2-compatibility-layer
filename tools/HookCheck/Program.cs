using System.Reflection;

// Verifies every hook the mod manager's integrations declare against the game's real assemblies, the way Harmony will bind them in the game:
//   * the game type exists and exactly one method matches the name and overload selector;
//   * every prefix/postfix is a static method whose parameters can be bound: __instance, __result, __0..__n (by position, same type);
//   * a prefix returns void or bool.
// Usage: HookCheck --interop <folder with the game's interop DLLs> --plugin <CarXModManager.dll>

string? interop = null, plugin = null;
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--interop") interop = args[i + 1];
    if (args[i] == "--plugin") plugin = args[i + 1];
}
if (interop == null || plugin == null)
{
    Console.Error.WriteLine("usage: HookCheck --interop <interop folder> --plugin <CarXModManager.dll>");
    return 2;
}

AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
{
    string file = Path.Combine(interop, new AssemblyName(e.Name).Name + ".dll");
    return File.Exists(file) ? Assembly.LoadFrom(file) : null;
};

int failures = 0, checks = 0;
void Report(bool ok, string what)
{
    checks++;
    Console.WriteLine((ok ? "ok   " : "FAIL ") + what);
    if (!ok) failures++;
}

Assembly pluginAssembly = Assembly.LoadFrom(Path.GetFullPath(plugin));
Type integrationInterface = pluginAssembly.GetType("CarXModManager.Integrations.IIntegration") ?? throw new InvalidOperationException("IIntegration not found");
Type[] integrationTypes = pluginAssembly.GetTypes().Where(t => t is { IsAbstract: false, IsInterface: false } && integrationInterface.IsAssignableFrom(t)).ToArray();
Console.WriteLine($"{integrationTypes.Length} integration(s) found");

const BindingFlags AnyMethod = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

foreach (Type integrationType in integrationTypes)
{
    object integration = Activator.CreateInstance(integrationType, nonPublic: true)!;
    var specs = ((System.Collections.IEnumerable)integrationType.GetProperty("Hooks")!.GetValue(integration)!).Cast<object>().ToList();
    Console.WriteLine($"== {integrationType.Name}: {specs.Count} hook(s)");

    foreach (object spec in specs)
    {
        T Get<T>(string name) => (T)spec.GetType().GetProperty(name)!.GetValue(spec)!;
        string typeName = Get<string>("TypeName"), assemblyName = Get<string>("AssemblyName"), methodName = Get<string>("MethodName");
        var select = Get<Func<ParameterInfo[], bool>>("SelectOverload");
        string? prefixName = (string?)spec.GetType().GetProperty("PrefixName")!.GetValue(spec);
        string? postfixName = (string?)spec.GetType().GetProperty("PostfixName")!.GetValue(spec);
        string label = $"{typeName.Split('.').Last()}.{methodName}";

        Type? gameType = Assembly.Load(assemblyName).GetType(typeName, throwOnError: false);
        Report(gameType != null, $"{label}: type found in {assemblyName}");
        if (gameType == null) continue;

        var candidates = gameType.GetMethods(AnyMethod).Where(m => m.Name == methodName && select(m.GetParameters())).ToList();
        Report(candidates.Count == 1, $"{label}: exactly one method matches (found {candidates.Count})");
        if (candidates.Count != 1) continue;
        MethodInfo target = candidates[0];
        ParameterInfo[] targetParameters = target.GetParameters();
        Console.WriteLine($"     target: {target.ReturnType.Name} {target.Name}({string.Join(", ", targetParameters.Select(p => $"{p.ParameterType.Name} {p.Name}"))})");

        foreach ((string? name, bool isPrefix) in new[] { (prefixName, true), (postfixName, false) })
        {
            if (name == null) continue;
            MethodInfo? hook = integrationType.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            Report(hook != null, $"{label}: {(isPrefix ? "prefix" : "postfix")} {name} exists and is static");
            if (hook == null) continue;
            Report(isPrefix ? hook.ReturnType == typeof(void) || hook.ReturnType == typeof(bool) : hook.ReturnType == typeof(void),
                $"{label}: {name} returns {(isPrefix ? "void or bool" : "void")} (returns {hook.ReturnType.Name})");

            foreach (ParameterInfo p in hook.GetParameters())
            {
                Type actual = p.ParameterType.IsByRef ? p.ParameterType.GetElementType()! : p.ParameterType;
                string pname = p.Name!;
                if (pname == "__instance")
                {
                    Report(!target.IsStatic && actual.IsAssignableFrom(gameType), $"{label}: {name}({pname}) is the object the method runs on ({actual.Name})");
                }
                else if (pname == "__result")
                {
                    Report(actual == target.ReturnType, $"{label}: {name}({pname}) has the method's return type ({actual.Name} vs {target.ReturnType.Name})");
                }
                else if (pname.StartsWith("__") && int.TryParse(pname.AsSpan(2), out int position))
                {
                    bool inRange = position < targetParameters.Length;
                    Type? expected = inRange ? targetParameters[position].ParameterType : null;
                    Type? expectedBase = expected is { IsByRef: true } ? expected.GetElementType() : expected;
                    Report(inRange && actual == expectedBase, $"{label}: {name}({pname}) matches argument {position} ({actual.Name} vs {expectedBase?.Name ?? "none"})");
                    if (p.ParameterType.IsByRef && inRange) Console.WriteLine($"     note: {pname} is ref, so the hook can replace that argument");
                }
                else
                {
                    Report(false, $"{label}: {name}: parameter '{pname}' is not a name Harmony can bind");
                }
            }
        }
    }
}

Console.WriteLine(failures == 0 ? $"all {checks} hook checks passed" : $"{failures} of {checks} hook checks FAILED");
return failures == 0 ? 0 : 1;

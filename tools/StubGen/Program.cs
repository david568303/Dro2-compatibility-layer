using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;

// StubGen: writes the compile-time stand-ins for the game's assemblies that CI builds against.
//
//   StubGen generate --plugin <CarXModManager.dll> --interop <game interop folder> --out <folder> [--lib <folder>]...
//   StubGen compare  <a.dll> <b.dll>
//
// generate: the plugin DLL must have been built against the game's REAL interop assemblies. Every game type and member it
//           references is looked up in the real assemblies and written out as a minimal C# stand-in (same assembly name,
//           namespace, type and member names, signatures). --lib adds folders holding other assemblies the plugin uses
//           (for example the compatibility layer's DLL).
// compare:  checks that two builds of the plugin (one against the real interop, one against the stand-ins) reference
//           exactly the same external members, which is what makes the CI build bind to the real game at runtime.

if (args.Length >= 1 && args[0] == "compare")
{
    if (args.Length != 3) { Console.Error.WriteLine("usage: StubGen compare <a.dll> <b.dll>"); return 2; }
    return Comparer.Run(args[1], args[2]);
}
if (args.Length >= 1 && args[0] == "generate")
{
    string? plugin = null, interop = null, output = null;
    var libraries = new List<string>();
    for (int i = 1; i < args.Length - 1; i++)
    {
        switch (args[i])
        {
            case "--plugin": plugin = args[++i]; break;
            case "--interop": interop = args[++i]; break;
            case "--out": output = args[++i]; break;
            case "--lib": libraries.Add(args[++i]); break;
        }
    }
    if (plugin == null || interop == null || output == null)
    {
        Console.Error.WriteLine("usage: StubGen generate --plugin <dll> --interop <folder> --out <folder> [--lib <folder>]...");
        return 2;
    }
    return new Generator(Path.GetFullPath(plugin), Path.GetFullPath(interop), Path.GetFullPath(output), libraries.Select(Path.GetFullPath).ToList()).Run();
}
Console.Error.WriteLine("usage: StubGen generate|compare ...");
return 2;

/// <summary>Finds what the plugin uses from the game's assemblies and writes minimal C# stand-ins for it.</summary>
internal sealed class Generator
{
    private const BindingFlags AllMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly HashSet<string> Keywords = new()
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue", "decimal", "default",
        "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto",
        "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out",
        "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc",
        "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort",
        "using", "virtual", "void", "volatile", "while",
    };

    private readonly string _pluginPath, _interopDirectory, _outputDirectory;
    private readonly List<string> _libraryDirectories;
    private readonly HashSet<string> _stubbedAssemblies = new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<Type> _neededTypes = new();
    private readonly Dictionary<Type, UsedMembers> _used = new();
    private readonly SortedDictionary<string, HashSet<string>> _dependencies = new(StringComparer.Ordinal);
    private string _currentAssembly = string.Empty;
    private readonly List<string> _warnings = new();

    /// <summary>What the plugin uses from one game type: member names, which are then emitted with all their overloads.</summary>
    private sealed class UsedMembers
    {
        public HashSet<string> Methods { get; } = new();
        public HashSet<string> Properties { get; } = new();
        public HashSet<string> Fields { get; } = new();
        public bool Constructors { get; set; }
        public bool Operators { get; set; }
    }

    public Generator(string pluginPath, string interopDirectory, string outputDirectory, List<string> libraryDirectories)
    {
        _pluginPath = pluginPath;
        _interopDirectory = interopDirectory;
        _outputDirectory = outputDirectory;
        _libraryDirectories = libraryDirectories;
    }

    public int Run()
    {
        foreach (string file in Directory.GetFiles(_interopDirectory, "*.dll"))
            _stubbedAssemblies.Add(Path.GetFileNameWithoutExtension(file));

        // framework first, then the game's assemblies, then everything next to this tool (BepInEx, Il2CppInterop, Harmony) and --lib folders
        var byFileName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string directory in new[] { RuntimeEnvironment.GetRuntimeDirectory(), _interopDirectory, AppContext.BaseDirectory }.Concat(_libraryDirectories))
            foreach (string file in Directory.GetFiles(directory, "*.dll"))
                byFileName.TryAdd(Path.GetFileName(file), file);
        byFileName[Path.GetFileName(_pluginPath)] = _pluginPath;

        using var context = new MetadataLoadContext(new PathAssemblyResolver(byFileName.Values), "System.Private.CoreLib");
        _context = context;

        using var stream = File.OpenRead(_pluginPath);
        using var pe = new PEReader(stream);
        MetadataReader metadata = pe.GetMetadataReader();
        var provider = new NodeProvider(this, metadata);

        // every type the plugin mentions anywhere, including generic instantiations
        foreach (TypeReferenceHandle handle in metadata.TypeReferences)
            TryResolve(() => { Type? type = ResolveTypeReference(metadata, handle); if (type != null) NeedType(type); });
        for (int row = 1; row <= metadata.GetTableRowCount(TableIndex.TypeSpec); row++)
        {
            TypeSpecificationHandle handle = MetadataTokens.TypeSpecificationHandle(row);
            TryResolve(() => NeedNode(metadata.GetTypeSpecification(handle).DecodeSignature(provider, null)));
        }
        for (int row = 1; row <= metadata.GetTableRowCount(TableIndex.MethodSpec); row++)
        {
            MethodSpecificationHandle handle = MetadataTokens.MethodSpecificationHandle(row);
            TryResolve(() => { foreach (TypeNode argument in metadata.GetMethodSpecification(handle).DecodeSignature(provider, null)) NeedNode(argument); });
        }
        // every member it calls or reads
        foreach (MemberReferenceHandle handle in metadata.MemberReferences)
            TryResolve(() => NeedMemberReference(metadata, provider, handle));

        return Write();
    }

    private MetadataLoadContext _context = null!;

    /// <summary>A type as seen in a signature: the real type definition (if it is a game type) and its generic arguments.</summary>
    internal sealed record TypeNode(Type? Definition, IReadOnlyList<TypeNode> Arguments);

    private void NeedNode(TypeNode node)
    {
        if (node.Definition != null) NeedType(node.Definition);
        foreach (TypeNode argument in node.Arguments) NeedNode(argument);
    }

    /// <summary>Finds the real type that a TypeRef row names, or null if it is not in one of the game's assemblies.</summary>
    internal Type? ResolveTypeReference(MetadataReader metadata, TypeReferenceHandle handle)
    {
        TypeReference reference = metadata.GetTypeReference(handle);
        string name = metadata.GetString(reference.Name), ns = metadata.GetString(reference.Namespace);
        switch (reference.ResolutionScope.Kind)
        {
            case HandleKind.AssemblyReference:
            {
                string assemblyName = metadata.GetString(metadata.GetAssemblyReference((AssemblyReferenceHandle)reference.ResolutionScope).Name);
                if (!_stubbedAssemblies.Contains(assemblyName)) return null;
                Assembly assembly = _context.LoadFromAssemblyName(assemblyName);
                return assembly.GetType(ns.Length > 0 ? ns + "." + name : name, throwOnError: false)
                    ?? throw new InvalidOperationException($"type {ns}.{name} was not found in {assemblyName}");
            }
            case HandleKind.TypeReference:
            {
                Type? declaring = ResolveTypeReference(metadata, (TypeReferenceHandle)reference.ResolutionScope);
                return declaring?.GetNestedType(name, BindingFlags.Public | BindingFlags.NonPublic)
                    ?? (declaring == null ? null : throw new InvalidOperationException($"nested type {name} was not found in {declaring.FullName}"));
            }
            default:
                return null;
        }
    }

    /// <summary>Records one MemberRef row: which game type it belongs to and which member name is used.</summary>
    private void NeedMemberReference(MetadataReader metadata, NodeProvider provider, MemberReferenceHandle handle)
    {
        MemberReference member = metadata.GetMemberReference(handle);
        TypeNode? parent = member.Parent.Kind switch
        {
            HandleKind.TypeReference => new TypeNode(ResolveTypeReference(metadata, (TypeReferenceHandle)member.Parent), Array.Empty<TypeNode>()),
            HandleKind.TypeSpecification => metadata.GetTypeSpecification((TypeSpecificationHandle)member.Parent).DecodeSignature(provider, null),
            _ => null,
        };
        if (parent?.Definition == null || !IsStubbed(parent.Definition)) return;
        NeedNode(parent);
        string name = metadata.GetString(member.Name);
        NeedMember(parent.Definition, name, member.GetKind() == MemberReferenceKind.Field);
    }

    private void TryResolve(Action action)
    {
        try { action(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _warnings.Add($"could not resolve a reference: {exception.GetType().Name}: {exception.Message}");
        }
    }

    // ---- what is needed -------------------------------------------------------------------------------------------------

    private bool IsStubbed(Type type) => _stubbedAssemblies.Contains(type.Assembly.GetName().Name ?? string.Empty);

    /// <summary>Records a type (and everything it needs: base type, declaring type, generic arguments and constraints).</summary>
    private void NeedType(Type type)
    {
        if (type.HasElementType) { NeedType(type.GetElementType()!); return; }
        if (type.IsGenericParameter)
        {
            foreach (Type constraint in type.GetGenericParameterConstraints()) NeedType(constraint);
            return;
        }
        if (type.IsConstructedGenericType)
        {
            NeedType(type.GetGenericTypeDefinition());
            foreach (Type argument in type.GenericTypeArguments) NeedType(argument);
            return;
        }
        if (!IsStubbed(type) || !_neededTypes.Add(type)) return;
        if (type.BaseType != null) NeedType(type.BaseType);
        if (type.DeclaringType != null) NeedType(type.DeclaringType);
        if (type.IsGenericTypeDefinition)
            foreach (Type parameter in type.GetGenericArguments()) NeedType(parameter);
    }

    /// <summary>
    /// Records a member the plugin uses, so it is written out (together with every overload of the same name). The member is
    /// looked up from the named type up through its base types, because the plugin may call an inherited member on a derived type.
    /// </summary>
    private void NeedMember(Type definition, string name, bool isField)
    {
        for (Type? type = definition; type != null && IsStubbed(type); type = type.BaseType)
        {
            if (type.IsConstructedGenericType) type = type.GetGenericTypeDefinition();
            if (!isField && name == ".ctor")
            {
                if (type != definition) continue;
                Used(type).Constructors = true;
                return;
            }
            if (isField)
            {
                if (type.GetFields(AllMembers).All(f => f.Name != name)) continue;
                Used(type).Fields.Add(name);
                NeedType(type);
                return;
            }
            MethodInfo[] methods = type.GetMethods(AllMembers).Where(m => m.Name == name).ToArray();
            if (methods.Length == 0) continue;
            UsedMembers used = Used(type);
            NeedType(type);
            if (name.StartsWith("op_", StringComparison.Ordinal)) used.Operators = true;
            else if (PropertyOfAccessor(type, methods[0]) is { } owningProperty) used.Properties.Add(owningProperty.Name);
            else used.Methods.Add(name);
            return;
        }
        _warnings.Add($"member {definition.FullName}::{name} was not found in the real assembly");
    }

    private UsedMembers Used(Type type)
    {
        if (!_used.TryGetValue(type, out UsedMembers? used)) _used[type] = used = new UsedMembers();
        return used;
    }

    // ---- writing ----------------------------------------------------------------------------------------------------------

    private int Write()
    {
        // pull in the signature types of everything that will be written, until nothing new appears
        int before;
        do
        {
            before = _neededTypes.Count;
            foreach (Type type in _neededTypes.ToList())
                foreach (MemberInfo member in MembersToWrite(type))
                    foreach (Type signatureType in SignatureTypes(member)) NeedType(signatureType);
        }
        while (_neededTypes.Count != before);

        var byAssembly = _neededTypes
            .Where(t => t.DeclaringType == null)
            .GroupBy(t => t.Assembly.GetName().Name!)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        if (Directory.Exists(_outputDirectory)) Directory.Delete(_outputDirectory, recursive: true);
        Directory.CreateDirectory(_outputDirectory);

        foreach (var group in byAssembly)
        {
            _currentAssembly = group.Key;
            _dependencies[_currentAssembly] = new HashSet<string>(StringComparer.Ordinal);
            var source = new StringBuilder();
            source.AppendLine("// <auto-generated> by tools/StubGen from the plugin's references. A stand-in with the real names and signatures; it holds no game code. </auto-generated>");
            source.AppendLine("#pragma warning disable");
            source.AppendLine();

            foreach (var byNamespace in group.GroupBy(t => t.Namespace ?? string.Empty).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                string? ns = byNamespace.Key.Length == 0 ? null : byNamespace.Key;
                if (ns != null) { source.AppendLine($"namespace {ns}"); source.AppendLine("{"); }
                foreach (Type type in byNamespace.OrderBy(t => t.Name, StringComparer.Ordinal))
                    WriteType(source, type, ns != null ? 1 : 0);
                if (ns != null) source.AppendLine("}");
                source.AppendLine();
            }

            string directory = Path.Combine(_outputDirectory, group.Key);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, group.Key + ".cs"), source.ToString());
        }

        // project files and build order
        List<string>? order = SortByDependency(out string? cycle);
        if (order == null)
        {
            Console.Error.WriteLine($"ERROR: the stand-in assemblies depend on each other in a circle ({cycle}); they cannot be built as separate projects.");
            return 1;
        }
        foreach (string assembly in order)
        {
            var project = new StringBuilder();
            project.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
            project.AppendLine($"  <PropertyGroup><AssemblyName>{assembly}</AssemblyName></PropertyGroup>");
            if (_dependencies[assembly].Count > 0)
            {
                project.AppendLine("  <ItemGroup>");
                foreach (string dependency in _dependencies[assembly].OrderBy(d => d, StringComparer.Ordinal))
                    project.AppendLine($"    <ProjectReference Include=\"../{dependency}/{dependency}.csproj\" />");
                project.AppendLine("  </ItemGroup>");
            }
            project.AppendLine("</Project>");
            File.WriteAllText(Path.Combine(_outputDirectory, assembly, assembly + ".csproj"), project.ToString());
        }
        File.WriteAllLines(Path.Combine(_outputDirectory, "build-order.txt"), order.Select(a => $"{a}/{a}.csproj"));
        File.WriteAllText(Path.Combine(_outputDirectory, "Directory.Build.props"), PropsFile);
        File.WriteAllText(Path.Combine(_outputDirectory, "README.md"), ReadmeFile);

        foreach (string warning in _warnings.Distinct().OrderBy(w => w)) Console.WriteLine("warning: " + warning);
        Console.WriteLine($"wrote {_neededTypes.Count} types in {order.Count} assemblies to {_outputDirectory}");
        foreach (string assembly in order) Console.WriteLine($"  {assembly}: {_neededTypes.Count(t => t.Assembly.GetName().Name == assembly)} types");
        return 0;
    }

    private List<string>? SortByDependency(out string? cycle)
    {
        var order = new List<string>();
        var state = new Dictionary<string, int>(); // 1 = visiting, 2 = done
        string? found = null;
        bool Visit(string assembly, Stack<string> path)
        {
            if (state.TryGetValue(assembly, out int s)) { if (s == 1) { found = string.Join(" -> ", path.Reverse().Append(assembly)); return false; } return true; }
            state[assembly] = 1;
            path.Push(assembly);
            foreach (string dependency in _dependencies[assembly].OrderBy(d => d, StringComparer.Ordinal))
                if (!Visit(dependency, path)) return false;
            path.Pop();
            state[assembly] = 2;
            order.Add(assembly);
            return true;
        }
        foreach (string assembly in _dependencies.Keys)
            if (!Visit(assembly, new Stack<string>())) { cycle = found; return null; }
        cycle = null;
        return order;
    }

    private const string PropsFile = @"<Project>
  <!-- Stand-ins for the game's Il2CppInterop assemblies, used only to compile in CI. They must never be shipped:
       the compiled plugin references them by assembly name and binds to the real ones inside the game. -->
  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <AssemblyVersion>0.0.0.0</AssemblyVersion>
    <FileVersion>0.0.0.0</FileVersion>
    <Version>0.0.0</Version>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
    <CopyLocalLockFileAssemblies>false</CopyLocalLockFileAssemblies>
    <NoWarn>$(NoWarn);NU1603;CS8618;CS0649;CS0169;CS0108;CS0114;CS0660;CS0661;CS8981</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""BepInEx.Unity.IL2CPP"" Version=""6.0.0-be.791"" PrivateAssets=""all"" />
  </ItemGroup>
</Project>
";

    private const string ReadmeFile = @"# Game stand-ins (generated)

Written by `tools/StubGen` from what the plugin references. Each folder is an assembly with the game's real assembly name,
holding only the types and members the plugin uses, with their real names and signatures and no bodies. CI compiles the
plugin against them because the game's own assemblies cannot be published; the compiled DLL then binds to the real ones
inside the game. They are never shipped.

Regenerate (needs the game's interop folder) with `tools/regenerate-stubs.sh`, which also checks that the stand-in build and
the real build reference exactly the same members. Do not edit by hand.
";

    // ---- what to write for a type ------------------------------------------------------------------------------------------

    /// <summary>Members of <paramref name="type"/> that the plugin's usage requires to be written (used names with all overloads).</summary>
    private IEnumerable<MemberInfo> MembersToWrite(Type type)
    {
        if (!_used.TryGetValue(type, out UsedMembers? used))
        {
            if (!type.IsValueType || type.IsEnum) yield break;
            // a struct is written with its public instance fields even if nothing of it is used
            foreach (FieldInfo field in type.GetFields(AllMembers).Where(f => f.IsPublic && !f.IsStatic)) yield return field;
            yield break;
        }
        foreach (FieldInfo field in type.GetFields(AllMembers))
            if (field.IsPublic && (used.Fields.Contains(field.Name) || (type.IsValueType && !field.IsStatic) || type.IsEnum)) yield return field;
        if (used.Constructors)
            foreach (ConstructorInfo constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                yield return constructor;
        foreach (PropertyInfo property in type.GetProperties(AllMembers))
            if (used.Properties.Contains(property.Name) && IsPublic(property)) yield return property;
        foreach (MethodInfo method in type.GetMethods(AllMembers))
        {
            if (!method.IsPublic) continue;
            if (method.IsSpecialName && method.Name.StartsWith("op_", StringComparison.Ordinal))
            {
                if (used.Operators && OperatorToken(method.Name) != null) yield return method;
                continue;
            }
            if (PropertyOfAccessor(type, method) != null) continue; // written as part of its property
            if (used.Methods.Contains(method.Name)) yield return method;
        }
    }

    /// <summary>The property that <paramref name="method"/> is the getter or setter of, or null if it is not an accessor.</summary>
    private static PropertyInfo? PropertyOfAccessor(Type type, MethodInfo method)
    {
        foreach (PropertyInfo property in type.GetProperties(AllMembers))
            if (property.GetMethod?.MetadataToken == method.MetadataToken || property.SetMethod?.MetadataToken == method.MetadataToken)
                if (property.GetMethod?.Module == method.Module || property.SetMethod?.Module == method.Module) return property;
        return null;
    }

    private static bool IsPublic(PropertyInfo property) => (property.GetMethod?.IsPublic ?? false) || (property.SetMethod?.IsPublic ?? false);

    private IEnumerable<Type> SignatureTypes(MemberInfo member)
    {
        switch (member)
        {
            case FieldInfo field: yield return field.FieldType; break;
            case ConstructorInfo constructor:
                foreach (ParameterInfo parameter in constructor.GetParameters()) yield return parameter.ParameterType;
                break;
            case PropertyInfo property:
                yield return property.PropertyType;
                foreach (ParameterInfo parameter in property.GetIndexParameters()) yield return parameter.ParameterType;
                break;
            case MethodInfo method:
                yield return method.ReturnType;
                foreach (ParameterInfo parameter in method.GetParameters()) yield return parameter.ParameterType;
                if (method.IsGenericMethodDefinition)
                    foreach (Type argument in method.GetGenericArguments())
                        foreach (Type constraint in argument.GetGenericParameterConstraints()) yield return constraint;
                break;
        }
    }

    private void WriteType(StringBuilder sb, Type type, int depth)
    {
        string pad = new(' ', depth * 4);
        string name = SimpleName(type);
        string generics = GenericParameterList(type);
        string constraints = ConstraintClauses(type, type.GetGenericArguments().Skip(OwnerGenericCount(type)).ToArray(), pad + "    ");

        if (type.IsEnum)
        {
            Type underlying = Enum.GetUnderlyingType(type);
            sb.AppendLine($"{pad}public enum {name} : {Keyword(underlying)}");
            sb.AppendLine($"{pad}{{");
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                sb.AppendLine($"{pad}    {Identifier(field.Name)} = {Literal(field.GetRawConstantValue(), underlying)},");
            sb.AppendLine($"{pad}}}");
            return;
        }

        bool isStaticClass = type.IsAbstract && type.IsSealed;
        string kind = type.IsInterface ? "interface" : type.IsValueType ? "struct" : "class";
        string modifiers = isStaticClass ? "public static " : "public ";
        string bases = string.Empty;
        if (kind == "class" && !isStaticClass && type.BaseType != null && type.BaseType != typeof(object) && type.BaseType.FullName != "System.Object")
            bases = " : " + TypeName(type.BaseType);

        sb.AppendLine($"{pad}{modifiers}{kind} {name}{generics}{bases}{constraints}");
        sb.AppendLine($"{pad}{{");

        // nested types first
        foreach (Type nested in _neededTypes.Where(t => t.DeclaringType == type).OrderBy(t => t.Name, StringComparer.Ordinal))
            WriteType(sb, nested, depth + 1);

        bool derivesFromIl2Cpp = kind == "class" && !isStaticClass && DerivesFrom(type, "Il2CppObjectBase");
        if (derivesFromIl2Cpp) sb.AppendLine($"{pad}    public {StripArity(type.Name)}(System.IntPtr pointer) : base(pointer) {{ }}");

        var members = MembersToWrite(type).ToList();
        var writtenProperties = new HashSet<PropertyInfo>();
        foreach (MemberInfo member in members)
        {
            switch (member)
            {
                case FieldInfo field: WriteField(sb, field, type, pad + "    "); break;
                case ConstructorInfo constructor:
                    // the wrapper constructor taking a pointer is already written above for every game class
                    bool isPointerConstructor = constructor.GetParameters() is { Length: 1 } only && only[0].ParameterType.FullName == "System.IntPtr";
                    if ((derivesFromIl2Cpp && !isPointerConstructor) || kind == "struct") WriteConstructor(sb, constructor, type, derivesFromIl2Cpp, pad + "    ");
                    break;
                case PropertyInfo property: if (writtenProperties.Add(property)) WriteProperty(sb, property, kind == "interface", pad + "    "); break;
                case MethodInfo method: WriteMethod(sb, method, type, kind == "interface", pad + "    "); break;
            }
        }
        sb.AppendLine($"{pad}}}");
    }

    private void WriteField(StringBuilder sb, FieldInfo field, Type owner, string pad)
    {
        string type = TypeName(field.FieldType);
        if (field.IsLiteral && !owner.IsEnum)
        {
            object? value = field.GetRawConstantValue();
            sb.AppendLine($"{pad}public const {type} {Identifier(field.Name)} = {Literal(value, field.FieldType)};");
            return;
        }
        string initial = field.IsStatic || field.IsInitOnly ? " = default!" : string.Empty;
        sb.AppendLine($"{pad}public {(field.IsStatic ? "static " : string.Empty)}{type} {Identifier(field.Name)}{initial};");
    }

    private void WriteConstructor(StringBuilder sb, ConstructorInfo constructor, Type owner, bool callBase, string pad)
    {
        string parameters = ParameterList(constructor.GetParameters());
        string baseCall = callBase ? " : base(default(System.IntPtr))" : string.Empty;
        sb.AppendLine($"{pad}public {StripArity(owner.Name)}({parameters}){baseCall} {{ }}");
    }

    private void WriteProperty(StringBuilder sb, PropertyInfo property, bool isInterface, string pad)
    {
        ParameterInfo[] index = property.GetIndexParameters();
        string name = index.Length > 0 ? $"this[{ParameterList(index)}]" : Identifier(property.Name);
        bool isStatic = (property.GetMethod ?? property.SetMethod)!.IsStatic;
        string accessors = string.Empty;
        if (property.GetMethod is { IsPublic: true }) accessors += isInterface ? " get;" : " get { throw null!; }";
        if (property.SetMethod is { IsPublic: true }) accessors += isInterface ? " set;" : " set { throw null!; }";
        sb.AppendLine($"{pad}public {(isStatic ? "static " : string.Empty)}{TypeName(property.PropertyType)} {name} {{{accessors} }}");
    }

    private void WriteMethod(StringBuilder sb, MethodInfo method, Type owner, bool isInterface, string pad)
    {
        string? operatorToken = method.IsSpecialName ? OperatorToken(method.Name) : null;
        if (method.IsSpecialName && method.Name.StartsWith("op_", StringComparison.Ordinal) && operatorToken == null) return;
        ParameterInfo[] parameters = method.GetParameters();
        string body = isInterface ? ";" : " { throw null!; }";

        if (operatorToken != null)
        {
            string returnType = TypeName(method.ReturnType);
            if (operatorToken == "implicit" || operatorToken == "explicit")
                sb.AppendLine($"{pad}public static {operatorToken} operator {returnType}({ParameterList(parameters)}){body}");
            else
                sb.AppendLine($"{pad}public static {returnType} operator {operatorToken}({ParameterList(parameters)}){body}");
            return;
        }

        string returnText = method.ReturnType.IsByRef ? "ref " + TypeName(method.ReturnType.GetElementType()!) : method.ReturnType == typeof(void) ? "void" : TypeName(method.ReturnType);
        string generics = method.IsGenericMethodDefinition ? "<" + string.Join(", ", method.GetGenericArguments().Select(a => a.Name)) + ">" : string.Empty;
        string constraints = method.IsGenericMethodDefinition ? ConstraintClauses(null, method.GetGenericArguments(), pad + "    ") : string.Empty;
        sb.AppendLine($"{pad}public {(method.IsStatic ? "static " : string.Empty)}{returnText} {Identifier(method.Name)}{generics}({ParameterList(parameters)}){constraints}{body}");
    }

    private string ParameterList(ParameterInfo[] parameters)
    {
        var parts = new List<string>();
        for (int i = 0; i < parameters.Length; i++)
        {
            ParameterInfo p = parameters[i];
            Type type = p.ParameterType;
            string modifier = string.Empty;
            if (type.IsByRef)
            {
                modifier = p.IsOut ? "out " : p.IsIn ? "in " : "ref ";
                type = type.GetElementType()!;
            }
            string name = string.IsNullOrEmpty(p.Name) ? $"arg{i}" : Identifier(p.Name);
            string text = $"{modifier}{TypeName(type)} {name}";
            if (p.HasDefaultValue && modifier.Length == 0)
            {
                try { text += " = " + DefaultValueText(p, type); } catch (Exception) { text += " = default!"; }
            }
            parts.Add(text);
        }
        return string.Join(", ", parts);
    }

    private static string DefaultValueText(ParameterInfo parameter, Type type)
    {
        object? value = parameter.RawDefaultValue;
        if (value == null || value == DBNull.Value || value == Type.Missing) return type.IsValueType ? "default" : "null";
        return Literal(value, type);
    }

    // ---- names and literals ------------------------------------------------------------------------------------------------

    private static string? OperatorToken(string name) => name switch
    {
        "op_Equality" => "==",
        "op_Inequality" => "!=",
        "op_Implicit" => "implicit",
        "op_Explicit" => "explicit",
        "op_Addition" => "+",
        "op_Subtraction" => "-",
        "op_Multiply" => "*",
        "op_Division" => "/",
        "op_UnaryNegation" => "-",
        "op_LessThan" => "<",
        "op_GreaterThan" => ">",
        "op_LessThanOrEqual" => "<=",
        "op_GreaterThanOrEqual" => ">=",
        _ => null,
    };

    private static string Identifier(string name) => Keywords.Contains(name) ? "@" + name : name;

    private static string StripArity(string name)
    {
        int tick = name.IndexOf('`');
        return tick < 0 ? name : name[..tick];
    }

    private static string SimpleName(Type type) => Identifier(StripArity(type.Name));

    private static int OwnerGenericCount(Type type) => type.DeclaringType != null && type.DeclaringType.IsGenericTypeDefinition ? type.DeclaringType.GetGenericArguments().Length : 0;

    private static string GenericParameterList(Type type)
    {
        if (!type.IsGenericTypeDefinition) return string.Empty;
        Type[] own = type.GetGenericArguments().Skip(OwnerGenericCount(type)).ToArray();
        return own.Length == 0 ? string.Empty : "<" + string.Join(", ", own.Select(a => a.Name)) + ">";
    }

    private string ConstraintClauses(Type? owner, Type[] parameters, string pad)
    {
        var sb = new StringBuilder();
        foreach (Type parameter in parameters)
        {
            var parts = new List<string>();
            GenericParameterAttributes attributes = parameter.GenericParameterAttributes;
            if (attributes.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint)) parts.Add("class");
            if (attributes.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint)) parts.Add("struct");
            foreach (Type constraint in parameter.GetGenericParameterConstraints())
            {
                if (constraint.FullName == "System.ValueType" && attributes.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint)) continue;
                parts.Add(TypeName(constraint));
            }
            if (attributes.HasFlag(GenericParameterAttributes.DefaultConstructorConstraint) && !attributes.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint)) parts.Add("new()");
            if (parts.Count > 0) sb.Append($"\n{pad}where {parameter.Name} : {string.Join(", ", parts)}");
        }
        return sb.ToString();
    }

    private static bool DerivesFrom(Type type, string baseTypeName)
    {
        for (Type? t = type.BaseType; t != null; t = t.BaseType)
            if (t.Name == baseTypeName) return true;
        return false;
    }

    /// <summary>The C# spelling of a type, always fully qualified; also records which stand-in assemblies this one depends on.</summary>
    private string TypeName(Type type)
    {
        if (type.IsByRef) return TypeName(type.GetElementType()!);
        if (type.IsArray)
        {
            int rank = type.GetArrayRank();
            return TypeName(type.GetElementType()!) + (rank == 1 ? "[]" : "[" + new string(',', rank - 1) + "]");
        }
        if (type.IsPointer) return TypeName(type.GetElementType()!) + "*";
        if (type.IsGenericParameter) return type.Name;
        if (type == typeof(void) || type.FullName == "System.Void") return "void";
        return "global::" + QualifiedName(type);
    }

    /// <summary>Namespace, declaring types and generic arguments of a type, e.g. <c>Ns.Outer&lt;T&gt;.Inner</c>.</summary>
    private string QualifiedName(Type type)
    {
        Type definition = type.IsConstructedGenericType ? type.GetGenericTypeDefinition() : type;
        if (IsStubbed(definition) && _currentAssembly.Length > 0)
        {
            string assembly = definition.Assembly.GetName().Name!;
            if (assembly != _currentAssembly && _dependencies.TryGetValue(_currentAssembly, out HashSet<string>? set)) set.Add(assembly);
        }

        Type[] allArguments = type.IsConstructedGenericType ? type.GenericTypeArguments : definition.IsGenericTypeDefinition ? definition.GetGenericArguments() : Array.Empty<Type>();
        string Build(Type d, Type[] arguments)
        {
            int owner = OwnerGenericCount(d);
            string prefix = d.DeclaringType != null ? Build(d.DeclaringType, arguments.Take(owner).ToArray()) + "." : (d.Namespace is { Length: > 0 } ns ? ns + "." : string.Empty);
            string own = Identifier(StripArity(d.Name));
            Type[] ownArguments = arguments.Skip(owner).ToArray();
            string generics = ownArguments.Length > 0 ? "<" + string.Join(", ", ownArguments.Select(TypeName)) + ">" : string.Empty;
            return prefix + own + generics;
        }
        return Build(definition, allArguments);
    }

    private static string Keyword(Type type) => type.FullName switch
    {
        "System.Byte" => "byte", "System.SByte" => "sbyte", "System.Int16" => "short", "System.UInt16" => "ushort",
        "System.Int32" => "int", "System.UInt32" => "uint", "System.Int64" => "long", "System.UInt64" => "ulong", _ => "int",
    };

    private static string Literal(object? value, Type type)
    {
        switch (value)
        {
            case null: return "default!";
            case string s: return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
            case bool b: return b ? "true" : "false";
            case char c: return $"'\\u{(int)c:x4}'";
            case float f: return float.IsNaN(f) ? "float.NaN" : float.IsInfinity(f) ? (f > 0 ? "float.PositiveInfinity" : "float.NegativeInfinity") : f.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "f";
            case double d: return double.IsNaN(d) ? "double.NaN" : double.IsInfinity(d) ? (d > 0 ? "double.PositiveInfinity" : "double.NegativeInfinity") : d.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "d";
            case long l: return l == long.MinValue ? "long.MinValue" : l + "L";
            case ulong ul: return ul + "UL";
            case uint ui: return ui + "U";
            case IConvertible convertible: return Convert.ToString(convertible, System.Globalization.CultureInfo.InvariantCulture)!;
            default: return "default!";
        }
    }
}

/// <summary>Decodes metadata signatures into <see cref="Generator.TypeNode"/>s: the game types they mention and their generic arguments.</summary>
internal sealed class NodeProvider : ISignatureTypeProvider<Generator.TypeNode, object?>
{
    private static readonly Generator.TypeNode None = new(null, Array.Empty<Generator.TypeNode>());
    private readonly Generator _generator;
    private readonly MetadataReader _metadata;

    public NodeProvider(Generator generator, MetadataReader metadata)
    {
        _generator = generator;
        _metadata = metadata;
    }

    public Generator.TypeNode GetPrimitiveType(PrimitiveTypeCode typeCode) => None;
    public Generator.TypeNode GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => None;
    public Generator.TypeNode GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) =>
        new(_generator.ResolveTypeReference(_metadata, handle), Array.Empty<Generator.TypeNode>());
    public Generator.TypeNode GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
    public Generator.TypeNode GetSZArrayType(Generator.TypeNode elementType) => elementType;
    public Generator.TypeNode GetArrayType(Generator.TypeNode elementType, ArrayShape shape) => elementType;
    public Generator.TypeNode GetByReferenceType(Generator.TypeNode elementType) => elementType;
    public Generator.TypeNode GetPointerType(Generator.TypeNode elementType) => elementType;
    public Generator.TypeNode GetGenericInstantiation(Generator.TypeNode genericType, ImmutableArray<Generator.TypeNode> typeArguments) => new(genericType.Definition, typeArguments);
    public Generator.TypeNode GetGenericMethodParameter(object? genericContext, int index) => None;
    public Generator.TypeNode GetGenericTypeParameter(object? genericContext, int index) => None;
    public Generator.TypeNode GetFunctionPointerType(MethodSignature<Generator.TypeNode> signature) => None;
    public Generator.TypeNode GetModifiedType(Generator.TypeNode modifier, Generator.TypeNode unmodifiedType, bool isRequired) => unmodifiedType;
    public Generator.TypeNode GetPinnedType(Generator.TypeNode elementType) => elementType;
}

/// <summary>Compares the external members two builds of the plugin reference.</summary>
internal static class Comparer
{
    public static int Run(string firstPath, string secondPath)
    {
        SortedSet<string> first = ExternalReferences(firstPath), second = ExternalReferences(secondPath);
        var onlyFirst = first.Except(second).ToList();
        var onlySecond = second.Except(first).ToList();
        Console.WriteLine($"{Path.GetFileName(firstPath)}: {first.Count} external references; {Path.GetFileName(secondPath)}: {second.Count}");
        foreach (string line in onlyFirst) Console.WriteLine($"only in the first:  {line}");
        foreach (string line in onlySecond) Console.WriteLine($"only in the second: {line}");
        Console.WriteLine(onlyFirst.Count == 0 && onlySecond.Count == 0 ? "identical: both builds bind to exactly the same members" : "DIFFERENT");
        return onlyFirst.Count == 0 && onlySecond.Count == 0 ? 0 : 1;
    }

    /// <summary>Every member reference, as "[assembly]Type::name(signature)", plus every referenced assembly name.</summary>
    private static SortedSet<string> ExternalReferences(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader md = pe.GetMetadataReader();
        var provider = new Provider(md);
        var result = new SortedSet<string>(StringComparer.Ordinal);
        foreach (AssemblyReferenceHandle handle in md.AssemblyReferences)
            result.Add("assembly " + md.GetString(md.GetAssemblyReference(handle).Name));
        foreach (MemberReferenceHandle handle in md.MemberReferences)
        {
            MemberReference member = md.GetMemberReference(handle);
            string parent = member.Parent.Kind switch
            {
                HandleKind.TypeReference => provider.TypeReference((TypeReferenceHandle)member.Parent),
                HandleKind.TypeSpecification => md.GetTypeSpecification((TypeSpecificationHandle)member.Parent).DecodeSignature(provider, null),
                _ => member.Parent.Kind.ToString(),
            };
            string signature = member.GetKind() == MemberReferenceKind.Method
                ? member.DecodeMethodSignature(provider, null) is { } s ? $"{s.ReturnType} ({string.Join(", ", s.ParameterTypes)}) generic={s.GenericParameterCount}" : string.Empty
                : member.DecodeFieldSignature(provider, null);
            result.Add($"{parent}::{md.GetString(member.Name)} {signature}");
        }
        return result;
    }

    private sealed class Provider : ISignatureTypeProvider<string, object?>
    {
        private readonly MetadataReader _md;
        public Provider(MetadataReader md) => _md = md;

        public string TypeReference(TypeReferenceHandle handle)
        {
            TypeReference type = _md.GetTypeReference(handle);
            string name = _md.GetString(type.Name), ns = _md.GetString(type.Namespace);
            string full = ns.Length > 0 ? ns + "." + name : name;
            return type.ResolutionScope.Kind switch
            {
                HandleKind.AssemblyReference => $"[{_md.GetString(_md.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope).Name)}]{full}",
                HandleKind.TypeReference => TypeReference((TypeReferenceHandle)type.ResolutionScope) + "/" + name,
                _ => full,
            };
        }

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => reader.GetString(reader.GetTypeDefinition(handle).Name);
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => TypeReference(handle);
        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";
        public string GetByReferenceType(string elementType) => elementType + "&";
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(",", typeArguments) + ">";
        public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
        public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
        public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
        public string GetPinnedType(string elementType) => elementType;
    }
}

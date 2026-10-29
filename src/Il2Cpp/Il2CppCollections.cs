using Il2CppInterop.Runtime.InteropTypes;
using IEnumerator = Il2CppSystem.Collections.IEnumerator;

namespace CarXModManager.Il2Cpp;

/// <summary>
/// Reading and building the game's own collection objects. The game's methods take and return IL2CPP collections
/// (<c>List&lt;T&gt;</c>, <c>IEnumerable&lt;T&gt;</c>), which are not .NET collections: this copies them into ordinary lists
/// to work on, and wraps an ordinary list back into the game's type.
/// </summary>
internal static class Il2CppCollections
{
    /// <summary>
    /// Copies any IL2CPP <c>IEnumerable&lt;T&gt;</c> (a list, a lazy query, ...) into a .NET list. Returns an empty list
    /// for <c>null</c>. Works through the enumerator, so it does not matter what kind of collection the game passed.
    /// </summary>
    public static List<T> ToManagedList<T>(Il2CppSystem.Collections.Generic.IEnumerable<T>? source) where T : Il2CppObjectBase
    {
        var result = new List<T>();
        if (source == null) return result;

        Il2CppSystem.Collections.Generic.IEnumerator<T> enumerator = source.GetEnumerator();
        // MoveNext lives on the non-generic IEnumerator interface, which the generic one extends
        IEnumerator mover = enumerator.Cast<IEnumerator>();
        try
        {
            while (mover.MoveNext()) result.Add(enumerator.Current);
        }
        finally
        {
            enumerator.TryCast<Il2CppSystem.IDisposable>()?.Dispose();
        }
        return result;
    }

    /// <summary>Wraps .NET items into a new IL2CPP list, passed as the <c>IEnumerable&lt;T&gt;</c> game methods expect.</summary>
    public static Il2CppSystem.Collections.Generic.IEnumerable<T> ToIl2CppEnumerable<T>(IEnumerable<T> items) where T : Il2CppObjectBase
    {
        var list = new Il2CppSystem.Collections.Generic.List<T>();
        foreach (T item in items) list.Add(item);
        return list.Cast<Il2CppSystem.Collections.Generic.IEnumerable<T>>();
    }
}

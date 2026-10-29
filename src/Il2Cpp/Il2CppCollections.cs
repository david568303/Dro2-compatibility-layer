using Il2CppInterop.Runtime.InteropTypes;

namespace CarXModManager.Il2Cpp;

/// <summary>
/// Reading and building the game's own collection objects. The game's methods take and return IL2CPP collections
/// (<c>List&lt;T&gt;</c>, <c>IEnumerable&lt;T&gt;</c>), which are not .NET collections: this copies them into ordinary lists
/// to work on, and wraps an ordinary list back into the game's type.
/// <para>
/// Collections are read by position (<c>Count</c> and the indexer), never by enumerating. An IL2CPP enumerator of a list is a
/// boxed struct, and calling its <c>MoveNext</c> through the interop fails ("Collection was modified"), so a list can only be
/// read safely when its exact type is known.
/// </para>
/// </summary>
internal static class Il2CppCollections
{
    /// <summary>
    /// Copies the elements of <paramref name="source"/> into <paramref name="into"/>, in order, if it is exactly a game
    /// <c>List&lt;<typeparamref name="TItem"/>&gt;</c>. Elements that are <c>null</c> are skipped.
    /// </summary>
    /// <typeparam name="TItem">The list's element type. Elements of a list of a subtype (a <c>List&lt;Sub&gt;</c> where
    /// <typeparamref name="TBase"/> is wanted) are read through that subtype, so try the likely element types one after another.</typeparam>
    /// <typeparam name="TBase">The type the caller wants the elements as.</typeparam>
    /// <param name="source">The collection the game passed (declared as an interface, so any kind of collection can arrive).</param>
    /// <param name="into">Receives the elements. Left unchanged when the result is <c>false</c>.</param>
    /// <returns><c>true</c> if <paramref name="source"/> is a <c>List&lt;<typeparamref name="TItem"/>&gt;</c> and was copied (an empty list also gives <c>true</c>);
    /// <c>false</c> if it is a different kind of collection, in which case the caller should leave the game's collection alone.</returns>
    public static bool TryCopyList<TItem, TBase>(Il2CppObjectBase source, List<TBase> into) where TItem : Il2CppObjectBase, TBase
    {
        var list = source.TryCast<Il2CppSystem.Collections.Generic.List<TItem>>();
        if (list == null) return false;

        int count = list.Count;
        var copy = new List<TBase>(count);
        for (int i = 0; i < count; i++)
        {
            TItem? item = list[i];
            if (item != null) copy.Add(item);
        }
        into.AddRange(copy);
        return true;
    }

    /// <summary>
    /// Copies the elements of <paramref name="source"/> into <paramref name="into"/>, in order, if it is exactly a game array
    /// <c>TItem[]</c> (the game passes an empty array while the first page of rooms is still loading). Elements that are
    /// <c>null</c> are skipped. Never throws: if the interop cannot treat the object as an array, the result is <c>false</c>.
    /// </summary>
    /// <returns><c>true</c> if <paramref name="source"/> is a <c>TItem[]</c> and was copied; <c>false</c> otherwise (<paramref name="into"/> is unchanged).</returns>
    public static bool TryCopyArray<TItem, TBase>(Il2CppObjectBase source, List<TBase> into) where TItem : Il2CppObjectBase, TBase
    {
        try
        {
            var array = source.TryCast<Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<TItem>>();
            if (array == null) return false;
            var copy = new List<TBase>(array.Length);
            for (int i = 0; i < array.Length; i++)
            {
                TItem? item = array[i];
                if (item != null) copy.Add(item);
            }
            into.AddRange(copy);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// The game's name for the type of an object, for the log: for a list of rooms it reads like
    /// <c>System.Collections.Generic.List`1[SomeNamespace.SomeRoomType]</c>. Never throws.
    /// </summary>
    public static string DescribeType(Il2CppObjectBase? source)
    {
        if (source == null) return "null";
        try { return source.Cast<Il2CppSystem.Object>().GetIl2CppType().ToString(); }
        catch (Exception exception) { return $"an unreadable type ({exception.GetType().Name})"; }
    }

    /// <summary>Wraps .NET items into a new IL2CPP list, passed as the <c>IEnumerable&lt;T&gt;</c> game methods expect.</summary>
    public static Il2CppSystem.Collections.Generic.IEnumerable<T> ToIl2CppEnumerable<T>(IEnumerable<T> items) where T : Il2CppObjectBase
    {
        var list = new Il2CppSystem.Collections.Generic.List<T>();
        foreach (T item in items) list.Add(item);
        return list.Cast<Il2CppSystem.Collections.Generic.IEnumerable<T>>();
    }
}

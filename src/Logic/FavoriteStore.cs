namespace CarXModManager.Logic;

/// <summary>
/// A set of favourite ids (for example track ids) that survives restarts. Stored as a plain text file, one id per line, so
/// a player can read or edit it. Lines that are blank or start with <c>#</c> are ignored. Saving never throws: a favourite
/// that cannot be written is still kept for the rest of the session.
/// Pure code with no game or Unity dependency, covered by the test project.
/// </summary>
internal sealed class FavoriteStore
{
    private readonly string _path;
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);

    /// <summary>Opens the store, reading the file if it exists.</summary>
    /// <param name="path">Where the favourites are kept.</param>
    public FavoriteStore(string path)
    {
        _path = path;
        Load();
    }

    /// <summary>The number of favourites.</summary>
    public int Count => _ids.Count;

    /// <summary>True if <paramref name="id"/> is a favourite.</summary>
    public bool Contains(string id) => _ids.Contains(id);

    /// <summary>Adds the id if it is not a favourite, removes it if it is, saves, and returns whether it is now a favourite.</summary>
    public bool Toggle(string id)
    {
        bool added = _ids.Add(id) || !_ids.Remove(id);
        Save();
        return added;
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            foreach (string line in File.ReadAllLines(_path))
            {
                string id = line.Trim();
                if (id.Length > 0 && !id.StartsWith('#')) _ids.Add(id);
            }
        }
        catch (Exception)
        {
            // an unreadable file simply means no favourites yet
        }
    }

    private void Save()
    {
        try
        {
            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temporary = _path + ".tmp";
            File.WriteAllLines(temporary, _ids.OrderBy(id => id, StringComparer.Ordinal));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception)
        {
            // keep the favourite in memory even if the disk refuses
        }
    }
}

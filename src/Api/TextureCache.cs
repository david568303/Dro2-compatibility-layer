using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace CarXModManager.Api;

/// <summary>How <see cref="TextureReplacer"/> caches finished textures on disk.</summary>
/// <param name="Directory">Where the cache files go (created if missing). Use a folder of your own, e.g. under BepInEx/cache.</param>
/// <param name="SettingsKey">Identifies everything besides the input pixels that changes the result (the processing algorithm's
/// version and every setting that affects it). A different key never hits an entry made with another one.</param>
/// <param name="MaxBytes">The cache is trimmed, least recently used first, to stay under this size.</param>
public sealed record TextureCacheOptions(string Directory, string SettingsKey, long MaxBytes = 256L * 1024 * 1024);

/// <summary>
/// On-disk store of processed pixels, keyed by a SHA-256 of the source pixels, their size and the settings key. The
/// source pixels are hashed rather than trusting a file name or id, so a map that changed gets reprocessed and one that
/// did not never is. Entries are deflate-compressed (flat-colour schematics shrink to a few hundred KB) and written via
/// a temp file so a crash cannot leave a half-written entry. Every problem degrades to "not cached".
/// </summary>
internal static class TextureCache
{
    private static readonly byte[] Magic = { (byte)'T', (byte)'X', (byte)'C', (byte)'1' };
    private const int HeaderSize = 12; // magic, width, height

    public static string PathFor(TextureCacheOptions options, byte[] rgba, int width, int height)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(options.SettingsKey));
        hash.AppendData(BitConverter.GetBytes(width));
        hash.AppendData(BitConverter.GetBytes(height));
        hash.AppendData(rgba);
        return Path.Combine(options.Directory, Convert.ToHexString(hash.GetHashAndReset()) + ".tex");
    }

    /// <summary>The cached pixels, or null if there is no usable entry.</summary>
    public static byte[]? TryLoad(string path, int width, int height, Action<string> warn)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var file = File.OpenRead(path);
            var header = new byte[HeaderSize];
            if (!ReadFully(file, header) || !header.AsSpan(0, 4).SequenceEqual(Magic)
                || BitConverter.ToInt32(header, 4) != width || BitConverter.ToInt32(header, 8) != height)
            {
                TryDelete(path);
                return null;
            }
            using var deflate = new DeflateStream(file, CompressionMode.Decompress);
            var pixels = new byte[width * height * 4];
            if (!ReadFully(deflate, pixels))
            {
                TryDelete(path);
                return null;
            }
            file.Dispose();
            try { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); } catch { /* recency only */ }
            return pixels;
        }
        catch (Exception exception)
        {
            warn($"cache entry unusable, reprocessing: {exception.Message}");
            TryDelete(path);
            return null;
        }
    }

    public static void Save(string path, byte[] pixels, int width, int height, long maxBytes, Action<string> warn)
    {
        string temp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var file = File.Create(temp))
            {
                file.Write(Magic);
                file.Write(BitConverter.GetBytes(width));
                file.Write(BitConverter.GetBytes(height));
                using var deflate = new DeflateStream(file, CompressionLevel.Fastest);
                deflate.Write(pixels);
            }
            File.Move(temp, path, overwrite: true);
            Trim(Path.GetDirectoryName(path)!, maxBytes);
        }
        catch (Exception exception)
        {
            warn($"could not write the cache entry: {exception.Message}");
            TryDelete(temp);
        }
    }

    /// <summary>Deletes least-recently-used entries until the folder is under <paramref name="maxBytes"/>.</summary>
    private static void Trim(string directory, long maxBytes)
    {
        var files = new DirectoryInfo(directory).GetFiles("*.tex").OrderBy(file => file.LastWriteTimeUtc).ToList();
        long total = files.Sum(file => file.Length);
        foreach (FileInfo file in files)
        {
            if (total <= maxBytes) break;
            try { file.Delete(); total -= file.Length; }
            catch { /* in use; try the next one */ }
        }
    }

    private static bool ReadFully(Stream stream, byte[] buffer)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int count = stream.Read(buffer, read, buffer.Length - read);
            if (count == 0) return false;
            read += count;
        }
        return true;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best effort */ }
    }
}

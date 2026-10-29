using System.Globalization;
using System.Text;

namespace CarXModManager.Logic;

/// <summary>
/// The matching every search box uses: case- and accent-insensitive, and a text matches when it contains <b>every</b>
/// word of the query (in any order). "ring sanc" finds "Sanctuary Ring", "cafe" finds "Café".
/// Pure code with no game or Unity dependency, so it is covered by the test project.
/// </summary>
internal static class TextSearch
{
    /// <summary>Splits a query into normalised words. An empty or blank query gives an empty array (which matches everything).</summary>
    public static string[] SplitQuery(string? query) =>
        string.IsNullOrWhiteSpace(query)
            ? Array.Empty<string>()
            : Normalize(query).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Lower-cases the text and removes accents (so "É" becomes "e"). Falls back to a plain lower-case if the runtime cannot normalise.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        string lower = text.ToLowerInvariant();
        try
        {
            string decomposed = lower.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);
            foreach (char c in decomposed)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) builder.Append(c);
            return builder.ToString();
        }
        catch (Exception)
        {
            return lower;
        }
    }

    /// <summary>True if <paramref name="normalizedText"/> contains every word in <paramref name="words"/> (see <see cref="SplitQuery"/>).</summary>
    public static bool ContainsAll(string normalizedText, string[] words)
    {
        foreach (string word in words)
            if (!normalizedText.Contains(word, StringComparison.Ordinal)) return false;
        return true;
    }
}

namespace CarXModManager.Logic;

/// <summary>
/// Splits a list into pages of a fixed size, for the map list (the game's UI is built from fixed buttons, so the list is shown
/// a page at a time instead of in a scroll view). Pages are numbered from 0.
/// </summary>
internal static class Pager
{
    /// <summary>How many pages <paramref name="total"/> items need (at least 1, so an empty list still has a first page).</summary>
    public static int PageCount(int total, int pageSize) => Math.Max(1, (total + pageSize - 1) / pageSize);

    /// <summary>The page number limited to what exists.</summary>
    public static int Clamp(int page, int total, int pageSize) => Math.Clamp(page, 0, PageCount(total, pageSize) - 1);

    /// <summary>The index of the first item of a page.</summary>
    public static int FirstIndex(int page, int pageSize) => page * pageSize;

    /// <summary>How many items the page holds (the last page may be short).</summary>
    public static int ItemsOnPage(int page, int total, int pageSize) => Math.Clamp(total - FirstIndex(page, pageSize), 0, pageSize);

    /// <summary>The page that holds the item at <paramref name="index"/>.</summary>
    public static int PageOf(int index, int pageSize) => Math.Max(0, index) / pageSize;
}

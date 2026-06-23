using System.Text;
using System.Text.Json;
using OpenAdoration.Plugins.Abstractions;

namespace OpenAdoration.Plugin.ApiBible;

/// <summary>A book as API.Bible returns it (USFM code + display names).</summary>
public sealed record ApiBook(string Code, string Name, string Abbreviation);

/// <summary>A chapter ref carrying its owning book code (verseIds are book-prefixed).</summary>
public sealed record ApiChapterRef(string Id, string BookCode, string Number);

/// <summary>A book together with its (intro-filtered) chapters, from <c>books?include-chapters=true</c>.</summary>
public sealed record ApiBookWithChapters(ApiBook Book, IReadOnlyList<ApiChapterRef> Chapters);

/// <summary>
/// One passage response parsed: its verses plus the API.Bible truncation signals. A
/// <see cref="VerseCount"/> of 200 means the passage was truncated and the walk must continue
/// from <see cref="LastVerseId"/>.
/// </summary>
public sealed record PassageResult(IReadOnlyList<PluginBibleVerse> Verses, int VerseCount, string? LastVerseId);

/// <summary>
/// Pure JSON → DTO parsing for the API.Bible responses. Network-free so it can be unit-tested
/// against canned payloads; <see cref="ApiBiblePlugin"/> does the HTTP and calls these.
/// </summary>
public static class ApiBibleParser
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<PluginBibleVersionInfo> ParseVersions(string json) =>
        Deser<BiblesResponse>(json)?.Data?.Select(ToVersionInfo).ToList() ?? [];

    public static PluginBibleVersionInfo ParseVersion(string json, string fallbackId)
    {
        var d = Deser<BibleResponse>(json)?.Data;
        return d is null ? new PluginBibleVersionInfo(fallbackId, fallbackId, fallbackId, "") : ToVersionInfo(d);
    }

    /// <summary>
    /// Books with their chapters from <c>books?include-chapters=true</c> — one request replaces the
    /// per-book chapter listing. The "intro" pseudo-chapter is filtered out.
    /// </summary>
    public static IReadOnlyList<ApiBookWithChapters> ParseBooksWithChapters(string json) =>
        Deser<BooksResponse>(json)?.Data?.Select(b => new ApiBookWithChapters(
            new ApiBook(b.Id, b.Name ?? b.Id, b.Abbreviation ?? b.Id),
            (b.Chapters ?? [])
                .Where(c => !string.Equals(c.Number, "intro", StringComparison.OrdinalIgnoreCase))
                .Select(c => new ApiChapterRef(c.Id, b.Id, c.Number ?? ""))
                .ToList())).ToList() ?? [];

    /// <summary>
    /// Extracts verses from a content-type=json <b>chapter</b> response. Every text node carries an
    /// explicit <c>attrs.verseId</c> (e.g. "GEN.1.1"); the book name is supplied by the caller.
    /// </summary>
    public static IReadOnlyList<PluginBibleVerse> ParseChapterVerses(string chapterJson, string bookName)
    {
        using var doc = JsonDocument.Parse(chapterJson);
        if (!doc.RootElement.TryGetProperty("data", out var data))
            return [];

        var (order, acc) = CollectContent(data);
        var verses = new List<PluginBibleVerse>();
        foreach (var id in order)
        {
            if (!TrySplitVerseId(id, out _, out var chapter, out var verse)) continue;
            var text = acc[id].ToString().Trim();
            if (text.Length > 0) verses.Add(new PluginBibleVerse(bookName, chapter, verse, text));
        }
        return verses;
    }

    /// <summary>
    /// Extracts verses from a content-type=json <b>passage</b> response. A passage can span books, so
    /// the book name is resolved per verse from the verseId's book code via <paramref name="nameByCode"/>.
    /// Also surfaces the passage's <c>verseCount</c> (truncation flag) and last verseId (walk cursor).
    /// </summary>
    public static PassageResult ParsePassageVerses(string passageJson, IReadOnlyDictionary<string, string> nameByCode)
    {
        using var doc = JsonDocument.Parse(passageJson);
        if (!doc.RootElement.TryGetProperty("data", out var data))
            return new PassageResult([], 0, null);

        var verseCount = data.TryGetProperty("verseCount", out var vc) && vc.ValueKind == JsonValueKind.Number
            ? vc.GetInt32() : 0;

        var (order, acc) = CollectContent(data);
        var verses = new List<PluginBibleVerse>();
        string? lastId = null;
        foreach (var id in order)
        {
            if (!TrySplitVerseId(id, out var code, out var chapter, out var verse)) continue;
            lastId = id; // advance the cursor even if the verse text is empty
            var name = nameByCode.TryGetValue(code, out var n) ? n : code;
            var text = acc[id].ToString().Trim();
            if (text.Length > 0) verses.Add(new PluginBibleVerse(name, chapter, verse, text));
        }
        return new PassageResult(verses, verseCount, lastId);
    }

    /// <summary>verseId "1CO.16.1" → ("1CO", 16, 1). Book codes never contain '.'.</summary>
    private static bool TrySplitVerseId(string id, out string bookCode, out int chapter, out int verse)
    {
        bookCode = ""; chapter = 0; verse = 0;
        var parts = id.Split('.');
        if (parts.Length < 3 || !int.TryParse(parts[^2], out chapter) || !int.TryParse(parts[^1], out verse))
            return false;
        bookCode = parts[0];
        return true;
    }

    /// <summary>
    /// Walks a content node tree (from a chapter or passage <c>data.content</c>), accumulating text per
    /// verseId in document order. API.Bible types <c>content</c> as a string but returns a native node
    /// array for content-type=json; handle both.
    /// </summary>
    private static (List<string> Order, Dictionary<string, StringBuilder> Acc) CollectContent(JsonElement data)
    {
        var acc = new Dictionary<string, StringBuilder>();
        var order = new List<string>();
        if (!data.TryGetProperty("content", out var content))
            return (order, acc);

        JsonDocument? inner = null;
        try
        {
            if (content.ValueKind == JsonValueKind.String)
            {
                inner = JsonDocument.Parse(content.GetString() ?? "[]");
                content = inner.RootElement;
            }
            Collect(content, acc, order);
        }
        finally { inner?.Dispose(); }
        return (order, acc);
    }

    private static void Collect(JsonElement node, Dictionary<string, StringBuilder> acc, List<string> order)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var el in node.EnumerateArray()) Collect(el, acc, order);
                break;
            case JsonValueKind.Object:
                if (node.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String &&
                    node.TryGetProperty("attrs", out var a) && a.ValueKind == JsonValueKind.Object &&
                    a.TryGetProperty("verseId", out var vid) && vid.ValueKind == JsonValueKind.String)
                {
                    var id = vid.GetString()!;
                    if (!acc.TryGetValue(id, out var sb)) { sb = new StringBuilder(); acc[id] = sb; order.Add(id); }
                    sb.Append(t.GetString());
                }
                if (node.TryGetProperty("items", out var items)) Collect(items, acc, order);
                break;
        }
    }

    private static PluginBibleVersionInfo ToVersionInfo(BibleDto b) =>
        new(b.Id, b.Name ?? b.Id, b.Abbreviation ?? b.AbbreviationLocal ?? b.Id, b.Language?.Name ?? "");

    private static T? Deser<T>(string json) => JsonSerializer.Deserialize<T>(json, Opts);

    private sealed record BiblesResponse(List<BibleDto>? Data);
    private sealed record BibleResponse(BibleDto? Data);
    private sealed record BibleDto(string Id, string? Name, string? Abbreviation, string? AbbreviationLocal, LanguageDto? Language);
    private sealed record LanguageDto(string? Id, string? Name);
    private sealed record BooksResponse(List<BookDto>? Data);
    private sealed record BookDto(string Id, string? Name, string? Abbreviation, string? NameLong, List<ChapterSummaryDto>? Chapters);
    private sealed record ChapterSummaryDto(string Id, string? Number, string? BookId);
}

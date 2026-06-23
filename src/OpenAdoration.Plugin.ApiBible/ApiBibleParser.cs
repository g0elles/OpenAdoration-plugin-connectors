using System.Text;
using System.Text.Json;
using OpenAdoration.Plugins.Abstractions;

namespace OpenAdoration.Plugin.ApiBible;

/// <summary>A book as API.Bible returns it (USFM code + display names).</summary>
public sealed record ApiBook(string Code, string Name, string Abbreviation);

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

    public static IReadOnlyList<ApiBook> ParseBooks(string json) =>
        Deser<BooksResponse>(json)?.Data?
            .Select(b => new ApiBook(b.Id, b.Name ?? b.Id, b.Abbreviation ?? b.Id)).ToList() ?? [];

    /// <summary>Chapter (id, number) refs; the caller filters the "intro" pseudo-chapter.</summary>
    public static IReadOnlyList<(string Id, string Number)> ParseChapterRefs(string json) =>
        Deser<ChaptersResponse>(json)?.Data?
            .Select(c => (c.Id, c.Number ?? "")).ToList() ?? [];

    /// <summary>
    /// Extracts verses from a content-type=json chapter response. Every text node carries an
    /// explicit <c>attrs.verseId</c> (e.g. "GEN.1.1"), so we accumulate text per verseId in order
    /// — no fragile in-text verse-number delimiter parsing.
    /// </summary>
    public static IReadOnlyList<PluginBibleVerse> ParseChapterVerses(string chapterJson, string bookName)
    {
        using var doc = JsonDocument.Parse(chapterJson);
        if (!doc.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("content", out var content))
            return [];

        // API.Bible types `content` as a string but returns a native node array for content-type=json.
        // Handle both: if it arrived JSON-encoded as a string, parse it again.
        JsonDocument? inner = null;
        try
        {
            if (content.ValueKind == JsonValueKind.String)
            {
                inner = JsonDocument.Parse(content.GetString() ?? "[]");
                content = inner.RootElement;
            }

            var acc = new Dictionary<string, StringBuilder>();
            var order = new List<string>();
            Collect(content, acc, order);

            var verses = new List<PluginBibleVerse>();
            foreach (var id in order)
            {
                var parts = id.Split('.');
                if (parts.Length < 3 || !int.TryParse(parts[^2], out var chapter) || !int.TryParse(parts[^1], out var verse))
                    continue;
                var text = acc[id].ToString().Trim();
                if (text.Length > 0)
                    verses.Add(new PluginBibleVerse(bookName, chapter, verse, text));
            }
            return verses;
        }
        finally { inner?.Dispose(); }
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
    private sealed record BookDto(string Id, string? Name, string? Abbreviation, string? NameLong);
    private sealed record ChaptersResponse(List<ChapterDto>? Data);
    private sealed record ChapterDto(string Id, string? Number);
}

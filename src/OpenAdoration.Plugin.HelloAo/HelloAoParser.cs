using System.Text;
using System.Text.Json;
using OpenAdoration.Plugins.Abstractions;

namespace OpenAdoration.Plugin.HelloAo;

/// <summary>
/// Pure JSON → DTO parsing for the helloao.org Free Use Bible API. Network-free so it can be
/// unit-tested against canned payloads; <see cref="HelloAoPlugin"/> does the HTTP.
/// </summary>
public static class HelloAoParser
{
    public static IReadOnlyList<PluginBibleVersionInfo> ParseTranslations(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<PluginBibleVersionInfo>();
        if (doc.RootElement.TryGetProperty("translations", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var t in arr.EnumerateArray())
            {
                var id = Str(t, "id");
                if (id is null) continue;
                list.Add(new PluginBibleVersionInfo(
                    id, Str(t, "englishName") ?? Str(t, "name") ?? id, id, Str(t, "language") ?? ""));
            }
        return list;
    }

    /// <summary>Parses a whole-translation <c>complete.json</c> into books + verses.</summary>
    public static PluginBibleData ParseComplete(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tr = root.GetProperty("translation");
        var trId = Str(tr, "id") ?? "";
        var version = new PluginBibleVersionInfo(
            trId, Str(tr, "englishName") ?? Str(tr, "name") ?? trId, trId, Str(tr, "language") ?? "");

        var books = new List<PluginBibleBook>();
        var verses = new List<PluginBibleVerse>();
        if (!root.TryGetProperty("books", out var bookArr)) return new PluginBibleData(version, books, verses);

        foreach (var b in bookArr.EnumerateArray())
        {
            var name = Str(b, "commonName") ?? Str(b, "name") ?? Str(b, "id") ?? "?";
            var order = Int(b, "order");
            // Protestant canon order: 1–39 Old Testament, 40–66 New.
            books.Add(new PluginBibleBook(
                name, Str(b, "id") ?? name, order,
                order is > 0 and <= 39 ? PluginTestament.Old : PluginTestament.New,
                Int(b, "numberOfChapters")));

            if (!b.TryGetProperty("chapters", out var chapters)) continue;
            foreach (var entry in chapters.EnumerateArray())
            {
                if (!entry.TryGetProperty("chapter", out var ch) || !ch.TryGetProperty("content", out var content))
                    continue;
                var chapterNumber = Int(ch, "number");
                foreach (var item in content.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    if (!item.TryGetProperty("type", out var type) || type.GetString() != "verse") continue;

                    var text = ExtractVerseText(item);
                    if (text.Length > 0)
                        verses.Add(new PluginBibleVerse(name, chapterNumber, Int(item, "number"), text));
                }
            }
        }
        return new PluginBibleData(version, books, verses);
    }

    // A verse's "content" is an array of plain strings interleaved with objects (footnote refs,
    // line breaks, words-of-Jesus / poem spans). Keep the text parts, drop the markup.
    private static string ExtractVerseText(JsonElement verse)
    {
        if (!verse.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            return "";
        var sb = new StringBuilder();
        foreach (var part in content.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.String)
                sb.Append(part.GetString());
            else if (part.ValueKind == JsonValueKind.Object &&
                     part.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                sb.Append(t.GetString());
        }
        return sb.ToString().Trim();
    }

    private static string? Str(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int Int(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.TryGetInt32(out var n) ? n : 0;
}

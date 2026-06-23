using OpenAdoration.Plugin.ApiBible;
using Xunit;

namespace OpenAdoration.Plugin.ApiBible.Tests;

public class ApiBibleParserTests
{
    [Fact]
    public void ParseVersions_maps_id_name_abbreviation_language()
    {
        const string json = """
        {"data":[{"id":"de4e-02","name":"King James Version","abbreviation":"engKJV",
                  "language":{"id":"eng","name":"English"}}]}
        """;

        var versions = ApiBibleParser.ParseVersions(json);

        var v = Assert.Single(versions);
        Assert.Equal("de4e-02", v.Id);
        Assert.Equal("King James Version", v.Name);
        Assert.Equal("engKJV", v.Abbreviation);
        Assert.Equal("English", v.Language);
    }

    [Fact]
    public void ParseChapterVerses_groups_text_by_verseId_in_order()
    {
        // content-type=json: each text node carries attrs.verseId; verse 1's text is split across
        // two nodes to prove concatenation; book name comes from the caller (the DB join key).
        const string json = """
        {"data":{"id":"GEN.1","content":[
          {"type":"tag","name":"para","attrs":{"style":"p"},"items":[
            {"type":"tag","name":"verse","attrs":{"number":"1","sid":"GEN 1:1"},"items":[]},
            {"type":"text","text":"In the beginning God created ","attrs":{"verseId":"GEN.1.1"}},
            {"type":"text","text":"the heavens and the earth.","attrs":{"verseId":"GEN.1.1"}},
            {"type":"tag","name":"verse","attrs":{"number":"2","sid":"GEN 1:2"},"items":[]},
            {"type":"text","text":"Now the earth was formless and empty.","attrs":{"verseId":"GEN.1.2"}}
          ]}
        ]},"meta":{"fumsToken":"abc123"}}
        """;

        var verses = ApiBibleParser.ParseChapterVerses(json, "Genesis");

        Assert.Equal(2, verses.Count);
        Assert.Equal("Genesis", verses[0].Book);
        Assert.Equal(1, verses[0].Chapter);
        Assert.Equal(1, verses[0].Verse);
        Assert.Equal("In the beginning God created the heavens and the earth.", verses[0].Text);
        Assert.Equal(2, verses[1].Verse);
        Assert.Equal("Now the earth was formless and empty.", verses[1].Text);
    }

    [Fact]
    public void ParseChapterVerses_returns_empty_when_no_content()
    {
        Assert.Empty(ApiBibleParser.ParseChapterVerses("""{"data":{"id":"GEN.1"}}""", "Genesis"));
    }

    [Fact]
    public void ParseBooksWithChapters_maps_books_and_filters_intro()
    {
        const string json = """
        {"data":[
          {"id":"GEN","name":"Genesis","abbreviation":"GEN","chapters":[
            {"id":"GEN.intro","number":"intro","bookId":"GEN"},
            {"id":"GEN.1","number":"1","bookId":"GEN"},
            {"id":"GEN.2","number":"2","bookId":"GEN"}]},
          {"id":"MAT","name":"Matthew","abbreviation":"MAT","chapters":[
            {"id":"MAT.1","number":"1","bookId":"MAT"}]}
        ]}
        """;

        var books = ApiBibleParser.ParseBooksWithChapters(json);

        Assert.Equal(2, books.Count);
        Assert.Equal("Genesis", books[0].Book.Name);
        Assert.Equal(2, books[0].Chapters.Count);              // intro filtered out
        Assert.Equal("GEN.1", books[0].Chapters[0].Id);
        Assert.Equal("GEN", books[0].Chapters[0].BookCode);
        Assert.Single(books[1].Chapters);
    }

    [Fact]
    public void ParsePassageVerses_resolves_book_per_verseId_and_reports_truncation()
    {
        // A passage spanning two books; verseCount==200 flags truncation; book name comes per verse
        // from the verseId's code via the map.
        const string json = """
        {"data":{"verseCount":200,"content":[
          {"type":"text","text":"In the beginning.","attrs":{"verseId":"GEN.1.1"}},
          {"type":"text","text":"The book of the genealogy.","attrs":{"verseId":"MAT.1.1"}}
        ]},"meta":{"fumsToken":"x"}}
        """;
        var map = new Dictionary<string, string> { ["GEN"] = "Genesis", ["MAT"] = "Matthew" };

        var res = ApiBibleParser.ParsePassageVerses(json, map);

        Assert.Equal(200, res.VerseCount);
        Assert.Equal("MAT.1.1", res.LastVerseId);              // cursor for the next window
        Assert.Equal(2, res.Verses.Count);
        Assert.Equal("Genesis", res.Verses[0].Book);
        Assert.Equal("Matthew", res.Verses[1].Book);
        Assert.Equal(1, res.Verses[1].Chapter);
        Assert.Equal(1, res.Verses[1].Verse);
    }

    [Fact]
    public async Task WalkAsync_paginates_dedups_and_fetches_final_chapter()
    {
        var chapters = new[]
        {
            new ApiChapterRef("GEN.1", "GEN", "1"),
            new ApiChapterRef("GEN.2", "GEN", "2"),
            new ApiChapterRef("REV.22", "REV", "22"),
        };
        var map = new Dictionary<string, string> { ["GEN"] = "Genesis", ["REV"] = "Revelation" };

        var passageCalls = new List<string>();
        var chapterCalls = new List<string>();

        Task<string> GetPassage(string passageId, CancellationToken _)
        {
            passageCalls.Add(passageId);
            // Window 1 is truncated (200) ending at GEN.1.2; window 2 returns the remainder (<200),
            // re-including GEN.1.2 to prove dedup.
            return Task.FromResult(passageCalls.Count == 1
                ? Passage(200, ("GEN.1.1", "a"), ("GEN.1.2", "b"))
                : Passage(2, ("GEN.1.2", "b"), ("GEN.2.1", "c")));
        }
        Task<string> GetChapter(string chapterId, CancellationToken _)
        {
            chapterCalls.Add(chapterId);
            return Task.FromResult(Chapter(("REV.22.1", "y"), ("REV.22.2", "z")));
        }

        var verses = await ApiBiblePlugin.WalkAsync(
            chapters, map, GetPassage, GetChapter, null, null, CancellationToken.None);

        Assert.Equal(new[] { "GEN.1.1-REV.22.1", "GEN.1.2-REV.22.1" }, passageCalls); // resumed at cursor
        Assert.Equal(new[] { "REV.22" }, chapterCalls);                               // final chapter once
        // GEN.1.1, GEN.1.2 (deduped), GEN.2.1, REV.22.1, REV.22.2
        Assert.Equal(5, verses.Count);
        Assert.Single(verses, v => v is { Book: "Genesis", Chapter: 1, Verse: 2 });
        Assert.Equal("Revelation", verses[^1].Book);
    }

    private static string Passage(int verseCount, params (string Id, string Text)[] nodes) =>
        "{\"data\":{\"verseCount\":" + verseCount + ",\"content\":[" + Nodes(nodes) + "]},\"meta\":{\"fumsToken\":\"x\"}}";

    private static string Chapter(params (string Id, string Text)[] nodes) =>
        "{\"data\":{\"content\":[" + Nodes(nodes) + "]}}";

    private static string Nodes((string Id, string Text)[] nodes) =>
        string.Join(",", nodes.Select(n =>
            "{\"type\":\"text\",\"text\":\"" + n.Text + "\",\"attrs\":{\"verseId\":\"" + n.Id + "\"}}"));
}

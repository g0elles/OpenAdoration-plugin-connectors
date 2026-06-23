using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAdoration.Plugins.Abstractions;
using Xunit;

namespace OpenAdoration.Plugin.ApiBible.Tests;

/// <summary>
/// Live checks against the real API.Bible service — they validate that the documented endpoints
/// and JSON shapes still match the parser. Gated on the APIBIBLE_KEY env var so they are a no-op
/// in CI / for anyone without a key (the key never enters source or the transcript). Override the
/// host with APIBIBLE_BASE (default https://rest.api.bible/v1/).
///
///   PowerShell:  $env:APIBIBLE_KEY="..."; dotnet test
/// </summary>
public class ApiBibleLiveTests
{
    private static string? Key => Environment.GetEnvironmentVariable("APIBIBLE_KEY");
    private static string Base => Environment.GetEnvironmentVariable("APIBIBLE_BASE") ?? "https://rest.api.bible/v1/";

    private static HttpClient NewHttp()
    {
        var b = Base.EndsWith('/') ? Base : Base + "/";
        var http = new HttpClient { BaseAddress = new Uri(b) };
        http.DefaultRequestHeaders.Add("api-key", Key);
        return http;
    }

    [Fact]
    public async Task Plugin_lists_versions_live()
    {
        if (string.IsNullOrWhiteSpace(Key)) return; // set APIBIBLE_KEY to run

        var plugin = new ApiBiblePlugin();
        plugin.Initialize(new TestHost(Key!, Base));

        var versions = await plugin.GetAvailableVersionsAsync();

        Assert.NotEmpty(versions);
        Assert.All(versions, v => Assert.False(string.IsNullOrWhiteSpace(v.Id)));
    }

    [Fact]
    public async Task Parser_matches_real_books_and_passage_shape_live()
    {
        if (string.IsNullOrWhiteSpace(Key)) return; // set APIBIBLE_KEY to run

        using var http = NewHttp();

        var versions = ApiBibleParser.ParseVersions(await http.GetStringAsync("bibles"));
        Assert.NotEmpty(versions);
        var versionId = versions[0].Id;

        // books?include-chapters=true must populate chapters inline (the per-book listing is gone).
        var books = ApiBibleParser.ParseBooksWithChapters(
            await http.GetStringAsync($"bibles/{versionId}/books?include-chapters=true"));
        Assert.NotEmpty(books);
        Assert.All(books, b => Assert.NotEmpty(b.Chapters));

        var chapters = books.SelectMany(b => b.Chapters).ToList();
        var nameByCode = books.ToDictionary(b => b.Book.Code, b => b.Book.Name);

        // One passage spanning the whole book range — confirms the real passage JSON shape (verseIds,
        // verseCount) the walk relies on, including the 200-verse truncation signal.
        var passageJson = await http.GetStringAsync(
            $"bibles/{versionId}/passages/{chapters[0].Id}.1-{chapters[^1].Id}.1" +
            "?content-type=json&include-notes=false&include-titles=false" +
            "&include-chapter-numbers=false&include-verse-numbers=false&include-verse-spans=false");
        var res = ApiBibleParser.ParsePassageVerses(passageJson, nameByCode);

        Assert.NotEmpty(res.Verses);
        Assert.All(res.Verses, v => Assert.False(string.IsNullOrWhiteSpace(v.Text)));
        Assert.True(res.VerseCount > 0);
        Assert.NotNull(res.LastVerseId);
    }

    private sealed class TestHost(string apiKey, string baseUrl) : IPluginHost
    {
        public IReadOnlyDictionary<string, string> Settings { get; } =
            new Dictionary<string, string> { ["apiKey"] = apiKey, ["baseUrl"] = baseUrl };
        public ILogger Logger { get; } = NullLogger.Instance;
    }
}

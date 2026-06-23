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
    public async Task Parser_matches_real_chapter_shape_live()
    {
        if (string.IsNullOrWhiteSpace(Key)) return; // set APIBIBLE_KEY to run

        using var http = NewHttp();

        var versions = ApiBibleParser.ParseVersions(await http.GetStringAsync("bibles"));
        Assert.NotEmpty(versions);
        var versionId = versions[0].Id;

        var books = ApiBibleParser.ParseBooks(await http.GetStringAsync($"bibles/{versionId}/books"));
        Assert.NotEmpty(books);

        var chapters = ApiBibleParser
            .ParseChapterRefs(await http.GetStringAsync($"bibles/{versionId}/books/{books[0].Code}/chapters"))
            .Where(c => !string.Equals(c.Number, "intro", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.NotEmpty(chapters);

        var chapterJson = await http.GetStringAsync(
            $"bibles/{versionId}/chapters/{chapters[0].Id}?content-type=json&include-notes=false&include-titles=false");
        var verses = ApiBibleParser.ParseChapterVerses(chapterJson, books[0].Name);

        Assert.NotEmpty(verses); // the parser understood the real chapter JSON
        Assert.All(verses, v => Assert.False(string.IsNullOrWhiteSpace(v.Text)));
    }

    private sealed class TestHost(string apiKey, string baseUrl) : IPluginHost
    {
        public IReadOnlyDictionary<string, string> Settings { get; } =
            new Dictionary<string, string> { ["apiKey"] = apiKey, ["baseUrl"] = baseUrl };
        public ILogger Logger { get; } = NullLogger.Instance;
    }
}

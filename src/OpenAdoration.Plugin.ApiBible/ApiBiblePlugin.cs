using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenAdoration.Plugins.Abstractions;

namespace OpenAdoration.Plugin.ApiBible;

/// <summary>
/// Bring-your-own-key Bible source backed by API.Bible (scripture.api.bible). The church
/// supplies its own key (Settings → Plugins → "API.Bible key"); this plugin fetches versions
/// that key can access and feeds them into OpenAdoration's Bible library. No key or copyrighted
/// text ships in OpenAdoration core — that's why this lives in its own repo.
/// </summary>
public sealed class ApiBiblePlugin : IBibleSourcePlugin
{
    // API.Bible's REST host. Configurable via the "baseUrl" setting (must include the /v1/ path);
    // the church's account may be issued a different host.
    private const string DefaultBaseAddress = "https://rest.api.bible/v1/";

    // 27 USFM codes for the New Testament; everything else is treated as Old Testament.
    private static readonly HashSet<string> NewTestament = new(StringComparer.OrdinalIgnoreCase)
    {
        "MAT","MRK","LUK","JHN","ACT","ROM","1CO","2CO","GAL","EPH","PHP","COL","1TH","2TH",
        "1TI","2TI","TIT","PHM","HEB","JAS","1PE","2PE","1JN","2JN","3JN","JUD","REV"
    };

    private HttpClient? _http;
    private ILogger? _log;

    public string Id => "apibible";
    public string Name => "API.Bible Source";
    public Version Version => new(0, 1, 0);

    public void Initialize(IPluginHost host)
    {
        _log = host.Logger;
        var apiKey = host.Settings.TryGetValue("apiKey", out var k) ? k?.Trim() : null;
        var baseUrl = host.Settings.TryGetValue("baseUrl", out var b) && !string.IsNullOrWhiteSpace(b)
            ? b.Trim() : DefaultBaseAddress;
        if (!baseUrl.EndsWith('/')) baseUrl += "/";

        _http?.Dispose();
        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        if (!string.IsNullOrWhiteSpace(apiKey))
            _http.DefaultRequestHeaders.Add("api-key", apiKey);
        else
            _log?.LogWarning("API.Bible plugin initialized without a key — set one in Settings → Plugins.");
    }

    public async Task<IReadOnlyList<PluginBibleVersionInfo>> GetAvailableVersionsAsync(CancellationToken ct = default) =>
        ApiBibleParser.ParseVersions(await GetAsync("bibles", ct));

    public async Task<PluginBibleData> FetchAsync(string versionId, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var version = ApiBibleParser.ParseVersion(await GetAsync($"bibles/{versionId}", ct), versionId);
        var apiBooks = ApiBibleParser.ParseBooks(await GetAsync($"bibles/{versionId}/books", ct));
        _log?.LogInformation("API.Bible: starting fetch of {Version} ({Books} books)", version.Name, apiBooks.Count);

        var books = new List<PluginBibleBook>();
        var verses = new List<PluginBibleVerse>();
        var number = 0;

        foreach (var b in apiBooks)
        {
            ct.ThrowIfCancellationRequested();
            number++;
            var bookStart = verses.Count;

            var chapters = ApiBibleParser
                .ParseChapterRefs(await GetAsync($"bibles/{versionId}/books/{b.Code}/chapters", ct))
                .Where(c => !string.Equals(c.Number, "intro", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var c in chapters)
            {
                ct.ThrowIfCancellationRequested();
                // Per-chapter fetch (not per-verse) keeps a whole-Bible sync under the free-tier
                // daily request cap. content-type=json gives explicit verseIds to parse.
                var chapterJson = await GetAsync(
                    $"bibles/{versionId}/chapters/{c.Id}?content-type=json" +
                    "&include-notes=false&include-titles=false&include-chapter-numbers=false&include-verse-spans=false",
                    ct);
                verses.AddRange(ApiBibleParser.ParseChapterVerses(chapterJson, b.Name));
                progress?.Report(verses.Count);
                ReportFums(chapterJson);
            }

            books.Add(new PluginBibleBook(
                b.Name, b.Abbreviation, number,
                NewTestament.Contains(b.Code) ? PluginTestament.New : PluginTestament.Old,
                chapters.Count));
            _log?.LogInformation("API.Bible: {Book} — {Chapters} chapters, {Verses} verses (total {Total})",
                b.Name, chapters.Count, verses.Count - bookStart, verses.Count);
        }

        _log?.LogInformation("API.Bible: finished {Version} — {Books} books, {Verses} verses",
            version.Name, books.Count, verses.Count);
        return new PluginBibleData(version, books, verses);
    }

    private async Task<string> GetAsync(string relativeUrl, CancellationToken ct)
    {
        var http = _http ?? throw new InvalidOperationException("API.Bible plugin is not initialized.");
        if (!http.DefaultRequestHeaders.Contains("api-key"))
            throw new InvalidOperationException("Set your API.Bible key in Settings → Plugins first.");

        for (var attempt = 0; ; attempt++)
        {
            using var resp = await http.GetAsync(relativeUrl, ct);
            if (resp.StatusCode == HttpStatusCode.TooManyRequests && attempt < 3)
            {
                var wait = resp.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
                _log?.LogWarning("API.Bible: 429 rate-limited on {Url}; retrying in {Wait}s", relativeUrl, wait.TotalSeconds);
                await Task.Delay(wait, ct);
                continue;
            }
            if (!resp.IsSuccessStatusCode)
                _log?.LogError("API.Bible: {Status} on {Url}", (int)resp.StatusCode, relativeUrl);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync(ct);
        }
    }

    private void ReportFums(string chapterJson)
    {
        // ponytail: best-effort FUMS — capture API.Bible's Fair-Use token so usage can be reported
        // per the church's account terms. Full reporting (the FUMS endpoint contract) is account-
        // specific; logged here so it's never silently dropped. Upgrade path: POST the token once
        // the church confirms its FUMS reporting endpoint.
        try
        {
            using var doc = JsonDocument.Parse(chapterJson);
            if (doc.RootElement.TryGetProperty("meta", out var meta) &&
                meta.TryGetProperty("fumsToken", out var token) && token.ValueKind == JsonValueKind.String)
                _log?.LogDebug("API.Bible FUMS token: {Token}", token.GetString());
        }
        catch { /* non-fatal — telemetry must never break an import */ }
    }
}

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

    // content-type=json gives explicit verseIds to parse; everything else is noise we strip.
    private const string ContentParams =
        "content-type=json&include-notes=false&include-titles=false" +
        "&include-chapter-numbers=false&include-verse-numbers=false&include-verse-spans=false";

    // API.Bible truncates a passage to the first 200 verses (Passage.verseCount == 200 signals it).
    private const int PassageVerseCap = 200;

    // FUMS (Fair Use Management System) reporting host — a SEPARATE host from the scripture API, so
    // these calls do NOT count against the request quota. Configurable via "fumsBaseUrl".
    private const string DefaultFumsBase = "https://fums.api.bible/";

    // Max fumsTokens per FUMS GET (repeated &t= params) — keeps the query string well under URL limits.
    private const int FumsBatchSize = 50;

    // 27 USFM codes for the New Testament; everything else is treated as Old Testament.
    private static readonly HashSet<string> NewTestament = new(StringComparer.OrdinalIgnoreCase)
    {
        "MAT","MRK","LUK","JHN","ACT","ROM","1CO","2CO","GAL","EPH","PHP","COL","1TH","2TH",
        "1TI","2TI","TIT","PHM","HEB","JAS","1PE","2PE","1JN","2JN","3JN","JUD","REV"
    };

    private HttpClient? _http;
    private HttpClient? _fums;
    private ILogger? _log;
    private string _deviceId = "";

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

        var fumsBase = host.Settings.TryGetValue("fumsBaseUrl", out var f) && !string.IsNullOrWhiteSpace(f)
            ? f.Trim() : DefaultFumsBase;
        if (!fumsBase.EndsWith('/')) fumsBase += "/";

        _http?.Dispose();
        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        if (!string.IsNullOrWhiteSpace(apiKey))
            _http.DefaultRequestHeaders.Add("api-key", apiKey);
        else
            _log?.LogWarning("API.Bible plugin initialized without a key — set one in Settings → Plugins.");

        // FUMS gets its own client (no api-key header). Device id is stable per install without any
        // persistence: a deterministic hash of machine+user — FUMS only needs an opaque stable id.
        _fums?.Dispose();
        _fums = new HttpClient { BaseAddress = new Uri(fumsBase) };
        _deviceId = StableDeviceId();
    }

    public async Task<IReadOnlyList<PluginBibleVersionInfo>> GetAvailableVersionsAsync(CancellationToken ct = default) =>
        ApiBibleParser.ParseVersions(await GetAsync("bibles", ct));

    public async Task<PluginBibleData> FetchAsync(string versionId, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var version = ApiBibleParser.ParseVersion(await GetAsync($"bibles/{versionId}", ct), versionId);

        // One request returns every book WITH its chapters — replaces the per-book chapter listing.
        var booksWithChapters = ApiBibleParser.ParseBooksWithChapters(
            await GetAsync($"bibles/{versionId}/books?include-chapters=true", ct));
        var chapters = booksWithChapters.SelectMany(b => b.Chapters).ToList();
        var nameByCode = booksWithChapters.ToDictionary(b => b.Book.Code, b => b.Book.Name);
        _log?.LogInformation("API.Bible: starting fetch of {Version} ({Books} books, {Chapters} chapters)",
            version.Name, booksWithChapters.Count, chapters.Count);

        var books = booksWithChapters.Select((b, i) => new PluginBibleBook(
            b.Book.Name, b.Book.Abbreviation, i + 1,
            NewTestament.Contains(b.Book.Code) ? PluginTestament.New : PluginTestament.Old,
            b.Chapters.Count)).ToList();

        // Collect every response's FUMS token and report them once at the end (per the fair-use docs:
        // report the token from each scripture access; batchable). FUMS hits a separate host, so it
        // doesn't consume the request quota.
        var fumsTokens = new List<string>();
        void Collect(string json) { var t = ExtractFumsToken(json); if (t is not null) fumsTokens.Add(t); }

        List<PluginBibleVerse> verses = chapters.Count == 0 ? [] : await WalkAsync(
            chapters, nameByCode,
            (passageId, c) => GetAsync($"bibles/{versionId}/passages/{passageId}?{ContentParams}", c),
            (chapterId, c) => GetAsync($"bibles/{versionId}/chapters/{chapterId}?{ContentParams}", c),
            Collect, progress, ct);

        await ReportFumsAsync(fumsTokens, ct);
        _log?.LogInformation("API.Bible: finished {Version} — {Books} books, {Verses} verses",
            version.Name, books.Count, verses.Count);
        return new PluginBibleData(version, books, verses);
    }

    /// <summary>
    /// Fetches a whole version's verses in ≤200-verse passages instead of one request per chapter
    /// (~160 requests vs ~1,250). Walks a cursor through the bible: each passage runs from the cursor
    /// to the last chapter's verse 1; the API truncates at 200 verses, so on a full window we resume
    /// just past the last verse returned (a 1-verse overlap, deduped). The final chapter is capped at
    /// verse 1 by the end marker, so it's fetched in full once at the end. HTTP is injected so the
    /// walk is unit-testable offline.
    /// </summary>
    internal static async Task<List<PluginBibleVerse>> WalkAsync(
        IReadOnlyList<ApiChapterRef> chapters,
        IReadOnlyDictionary<string, string> nameByCode,
        Func<string, CancellationToken, Task<string>> getPassage,
        Func<string, CancellationToken, Task<string>> getChapter,
        Action<string>? onFums,
        IProgress<int>? progress,
        CancellationToken ct)
    {
        var verses = new List<PluginBibleVerse>();
        var seen = new HashSet<(string, int, int)>();
        var endMarker = $"{chapters[^1].Id}.1";   // verse 1 always exists — no out-of-range guessing
        var cursor = $"{chapters[0].Id}.1";

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var json = await getPassage($"{cursor}-{endMarker}", ct);
            onFums?.Invoke(json);
            var res = ApiBibleParser.ParsePassageVerses(json, nameByCode);
            AddNew(res.Verses, verses, seen);
            progress?.Report(verses.Count);

            // Stop when the API returned fewer than the cap (reached the end marker) or made no
            // progress; otherwise resume past the last verse this window returned.
            if (res.VerseCount < PassageVerseCap || res.LastVerseId is null || res.LastVerseId == cursor)
                break;
            cursor = res.LastVerseId;
        }

        // The end marker stopped the walk at the last chapter's verse 1; pull that chapter in full.
        ct.ThrowIfCancellationRequested();
        var lastBook = nameByCode.TryGetValue(chapters[^1].BookCode, out var bn) ? bn : chapters[^1].BookCode;
        var lastJson = await getChapter(chapters[^1].Id, ct);
        onFums?.Invoke(lastJson);
        AddNew(ApiBibleParser.ParseChapterVerses(lastJson, lastBook), verses, seen);
        progress?.Report(verses.Count);

        return verses;
    }

    private static void AddNew(IReadOnlyList<PluginBibleVerse> incoming, List<PluginBibleVerse> into, HashSet<(string, int, int)> seen)
    {
        foreach (var v in incoming)
            if (seen.Add((v.Book, v.Chapter, v.Verse))) into.Add(v);
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

    private static string? ExtractFumsToken(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("meta", out var meta) &&
                meta.TryGetProperty("fumsToken", out var token) && token.ValueKind == JsonValueKind.String)
                return token.GetString();
        }
        catch { /* non-fatal — telemetry must never break an import */ }
        return null;
    }

    /// <summary>
    /// Reports the collected FUMS tokens to API.Bible's Fair-Use system (GET fums.api.bible/f3 with a
    /// device id, a per-import session id, and the tokens as repeated &amp;t= params, batched). Required
    /// by the API.Bible terms; best-effort — a reporting failure is logged, never fatal to the import.
    /// </summary>
    private async Task ReportFumsAsync(IReadOnlyList<string> tokens, CancellationToken ct)
    {
        if (_fums is null || tokens.Count == 0) return;
        var sessionId = Guid.NewGuid().ToString("N");
        try
        {
            foreach (var url in BuildFumsUrls(tokens, _deviceId, sessionId, FumsBatchSize))
            {
                using var resp = await _fums.GetAsync(url, ct);
                if (!resp.IsSuccessStatusCode)
                    _log?.LogWarning("API.Bible FUMS: {Status} reporting usage", (int)resp.StatusCode);
            }
            _log?.LogInformation("API.Bible FUMS: reported {Count} access tokens", tokens.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log?.LogWarning(ex, "API.Bible FUMS: usage reporting failed (non-fatal)");
        }
    }

    /// <summary>Builds the relative FUMS report URLs, chunking tokens into batches of repeated t= params.</summary>
    internal static IEnumerable<string> BuildFumsUrls(IReadOnlyList<string> tokens, string deviceId, string sessionId, int batchSize)
    {
        for (var i = 0; i < tokens.Count; i += batchSize)
        {
            var batch = tokens.Skip(i).Take(batchSize)
                .Select(t => "t=" + Uri.EscapeDataString(t));
            yield return $"f3?dId={Uri.EscapeDataString(deviceId)}&sId={Uri.EscapeDataString(sessionId)}&"
                + string.Join("&", batch);
        }
    }

    /// <summary>
    /// A stable, opaque per-install device id for FUMS, derived from machine + user so it survives
    /// restarts without any persistence (IPluginHost is read-only). Not PII — a one-way hash.
    /// </summary>
    private static string StableDeviceId()
    {
        var seed = Environment.MachineName + "|" + Environment.UserName + "|openadoration-apibible";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed));
        return new Guid(hash[..16]).ToString("N");
    }
}

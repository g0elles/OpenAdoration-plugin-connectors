using Microsoft.Extensions.Logging;
using OpenAdoration.Plugins.Abstractions;

namespace OpenAdoration.Plugin.ApiBible;

/// <summary>
/// Bring-your-own-key Bible source backed by API.Bible (scripture.api.bible). The church
/// supplies its own key (Settings → Plugins → "API.Bible key"); this plugin fetches versions
/// that key can access and feeds them into OpenAdoration's Bible library. No key or copyrighted
/// text ships in OpenAdoration core — that's the whole point of this living in its own repo.
/// </summary>
public sealed class ApiBiblePlugin : IBibleSourcePlugin
{
    private const string BaseAddress = "https://api.scripture.api.bible/v1/";

    private HttpClient? _http;
    private ILogger? _log;

    public string Id => "apibible";
    public string Name => "API.Bible Source";
    public Version Version => new(0, 1, 0);

    public void Initialize(IPluginHost host)
    {
        _log = host.Logger;
        var apiKey = host.Settings.TryGetValue("apiKey", out var k) ? k : string.Empty;

        _http?.Dispose();
        _http = new HttpClient { BaseAddress = new Uri(BaseAddress) };
        if (!string.IsNullOrWhiteSpace(apiKey))
            _http.DefaultRequestHeaders.Add("api-key", apiKey.Trim());
        else
            _log?.LogWarning("API.Bible plugin initialized without a key — set one in Settings → Plugins.");
    }

    public Task<IReadOnlyList<PluginBibleVersionInfo>> GetAvailableVersionsAsync(CancellationToken ct = default)
        // ponytail: Phase C — GET /v1/bibles, map data[] → PluginBibleVersionInfo(id,name,abbreviation,language).
        => throw new NotImplementedException("API.Bible version listing lands in Phase C.");

    public Task<PluginBibleData> FetchAsync(string versionId, IProgress<int>? progress = null, CancellationToken ct = default)
        // ponytail: Phase C — /bibles/{id}/books → /chapters → per-chapter text fetch+parse to verses,
        // progress.Report per chapter, honor ct, forward FUMS. Chapter granularity = rate-limit-safe.
        => throw new NotImplementedException("API.Bible fetch lands in Phase C.");
}

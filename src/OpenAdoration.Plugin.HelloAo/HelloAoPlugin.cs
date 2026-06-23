using Microsoft.Extensions.Logging;
using OpenAdoration.Plugins.Abstractions;

namespace OpenAdoration.Plugin.HelloAo;

/// <summary>
/// Free public-domain / openly-licensed Bibles from the helloao.org Free Use Bible API.
/// No key, no rate limit, no copyright restriction — a whole translation downloads in a single
/// <c>complete.json</c> request. The church just picks a version; nothing to configure.
/// </summary>
public sealed class HelloAoPlugin : IBibleSourcePlugin
{
    private const string DefaultBaseAddress = "https://bible.helloao.org/";

    private HttpClient? _http;
    private ILogger? _log;

    public string Id => "helloao";
    public string Name => "Free Use Bible (helloao.org)";
    public Version Version => new(0, 1, 0);

    public void Initialize(IPluginHost host)
    {
        _log = host.Logger;
        var baseUrl = host.Settings.TryGetValue("baseUrl", out var b) && !string.IsNullOrWhiteSpace(b)
            ? b.Trim() : DefaultBaseAddress;
        if (!baseUrl.EndsWith('/')) baseUrl += "/";

        _http?.Dispose();
        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
    }

    public async Task<IReadOnlyList<PluginBibleVersionInfo>> GetAvailableVersionsAsync(CancellationToken ct = default) =>
        HelloAoParser.ParseTranslations(await GetAsync("api/available_translations.json", ct));

    public async Task<PluginBibleData> FetchAsync(string versionId, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        _log?.LogInformation("helloao: downloading {Version} (complete.json)", versionId);
        var data = HelloAoParser.ParseComplete(await GetAsync($"api/{versionId}/complete.json", ct));
        progress?.Report(data.Verses.Count);
        _log?.LogInformation("helloao: parsed {Version} — {Books} books, {Verses} verses",
            versionId, data.Books.Count, data.Verses.Count);
        return data;
    }

    private async Task<string> GetAsync(string relativeUrl, CancellationToken ct)
    {
        var http = _http ?? throw new InvalidOperationException("helloao plugin is not initialized.");
        using var resp = await http.GetAsync(relativeUrl, ct);
        if (!resp.IsSuccessStatusCode)
            _log?.LogError("helloao: {Status} on {Url}", (int)resp.StatusCode, relativeUrl);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }
}

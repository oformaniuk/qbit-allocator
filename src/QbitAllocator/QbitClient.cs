using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QbitAllocator;

public sealed class QbitClient
{
    private readonly HttpClient _client;
    private readonly AllocatorOptions _options;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public QbitClient(HttpClient client, Microsoft.Extensions.Options.IOptions<AllocatorOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public async Task<UpstreamStatus> GetConnectivityAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _client.GetAsync("api/v2/app/version", cancellationToken);
            var version = response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(cancellationToken) : null;
            return new UpstreamStatus(true, response.StatusCode != HttpStatusCode.Forbidden && response.StatusCode != HttpStatusCode.Unauthorized, version, (int)response.StatusCode, null);
        }
        catch (Exception ex)
        {
            return new UpstreamStatus(false, false, null, null, ex.Message);
        }
    }

    public async Task<IReadOnlyList<QbitTorrent>> GetTorrentsAsync(CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync("api/v2/torrents/info", cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<List<QbitTorrent>>(stream, JsonOptions, cancellationToken) ?? [];
    }

    public async Task<int> CountPausedNeverStartedUnassignedAsync(CancellationToken cancellationToken)
    {
        var torrents = await GetTorrentsAsync(cancellationToken);
        return torrents.Count(t => ReconcilerService.IsAssignableCandidate(t, _options.AssignmentTag));
    }

    public async Task SetLocationAsync(string hash, string location, CancellationToken cancellationToken)
    {
        await PostFormAsync("api/v2/torrents/setLocation", new Dictionary<string, string>
        {
            ["hashes"] = hash,
            ["location"] = location
        }, cancellationToken);
    }

    public async Task AddTagsAsync(string hash, params string[] tags)
    {
        await PostFormAsync("api/v2/torrents/addTags", new Dictionary<string, string>
        {
            ["hashes"] = hash,
            ["tags"] = string.Join(",", tags.Where(t => !string.IsNullOrWhiteSpace(t)))
        }, CancellationToken.None);
    }

    public async Task RemoveTagsAsync(string hash, params string[] tags)
    {
        await PostFormAsync("api/v2/torrents/removeTags", new Dictionary<string, string>
        {
            ["hashes"] = hash,
            ["tags"] = string.Join(",", tags.Where(t => !string.IsNullOrWhiteSpace(t)))
        }, CancellationToken.None);
    }

    public async Task StartAsync(string hash, CancellationToken cancellationToken)
    {
        await PostFormAsync("api/v2/torrents/start", new Dictionary<string, string>
        {
            ["hashes"] = hash
        }, cancellationToken);
    }

    private async Task PostFormAsync(string path, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var response = await _client.PostAsync(path, new FormUrlEncodedContent(form), cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}

public sealed record UpstreamStatus(bool Reachable, bool Authenticated, string? Version, int? StatusCode, string? Error);

public sealed class QbitTorrent
{
    [JsonPropertyName("hash")]
    public string Hash { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("tags")]
    public string? Tags { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = "";

    [JsonPropertyName("progress")]
    public double Progress { get; set; }

    [JsonPropertyName("downloaded")]
    public long Downloaded { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("last_activity")]
    public long LastActivity { get; set; }
}

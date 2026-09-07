using Microsoft.Extensions.Options;
using TripUpdates.Configuration;

namespace TripUpdates.Gtfs;

/// <summary>
/// Owns the on-disk copy of the 19 MB static feed and the catalogs resolved from it.
/// Keeping the zip on disk means a restart, or a new line/stop from settings, does not
/// need a fresh download, and an upstream outage cannot leave the app with nothing.
/// </summary>
public sealed class StaticFeedStore(
    IHttpClientFactory httpClientFactory,
    IOptions<TripUpdatesOptions> options,
    ILogger<StaticFeedStore> logger)
{
    private readonly TripUpdatesOptions _options = options.Value;
    private readonly SemaphoreSlim _downloadLock = new(1, 1);
    private readonly Dictionary<(string Line, string Stop), StaticCatalog> _catalogs = [];

    private string CacheDirectory => _options.Feed.CacheDirectory;
    private string ZipPath => Path.Combine(CacheDirectory, "gtfs-static.zip");
    private string ETagPath => Path.Combine(CacheDirectory, "gtfs-static.etag");

    public DateTimeOffset? LastDownloadedAt { get; private set; }

    /// <summary>Resolves a line and stop against the cached feed, memoising the result.</summary>
    public async Task<StaticCatalog> GetCatalogAsync(string line, string stopName, CancellationToken ct)
    {
        var key = (line.Trim(), stopName.Trim());

        lock (_catalogs)
            if (_catalogs.TryGetValue(key, out var cached)) return cached;

        if (!File.Exists(ZipPath)) await RefreshAsync(ct);

        await using var zip = File.OpenRead(ZipPath);
        var catalog = GtfsStaticParser.Resolve(zip, key.Item1, key.Item2, DirectionsFor(key.Item1));

        lock (_catalogs) _catalogs[key] = catalog;
        logger.LogInformation(
            "Resolved line {Line} at {Stop} to route {RouteId}: {Directions}",
            catalog.Line, catalog.StopName, catalog.RouteId,
            string.Join(", ", catalog.Directions.Select(d => $"{d.Label} -> [{string.Join(",", d.StopIds)}]")));
        return catalog;
    }

    /// <summary>Label overrides only apply to the line they were written for.</summary>
    private List<DirectionOptions> DirectionsFor(string line) =>
        string.Equals(line, _options.Line.Trim(), StringComparison.OrdinalIgnoreCase)
            ? _options.Directions
            : [];

    public async Task RefreshAsync(CancellationToken ct)
    {
        await _downloadLock.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(CacheDirectory);

            using var request = new HttpRequestMessage(HttpMethod.Get, _options.Feed.StaticUrl);
            if (File.Exists(ZipPath) && File.Exists(ETagPath))
                request.Headers.TryAddWithoutValidation("If-None-Match", await File.ReadAllTextAsync(ETagPath, ct));

            var client = httpClientFactory.CreateClient(nameof(StaticFeedStore));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
            {
                logger.LogInformation("Static GTFS feed unchanged.");
                LastDownloadedAt = DateTimeOffset.UtcNow;
                return;
            }

            response.EnsureSuccessStatusCode();

            // Download to a temp file first so a failure never truncates a good cached feed.
            var temp = ZipPath + ".tmp";
            await using (var destination = File.Create(temp))
                await response.Content.CopyToAsync(destination, ct);

            File.Move(temp, ZipPath, overwrite: true);
            if (response.Headers.ETag is { } etag)
                await File.WriteAllTextAsync(ETagPath, etag.Tag, ct);

            lock (_catalogs) _catalogs.Clear();   // ids may have changed
            LastDownloadedAt = DateTimeOffset.UtcNow;
            logger.LogInformation("Downloaded static GTFS feed ({Bytes:N0} bytes).", new FileInfo(ZipPath).Length);
        }
        finally
        {
            _downloadLock.Release();
        }
    }
}

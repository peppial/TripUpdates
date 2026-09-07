using Microsoft.Extensions.Options;
using TripUpdates.Configuration;

namespace TripUpdates.Gtfs;

/// <summary>
/// Keeps the realtime feed fresh, and the static feed current, on behalf of every device.
/// Phones poll this app, never the upstream feed.
/// </summary>
public sealed class FeedPoller(
    IHttpClientFactory httpClientFactory,
    RealtimeFeed feed,
    StaticFeedStore staticFeed,
    IOptions<TripUpdatesOptions> options,
    ILogger<FeedPoller> logger) : BackgroundService
{
    private readonly FeedOptions _options = options.Value.Feed;
    private readonly TripUpdatesOptions _settings = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await WarmUpAsync(stoppingToken);

        var nextStaticRefresh = DateTimeOffset.UtcNow + _options.StaticRefreshInterval;
        using var timer = new PeriodicTimer(_options.PollInterval);

        do
        {
            await PollRealtimeAsync(stoppingToken);

            if (DateTimeOffset.UtcNow >= nextStaticRefresh)
            {
                nextStaticRefresh = DateTimeOffset.UtcNow + _options.StaticRefreshInterval;
                await TryRefreshStaticAsync(stoppingToken);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task WarmUpAsync(CancellationToken ct)
    {
        await TryRefreshStaticAsync(ct);
        try
        {
            await staticFeed.GetCatalogAsync(_settings.Line, _settings.StopName, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Surfaced to the client as an error rather than crashing the app at boot.
            logger.LogError(ex, "Could not resolve line {Line} at {Stop} on startup.",
                _settings.Line, _settings.StopName);
        }
    }

    private async Task TryRefreshStaticAsync(CancellationToken ct)
    {
        try
        {
            await staticFeed.RefreshAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Static GTFS refresh failed; continuing with the cached feed.");
        }
    }

    private async Task PollRealtimeAsync(CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient(nameof(FeedPoller));
            var bytes = await client.GetByteArrayAsync(_options.TripUpdatesUrl, ct);
            feed.Publish(bytes, DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Realtime poll failed.");
            feed.RecordFailure(ex.Message);
        }
    }
}

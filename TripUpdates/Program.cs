using Microsoft.Extensions.Options;
using TripUpdates.Arrivals;
using TripUpdates.Configuration;
using TripUpdates.Gtfs;

var builder = WebApplication.CreateBuilder(args);

// Most container hosts (Fly.io, Railway, Cloud Run) inject the listening port as $PORT.
if (Environment.GetEnvironmentVariable("PORT") is { Length: > 0 } port)
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Services.Configure<TripUpdatesOptions>(
    builder.Configuration.GetSection(TripUpdatesOptions.SectionName));

builder.Services.AddHttpClient(nameof(FeedPoller), c => c.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddHttpClient(nameof(StaticFeedStore), c => c.Timeout = TimeSpan.FromMinutes(5));

builder.Services.AddSingleton<RealtimeFeed>();
builder.Services.AddSingleton<StaticFeedStore>();
builder.Services.AddSingleton(sp =>
    new ArrivalsService(sp.GetRequiredService<IOptions<TripUpdatesOptions>>().Value.Feed));
builder.Services.AddHostedService<FeedPoller>();

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition =
        System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/arrivals", async (
    string? line,
    string? stop,
    IOptions<TripUpdatesOptions> options,
    StaticFeedStore staticFeed,
    RealtimeFeed feed,
    ArrivalsService arrivals,
    CancellationToken ct) =>
{
    var settings = options.Value;
    var requestedLine = string.IsNullOrWhiteSpace(line) ? settings.Line : line;
    var requestedStop = string.IsNullOrWhiteSpace(stop) ? settings.StopName : stop;

    try
    {
        var catalog = await staticFeed.GetCatalogAsync(requestedLine, requestedStop, ct);
        return Results.Ok(arrivals.Build(catalog, feed.StateFor(catalog), DateTimeOffset.UtcNow));
    }
    catch (GtfsResolutionException ex)
    {
        return Results.Problem(title: "Неразпозната линия или спирка", detail: ex.Message, statusCode: 404);
    }
});

app.MapGet("/api/health", (RealtimeFeed feed, StaticFeedStore staticFeed) =>
    Results.Ok(new { staticFeedDownloadedAt = staticFeed.LastDownloadedAt }));

app.Run();

public partial class Program;

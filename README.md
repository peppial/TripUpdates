# TripUpdates

A phone-installable board for one bus line at one stop. It answers a single question at a
glance — *when is the next 66, in each direction?*

```
66 към Алеко ще дойде след 12 минути
66 към София ще дойде след 3 минути
```

Live data comes from the Sofia Urban Mobility Centre (ЦГМ) GTFS-Realtime feed published via
Данните на София. The app refreshes every 30 seconds.

## How it works

One ASP.NET Core project serves both the API and the PWA.

```
Browser (PWA, wwwroot)  ──GET /api/arrivals (30s)──▶  ASP.NET Core
                                                          │
                                    FeedPoller (30s)      ├──▶ /api/v1/trip-updates  (560 KB protobuf)
                                    StaticFeedStore       └──▶ /api/v1/static        (19 MB GTFS zip)
```

The server polls upstream once per interval no matter how many phones are watching, and hands
each phone roughly 200 bytes of JSON. Polling the 560 KB protobuf directly from a phone would
cost about 67 MB per hour.

### Resolving settings to feed ids

The line and stop are configured by name and resolved against the static feed on every refresh,
never hardcoded — the feed is regenerated periodically and its ids change.

Two details of the Sofia feed drive the design:

- **`direction_id` is empty throughout it.** Direction is derived from `trip_headsign`, joined
  to the stop through `stop_times.txt`.
- **Stop names carry suffixes.** The rider's "Воденичарски механи" is
  "ВОДЕНИЧАРСКИ МЕХАНИ-ПО ЖЕЛАНИЕ" upstream, so matching is case-insensitive substring.

For line 66 this resolves to route `A63`, with `A1471` (uphill, signed ХИЖА АЛЕКО) and `A1472`
(downhill, signed ЗООПАРКА or МЕТРОСТАНЦИЯ ВИТОША) — which is why one label can cover several
headsigns.

## Configuration

`appsettings.json`, or environment variables using `__` as the separator:

| Setting | Default |
|---|---|
| `TripUpdates:Line` | `66` |
| `TripUpdates:StopName` | `Воденичарски механи` |
| `TripUpdates:Directions` | label overrides mapping headsigns onto "към Алеко" / "към София" |
| `TripUpdates:Feed:PollInterval` | `00:00:30` |
| `TripUpdates:Feed:StaticRefreshInterval` | `1.00:00:00` |
| `TripUpdates:Feed:StaleAfter` | `00:02:00` |
| `TripUpdates:Feed:CacheDirectory` | `cache` |

Direction labels only apply to the line they were written for. Any other line falls back to
labels taken from its own headsigns.

The in-app settings screen (⚙) overrides the line and stop per device, stored in `localStorage`
and passed as `?line=&stop=`.

## Running

```bash
dotnet run --project TripUpdates      # http://localhost:5011
dotnet test                           # 37 tests, no network needed
```

Tests run against committed fixtures — a trimmed GTFS zip and a captured protobuf response —
so they are deterministic and work offline.

## Deploying

The app must be served over HTTPS: Android requires it for PWA install and service workers.

```bash
docker build -t tripupdates .
docker run -p 8080:8080 tripupdates
```

Any container host works (Fly.io, Railway, Azure App Service, Cloud Run); `$PORT` is honoured
if the platform sets it. Mount a volume at the cache directory to avoid re-downloading the
19 MB static feed on every restart.

The script sets the cache directory to `/home/cache` — the only path App Service persists
across restarts — and turns on Always On so the 30 second poller keeps running while nobody is
looking. `SUBSCRIPTION`, `RESOURCE_GROUP` and `APP_NAME` override the targets.

Then open the HTTPS URL on the phone and use "Add to Home screen".

## Behaviour when things break

- **No upcoming departures** is a normal state, not an error — line 66 is a mountain route with
  long gaps and an early last bus. It shows "няма предстоящи курсове".
- **Upstream failure** keeps the last good reading and marks it stale rather than discarding it.
- **App unreachable** still opens: the service worker serves the shell, and the last successful
  reading is restored from `localStorage`, dimmed, with its age shown.
- Arrival times are never served from the service worker cache. A cached minute count is a
  wrong one.
# TripUpdates

# OLXAPI

The backend of the Real Estate App: it scrapes Polish property portals (OLX,
Morizon, Nieruchomości-online), stores every listing as a timestamped snapshot,
and serves the aggregated statistics the Angular dashboard renders.

ASP.NET Core · .NET 10 · EF Core · MySQL

## Projects

| Project | What |
|---|---|
| `AF_mobile_web_api` | web API, scrapers, statistics — the whole application |
| `ApplicationDatabase` | `AppDbContext`, the `PropertyData` model and EF migrations |

## Running it

```powershell
cd AF_mobile_web_api
dotnet run
```

Listens on **http://localhost:5016** (or `$PORT`, which is what the hosting
platform sets). It needs a reachable MySQL — the connection string lives in
`appsettings.json` under `ConnectionStrings:ConnectionString`.

Migrations are **not** applied at startup. Run them yourself after pulling a
schema change:

```powershell
dotnet ef database update --project ApplicationDatabase --startup-project AF_mobile_web_api
```

## Configuration

| Setting | Purpose | Default |
|---|---|---|
| `ConnectionStrings:ConnectionString` | MySQL | `localhost/realestatedb` |
| `AllowedOrigins:Frontend` | CORS origins, comma-separated | `http://localhost:4200` |
| `ScrapeApiKey` | protects the scrape triggers; empty disables the gate | empty |
| `Database:ServerVersion` | override the assumed MySQL/MariaDB version | `7.0.0` (conservative) |
| `PORT` (env) | listen port | `5016` |

In production, supply all of these as environment variables
(`ConnectionStrings__ConnectionString`, `ScrapeApiKey`, …) rather than editing
the JSON files. This is not optional on a container host: `appsettings.Production.json`
is gitignored, so an image built from a clone of this repo does **not** contain
it and falls back to `appsettings.json` — whose `localhost` MySQL does not exist
inside the container. The symptom is every DB-backed endpoint returning 500 with
`Unable to connect to any of the specified MySQL hosts`, while the deploy itself
reports success.

Values in use today (Render):

```
ConnectionStrings__ConnectionString=server=93.157.102.234;database=nlddzucmzp_OLXAPI;user=…;password=…;
AllowedOrigins__Frontend=https://cf9fdf5ccf7d71a61b2548.cyberfolks.host
```

The database host must be the IP: `krzysztofklich.pl` has no DNS record any more,
and `AllowedOrigins__Frontend` is scheme + host only — a path or trailing slash
never matches an `Origin` header.

> ⚠️ `appsettings.Development.json` currently holds a **real database password**
> and it is in this repository's git history. Rotate it and purge the history
> before treating this repo as public.

## API

Base path `/api/RealEstate`. Read endpoints are open; 🔒 marks the scrape
triggers, which require an `X-Api-Key` header once `ScrapeApiKey` is set.

### Reads

| Endpoint | Returns |
|---|---|
| `getFullDashboard/{city}` | charts + insights + map points — everything the dashboard needs, one call. `?includeMapPoints=false` leaves out the points, which are ~98% of the payload; the app uses that and fetches them from `getMapPoints` when a map is opened |
| `properties?…` | paged, filterable, sortable list of distinct offers |
| `propertyHistory/{city}?url=` | price history for one offer |
| `getDashboardCharts/{city}` | the charts slice on its own |
| `getMarketInsights/{city}` | medians, ranges, source counts, best deals |
| `getMapPoints/{city}` | map markers, colour-graded by price/m² |
| `getTimelinePrice/{city}` | average price per scrape day |
| `getGroupedStatistics/{groupBy}/{city}` | bar-chart data grouped by any field |
| `filterByParameter/{groupBy}/{city}/{parameter}` | one series out of the above |
| `getRealEstate/{city}`, `getUniqueOffers`, `RealEstateStats`, `RealEstateGropuBy` | older endpoints, kept for compatibility |

`{city}` is a `CityEnum` name — currently `Krakow` or `Katowice`.

### Health

Outside `/api/RealEstate`, on `/api/Health`. EF opens its connection lazily on
the first query, so a bad connection string still starts cleanly and passes a
port-based probe — these make that visible straight after a deploy.

| Endpoint | Returns |
|---|---|
| `GET /api/Health` | `{"status":"ok"}` — the process is up. Says nothing about the database |
| `GET /api/Health/db` | opens a real connection: 200 when reachable, 503 when not |

`/api/Health/db` deliberately returns no host, credentials or driver text — it is
public whenever `ScrapeApiKey` is unset. Those details go to the log, including
the host that was actually tried, which is the one thing the driver's own error
message leaves out.

### Scrape triggers

| Endpoint | Scope |
|---|---|
| `loadDataMarkeplaces` 🔒 | one city |
| `getdataForManyCities` 🔒 | every city |
| `morizon` 🔒, `nieruchomosciOnline` 🔒 | a single source, returns the data without saving |

The first two return **202 Accepted** immediately and run in the background —
a full run makes ~12,000 outbound requests and takes minutes to an hour. A
second call while one is running gets **409 Conflict**; only one scrape runs at
a time.

```bash
curl -H "X-Api-Key: $KEY" http://localhost:5016/api/RealEstate/getdataForManyCities
```

Nothing inside the app schedules scrapes. A cron job or CI workflow has to call
the trigger — weekly is what the data model assumes.

## How it works

The short version: **every scrape appends immutable rows; nothing is updated or
deleted.** One offer scraped ten weeks running is ten rows sharing a `Url`, each
stamped with `AddedRecordTime`. "The current market" is the newest scrape day;
price history is a row sequence; duplicates across portals are resolved by
`PropertyComparer`.

Layout:

```
Controllers/    one controller, thin — no logic
Services/       scrapers (OLX, Morizon, N-online), statistics, scrape runner, comparer
Repositories/   every database query
DTO/ Domain/    wire shapes and internal shapes
Mappings/       AutoMapper: SearchData ⇄ PropertyData
Filters/        the X-Api-Key gate
Middleware/     exception → JSON error (400 for an unknown city, 500 otherwise)
```

**The full explanation — data flow, batch semantics, caching, the aggregation
rules and the traps — is in `ARCHITECTURE.md` in the parent workspace**
(`realEstateApp/`), alongside `DEPLOYMENT.md` and `DATABASE_SETUP.md`.

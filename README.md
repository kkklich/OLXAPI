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
| `OlxScrape:InitialBucketSize` | initial OLX price-band width in PLN; dense bands split automatically | `1000000` |
| `OlxScrape:MinimumBucketSize` | stop splitting dense OLX bands below this width; report remaining truncation | `5000` |
| `OlxScrape:MaxConcurrentBuckets` | maximum concurrent OLX price-band workers | `3` |
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
| `getMapPoints/{city}` | map markers; `?compact=true` selects version 1 compact rows and shared text dictionaries. Both formats support Brotli/gzip, ETag and 304 |
| `getTimelinePrice/{city}` | average price per scrape day |
| `getGroupedStatistics/{groupBy}/{city}` | bar-chart data grouped by any field |
| `filterByParameter/{groupBy}/{city}/{parameter}` | one series out of the above |
| `getRealEstate/{city}`, `getUniqueOffers`, `RealEstateStats`, `RealEstateGropuBy` | older endpoints, kept for compatibility |

`{city}` is a `CityEnum` name — `Krakow`, `Katowice`, `Chorzow`, `Tychy`,
`Mikolow`, `Myslowice`, `Sosnowiec` — or an area of several cities: `Silesia`
is Katowice, Chorzów, Tychy, Mikołów, Mysłowice and Sosnowiec together. The
app offers `Silesia` and `Krakow`; the `city` filter of `properties` takes an
area too.

An area ([`CityArea`](AF_mobile_web_api/Domain/CityArea.cs)) exists only when
data is read: every row keeps the city it was scraped for, and an area's
statistics are computed over its cities' rows — each city contributing its own
newest scrape. So Katowice's history carries on inside Silesia. Within an area a
district is labelled with its city (`Tychy – Osiedle Z`), because "Centrum" in
Chorzów and "Centrum" in Sosnowiec are different places; an offer without a
district counts towards its city.

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

### OLX preview (without database writes)

`GET /api/RealEstate/olx?city=Krakow&limit=10&offset=0&priceFrom=50000&priceTo=10000000`

Downloads **one page directly from OLX**, returns JSON in the request, and never
calls the database or starts a background scrape. It uses the full scraper's
HTTP downloader and field mapping. Supported cities: `Krakow`, `Katowice`.
If `ScrapeApiKey` is configured, supply the `X-Api-Key` header.

```powershell
Invoke-RestMethod 'http://localhost:5016/api/RealEstate/olx?city=Krakow&limit=10' |
    ConvertTo-Json -Depth 10
```

Parameters: `limit` 1–40 (default 40), `offset` 0–`1000-limit` (default 0),
`priceFrom` (default 50000), `priceTo` (default 10000000). Prices must be
nonnegative and `priceFrom <= priceTo`. To view another page, increase `offset`
by `limit`.

The response contains `savedToDatabase: false`, the exact OLX `sourceUrl`,
`totalCount` reported by OLX (not the number downloaded), `returnedCount`, and
`data` with IDs, titles, URLs, prices, area, price per square metre, floor,
market, building type and location. The usual scraper filters still apply
(e.g. TBS titles are excluded), so the returned count may be below the requested
limit. Promoted offers may also affect OLX's page size. This is a sample page,
not a complete market download. Responses are marked `no-store`.

Invalid filters return 400; OLX/network failures or malformed responses return
502 (including `upstreamStatusCode` for HTTP failures); timeouts return 504.

### Background scrape triggers

| Endpoint | Scope |
|---|---|
| `loadDataMarkeplaces` 🔒 | one city |
| `getdataForManyCities` 🔒 | every city |
| `morizon` 🔒, `nieruchomosciOnline` 🔒 | a single source, returns the data without saving |

The first two return **202 Accepted** immediately and run in the background —
a full run makes ~12,000 outbound requests and takes minutes to an hour. A
second call while one is running gets **409 Conflict**; only one scrape runs at
a time. Since the Silesian cities were added, a full run covers seven cities
instead of two; the requests per city are set by price ranges, not by how many
offers a city has, so expect it to take about three and a half times as long.

OLX filters by a numeric `city_id` per city (`CityExtensions`). Only Kraków's
and Katowice's are known so far; the other Silesian cities are scraped from
Morizon and Nieruchomości-online only — OLX is skipped for them, with a warning
in the log — until their ids are added.

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

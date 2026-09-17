# Deployment Guide — Real Estate App

How to build, publish and serve this project on your own infrastructure
(**cyberFolks hosting** + **VPS**).

- [1. What this project actually is](#1-what-this-project-actually-is)
- [2. Choosing a deployment shape](#2-choosing-a-deployment-shape)
- [3. Before you deploy anything (security)](#3-before-you-deploy-anything-security)
- [4. Option A — everything on the VPS (recommended)](#4-option-a--everything-on-the-vps-recommended)
- [5. Option B — frontend on cyberFolks, API on VPS](#5-option-b--frontend-on-cyberfolks-api-on-vps)
- [6. Database](#6-database)
- [7. Building the release locally (Windows)](#7-building-the-release-locally-windows)
- [8. Shipping a release](#8-shipping-a-release)
- [9. Scheduled scraping](#9-scheduled-scraping)
- [10. Verification checklist](#10-verification-checklist)
- [11. Rollback](#11-rollback)
- [12. Troubleshooting](#12-troubleshooting)

---

## 1. What this project actually is

Two independently versioned applications plus one database:

| Part | Path | Tech | Listens on | Git remote |
|---|---|---|---|---|
| API | `AF_mobile_web_api` (this repo) | ASP.NET Core, **.NET 10** | `$PORT`, default **5016** | `github.com/kkklich/OLXAPI` |
| Web | `../real-estate-app` (separate clone) | **Angular 20 with SSR** (Node + Express) | `$PORT`, default **4000** | `github.com/kkklich/real-estate-app` |
| DB | — | MySQL / MariaDB | 3306 | — |

Two facts that drive every decision below:

1. **The frontend is not a static site.** `angular.json` sets
   `"outputMode": "server"` with an SSR entry point, so `ng build` emits a
   **Node server** (`dist/real-estate-app/server/server.mjs`) that must be kept
   running. Uploading files over FTP is not enough for the default build.
   A static build is available too — see [Option B](#5-option-b--frontend-on-cyberfolks-api-on-vps).
2. **cyberFolks shared hosting cannot run .NET.** Shared hosting gives you
   PHP, Node.js (via DirectAdmin) and MySQL. The API *must* live on the VPS.

The app is currently built with `baseHref: /realestate/`, i.e. it expects to be
served from `https://<domain>/realestate/`, not from the domain root. You can
change this per build with `--base-href` — this matters, see below.

---

## 2. Choosing a deployment shape

**Option A — everything on the VPS.** Nginx terminates TLS and reverse-proxies
`/` to the Angular SSR process and `/api/` to the .NET API. One domain, one
origin, no CORS, SSR fully working.

```
                         ┌──────────────────── VPS ────────────────────┐
browser ──HTTPS──► nginx ─┬─► 127.0.0.1:4000  Angular SSR (node)       │
                          └─► 127.0.0.1:5016  .NET API  ──► MySQL ─────┼──► cyberFolks
                         └─────────────────────────────────────────────┘       (or local)
```

**Option B — static frontend on cyberFolks, API on VPS.** The frontend is built
in static/pre-rendered mode and uploaded to `public_html/realestate/`; the
browser talks cross-origin to the API on the VPS.

```
browser ──HTTPS──► cyberFolks Apache  (static files, /realestate/)
   └────HTTPS(CORS)──► VPS nginx ──► 127.0.0.1:5016  .NET API ──► MySQL
```

| | Option A | Option B |
|---|---|---|
| SSR / SEO | yes | no (pre-rendered HTML only) |
| CORS setup | not needed | required, plus TLS on the API |
| Where the frontend lives | VPS | cyberFolks |
| Complexity | one-time nginx + systemd setup | FTP upload per release |
| Recommended | **✔** | when you want to use the hosting you already pay for |

**Take Option A** unless you specifically want the page served from your
cyberFolks account. Everything under [`deploy/`](deploy/) supports both.

> **Where this file lives.** `deploy/` and this guide sit in the **API**
> repository (`github.com/kkklich/OLXAPI`), so paths below are relative to that
> checkout: `AF_mobile_web_api/` is right here, and the frontend is a separate
> clone assumed to sit beside it as `../real-estate-app`. They used to live in
> the parent `realEstateApp/` workspace folder, which is a local-only git repo
> with no remote — nothing there is backed up, which is why they moved.
>
> `build-release.ps1` takes `-WebDir <path>` if your frontend clone is somewhere
> other than beside this repo, and `-SkipWeb` if you only want the API.

---

## 3. Before you deploy anything (security)

These are blocking issues — fix them **before** the code reaches a public server.

### 3.1 Live database credentials are committed to git

`AF_mobile_web_api/appsettings.Development.json` and
`appsettings.Production.json` contain the real MySQL host, user and password in
plain text, and both files are tracked by git and pushed to GitHub. They are
also copied into the publish output, so deploying them puts the password on the
server in clear text as well.

Do this, in order:

1. **Rotate the MySQL password** in cyberFolks DirectAdmin → *MySQL Management*.
2. Replace the secrets in both JSON files with empty strings:
   ```json
   "ConnectionStrings": { "ConnectionString": "" }
   ```
3. Stop tracking the files that hold environment-specific values:
   ```bash
   git rm --cached AF_mobile_web_api/appsettings.Development.json
   printf '\nAF_mobile_web_api/appsettings.Development.json\n' >> .gitignore
   git commit -m "Stop tracking local appsettings with credentials"
   ```
4. Purge the old password from git history (`git filter-repo` or BFG) and
   force-push, otherwise it stays readable in every old commit on GitHub.
5. Supply the real values through **environment variables** only — see below.

### 3.2 Configuration by environment variable

ASP.NET Core reads environment variables *after* the JSON files, so env vars
win. `__` (double underscore) replaces the `:` nesting separator:

| Config key (Program.cs) | Environment variable |
|---|---|
| `ConnectionStrings:ConnectionString` | `ConnectionStrings__ConnectionString` |
| `AllowedOrigins:Frontend` | `AllowedOrigins__Frontend` |
| `ScrapeApiKey` | `ScrapeApiKey` |
| `Database:ServerVersion` | `Database__ServerVersion` |

The template is [`deploy/env/realestate-api.env.example`](deploy/env/realestate-api.env.example).
It is deployed to `/etc/realestate/api.env` with mode `0600` and is **never**
committed.

> `Database__ServerVersion`: leave it **unset**. `Program.cs` deliberately falls
> back to a conservative feature set that today's production SQL was generated
> against; overriding it can change how EF translates queries against a live DB.

### 3.3 Lock down the scrape endpoints

Four endpoints (`nieruchomosciOnline`, `morizon`, `loadDataMarkeplaces`,
`getdataForManyCities`) trigger scraping and are guarded by
`[RequireScrapeApiKey]`. That guard is **disabled while `ScrapeApiKey` is
empty** — which is how it currently ships. On a public server anyone could
trigger a full scrape run.

Generate a key and set it in `/etc/realestate/api.env`:

```bash
openssl rand -hex 32
```

Callers then need the header `X-Api-Key: <that value>` — including the GitHub
Actions cron job (see [§9](#9-scheduled-scraping)).

### 3.4 The MapTiler key is public

`environment.prod.ts` ships `maptilerKey` to the browser — unavoidable for a
client-side map. Restrict it by HTTP referrer in the MapTiler dashboard to your
domain so it cannot be used elsewhere.

---

## 4. Option A — everything on the VPS (recommended)

Assumes Ubuntu 22.04/24.04, root or sudo access, and a DNS **A record** for your
domain (e.g. `realestate.krzysztofklich.pl`) pointing at the VPS IP.

### 4.1 One-time server bootstrap

Copy the deploy folder to the VPS and run the bootstrap script:

```powershell
# from D:\Dokumenty\Praca\repos\realEstateApp\OLXAPI  (PowerShell on your machine)
scp -r deploy root@VPS_IP:/root/realestate-deploy
ssh root@VPS_IP
```

```bash
# on the VPS
cd /root/realestate-deploy
bash scripts/setup-vps.sh            # installs .NET 10 runtime, Node 22, nginx, certbot, ufw
```

The script is idempotent — safe to re-run. It creates:

- system user `realestate` (no login shell)
- `/opt/realestate/{api,web}/{releases,shared}` — release directories
- `/etc/realestate/` — env files, mode `0600`, owned by `realestate`

### 4.2 Fill in the environment files

```bash
install -m 600 -o realestate -g realestate \
  /root/realestate-deploy/env/realestate-api.env.example /etc/realestate/api.env
install -m 600 -o realestate -g realestate \
  /root/realestate-deploy/env/realestate-ssr.env.example /etc/realestate/web.env

nano /etc/realestate/api.env     # connection string, CORS origin, scrape key
```

### 4.3 Install the services and the vhost

```bash
cp /root/realestate-deploy/systemd/*.service /etc/systemd/system/
systemctl daemon-reload

cp /root/realestate-deploy/nginx/realestate.conf /etc/nginx/sites-available/realestate.conf
sed -i 's/realestate.example.com/YOUR.DOMAIN.HERE/g' /etc/nginx/sites-available/realestate.conf
ln -sf /etc/nginx/sites-available/realestate.conf /etc/nginx/sites-enabled/realestate.conf
rm -f /etc/nginx/sites-enabled/default
nginx -t && systemctl reload nginx
```

### 4.4 Issue the TLS certificate

```bash
certbot --nginx -d YOUR.DOMAIN.HERE
```

Certbot rewrites the vhost in place: it adds the `443` server block, the
certificate paths and an HTTP→HTTPS redirect. Renewal is automatic via the
`certbot.timer` systemd unit — verify with `certbot renew --dry-run`.

### 4.5 Point the frontend at your own domain

Edit `../real-estate-app/src/enviroments/environment.prod.ts`:

```ts
export const environment = {
    production: true,
    apiUrl: 'https://YOUR.DOMAIN.HERE',      // was: https://olxapi-jwcz.onrender.com
    maptilerKey: 'fE7HmfEfHzBPNM7hOEzA'
};
```

> **Use the full absolute URL, not `''`.** With `apiUrl: ''` the services would
> build relative URLs like `/api/RealEstate/...`, and Angular's server-side
> `HttpClient` cannot resolve a relative URL during SSR — the dashboard would
> fail to render server-side. Pointing at your own HTTPS domain keeps SSR
> working *and* keeps browser requests same-origin (so no CORS preflight).

Then build and ship — see [§7](#7-building-the-release-locally-windows) and
[§8](#8-shipping-a-release).

### 4.6 Base href

Serving from the domain root (`https://realestate.krzysztofklich.pl/`) requires
building with `--base-href=/`; `deploy/scripts/build-release.ps1` does this by
default. To keep the app under `/realestate/`, pass `-BaseHref /realestate/` and
use the sub-path block that is commented out in
[`deploy/nginx/realestate.conf`](deploy/nginx/realestate.conf).

---

## 5. Option B — frontend on cyberFolks, API on VPS

The API half is identical to Option A (§4.1–4.4) minus the SSR service — skip
`realestate-ssr.service` and use the API-only nginx block.

Frontend, per release:

1. Set `apiUrl` in `environment.prod.ts` to the **public HTTPS URL of the API**
   (currently `https://olxapi-jwcz.onrender.com`). It must be HTTPS — a page
   served over HTTPS cannot call a plain-HTTP API (mixed content).
2. Set the CORS origin wherever the API runs — `/etc/realestate/api.env` on a
   VPS, the Environment tab on Render — to the origin serving the page, then
   restart the API:
   ```
   AllowedOrigins__Frontend=https://cf9fdf5ccf7d71a61b2548.cyberfolks.host
   ```
   Origin means scheme + host only. The `/realestate/` sub-path is not part of
   it, a trailing slash breaks the match, and getting it wrong shows up as a
   page that loads but stays empty with CORS errors in the browser console.
3. Build the static bundle:
   ```powershell
   cd ..\real-estate-app
   npm ci
   $env:NG_BUILD_MAX_WORKERS=1
   npx ng build --configuration production-static --base-href /realestate/
   ```
   Output: `dist/real-estate-app/browser/` — plain files, no Node needed.
   Routes `/`, `/properties` and `/properties/history` are pre-rendered to HTML.
4. Upload the **contents** of `dist/real-estate-app/browser/` to
   `public_html/realestate/` on cyberFolks (FTP / File Manager / rsync).
5. Upload [`deploy/shared-hosting/.htaccess`](deploy/shared-hosting/.htaccess)
   into the same directory. Without it, deep links like
   `/realestate/properties` return Apache 404s.

Full DirectAdmin walkthrough, including the Node.js-app route if you would
rather run real SSR on the hosting: [`deploy/CYBERFOLKS.md`](deploy/CYBERFOLKS.md).

---

## 6. Database

Currently: MySQL on cyberFolks (`93.157.102.234`, database
`nlddzucmzp_OLXAPI`), reached over the public internet.

> **Do not use `krzysztofklich.pl` as the database host.** That name has no DNS
> record of any kind (no A, no NS, no SOA — on 8.8.8.8 and 1.1.1.1), so the
> connection fails to resolve and surfaces as `Unable to connect to any of the
> specified MySQL hosts`. The server itself is fine: `93.157.102.234`
> (`www-cl-1.cyberadmin.cyber-folks.pl`) answers on 3306 with MariaDB 10.11.16.

### 6.1 Keep it on cyberFolks

Works, and is the least effort. In DirectAdmin → *MySQL Management* → *Access
Hosts*, add the **VPS IP** so the API is allowed to connect. Expect
10–40 ms per round-trip; endpoints that issue many queries feel it.

### 6.2 Move it to the VPS (better)

Lower latency, no remote-access exposure, and the API talks to `localhost`.

```bash
# on your machine or the VPS — dump from cyberFolks
mysqldump -h 93.157.102.234 -u nlddzucmzp_krzysztof -p \
  --single-transaction --routines --no-tablespaces \
  nlddzucmzp_OLXAPI > olxapi.sql

# on the VPS
apt install -y mariadb-server && mysql_secure_installation
mysql -u root -p -e "CREATE DATABASE olxapi CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
  CREATE USER 'olxapi'@'localhost' IDENTIFIED BY 'STRONG_PASSWORD';
  GRANT ALL PRIVILEGES ON olxapi.* TO 'olxapi'@'localhost'; FLUSH PRIVILEGES;"
mysql -u root -p olxapi < olxapi.sql
```

Then in `/etc/realestate/api.env`:

```
ConnectionStrings__ConnectionString=server=127.0.0.1;database=olxapi;user=olxapi;password=STRONG_PASSWORD;
```

### 6.3 Migrations — required before first serve

The app does **not** run migrations at startup. Two migrations that add
`PropertyData` indexes (`20260717000000_AddUrlAddedRecordTimeIndex` and
`20260720194121_AddPropertyDataQueryIndexes`) are still **missing from the live
database** (confirmed 2026-09-17). `/properties` no longer depends on them — it
is served from an in-memory snapshot — but the queries behind that snapshot and
behind price history still run without the index they were written for.

From your Windows machine, with the .NET SDK and the EF tool
(`dotnet tool install --global dotnet-ef`, already present here at 10.0.0):

```powershell
cd D:\Dokumenty\Praca\repos\realEstateApp\OLXAPI

# see what the database is missing
dotnet ef migrations list --project ApplicationDatabase --startup-project AF_mobile_web_api

# apply directly (needs network access to the DB)
dotnet ef database update --project ApplicationDatabase --startup-project AF_mobile_web_api
```

If the database is only reachable through phpMyAdmin, generate a script instead
and paste it into phpMyAdmin → *SQL*. `--idempotent` makes it safe to run twice:

```powershell
dotnet ef migrations script --idempotent `
  --project ApplicationDatabase --startup-project AF_mobile_web_api `
  --output ..\artifacts\migrations.sql
```

Index creation on a large `PropertyData` table can run for minutes — run it in a
quiet window and do not interrupt it.

---

## 7. Building the release locally (Windows)

Everything is wrapped in one script:

```powershell
cd D:\Dokumenty\Praca\repos\realEstateApp\OLXAPI
.\deploy\scripts\build-release.ps1
```

It produces, in `artifacts/`:

- `api.tar.gz` — `dotnet publish -c Release` output of `AF_mobile_web_api`
- `web.tar.gz` — SSR build (`browser/` + `server/`), base href `/`

Useful switches:

| Switch | Effect |
|---|---|
| `-BaseHref /realestate/` | build the app for a sub-path |
| `-Static` | static/pre-rendered build instead of SSR (Option B) |
| `-SkipApi` / `-SkipWeb` | build only one side |

Doing it by hand instead:

```powershell
# API
cd AF_mobile_web_api
dotnet publish -c Release -o ..\artifacts\api

# Web (SSR)
cd ..\..\real-estate-app
npm ci
$env:NG_BUILD_MAX_WORKERS=1        # the Angular build OOMs with parallel workers here
npx ng build --configuration production --base-href /
```

---

## 8. Shipping a release

```powershell
.\deploy\scripts\deploy-vps.ps1 -VpsHost root@VPS_IP
```

The script uploads the tarballs from `artifacts/` and runs
[`deploy/scripts/release.sh`](deploy/scripts/release.sh) on the server, which:

1. unpacks into `/opt/realestate/{api,web}/releases/<UTC timestamp>/`
2. re-points the `current` symlink
3. `systemctl restart realestate-api realestate-ssr`
4. waits for both to report healthy, and keeps the last 5 releases

Manual equivalent on the VPS:

```bash
systemctl restart realestate-api realestate-ssr
systemctl status  realestate-api realestate-ssr
journalctl -u realestate-api -f
```

---

## 9. Scheduled scraping

`.github/workflows/call-api.yml` runs every Sunday 11:00 UTC and curls
the old Render URL without an API key. After migrating, update it — or move the
schedule onto the VPS, which is more reliable and keeps the key off GitHub:

```bash
# /etc/cron.d/realestate-scrape  — Sundays 11:00 UTC
0 11 * * 0 realestate curl -fsS -m 3600 -H "X-Api-Key: YOUR_KEY" \
  https://YOUR.DOMAIN.HERE/api/RealEstate/getdataForManyCities >> /var/log/realestate-scrape.log 2>&1
```

If you keep it in GitHub Actions, store the key as a repository secret and send
it as a header — never inline it in the workflow file:

```yaml
run: curl -fsS -m 3600 -H "X-Api-Key: ${{ secrets.SCRAPE_API_KEY }}" \
       https://YOUR.DOMAIN.HERE/api/RealEstate/getdataForManyCities
```

A scrape run is long. The nginx vhost gives the scrape endpoints a 1-hour
`proxy_read_timeout` for exactly this reason.

---

## 10. Verification checklist

**Check the database first.** A successful deploy proves nothing about it: EF
opens its connection on the first query, so a container with a missing or wrong
connection string starts, binds its port, and reports healthy right up until a
real request arrives.

```bash
curl -s http://127.0.0.1:5016/api/Health      # {"status":"ok"} - process is up
curl -s http://127.0.0.1:5016/api/Health/db   # {"status":"ok","database":"reachable",...}
```

`/api/Health/db` returns **503** when the database cannot be reached, and the
log line next to it names the host that was tried — which is the one fact the
driver's own `Unable to connect to any of the specified MySQL hosts` leaves out:

```bash
journalctl -u realestate-api -n 20 | grep Health   # or: docker logs realestate-api
```

On the VPS:

```bash
systemctl is-active realestate-api realestate-ssr     # active, active
ss -ltnp | grep -E '5016|4000'                        # both listening on loopback
curl -s -o /dev/null -w '%{http_code}\n' http://127.0.0.1:5016/api/RealEstate/getUniqueOffers
curl -s -o /dev/null -w '%{http_code}\n' http://127.0.0.1:4000/
```

From outside:

```bash
curl -I https://YOUR.DOMAIN.HERE/                     # 200, text/html
curl -I https://YOUR.DOMAIN.HERE/api/RealEstate/getUniqueOffers
curl -s https://YOUR.DOMAIN.HERE/ | grep -c 'app-root'  # >0 → SSR HTML is being produced
```

Option B (page on cyberFolks, API on Render) — the page and the API are on
different origins, so the CORS grant is the thing to check, and `curl` sees it
even when the request itself fails:

```bash
curl -I https://cf9fdf5ccf7d71a61b2548.cyberfolks.host/realestate/   # 200, text/html
curl -sI -H 'Origin: https://cf9fdf5ccf7d71a61b2548.cyberfolks.host' \
  https://olxapi-jwcz.onrender.com/api/RealEstate/getUniqueOffers |
  grep -i access-control-allow-origin      # must echo that exact origin back
```

No `access-control-allow-origin` line means the API does not know this origin:
the page will load and every panel will stay empty.

In a browser: load the dashboard, confirm charts render, open the map, click
through to `/properties`, then **reload directly on that URL** — a deep-link
reload is what catches base-href and rewrite mistakes.

Also confirm the scrape gate is closed:

```bash
curl -s -o /dev/null -w '%{http_code}\n' \
  https://YOUR.DOMAIN.HERE/api/RealEstate/loadDataMarkeplaces      # expect 401
```

---

## 11. Rollback

Releases are kept on disk, so rolling back is a symlink swap:

```bash
ls -1 /opt/realestate/web/releases          # pick the previous timestamp
ln -sfn /opt/realestate/web/releases/20260722T101500Z /opt/realestate/web/current
ln -sfn /opt/realestate/api/releases/20260722T101500Z /opt/realestate/api/current
systemctl restart realestate-api realestate-ssr
```

Database migrations do **not** roll back with the code. If a release included a
migration, either leave the schema in place (additive index migrations are
backwards-compatible) or revert deliberately with
`dotnet ef database update <PreviousMigrationName>`.

---

## 12. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| 502 Bad Gateway on `/` | SSR process is down | `journalctl -u realestate-ssr -n 50` |
| 502 on `/api/...` | API is down, usually a DB connection failure at startup | `journalctl -u realestate-api -n 50` |
| Page loads, all data empty, browser console shows CORS errors | `AllowedOrigins__Frontend` doesn't match the origin *exactly* (scheme + host + port, no trailing slash) | fix `/etc/realestate/api.env`, restart the API |
| CSS/JS 404 after deploy | base href doesn't match the serving path | rebuild with the right `--base-href` |
| Deep link works from a menu click but 404s on reload | missing SPA fallback | Option A: nginx block; Option B: upload `.htaccess` |
| `/properties` hangs then 504s | index migrations not applied | [§6.3](#63-migrations--required-before-first-serve) |
| API logs `Unable to connect to any of the specified MySQL hosts` | The server never got a usable connection string. Three ways in, all with this one message: (a) `ConnectionStrings__ConnectionString` is unset, so the app falls back to `appsettings.json`'s `Server=localhost` — nothing listens on 3306 inside the container/VM; (b) it is set to `server=krzysztofklich.pl`, which has no DNS record; (c) the artifact's `appsettings.Production.json` was blanked by `build-release.ps1` and no env file replaced it. Being refused by *Access Hosts* is a **different** message (`Access denied for user ...'@'IP'`) | [§6](#6-database) |
| Angular build dies with a heap/OOM error | parallel build workers | `$env:NG_BUILD_MAX_WORKERS=1` before building |
| `dotnet: command not found` in systemd | dotnet installed somewhere other than `/usr/bin/dotnet` | `which dotnet`, update `ExecStart` |
| Scrape endpoint returns 401 | expected — the key gate is on | send `X-Api-Key` |
| Scrape endpoint 504s halfway | request outlives the proxy timeout | already handled by the scrape `location` block; confirm it wasn't lost when certbot rewrote the vhost |

Logs:

```bash
journalctl -u realestate-api  -f          # API
journalctl -u realestate-ssr  -f          # SSR
tail -f /var/log/nginx/realestate.error.log
```

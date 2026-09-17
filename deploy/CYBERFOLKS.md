# cyberFolks — hosting-side setup

Everything you do in the **DirectAdmin** panel at cyberFolks. Read
[`../DEPLOYMENT.md`](../DEPLOYMENT.md) first for the overall picture.

> Panel labels shift between DirectAdmin versions and cyberFolks' own skin, and
> feature availability differs per plan. Where the exact menu name matters and
> may not match, it is flagged. If something described here isn't in your panel,
> it is a plan limit, not a mistake — cyberFolks support can confirm.

- [What shared hosting can and cannot run](#what-shared-hosting-can-and-cannot-run)
- [1. Database (you already use this)](#1-database-you-already-use-this)
- [2. Domain and DNS split](#2-domain-and-dns-split)
- [3. SSL](#3-ssl)
- [4. Publishing the static frontend](#4-publishing-the-static-frontend)
- [5. Alternative: real SSR via DirectAdmin's Node.js app](#5-alternative-real-ssr-via-directadmins-nodejs-app)
- [6. Per-release routine](#6-per-release-routine)

---

## What shared hosting can and cannot run

| | cyberFolks shared | Your VPS |
|---|---|---|
| .NET 10 API | **no** | yes |
| Static Angular build | yes | yes |
| Angular SSR (Node) | only if the plan offers Node.js apps | yes |
| MySQL / MariaDB | yes | yes |
| Let's Encrypt TLS | yes (one click) | yes (certbot) |
| Long-running background work (scrape runs) | no | yes |

**The API always goes on the VPS.** There is no configuration that makes ASP.NET
Core run on shared hosting.

---

## 1. Database (you already use this)

The app currently points at `nlddzucmzp_OLXAPI` on this hosting. The
`nlddzucmzp_` prefix is your DirectAdmin account prefix — DirectAdmin prepends
it to every database and user name, and you cannot drop it.

### Let the VPS connect

Shared-hosting MySQL refuses remote connections until you whitelist the caller.

> **DirectAdmin → Account Manager → MySQL Management**, open the database, find
> **Access Hosts** (sometimes shown as *Remote MySQL* / *Zdalny dostęp*).
> Add your **VPS IP address**.

Avoid the `%` wildcard — it exposes the database to the entire internet, and the
password is only as good as its rotation history (see
[DEPLOYMENT.md §3.1](../DEPLOYMENT.md#31-live-database-credentials-are-committed-to-git);
the current one is in git and needs rotating regardless).

### Address the database by IP

`krzysztofklich.pl` has **no DNS record** — no A, no NS, no SOA, on 8.8.8.8 and
1.1.1.1 alike. Any connection string using that name fails to resolve, and
MySqlConnector reports it as `Unable to connect to any of the specified MySQL
hosts`. Use the hosting server's address instead:

| | |
|---|---|
| IP | `93.157.102.234` |
| reverse DNS | `www-cl-1.cyberadmin.cyber-folks.pl` (resolves back to the same IP) |
| server | MariaDB 10.11.16 (CloudLinux), answering on 3306 |

Verify from the VPS:

```bash
apt install -y mariadb-client
mysql -h 93.157.102.234 -u nlddzucmzp_krzysztof -p nlddzucmzp_OLXAPI -e "SELECT COUNT(*) FROM PropertyData;"
```

Failure modes:
- `Unknown MySQL server host` / `Unable to connect to any of the specified MySQL
  hosts` **instantly** → the host name doesn't resolve. This is what the dead
  `krzysztofklich.pl` produces.
- `Host 'x.x.x.x' is not allowed` or `Access denied for user ...'@'x.x.x.x'` →
  the IP is not in Access Hosts. Note this is a *different* error: the server was
  reached, it refused the caller.
- Hangs, then times out → outbound 3306 is blocked, or the hosting firewall
  dropped it. Check `ufw status` on the VPS first.

### Applying migrations through phpMyAdmin

When `dotnet ef database update` can't reach the database directly, generate the
SQL on your machine and paste it in:

```powershell
cd D:\Dokumenty\Praca\repos\realEstateApp\OLXAPI
dotnet ef migrations script --idempotent `
  --project ApplicationDatabase --startup-project AF_mobile_web_api `
  --output ..\artifacts\migrations.sql
```

**DirectAdmin → phpMyAdmin → (database) → Import** (or paste into the **SQL**
tab). `--idempotent` means re-running it is harmless.

Two index migrations are known to be missing from the live database and
`/properties` times out without them — see
[DEPLOYMENT.md §6.3](../DEPLOYMENT.md#63-migrations--required-before-first-serve).
Index builds on a large `PropertyData` table can exceed phpMyAdmin's execution
limit; if the import dies partway, run the statements one at a time, or run them
over the `mysql` CLI from the VPS instead.

### Backups

```bash
mysqldump -h 93.157.102.234 -u nlddzucmzp_krzysztof -p \
  --single-transaction --routines --no-tablespaces \
  nlddzucmzp_OLXAPI | gzip > olxapi-$(date +%F).sql.gz
```

`--no-tablespaces` matters: shared-hosting users lack the `PROCESS` privilege
that `mysqldump` otherwise wants, and the dump fails without it.

---

## 2. Domain and DNS split

`krzysztofklich.pl` is managed at cyberFolks, and the VPS needs a name too. Add
records in **DirectAdmin → Account Manager → DNS Management**:

| Record | Points at | Used for |
|---|---|---|
| `realestate` A | VPS IP | Option A — the whole app on the VPS |
| `api` A | VPS IP | Option B — API only on the VPS |
| `@` / `www` | leave as-is | the hosting itself |

DNS propagation is usually minutes but can take hours. Check before running
certbot — certbot fails if the name doesn't resolve to the VPS yet:

```bash
dig +short realestate.krzysztofklich.pl
```

---

## 3. SSL

**On the VPS:** `certbot --nginx -d realestate.krzysztofklich.pl`
(covered in DEPLOYMENT.md §4.4).

**On the hosting:** DirectAdmin → **SSL Certificates** → *Free & automatic
certificate from Let's Encrypt*. Only needed if you serve the page from
cyberFolks (Option B). Once it's active, uncomment the HTTPS redirect block at
the bottom of [`shared-hosting/.htaccess`](shared-hosting/.htaccess).

Both sides need HTTPS in Option B: a page served over HTTPS cannot call an API
over plain HTTP — the browser blocks it as mixed content, and the app shows
empty charts with a console error.

---

## 4. Publishing the static frontend

Build on your machine:

```powershell
cd D:\Dokumenty\Praca\repos\realEstateApp
.\deploy\scripts\build-release.ps1 -Static -SkipApi
```

Produces `artifacts\web-static.zip` — the contents of
`dist\real-estate-app\browser\` plus the `.htaccess`. Routes `/`, `/properties`
and `/properties/history` are pre-rendered to real HTML files.

Before building, `src/enviroments/environment.prod.ts` must point `apiUrl` at
the **public HTTPS URL of the API** — currently
`https://olxapi-jwcz.onrender.com`.

### Upload

The account is addressed by its cyberFolks technical domain,
`cf9fdf5ccf7d71a61b2548.cyberfolks.host` (→ 93.157.102.234, the same server as
the database). Its document root already serves a different site, so the app
goes in a `realestate/` sub-directory rather than over the top of it.

**File Manager** (simplest): DirectAdmin → **File Manager** → the `public_html/`
that serves that technical domain → create `realestate/` → upload
`web-static.zip` into it → **Extract**. Delete the zip afterwards.

Make sure `.htaccess` actually landed — File Manager hides dotfiles by default
until you turn on *Show hidden files*. Without it, deep links 404.

**FTP** (WinSCP / FileZilla): host `cf9fdf5ccf7d71a61b2548.cyberfolks.host`,
credentials from DirectAdmin → *FTP Management*. Upload the **contents** of
`browser\` into `public_html/realestate/` — not the folder itself.

**rsync**, if your plan includes SSH — much better for repeat deploys:

```bash
rsync -avz --delete \
  real-estate-app/dist/real-estate-app/browser/ \
  USER@cf9fdf5ccf7d71a61b2548.cyberfolks.host:~/public_html/realestate/
```

`--delete` removes the previous release's hashed bundles. Without it the
directory grows with every deploy.

### Result

`https://cf9fdf5ccf7d71a61b2548.cyberfolks.host/realestate/`

The API must be told about that origin or every request from the page is
blocked by CORS — set `AllowedOrigins__Frontend` to
`https://cf9fdf5ccf7d71a61b2548.cyberfolks.host` (scheme + host, no
`/realestate/`, no trailing slash) wherever the API runs.

If you'd rather have `realestate.krzysztofklich.pl` served by the hosting,
create it under **Subdomain Management**, upload into its document root, and
rebuild with `-BaseHref /` — then also change `RewriteBase` in `.htaccess` to
`/`. The base href, the upload path and the `RewriteBase` must agree; a mismatch
is the usual cause of "CSS and JS 404 after deploy".

---

## 5. Alternative: real SSR via DirectAdmin's Node.js app

If your plan has **Node.js Selector / Setup Node.js App** (not on every
cyberFolks plan — check the panel, and note it typically runs Passenger), you can
run the SSR bundle on the hosting instead of the VPS.

1. Build the SSR bundle: `.\deploy\scripts\build-release.ps1 -SkipApi`
2. Upload the contents of `web.tar.gz` — the `browser/` and `server/` folders —
   into an application root such as `~/nodeapps/realestate/`, keeping that
   layout. `server.mjs` resolves its static folder as `../browser`; flatten the
   two folders together and the app boots but serves no assets.
3. In the panel create the app: Node **22**, application root
   `~/nodeapps/realestate`, application URL `/realestate`, startup file
   `server/server.mjs`.
4. There is no `npm install` step — the Angular build inlines Express and the
   Angular runtime into `server.mjs`.
5. Start the app; Passenger assigns the port itself and `server.ts` picks it up
   from `PORT`.

Caveats worth knowing before you commit to this:

- Passenger idles apps out and cold-starts them on the next request; the first
  hit after a quiet period is slow.
- Memory limits on shared plans are tight, and this SSR bundle is not small
  (`main.server.mjs` alone is ~540 kB, and the map chunk ~930 kB).
- No `journalctl` — you get whatever log file the panel exposes.

The VPS path (Option A) avoids all three. Use this only if you specifically want
the frontend on the hosting *and* need SSR.

---

## 6. Per-release routine

**Option A (everything on the VPS)** — the hosting is not involved at all:

```powershell
.\deploy\scripts\build-release.ps1
.\deploy\scripts\deploy-vps.ps1 -VpsHost root@VPS_IP
```

**Option B (static frontend here, API on the VPS):**

```powershell
# 1. API to the VPS
.\deploy\scripts\build-release.ps1 -SkipWeb
.\deploy\scripts\deploy-vps.ps1 -VpsHost root@VPS_IP -Target api

# 2. frontend to cyberFolks
.\deploy\scripts\build-release.ps1 -Static -SkipApi
#    then upload artifacts\web-static.zip and extract into public_html/realestate/
```

After either, walk the checklist in
[DEPLOYMENT.md §10](../DEPLOYMENT.md#10-verification-checklist) — in particular
**reload the browser directly on `/properties`**, which is what catches base
href and rewrite mistakes.

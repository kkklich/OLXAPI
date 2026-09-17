# deploy/

Server configuration and release tooling. The narrative guide is
[`../DEPLOYMENT.md`](../DEPLOYMENT.md) — start there.

This folder covers **both halves of the stack** even though it lives in the API
repository: the frontend is a separate repo (`github.com/kkklich/real-estate-app`)
that the scripts expect to find beside this one, or at `-WebDir <path>`. The
purely frontend pieces here — `shared-hosting/.htaccess`, `docker/Dockerfile.web`,
`env/realestate-ssr.env.example`, `systemd/realestate-ssr.service` — are kept
next to the API ones so one guide describes one deployment.

```
deploy/
├── CYBERFOLKS.md               hosting-panel steps (DirectAdmin, MySQL, FTP, DNS)
├── nginx/
│   └── realestate.conf         vhost: / -> SSR :4000, /api/ -> API :5016
├── systemd/
│   ├── realestate-api.service  .NET API unit
│   └── realestate-ssr.service  Angular SSR unit
├── env/
│   ├── realestate-api.env.example   -> /etc/realestate/api.env  (0600)
│   └── realestate-ssr.env.example   -> /etc/realestate/web.env  (0600)
├── scripts/
│   ├── setup-vps.sh            one-time server bootstrap (idempotent)
│   ├── build-release.ps1       build + pack artifacts   [run on Windows]
│   ├── deploy-vps.ps1          upload + activate        [run on Windows]
│   └── release.sh              unpack/switch/restart/rollback  [runs on VPS]
├── shared-hosting/
│   └── .htaccess               SPA routing + caching for cyberFolks
└── docker/
    ├── docker-compose.yml      alternative to systemd — pick one
    └── Dockerfile.web          Angular SSR image
```

## The two commands you'll actually use

```powershell
.\deploy\scripts\build-release.ps1
.\deploy\scripts\deploy-vps.ps1 -VpsHost root@VPS_IP
```

## Where things live on the server

| Path | What |
|---|---|
| `/opt/realestate/api/current` | symlink → active API release |
| `/opt/realestate/web/current` | symlink → active SSR release |
| `/opt/realestate/*/releases/` | last 5 releases, timestamped |
| `/etc/realestate/api.env` | connection string, CORS origin, scrape key |
| `/etc/realestate/web.env` | SSR port |
| `/etc/nginx/sites-available/realestate.conf` | vhost (certbot edits this) |

```bash
journalctl -u realestate-api -f
journalctl -u realestate-ssr -f
systemctl restart realestate-api realestate-ssr
```

## Nothing here contains secrets

The `.env.example` files carry placeholders only. Real values live in
`/etc/realestate/*.env` on the server, mode `0600`, and are never committed.
Before the first deploy, read
[DEPLOYMENT.md §3](../DEPLOYMENT.md#3-before-you-deploy-anything-security) —
the repository currently has live database credentials in its git history.

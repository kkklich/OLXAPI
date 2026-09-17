#!/usr/bin/env bash
#
# One-time VPS bootstrap for the Real Estate App (Ubuntu 22.04 / 24.04, Debian 12).
# Idempotent - safe to re-run.
#
#   bash setup-vps.sh
#
# Installs: ASP.NET Core 10 runtime, Node.js 22, nginx, certbot, ufw.
# Creates:  user "realestate", /opt/realestate/{api,web}, /etc/realestate.
#
# It does NOT install the vhost, the services or the env files - those are
# deliberate copy steps, see DEPLOYMENT.md section 4.

set -euo pipefail

APP_USER=realestate
APP_ROOT=/opt/realestate
ETC_DIR=/etc/realestate
NODE_MAJOR=22

log() { printf '\n\033[1;36m==> %s\033[0m\n' "$*"; }
warn() { printf '\033[1;33m[warn]\033[0m %s\n' "$*"; }

if [[ $EUID -ne 0 ]]; then
    echo "Run as root (sudo bash setup-vps.sh)" >&2
    exit 1
fi

# --------------------------------------------------------------- packages ---
log "Base packages"
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y -qq curl wget ca-certificates gnupg tar rsync ufw

# ------------------------------------------------------------------ .NET  ---
if command -v dotnet >/dev/null 2>&1 && dotnet --list-runtimes 2>/dev/null | grep -q 'Microsoft.AspNetCore.App 10\.'; then
    log ".NET 10 ASP.NET runtime already present - skipping"
else
    log "Installing ASP.NET Core 10 runtime"
    # dotnet-install.sh is used rather than the distro packages because .NET 10
    # is not in Ubuntu's own feed, and mixing the Ubuntu and Microsoft feeds is
    # a well-known source of broken dotnet installs.
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
    bash /tmp/dotnet-install.sh --channel 10.0 --runtime aspnetcore --install-dir /usr/share/dotnet
    ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet
    rm -f /tmp/dotnet-install.sh
fi
dotnet --list-runtimes | grep 'Microsoft.AspNetCore.App' || warn "ASP.NET runtime not detected"

# ------------------------------------------------------------------ Node  ---
if command -v node >/dev/null 2>&1 && [[ "$(node -v)" == v${NODE_MAJOR}.* ]]; then
    log "Node ${NODE_MAJOR} already present ($(node -v)) - skipping"
else
    log "Installing Node.js ${NODE_MAJOR}"
    curl -fsSL "https://deb.nodesource.com/setup_${NODE_MAJOR}.x" | bash -
    apt-get install -y -qq nodejs
fi
node -v

# The SSR bundle is self-contained (esbuild inlines express and the Angular
# runtime), so there is no npm install step on the server.

# ----------------------------------------------------------- nginx/certbot ---
log "nginx + certbot"
apt-get install -y -qq nginx certbot python3-certbot-nginx
systemctl enable --now nginx

# ------------------------------------------------------------------ user  ---
if id "$APP_USER" >/dev/null 2>&1; then
    log "User $APP_USER already exists"
else
    log "Creating service user $APP_USER"
    useradd --system --create-home --home-dir "/home/$APP_USER" --shell /usr/sbin/nologin "$APP_USER"
fi

# ------------------------------------------------------------------ dirs  ---
log "Directory layout under $APP_ROOT"
for part in api web; do
    install -d -o "$APP_USER" -g "$APP_USER" -m 0755 "$APP_ROOT/$part/releases"
    install -d -o "$APP_USER" -g "$APP_USER" -m 0755 "$APP_ROOT/$part/shared"
done
install -d -o "$APP_USER" -g "$APP_USER" -m 0750 "$ETC_DIR"

# --------------------------------------------------------------- firewall ---
log "Firewall (ufw): 22, 80, 443 only"
ufw allow OpenSSH        >/dev/null
ufw allow 'Nginx Full'   >/dev/null
# 4000 (SSR) and 5016 (API) stay closed - they are reached through nginx on
# loopback only.
if ! ufw status | grep -q '^Status: active'; then
    warn "Enabling ufw - make sure port 22 access works before you disconnect"
    ufw --force enable
fi
ufw status verbose

# ------------------------------------------------------------------ done  ---
cat <<EOF

$(log "Bootstrap complete")

Next (see DEPLOYMENT.md section 4):

  1. env files
       install -m 600 -o $APP_USER -g $APP_USER env/realestate-api.env.example $ETC_DIR/api.env
       install -m 600 -o $APP_USER -g $APP_USER env/realestate-ssr.env.example $ETC_DIR/web.env
       nano $ETC_DIR/api.env

  2. services
       cp systemd/*.service /etc/systemd/system/ && systemctl daemon-reload

  3. vhost
       cp nginx/realestate.conf /etc/nginx/sites-available/
       sed -i 's/realestate.example.com/YOUR.DOMAIN/g' /etc/nginx/sites-available/realestate.conf
       ln -sf /etc/nginx/sites-available/realestate.conf /etc/nginx/sites-enabled/
       rm -f /etc/nginx/sites-enabled/default
       nginx -t && systemctl reload nginx

  4. TLS
       certbot --nginx -d YOUR.DOMAIN

  5. push a release from your machine
       .\\deploy\\scripts\\build-release.ps1
       .\\deploy\\scripts\\deploy-vps.ps1 -VpsHost root@$(hostname -I | awk '{print $1}')

EOF

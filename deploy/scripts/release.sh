#!/usr/bin/env bash
#
# Install an uploaded release on the VPS. Normally invoked by deploy-vps.ps1,
# but fine to run by hand:
#
#   bash release.sh both          # default
#   bash release.sh api
#   bash release.sh web
#
# Expects the tarballs produced by build-release.ps1 to already be at:
#   /tmp/realestate-upload/api.tar.gz
#   /tmp/realestate-upload/web.tar.gz
#
# On a failed health check it puts the previous release back and exits non-zero,
# so a bad build does not leave the site down.

set -euo pipefail

TARGET="${1:-both}"
APP_USER=realestate
APP_ROOT=/opt/realestate
UPLOAD_DIR=/tmp/realestate-upload
KEEP_RELEASES=5
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"

log()  { printf '\n\033[1;36m==> %s\033[0m\n' "$*"; }
fail() { printf '\033[1;31m[fail]\033[0m %s\n' "$*" >&2; exit 1; }

[[ $EUID -eq 0 ]] || fail "Run as root (sudo bash release.sh)"

# Wait until something is listening on a loopback port.
wait_for_port() {
    local port=$1 tries=${2:-30}
    for ((i = 1; i <= tries; i++)); do
        if (exec 3<>"/dev/tcp/127.0.0.1/$port") 2>/dev/null; then
            exec 3<&- 3>&-
            return 0
        fi
        sleep 1
    done
    return 1
}

# unpack <part> <tarball> <service> <port>
unpack() {
    local part=$1 tarball=$2 service=$3 port=$4
    local dest="$APP_ROOT/$part/releases/$STAMP"
    local link="$APP_ROOT/$part/current"

    [[ -f "$tarball" ]] || fail "missing $tarball - did the upload step run?"

    log "$part: unpacking into $dest"
    install -d -o "$APP_USER" -g "$APP_USER" -m 0755 "$dest"
    tar -xzf "$tarball" -C "$dest"
    chown -R "$APP_USER:$APP_USER" "$dest"

    # Remember where we were, so a failed health check can go back.
    local previous=""
    [[ -L "$link" ]] && previous="$(readlink -f "$link")"

    log "$part: switching 'current' -> $STAMP"
    ln -sfn "$dest" "$link"

    log "$part: restarting $service"
    systemctl restart "$service"

    if wait_for_port "$port" 45 && systemctl is-active --quiet "$service"; then
        printf '\033[1;32m[ok]\033[0m %s is up on port %s\n' "$service" "$port"
    else
        printf '\033[1;31m[fail]\033[0m %s did not come up on port %s\n' "$service" "$port" >&2
        journalctl -u "$service" -n 40 --no-pager >&2 || true
        if [[ -n "$previous" && -d "$previous" ]]; then
            printf '\033[1;33m[rollback]\033[0m restoring %s\n' "$previous" >&2
            ln -sfn "$previous" "$link"
            systemctl restart "$service"
        fi
        exit 1
    fi

    # Keep the newest KEEP_RELEASES directories, drop the rest.
    log "$part: pruning old releases (keeping $KEEP_RELEASES)"
    # shellcheck disable=SC2012  # names are timestamps, ls sorting is exact here
    ls -1d "$APP_ROOT/$part/releases"/*/ 2>/dev/null \
        | sort -r | tail -n +$((KEEP_RELEASES + 1)) \
        | xargs -r rm -rf
}

case "$TARGET" in
    api)  unpack api "$UPLOAD_DIR/api.tar.gz" realestate-api 5016 ;;
    web)  unpack web "$UPLOAD_DIR/web.tar.gz" realestate-ssr 4000 ;;
    both)
        unpack api "$UPLOAD_DIR/api.tar.gz" realestate-api 5016
        unpack web "$UPLOAD_DIR/web.tar.gz" realestate-ssr 4000
        ;;
    *) fail "unknown target '$TARGET' (expected: api | web | both)" ;;
esac

# First deploy only: the units are not enabled until something is installed.
systemctl enable realestate-api realestate-ssr >/dev/null 2>&1 || true

# Only the tarballs - this script is still executing from $UPLOAD_DIR.
rm -f "$UPLOAD_DIR"/*.tar.gz

log "Release $STAMP live"
systemctl --no-pager --lines=0 status realestate-api realestate-ssr || true

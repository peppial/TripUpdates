#!/usr/bin/env bash
# Deploy TripUpdates to its Azure App Service (Linux, built-in .NET 10 runtime).
#
# The resource group, plan and web app were created by hand in the portal; this script
# only publishes, packs and pushes, and enforces the few settings the app depends on.
#
#   ./deploy.sh
#   APP_NAME=tripupdates-staging ./deploy.sh
#
# The target subscription lives in deploy.env, which is not committed — a subscription id is a
# precise target for support-impersonation, and this repo is meant to be publishable.
#
#   echo 'SUBSCRIPTION=<id>' > deploy.env

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=/dev/null
[ -f "$repo_root/deploy.env" ] && . "$repo_root/deploy.env"

RESOURCE_GROUP="${RESOURCE_GROUP:-TripUpdates}"
APP_NAME="${APP_NAME:-tripupdates}"

if [ -z "${SUBSCRIPTION:-}" ]; then
  echo "No subscription set. Put it in deploy.env:" >&2
  echo "  echo 'SUBSCRIPTION=<id>' > $repo_root/deploy.env" >&2
  echo "Available:" >&2
  az account list --query "[?state=='Enabled'].{name:name,id:id}" -o table >&2 || true
  exit 1
fi

# The static GTFS feed is 19 MB. /home is the only path App Service persists across
# restarts, so caching there means it is not re-downloaded on every boot.
CACHE_DIR="${CACHE_DIR:-/home/cache}"

project="$repo_root/TripUpdates/TripUpdates.csproj"

command -v az >/dev/null || { echo "azure-cli is not installed: brew install azure-cli" >&2; exit 1; }
command -v dotnet >/dev/null || { echo "dotnet sdk is not installed" >&2; exit 1; }

az account show --output none 2>/dev/null || { echo "Not signed in. Run: az login" >&2; exit 1; }

# az takes global flags after the command group, not before it, so append rather than prepend.
az() { command az "$@" --subscription "$SUBSCRIPTION"; }

az webapp show -g "$RESOURCE_GROUP" -n "$APP_NAME" --output none 2>/dev/null || {
  echo "No web app '$APP_NAME' in resource group '$RESOURCE_GROUP' of subscription $SUBSCRIPTION." >&2
  exit 1
}

echo "==> Target $APP_NAME in $RESOURCE_GROUP"

# Merged into whatever is already there — the portal's Application Insights settings survive.
echo "==> Settings"
az webapp config appsettings set -g "$RESOURCE_GROUP" -n "$APP_NAME" --settings \
    "TripUpdates__Feed__CacheDirectory=$CACHE_DIR" \
    ASPNETCORE_ENVIRONMENT=Production \
    SCM_DO_BUILD_DURING_DEPLOYMENT=false \
  --output none

# Always On keeps the 30s poller alive; without it the app unloads when idle and the
# next visitor waits out a fresh 19 MB feed download.
az webapp config set -g "$RESOURCE_GROUP" -n "$APP_NAME" \
  --always-on true \
  --startup-file "dotnet TripUpdates.dll" \
  --output none

# Android requires HTTPS for PWA install and service workers.
az webapp update -g "$RESOURCE_GROUP" -n "$APP_NAME" --https-only true --output none

staging="$(mktemp -d)"
trap 'rm -rf "$staging"' EXIT

echo "==> Publishing"
dotnet publish "$project" -c Release -o "$staging/app" --nologo

# The zip must hold the published files at its root, not under a wrapper directory.
echo "==> Packing"
(cd "$staging/app" && zip -qr "$staging/app.zip" .)

echo "==> Deploying"
az webapp deploy -g "$RESOURCE_GROUP" -n "$APP_NAME" \
  --src-path "$staging/app.zip" --type zip --output none

url="https://$(az webapp show -g "$RESOURCE_GROUP" -n "$APP_NAME" --query defaultHostName -o tsv)"

echo "==> Waiting for $url"
for _ in $(seq 1 30); do
  code="$(curl -s -o /dev/null -w '%{http_code}' --max-time 10 "$url/api/arrivals" || true)"
  [ "$code" = "200" ] && { echo "==> Live: $url"; exit 0; }
  sleep 5
done

echo "Deployed, but $url/api/arrivals did not return 200 (last: ${code:-no response})." >&2
echo "Logs: az webapp log tail --subscription $SUBSCRIPTION -g $RESOURCE_GROUP -n $APP_NAME" >&2
exit 1

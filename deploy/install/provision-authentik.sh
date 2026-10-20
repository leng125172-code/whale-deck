#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
rotate=false
if [[ ${1:-} == --rotate-token ]]; then
  rotate=true
  shift
fi
output=${1:-}
[[ -n $output && $output == /* ]] || { echo 'Usage: provision-authentik.sh /absolute/private-output.env' >&2; exit 2; }
docker inspect database-platform-authentik-server >/dev/null 2>&1 || { echo 'Authentik server is not running.' >&2; exit 1; }

temporary=$(mktemp "${output}.tmp.XXXXXX")
trap 'rm -f -- "$temporary"' EXIT
docker exec -e "WHALEDECK_ROTATE_API_TOKEN=$rotate" -i database-platform-authentik-server ak shell --no-startup --no-imports \
  < "$script_dir/provision-authentik.py" 2>/dev/null \
  | grep '^WHALEDECK_' > "$temporary"

for key in \
  WHALEDECK_OIDC_INTERNAL_AUTHORITY WHALEDECK_OIDC_INTERNAL_CLIENT_ID WHALEDECK_OIDC_INTERNAL_CLIENT_SECRET \
  WHALEDECK_OIDC_EXTERNAL_AUTHORITY WHALEDECK_OIDC_EXTERNAL_CLIENT_ID WHALEDECK_OIDC_EXTERNAL_CLIENT_SECRET \
  WHALEDECK_AUTHENTIK_API_TOKEN WHALEDECK_AUTHENTIK_ADMIN_GROUP_ID; do
  grep -q "^${key}='[^']\+'$" "$temporary" || { echo "Authentik did not produce $key." >&2; exit 1; }
done
docker exec -e WHALEDECK_COMPLETE_SETUP=true -i database-platform-authentik-server ak shell --no-startup --no-imports \
  < "$script_dir/complete-authentik-setup.py" 2>/dev/null \
  | grep '^AUTHENTIK_SETUP='
chmod 0600 "$temporary"
mv -T "$temporary" "$output"
trap - EXIT
python3 "$script_dir/sync-authentik-presentation.py" --env-file "$output"
python3 "$script_dir/sync-authentik-branding.py" --env-file "$output" --apply
echo "Whale Deck Authentik applications and API token are ready; private values were written to $output."

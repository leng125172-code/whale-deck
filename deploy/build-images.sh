#!/usr/bin/env bash
# Build only the frozen public commit. Private files remain outside Docker's context.
set -Eeuo pipefail
export GIT_OPTIONAL_LOCKS=0
repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
git_source() { git -c "safe.directory=$repo_root" -C "$repo_root" "$@"; }
[[ -z $(git_source status --porcelain) ]] || { echo 'Build requires a clean committed checkout.' >&2; exit 2; }
source_revision=$(git_source rev-parse HEAD)
[[ $source_revision =~ ^[a-f0-9]{40}$ ]] || exit 2
[[ -z ${WHALEDECK_BUILD_REVISION:-} || $WHALEDECK_BUILD_REVISION == "$source_revision" ]] || {
  echo 'Requested build revision does not match the committed checkout.' >&2; exit 2;
}
export WHALEDECK_BUILD_REVISION=$source_revision
[[ -r $repo_root/.env && -r $repo_root/.images.env ]] || {
  echo 'Private .env and immutable .images.env must be prepared first.' >&2; exit 2;
}
for key in DOTNET_SDK_IMAGE ASPNET_IMAGE DOTNET_RUNTIME_IMAGE NODE_IMAGE NGINX_IMAGE; do
  image=$(awk -F= -v key="$key" '$1 == key {print substr($0, index($0, "=") + 1); exit}' "$repo_root/.images.env")
  [[ $image =~ @sha256:[a-f0-9]{64}$ ]] || { echo "$key must use an immutable digest." >&2; exit 2; }
done
install -d -m 0700 "$repo_root/deploy/artifacts"
frozen_source=$(mktemp -d "$repo_root/deploy/artifacts/image-source.XXXXXX")
cleanup() {
  [[ $frozen_source == "$repo_root/deploy/artifacts/image-source."* && -d $frozen_source && ! -L $frozen_source ]] || return 1
  rm -rf -- "$frozen_source"
}
trap cleanup EXIT
git_source archive "$source_revision" | tar -xf - -C "$frozen_source"
[[ ! -e $frozen_source/.env && ! -e $frozen_source/.images.env && ! -e $frozen_source/.git ]] || exit 2
docker compose --project-directory "$frozen_source" \
  --env-file "$repo_root/.env" --env-file "$repo_root/.images.env" \
  -f "$frozen_source/compose.yml" --profile tools build --pull
[[ $(git_source rev-parse HEAD) == "$source_revision" && -z $(git_source status --porcelain) ]] || {
  echo 'Checkout changed during the build; do not deploy these images.' >&2; exit 2;
}
printf 'Built Whale Deck source revision %s\n' "$source_revision"

#!/usr/bin/env bash
set -Eeuo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
runtime_id="${WHALEDECK_RUNTIME_ID:-linux-x64}"
requested_output=${1:-}

command -v docker >/dev/null 2>&1 || { echo 'Docker is required.' >&2; exit 1; }
command -v python3 >/dev/null 2>&1 || { echo 'Python 3 is required for runtime servicing validation.' >&2; exit 1; }
command -v git >/dev/null 2>&1 || { echo 'Git is required to freeze public build inputs.' >&2; exit 1; }
images_env="$repo_root/.images.env"
[[ -r $images_env ]] || { echo "Missing $images_env." >&2; exit 1; }
sdk_image=$(awk -F= '$1 == "DOTNET_SDK_IMAGE" {print substr($0, index($0, "=") + 1); exit}' "$images_env")
[[ $sdk_image =~ @sha256:[a-f0-9]{64}$ ]] || { echo 'DOTNET_SDK_IMAGE must be pinned to an immutable digest.' >&2; exit 1; }

install -d -m 0700 "$repo_root/deploy/artifacts"
if [[ -n $requested_output ]]; then
  if [[ $requested_output = /* ]]; then
    artifacts_root=$requested_output
  else
    artifacts_root="$repo_root/$requested_output"
  fi
  [[ ! -e $artifacts_root ]] || { echo "Output already exists: $artifacts_root" >&2; exit 2; }
  install -d -m 0700 "$artifacts_root"
else
  artifacts_root=$(mktemp -d "$repo_root/deploy/artifacts/host-publish.XXXXXX")
fi

source_revision=$(git -c "safe.directory=$repo_root" -C "$repo_root" rev-parse HEAD)
[[ $source_revision =~ ^[a-f0-9]{40}$ ]] || exit 2
public_source=$(mktemp -d "$repo_root/deploy/artifacts/host-source.XXXXXX")
cleanup_source() {
  # Only this invocation's verified mktemp child may be removed, never the checkout.
  [[ $public_source == "$repo_root/deploy/artifacts/host-source."* && -d $public_source && ! -L $public_source ]] || return 1
  rm -rf -- "$public_source"
}
trap cleanup_source EXIT
git -c "safe.directory=$repo_root" -C "$repo_root" archive "$source_revision" | tar -xf - -C "$public_source"

docker run --rm \
  --mount "type=bind,src=$public_source,dst=/src,readonly" \
  --mount "type=bind,src=$artifacts_root,dst=/out" \
  --workdir /src \
  --env DOTNET_CLI_HOME=/tmp/dotnet \
  --env WHALEDECK_BUILD_REVISION="$source_revision" \
  "$sdk_image" sh -ec '
    test ! -e /src/.env
    test ! -e /src/.images.env
    dotnet publish src/WhaleDeck.Agent/WhaleDeck.Agent.csproj \
      --configuration Release --runtime '"$runtime_id"' --self-contained true \
      --artifacts-path /tmp/artifacts/agent --output /out/agent \
      -p:SourceRevisionId="$WHALEDECK_BUILD_REVISION"
    dotnet publish src/WhaleDeck.MaintenanceHost/WhaleDeck.MaintenanceHost.csproj \
      --configuration Release --runtime '"$runtime_id"' --self-contained true \
      --artifacts-path /tmp/artifacts/maintenance --output /out/maintenance \
      -p:SourceRevisionId="$WHALEDECK_BUILD_REVISION"
  '

python3 "$repo_root/deploy/host/validate-runtime.py" "$artifacts_root"

(
  cd "$artifacts_root"
  find agent maintenance -type f -print0 \
    | sort -z \
    | xargs -0 sha256sum > SHA256SUMS
)
chmod 0600 "$artifacts_root/SHA256SUMS"
printf 'Host artifacts published to %s\n' "$artifacts_root"

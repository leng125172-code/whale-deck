#!/usr/bin/env bash
set -Eeuo pipefail
repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
sdk_image=$(awk -F= '$1 == "DOTNET_SDK_IMAGE" {print substr($0, index($0, "=") + 1); exit}' "$repo_root/.images.env")
[[ $sdk_image =~ @sha256:[a-f0-9]{64}$ ]] || exit 2
source_revision=$(git -C "$repo_root" rev-parse HEAD)
[[ $source_revision =~ ^[a-f0-9]{40}$ ]] || exit 2
install -d -m 0700 "$repo_root/deploy/artifacts"
probe_output=$(mktemp -d "$repo_root/deploy/artifacts/terminal-probe.XXXXXX")
# Build only tracked frozen source. Never mount the workstation checkout: it
# also contains ignored private environment files, not just source code.
git -C "$repo_root" archive "$source_revision" | docker run --rm --interactive \
  --mount "type=bind,src=$probe_output,dst=/out" \
  --env "WHALEDECK_BUILD_REVISION=$source_revision" \
  --workdir /tmp --env DOTNET_CLI_HOME=/tmp/dotnet \
  "$sdk_image" sh -ec 'mkdir /tmp/src; tar -xf - -C /tmp/src; cd /tmp/src;
    dotnet publish deploy/validation/TerminalProbe/TerminalProbe.csproj \
      --configuration Release --runtime linux-x64 --self-contained true \
      --artifacts-path /tmp/artifacts/probe --output /out \
      -p:SourceRevisionId="$WHALEDECK_BUILD_REVISION"'
(cd "$probe_output"; find . -type f ! -name SHA256SUMS -print0 | sort -z | xargs -0 sha256sum > SHA256SUMS)
chmod 0600 "$probe_output/SHA256SUMS"
printf 'Terminal probe published to %s\n' "$probe_output"

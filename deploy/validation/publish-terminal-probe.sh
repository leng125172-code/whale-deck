#!/usr/bin/env bash
set -Eeuo pipefail
repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
sdk_image=$(awk -F= '$1 == "DOTNET_SDK_IMAGE" {print substr($0, index($0, "=") + 1); exit}' "$repo_root/.images.env")
[[ $sdk_image =~ @sha256:[a-f0-9]{64}$ ]] || exit 2
install -d -m 0700 "$repo_root/deploy/artifacts"
probe_output=$(mktemp -d "$repo_root/deploy/artifacts/terminal-probe.XXXXXX")
# Only source and its own output are mounted; no Docker/Agent socket or key.
docker run --rm \
  --mount "type=bind,src=$repo_root,dst=/src,readonly" \
  --mount "type=bind,src=$probe_output,dst=/out" \
  --workdir /src --env DOTNET_CLI_HOME=/tmp/dotnet \
  "$sdk_image" dotnet publish deploy/validation/TerminalProbe/TerminalProbe.csproj \
    --configuration Release --runtime linux-x64 --self-contained true \
    --artifacts-path /tmp/artifacts/probe --output /out
(cd "$probe_output"; find . -type f ! -name SHA256SUMS -print0 | sort -z | xargs -0 sha256sum > SHA256SUMS)
chmod 0600 "$probe_output/SHA256SUMS"
printf 'Terminal probe published to %s\n' "$probe_output"

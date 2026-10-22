#!/usr/bin/env bash
# Retry only the observed pinned pnpm binary download timeout, never tests,
# dependency audits, lockfile mismatches, or arbitrary installation failures.
set -euo pipefail

for attempt in 1 2 3; do
  install_log=$(mktemp)
  if corepack pnpm install --frozen-lockfile >"$install_log" 2>&1; then
    install_status=0
  else
    install_status=$?
  fi
  cat "$install_log"
  retry_download=false
  if grep -Eq '^Could not download the pnpm [0-9]+\.[0-9]+\.[0-9]+ binary: .*timed out after [0-9]+s$' "$install_log"; then
    retry_download=true
  fi
  rm -- "$install_log"
  if (( install_status == 0 )); then exit 0; fi
  if [[ "$retry_download" != true ]] || (( attempt == 3 )); then exit "$install_status"; fi
  printf '::warning::Pinned pnpm binary download timed out; retry %s of 2.\n' "$attempt"
  sleep "$((attempt * 5))"
done

#!/usr/bin/env bash
# A skipped, cancelled, failed or missing matrix result must never look green.
set -euo pipefail
[[ ${FRONTEND_CHECKS_RESULT:-} == success && ${FRONTEND_BROWSER_RESULT:-} == success ]] || {
  echo 'Frontend preflight and every browser shard must succeed.' >&2
  exit 1
}

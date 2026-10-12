# Container usage acceptance inventory

Scope: continuous CPU/memory usage in container cards and table, batch Agent sampling, configurable page refresh. Complete resource relationships and application parameter editing remain separate work.

| Claim/control | Functional check | Visual evidence |
| --- | --- | --- |
| Cards/table CPU and memory | Real Engine counters; Docker-compatible CPU and cache subtraction; 100% is one logical core | Populated cards/table, CPU >100%, warmup, 320/760/1045/1440px light/dark |
| Resource tooltip | Hover and keyboard focus show cumulative network/block I/O, PID count and sample time | Dense desktop tooltip and mobile viewport bounds |
| Manual / 6s / 30s / 60s refresh | Full cycle, default 6s, manual remains usable; no overlap | No full-panel loading overlay on background refresh |
| Background tab | Hidden tab does not poll, foreground resumes; leaving page cancels timer/request | State/filter/selection remain intact on return |
| Unknown/stale/failure | Missing stays missing, stopped never fabricated zero, individual timestamps checked, 18s freshness bound | Explicit status/warning, no old values presented as live |
| Permission loss and late details | 401/403 cancels refresh/clears private state; older detail response cannot repopulate cleared view | Administrator data disappears |
| Core protection | Batch selection excludes protected rows; added resource view has no lifecycle actions | Existing protected container tests still pass |

Exploratory cases: rapidly switch refresh/mode/search and resize while populated; hide and return; simulate 503/403 and delayed details; restart/delete a uniquely named temporary container during sampling. Native Agent collector: 6s interval, total cycle budget 5s, max 8 concurrent reads, bounded 256 rows with explicit truncated/skipped metadata. Requests only read cached statistics, cannot alter CPU baselines or cause native sampling fan-out. No successful-sample Info logs. Systemd/Docker failure makes cached batches stale; no unbounded stream or polling retry.

Statistics references: [Docker Engine API](https://docs.docker.com/reference/api/engine/version/v1.47/) and [pinned Docker.DotNet parameters](https://github.com/dotnet/Docker.DotNet/blob/v3.125.15/src/Docker.DotNet/Models/ContainerStatsParameters.Generated.cs). Native calls use `stream=false` and `one-shot=true`; counters are differenced by the Agent, not by each browser. Network/block I/O fields are cumulative byte counters, not throughput.

## Local signoff (2026-10-12)

- Release: 0 warnings/errors; 263 unit + 60 integration tests; 39 Vitest tests; all 43 browser regressions passed. TypeScript, lint, formatting and production frontend build passed.
- Headed interactive walkthrough covered manual refresh, pause/resume refresh, search/clear, card/table switching, theme and 320/760/1045/1440px resize. Tooltip checked with hover and keyboard focus. Automated off-happy checks cover slow/nonoverlapping requests, hidden/unmounted views, 503 and 403.
- Independent viewport screenshots reviewed for populated cards/table and tooltips in both themes. Narrow tables intentionally scroll inside their panel; default cards stay readable. Mobile core titles, status and primary actions fit. No page overflow or tooltip-blocked controls remain. At 320px with a native 15px scrollbar, body width now follows the 305px client area and the theme switch fits (right edge 295px). Headless numeric viewport tests alone did not catch the old minimum-width clipping.
- Captures are generated in `web/test-results/container-usage-*.png` and `container-usage-table-*.png` (ignored test artifacts). Owned browser and Vite 5175 session were closed after the walkthrough. Native workstation validation is a separate gate; local fixtures do not prove Docker or PostgreSQL integration.

## Native signoff (2026-10-12)

Runtime `8679e78`, validation script `3423b23`: 11 canonical rows, six-second native sampling, isolated 0.25-core/32 MiB container CPU/native comparison, cache-adjusted memory accounting, PostgreSQL CPU/memory history, stopped null values and restart recovery passed. Cleanup uses stop followed by a planned delete with canonical `timeoutSeconds`/`preserveVolumes` fields and exact container ID. The first script used an unsupported extra delete parameter, causing plan rejection; that exact leftover was inspected and removed without volume deletion, and the corrected full run passed including cleanup. Anonymous 401/ordinary 403 checks passed and wrapper-managed users were removed. Core protection, 11-container/8-application management view, OIDC/CSRF/full logout and historical telemetry (75 completed native SQL buckets) regressions passed; recent ten-minute Worker cycle error count was zero.

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

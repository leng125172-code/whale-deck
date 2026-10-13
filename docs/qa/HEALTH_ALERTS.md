# Host load / systemd / Docker health alerts

## Implementation

- Agent 1.16 adds `host.load-average`: canonical six-second `/proc/loadavg` 1/5/15 minute task counts plus `load.normalized = load1 / logical cores * 100`. Missing/malformed data has `Unavailable` quality, not valid zero. Load is not CPU busy time and can exceed 100%.
- `LoadAverage` rules use the normalized metric, same coverage/freshness/critical/recovery policies as existing metric rules; thresholds 0–1000%. `SystemdHealth` reads registered service/timer state. Systemd output is parsed by named properties, never output order, with bounded reads.
- ContainerHealth observes both process state and native Docker status: unhealthy triggers even when running; starting/unknown/stale do not clear risk; no-healthcheck running is labelled explicitly, not called healthy. The authoritative Docker status overwrites same-named user labels in discovery metadata. Registered database resources also carry the native status.
- Worker persists registered systemd resources. A separate hosted six-second evaluator uses a fresh database scope and no Agent dependency, so collection/dispatch failure cannot stop AgentOffline evaluation. Advisory lock, unique fingerprint and optimistic concurrency remain. Repeated collection/evaluation failure logs are limited to one per minute per loop.
- Existing generic metric tables store new kinds; no model/schema change, no extra TCP port or Agent permission. Ordinary-user permissions are unchanged; rule updates remain CSRF/Origin-protected, optimistic, audited.
- Frontend adds load/systemd rules, >100% load thresholds, semantics and state-specific explanations. External notifications/read states are still pending, and this does not complete all P0-07 or P0–P5.

## Local and isolated evidence

Release: 438 unit / 62 existing integration passed, three native PostgreSQL tests explicitly skipped locally. Frontend: 74 unit, TypeScript, lint, Prettier, build passed. Eleven alert/telemetry browser regressions passed on 320/1045/1440px light/dark; load >100% threshold and systemd nonpercentage forms are exercised. The first expanded browser test tried to click the hidden Element Plus input instead of its wrapper and timed out; corrected to the wrapper without loosening the timeout, then all eleven passed.

`validate_monitoring_alerts_postgres.py` passed against isolated PostgreSQL from source `3c26e13`: exact one executed/passed native Fact; migration unchanged, selectors/coverage/hysteresis/optimistic updates/audit/unique fingerprint still pass. Added assertions prove load Critical→Recovered, running-unhealthy→starting Unknown (risk retained)→healthy Recovered, failed→active systemd recovery, and actual hosted AgentOffline evaluator using a DI Agent factory that throws if queried. Temporary SDK, database and private scratch files cleaned. This proves the independent hosted evaluator and real SQL persistence, not a real production Agent shutdown.

## Workstation acceptance

Runtime `3c26e13` published in place through scheduling job `d5a54d2f-990e-46a8-809c-494c40f113f7`. Actual update service ended `inactive/dead`, `Result=success`, `ExecMainStatus=0`; eleven core containers healthy, Agent/Maintenance active. Agent protocol 1.16 with `host.load-average` verified through the API. user sudo -n retained; Agent permissions unchanged.

`run_with_ephemeral_admin.py validate_health_alerts.py` passed: actual Authentik Cookie/CSRF, 32 logical processors, four same-batch load kinds and persisted live history, normalized arithmetic, three registered units compared against native systemctl named properties, eleven native Docker health suffixes. A temporary scoped Info load rule triggered with valid coverage, then explicit disable yielded Recovered/RuleDisabled; its history is deliberately retained, not described as deleted. Ephemeral user was cleaned. Production failure states were not manufactured: zero load generation, saturation, Agent/service stop or metric rewrites. The preceding isolated native Fact is the failure/offline proof.

Readable-unit real preflight was repeated on this runtime: zero dangling images/0 B, image/volume/container inventories unchanged, no cleanup job. Initial two publication attempts failed at GitHub TLS fetch **before** job submission; bounded per-command retry succeeded without changing permanent proxy configuration or restarting an active update.

Notification routing/read state, external delivery, long-running log-pressure and broader outage combinations remain. P0-07 and the entire 76-task objective are not complete.

## Review

GPT performed state parsing, independent-loop design, permission review, implementation and test review. The preceding unit-formatting work used the approved GLM Flash read-only review; its token/latency/cost evidence is in [display units](DISPLAY_UNITS.md). No delegated architecture/security decision or Agent privilege change.

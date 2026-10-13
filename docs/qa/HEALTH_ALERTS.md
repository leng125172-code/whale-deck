# Host load / systemd / Docker health alerts

## Implementation

- Agent 1.16 adds `host.load-average`: canonical six-second `/proc/loadavg` 1/5/15 minute task counts plus `load.normalized = load1 / logical cores * 100`. Missing/malformed data has `Unavailable` quality, not valid zero. Load is not CPU busy time and can exceed 100%.
- `LoadAverage` rules use the normalized metric, same coverage/freshness/critical/recovery policies as existing metric rules; thresholds 0–1000%. `SystemdHealth` reads registered service/timer state. Systemd output is parsed by named properties, never output order, with bounded reads.
- ContainerHealth observes both process state and native Docker status: unhealthy triggers even when running; starting/unknown/stale do not clear risk; no-healthcheck running is labelled explicitly, not called healthy. The authoritative Docker status overwrites same-named user labels in discovery metadata. Registered database resources also carry the native status.
- Worker persists registered systemd resources. A separate hosted six-second evaluator uses a fresh database scope and no Agent dependency, so collection/dispatch failure cannot stop AgentOffline evaluation. Advisory lock, unique fingerprint and optimistic concurrency remain. Repeated collection/evaluation failure logs are limited to one per minute per loop.
- Existing generic metric tables store new kinds; no model/schema change, no extra TCP port or Agent permission. Ordinary-user permissions are unchanged; rule updates remain CSRF/Origin-protected, optimistic, audited.
- Frontend adds load/systemd rules, >100% load thresholds, semantics and state-specific explanations. External notifications/read states are still pending, and this does not complete all P0-07 or P0–P5.

## Evidence pending

Local build/tests, isolated native PostgreSQL lifecycle/independent evaluator, browser and real workstation validation will be appended after completion. Production failure states are not manufactured: no CPU load generation, saturation, Agent/service stop or metric rewrites. Isolated native tests cover unhealthy/starting/healthy, failed/active systemd, load critical/recovery and actual hosted AgentOffline evaluation with an unusable Agent factory.

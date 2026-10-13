# Monitoring alert lifecycle — P0-07

## Implemented semantics

- CPU/memory/disk window averages use one grouped raw query per rule with bounded time predicates. Raw data remains necessary for fresh partial windows; completed historical rollups are not substituted for current alert observations.
- Only finite Good/Idle observations contribute. A latest invalid observation, stale telemetry (>18 seconds) or coverage below the configurable 50–100% minimum is Unknown, not healthy zero. Expected samples derive from the six-second collection interval.
- Resource selectors accept `resourceIds` (stable UUID or exact external ID) and metric-only `deviceIds` (exact DeviceId); empty `{}` means all. Unsupported fields, duplicates, non-object/oversized JSON and invalid identifiers are rejected before persistence.
- Utilization thresholds accept `operator: >=`, `value`, optional `criticalValue` and `recoveryValue`, `minimumCoveragePercent` (default 80). Percentages 0–100; recovery <= trigger <= critical. State-only rules do not use metric hysteresis.
- Each resource has one active fingerprint across Active/Acknowledged/Silenced. Any selected disk risk triggers the resource; all selected disk observations must be healthy to recover. Unknown never clears an existing risk.
- Severity escalation reopens acknowledged risk; silence expiry reactivates even if telemetry becomes Unknown, without inventing another occurrence. Counts increment at most once per evaluation window. Disable or explicit rule scope/type changes close old risk; malformed legacy rules hold risk without aborting other rules.
- PostgreSQL partial unique fingerprint index and optimistic event Version protect duplicate creation and administrator/evaluator races. Generic evaluators use a transaction advisory lock; failures retain state for retry on the next normal Worker cycle.
- Rule form has direct threshold/coverage controls and bounded internal scrolling. Events expose measured average/coverage or explicit Unknown reason. Backup capacity policy controls remain separate and unchanged.

## Verification before remote acceptance

- Release tests: 399 Unit + 62 existing Integration passed; 3 native PostgreSQL Facts explicitly skipped locally. They are not real-dependency proof.
- Frontend build/type/lint and 44 unit tests passed. 16 browser tests passed (4 new threshold/quality regressions and 12 existing backup capacity tests); mobile dark and desktop light screenshots inspected. Fixtures do not prove production authentication/alert execution.
- Production read-only migration preflight found zero duplicate active fingerprints. No production saturation, high CPU load or metric rewriting was used.
- `NativeMonitoringAlertsTests` and `validate_monitoring_alerts_postgres.py` provision a disposable namespaced database using real PostgreSQL/EF migrations. Native acceptance and deployment will be recorded after execution; source generation alone is not acceptance.

## Remaining broader scope

This does not complete P0-07: load-average/systemd-health rules, full notification delivery/routing/read state, long-running log-pressure tests and broader outage combinations remain. ContainerHealth currently consumes discovered running-state snapshots, not Docker's distinct healthcheck status. No external notification or production service disruption is enabled by this change.

## Assistance

Read-only GLM `glm-5.3-flash` review through the approved router: one successful call, 146765 ms, input 350523 tokens (307712 cached), output 3868, cost unavailable. GPT verified selectors, silence and escalation gaps, chose/implemented policy and concurrency rules and reviewed all changes. The review's claim that nullable Average necessarily throws on empty samples and that silenced fingerprints always duplicate was not adopted; neither was established by the existing code.

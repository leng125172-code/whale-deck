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
- Rule save, acknowledge and silence capture the authenticated subject, HTTP trace and source IP and insert unified audit in the same SaveChanges transaction as the state. Audit contains action/target only, no rule body. Required actor/trace are validated in Application; controller only supplies request context.

## Verification before remote acceptance

- Final Release tests: 402 Unit + 62 existing Integration passed; 3 native PostgreSQL Facts explicitly skipped locally. They are not real-dependency proof.
- Frontend build/type/lint and 44 unit tests passed. 16 browser tests passed (4 new threshold/quality regressions and 12 existing backup capacity tests); mobile dark and desktop light screenshots inspected. Fixtures do not prove production authentication/alert execution.
- Production read-only migration preflight found zero duplicate active fingerprints. No production saturation, high CPU load or metric rewriting was used.
- `NativeMonitoringAlertsTests` and `validate_monitoring_alerts_postgres.py` provision a disposable namespaced database using real PostgreSQL/EF migrations. Native lifecycle passed at `8535884`, `e998a3f` and audit revision `af4e92b`; the last includes three atomic acknowledgement/silence audit records. Each TRX reported exactly one executed/passed Fact, no skipped/incomplete test, followed by exact container/database cleanup. Backup capacity native regression also passed at `8535884` and audit revision `af4e92b`.
- Latest UI clear-optional-threshold regression passed in all four viewport/theme combinations. A further 31 layout/search/telemetry regressions passed (47 distinct browser tests across the two selected suites, plus four repeated after the optional-field change). The later `57a94d3` change only wraps malformed JSON parsing as ArgumentException and normalizes optional frontend fields; evaluator SQL, migration and atomic audit logic are unchanged from the native audit proof. Unit and real HTTP rejection tests cover the final malformed-input change.

## Workstation acceptance

Runtime `57a94d3` published in place. Scheduling job `d54b76d7-52f9-41ad-88c2-264d50a1b78e` completed, followed by actual update unit `inactive/dead`, `Result=success`, `ExecMainStatus=0`. Eleven core containers healthy; Agent/MaintenanceHost active; user sudo -n still succeeds without changing the Agent grant.

`run_with_ephemeral_admin.py validate_monitoring_alerts.py` passed against real Authentik/API/Worker: anonymous 401, real ordinary-user 403 for events/rules/history, administrator Cookie/CSRF; malformed JSON/operator/unknown selector return 400 with trace; one real selected host/device event with valid coverage, acknowledge, silence, Critical upgrade without duplicate or losing silence, explicit disable/recovery and complete transition history. Unified rule/acknowledge/silence audit contains actual target and request trace. Validation rule is disabled and its event Recovered; history/audit are retained intentionally. Both ephemeral users were cleaned by the wrapper; native SDK absence rechecked. No metric rewriting, production saturation or container lifecycle operation was used for this live proof.

Login HTML available, recent five-minute bounded Worker logs (eight lines inspected) contain zero Error/Critical records. This is a short post-deploy check, not long-running log-pressure proof. Source and runtime are recorded separately; final documentation-only commits do not imply a further rebuild.

## Remaining broader scope

Later runtime `3c26e13` implements load-average/systemd and distinct native Docker health-state alerts plus an independent hosted offline evaluator. Its local, isolated PostgreSQL and real workstation evidence is recorded separately in [health alerts](HEALTH_ALERTS.md); the following paragraph describes the earlier `57a94d3` boundary, not the latest implementation.

This does not complete P0-07: load-average/systemd-health rules, full notification delivery/routing/read state, long-running log-pressure tests and broader outage combinations remain. ContainerHealth currently consumes discovered running-state snapshots, not Docker's distinct healthcheck status. No external notification or production service disruption is enabled by this change.

## Assistance

Read-only GLM `glm-5.3-flash` review through the approved router: one successful call, 146765 ms, input 350523 tokens (307712 cached), output 3868, cost unavailable. GPT verified selectors, silence and escalation gaps, chose/implemented policy and concurrency rules and reviewed all changes. The review's claim that nullable Average necessarily throws on empty samples and that silenced fingerprints always duplicate was not adopted; neither was established by the existing code.

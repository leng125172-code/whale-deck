# Display units / 2026-10-13

## Policy and changes

- Capacities and byte rates use **1024** and `B / KiB / MiB / GiB / TiB / PiB / EiB`; network link speed remains **1000** and `bps / Kbps / Mbps / Gbps / Tbps` (bits, not bytes).
- `16131004906` bytes displays as **15.02 GiB**. UI capacity/rate display shares `web/src/services/displayUnits.ts`; Agent-generated cleanup plans use invariant-culture binary formatting. Charts retain their numeric MiB/s scaling and now label it accurately; IOPS and milliseconds are not scaled as bytes.
- Applies to image/volume lists, host disks/network counters, process/container memory and I/O, application backups, overview totals, backup-capacity alerts, CPU accumulation/uptime, maintenance waits and service latency. Configuration inputs keep explicit native units (seconds, MiB); raw API values and debug JSON remain unchanged.
- Missing/empty/negative/nonfinite data is unknown, not fabricated zero. Known zero remains zero; tiny nonzero rates remain visible. CPU percentages may legitimately exceed 100% for multicore processes/containers. Overview unknown gauges have neutral color and an unknown label, not `0.0%`.
- Container default memory no longer renders `默认 MiB`. Long durations use days/hours/minutes with at most two significant components.
- Image preflight now queries Docker with **the same dangling-only filter** as execution, rather than counting all unused images. Shared-layer sizes remain an upper-bound estimate. This change does not broaden cleanup, clear volumes or perform any cleanup during validation.

## Verification

Release backend tests: 412 unit / 62 integration passed; three native PostgreSQL tests explicitly skipped locally (not claimed as rerun for a display-only change). Frontend: 74 unit tests, TypeScript, lint, Prettier and production build passed. Fifty browser regressions passed for telemetry, backup capacity, monitoring alerts, application backup records and container usage across light/dark and 320–1920px. Checked the 320px dark telemetry screenshot: compact controls, duration label, summary and axes remain readable.

Runtime `c671b7e` published in place through job `b3aa8d26-2d64-4aa2-9f2e-138375ea44b8`; actual update service finished `inactive/dead`, `Result=success`, `ExecMainStatus=0`. Eleven core containers healthy, Agent/Maintenance active, user sudo retained without expanding Agent permissions. Real Authentik/CSRF/native Docker preflight passed: **zero dangling images / 0 B**, consistent with the same native filter; image/volume/container inventories unchanged and no cleanup job submitted. The 15.02 GiB example is a formatting fixture, not the workstation's actual reclaimable space. Ephemeral administrator was removed by the existing wrapper.

`validate_display_units.py` authenticates through the existing ephemeral-account wrapper, requests only the cleanup **plan**, compares its formatted estimate with Docker's read-only dangling-image query and verifies image/volume/container inventories remain unchanged. It never enqueues a cleanup job.

## Review assistance

Approved read-only router: GLM `glm-5.3-flash`, one successful attempt, 152739 ms; input 300152 tokens (224576 cached), output 4420; fee unavailable. GPT checked the findings, performed all modifications and verification. No credentials or private settings were included in the review prompt.

Follow-up narrow review: GLM Flash, one success / no retry, 69975 ms; input 40266 (13568 cached), output 2187, fee unavailable. GPT did not adopt the incorrect claim that Python removes both fractional zeros (`removesuffix` removes only one), or that a natural nonnullable summed-byte count must accept null. The acceptance check compares actual native integers with the Agent plan; no cleanup execution was used.

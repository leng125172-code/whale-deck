# Search layout acceptance — 2026-10-13

## Changes

- Header entry shortened to `搜索`; desktop width capped at 168 px. Existing mobile icon-only behavior and accessible label retained.
- Search dialog title and input hint shortened. Titles and subtitles truncate on a single line; native hover titles preserve full text.
- Result grid uses `minmax(0, 1fr)`, non-shrinking icons and bounded internal scrolling (maximum 420 px, reduced for short windows). This prevents long unbroken names or a full result list from enlarging the dialog.
- Search permission filtering, remote query debounce, target navigation, login and management actions are unchanged.

## Evidence

- `web/e2e/search-layout.spec.ts`: 2 tests passed (light/dark). Each dynamically resizes through 1920, 1046, 1045, 760, 390 and 320 px with 30 long-resource fixtures; checks title/subtitle full-text attributes, actual ellipsis, local vertical scroll, no horizontal overflow, close/reopen, Ctrl K, a 400 px-high window and clicking the last result.
- Desktop light and mobile dark screenshots reviewed: result names visibly ellipsized, icons and close/input controls intact, no clipped controls or viewport overflow. Fixtures are layout evidence, not production search or permissions evidence.
- Frontend build/type check and lint succeeded; 44 unit tests passed; targeted Prettier and `git diff --check` passed.
- Existing multi-page layout suite also passed: 24 tests total (22 existing + 2 search regressions), covering 320–1920 px, both themes, page navigation and empty/dependency-error states. Workstation publication is verified separately after completion; scheduling an update does not constitute deployment success.

## Workstation acceptance

Runtime `b05057b` published in place on 2026-10-13. Scheduling job `7d8903d6-a4d5-4b7b-bc3d-eb67c9d14f1e` completed; actual `whaledeck-platform-update.service` subsequently ended `inactive/dead`, `Result=success`, `ExecMainStatus=0`. All 11 core containers healthy and Agent/MaintenanceHost active. HTTP `/login` and the deployed JavaScript asset containing the shortened input and bounded result layout were verified. Real Authentik administrator acceptance checked 11 container summaries, 8 Compose applications, native network projection and control-plane protection. The ephemeral identity was cleaned by the wrapper. No production data or sudo authorization changes were required. Agent protocol remains 1.15; this delivery does not complete the overall P0–P5 goal.

## Assistance

Read-only mechanical review used GLM `glm-5.3-flash` through the approved router: one successful attempt, 80,718 ms; 75,540 input tokens (47,040 cached), 1,458 output tokens. Actual resource-package charge unavailable. Review identified missing result-title truncation; GPT selected, implemented and verified the changes. No credentials were supplied to the reviewer.

The interactive Playwright skill was inspected, but its required `js_repl` is unavailable. Existing project Playwright regression tooling and local screenshot inspection were used without changing user runtime configuration.

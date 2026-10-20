# Directory overwrite restoration UI candidate (2026-10-20)

Status: active, not deployed or live-BFF accepted. UI source `f35ee058b774cf0e8cca50bdbfc2e299633e7355`.

## Implementation

- Independent `fileTreeOverwriteRestorings` public contract and administrator-only panel; the queue sends exactly `captureId`, a registered target location, and `targetExpectedVersion` from a currently read, unprotected directory entry. No manually supplied version, historical owner, root path, private plan or member list.
- A separate catalogue action clearly distinguishes overwriting an existing directory (first create/verify its HDD backup) from restoring to an absent target. Target scope/version is frozen and rechecked against the displayed entries before queueing; the server independently validates it again.
- Ready confirmation displays source capture and archive proof, current target version/count/bytes, separate planning and execution Jobs and the expiry/warning. All active file-mutation panels share the overwrite lock.
- URL references contain only independent opaque registration keys/intent IDs. Lost registration uses GET by-key; lost confirmation queries the original intent without an automatic second POST. Session/administrator changes reset the panel and invalidate in-flight responses.
- Cancellation and Unknown acknowledgement require explicit confirmation, never promise successful cancellation, automatic rollback or a repeated overwrite. Result UI shows verified backup ID/bytes/hash and distinguishes possible retention from previously verified facts; a historical success does not guarantee current disk health or unchanged contents.
- Response validation checks all 24 public fields, source/target bindings, independent Job identity, limits, immutable ready confirmation and monotonic backup/retention proof. Terminal comparison is semantic rather than JSON property-order dependent. Private/unknown fields and contradictory facts are rejected.
- Capture decoding recognizes only `Trash` and `Overwrite`; old omitted kind defaults to Trash. Overwrite requires its content hash, while Trash cannot carry one. Both can be restored to an absent target because the actual existing server/helper supports both, without turning absent-target restoration into an overwrite operation.
- The tenth current-administrator recovery kind is recognized and explicitly warns that takeover inspects/fences/reconciles; it never reapplies an overwrite.

## Verified local evidence

- Frontend unit tests: 40 files, 364 tests passed. Type-check, production build, lint and source formatting passed.
- Main-agent Chrome production-preview run: **88/88 passed**, two bounded browser workers, 7.6 minutes. Covers new overwrite workflow plus previous recycle/absent-target restore/current-administrator recovery, including the new tenth recovery kind.
- Viewports 320/1045/1440, light/dark and short mobile dialogs are exercised; viewport/document bounds, HTML escaping, strict request/CSRF headers, planning-before-confirmation, reload/response loss, changed target or archive proof, immutable backup evidence, cancellation, Unknown acknowledgement and cross-panel locks are checked.
- Ordinary-user tests wait for the denied-route heading before asserting no management UI and zero file API requests, on initial navigation and reload. Immediate absence of a component is not accepted as a permission-readiness check.
- These are local real-browser protocol fixtures against the production frontend bundle, **not** actual Authentik, backend Cookie/CSRF enforcement, Worker, PostgreSQL, Agent or HDD acceptance.

## Failures retained, not hidden

- An earlier main-agent run targeted the standalone DEV preview: 86/87 passed, one ordinary-user recycle test failed. Its screenshot shows the intentional DEV preview administrator, not the requested fixture's ordinary identity. The failure artifacts were copied to ignored `deploy/artifacts/qa-failures/recycling-dev-preview-20261020` before the next test run.
- The production-preview run uses the built bundle and mocked server session, matching the existing CI mode; all 88 tests then pass. No production permission rule or old assertion was weakened. The new ordinary-user test was additionally strengthened to wait for actual denial instead of permitting an early zero-request assertion.
- The test agent initially lacked a bundled Playwright browser and subsequently corrected its new fixture's missing inherited restore grant; final primary evidence is the main-agent production-preview run, not those initial attempts.

## Installed-client observer preparation

- Frozen observer/probe source `d4274172b3b6353fff20b2d0ff1926acc657268a`. Workstation Linux root scope/cleanup tests: **3 executed / 3 passed**. Windows: 2 passed, 1 explicit Linux-root skip. This is not UDS overwrite execution.
- The independent observer verifies full original/displaced/output file bytes and metadata, old/new inode identities, both HDD archives and manifests, operation bindings and only exact owned cleanup. Unknown objects, mismatched inode/bytes, links or nested mounts block cleanup. No broad recursive deletion.
- Windows cross-publish stalled acquiring the official Linux runtime package. An independent HTTPS download completed and matched the official SHA-512; no global NuGet/proxy config, audit bypass or signature bypass was applied. Only the identified owned publish/restore processes were stopped. An attempted repeated `--source` CLI argument misparsed its URL and failed NU1301; a task-local config preserved official audit sources, but Windows restore still did not finish.
- Publishing therefore moved to the workstation's existing pinned SDK image with read-only Git-archive source, capability drop, unprivileged UID, bounded memory/CPU/PIDs/tmp and no Docker/Agent socket or private secret mounts. The first run failed before restore because its isolated runtime directory was missing; the corrected run restored both projects in 12.58 seconds and successfully published the self-contained Linux client. The owned SDK container was independently confirmed absent. No Whale Deck image has been built by this client-only validation step.
- Client artifact SHA-256: TerminalProbe `5a5b0ce4a436bb73bd3730b90be1906db06a868531c3814e174f74885912d927`; TerminalProbe.dll `754a99cbd2ec258c1c7de2d5e842dc1efb8064068d8f6b4239c3d500956a0a9f`. Both matched after copying into a new root-owned, non-writable-by-group/other artifact directory.
- Installed Agent source independently read as `6a6f8e25873639df93611c92c6aed49d1045fd41`. Dedicated observer execution has started with a newly generated owned fixture; the installed UDS/disk gate is still in progress and no success is claimed yet.

## Assistance

- GLM / glm-5.3-flash, router mechanical, no tools/repository reads: 1 successful attempt, 20,365 ms, input 11,895 / cached 0 / output 160 tokens. Actual charge/balance not returned. GPT checked the field mapping and made all implementation/security decisions.
- Independent agents contributed service and local browser tests only. GPT reviewed their changes, corrected the historical-kind assumption against real helper/server code, tightened nested field and permission checks, and ran combined verification.

## Remaining acceptance

Observe the existing live installed UDS probe handle through independent bytes and exact cleanup, then finish frozen CI, actual authenticated BFF/Worker and real page acceptance before a development overwrite release is accepted. Current production Agent/MaintenanceHost/Gateway remains 6a6f8e2 and API/Worker 18f38bb. The full 76-item P0–P5 objective remains active; this does not complete all file management.

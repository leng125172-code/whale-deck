# Directory move BFF implementation

2026-10-18. **Latest installed 1399563 / Agent1.28 includes accepted real
administrator/ordinary WebUI and repeat installed move/copy BFF validation**;
see [final UI and release evidence](FILE_TREE_MOVE_UI.md). The earlier 39cb274
backend acceptance below remains its own historical evidence. Agent evidence is in
[its separate report](FILE_TREE_MOVE_AGENT.md). The earlier foundation-only
paragraphs below describe 80d3de4, not the current candidate.

## Installed backend: 39cb274

- CI [39059348941](https://github.com/leng125172-code/whale-deck/actions/runs/39059348941)
  installer/backend/native-postgres/frontend all completed successfully. Local
  Release passed 997 Unit / 122 Integration, six native suites explicitly skipped.
- Frozen same-source real PostgreSQL: `dispatch-707c3e2c7e9a4babac1da5f67062f346.json`,
  2026-10-17 18:44:25.801977–18:46:22.745129 UTC, executed 1 / passed 1,
  cleanup confirmed. SDK image ID
  `sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa`.
  Its Root transport is explicitly stubbed, not installed-byte evidence.
- Actual update unit ended successfully (1min16.250s); installed host assemblies
  carry 39cb274, UDS/permissions/dynamic entry and PKCS/.NET10.0.13 validation
  passed. Move table and SQL guard exist in production. Container assembly Git
  provenance is still a P0/P5 gap, not proved by the host assembly SHA.
- The corrected validator ran against installed 39cb274 with real temporary
  Authentik administrator and ordinary identities. It proved original-login
  queue/confirm/two Jobs/Worker/UDS/human audit, full independent 2,097,159-byte
  target and retained-original data, original inode, modes/mtime/registered
  ownership, private manifest hash, CSRF/Origin, second-login refusal and ordinary
  valid-Cookie/CSRF GET and POST 403. Cancel, protected members and changed
  descendants never replayed the move. Changed-source Unknown was explicitly
  acknowledged by its original login, not forced to Failed or successful.
- Executed corrected script SHA-256:
  `f90f14d67b3869321f630df9000636c32cf0a96758b1c448b58a0ccc2cdcaf5f`;
  exact successful fixture `wdv-move-bff-d006d0ccabd54871` removed, permanent
  Root receipts, intents, Jobs and audits retained. This proves the backend,
  not real-browser use of the new move component.
- Original upload (33 MiB+7 and 7-second idle), ordinary mutations, text/HDD,
  ordinary recycle/restore and directory-copy BFF flows passed real regression
  after publication and cleaned their exact fixtures. Eleven core containers
  healthy, host services active, no active file workflows/locks/open terminals
  or temporary acceptance identities at the recorded check.

### Preserved validator failures and remaining recovery gap

The first fixture (`wdv-move-bff-7f75c38f3339429a`) stopped because the ordinary
login helper defaulted to requiring administrator. The product plan later
expired without execution. 58263de fixes that validator call, not authorization.
The second fixture (`wdv-move-bff-f90ce874a0af4d95`) completed its first real move
but the validator wrongly waited for permanent Root Unknown to become Failed
after changing its own source. A subsequent read returned non-200; the old
script did not record the actual status, so 429 is not an observed fact.

Both failed fixtures were later independently checked and precisely deleted by
an explicit root validation janitor. One exact orphaned Unknown intent was
retired with its own system audit, original Root Unknown and all permanent
evidence unchanged; no deleted user's login was impersonated. The deleted
synthetic file copies are not retained for recovery and no business data was
removed. **This janitor is not a product recovery feature.** P0-03 still needs
current-administrator, second-confirmed and audited disposition after loss of
the original login. The immutable original outcome must remain unchanged.

## Backend implementation (candidate-stage history)

- Added administrator-only `/api/v1/files/tree-moves` queue/status/by-key/confirm/
  cancel/acknowledge routes. Controllers map HTTP only. Original-login binding,
  UUID keys, human confirmation, current permissions and immutable plan signing
  are checked by Application and the transactional repository.
- Independent permanent intent/table/migration, two restricted Job FKs, immutable
  request/plan/execution/start/terminal guards and no-delete trigger. No downgrade
  can discard a nonempty permanent table. Existing schema/data are not removed.
- Dedicated planning/execution/cancellation outbox and Worker; generic processing
  excludes the new message types. Lease/permission renewal, cancel fence,
  response-loss Unknown, retained hierarchical locks and inspect/discard-only
  reconciliation mirror the verified no-replay contract. Human result audit is
  committed with each state change; repeat transport failures do not flood events.
- Move and copy participate in one actor/global budget and resolve each other's
  lock owners. No timeout is permission to release a path lock.
- Destination publication/source removal/source retention remain nullable facts
  until a validated Root receipt arrives. Completed requires all three true;
  Unknown never silently becomes false facts or a successful Job. Root plans and
  private original paths do not appear in the public intent DTO.
- Native PostgreSQL suite now includes the move workflow, mixed copy/move budget,
  copy-vs-move descendant conflict, actual Job-row lock wait and lease expiration,
  immutable-row rejection and transactional audit failure. Root transport is a
  deliberate stub in that suite; real Agent bytes need separate installed proof.
- Local Release: **997 Unit / 122 Integration passed**; six native suites are
  explicitly skipped, not counted as real PostgreSQL. New API/model/lease/binding
  tests passed. At this historical stage only script AST had passed; actual
  installed execution is recorded above.
- Migration generation initially selected a startup project without Design;
  switched to the existing Infrastructure design-time factory, no new package.
  Generated block namespace failed IDE0161; corrected style, kept analyzers on.
- GLM Flash read-only locator: one successful attempt, 71.111 s, 136,294 input /
  103,488 cached input / 1,730 output tokens; price unavailable. GPT made and
  reviewed all permission/locking/persistence decisions and implementation.

`validate_file_tree_move_bff.py` is prepared for real original-login Cookie ->
HTTP -> SQL -> Worker -> installed UDS operations: 2 MiB+7 full target/original
bytes and inode/metadata/manifest, two Jobs/audit, valid-CSRF ordinary-user 403,
second-login refusal, cancellation/protection/source change and exact fixture
cleanup. It preserves uncertain fixtures and permanent records; this is a
prepared validator at that stage. Source CI, same-frozen-source real PostgreSQL,
publication and installed identity flow were subsequently completed above.

## Foundation evidence (historical)

- Application models keep move receipt publication/source-removal/source-retention
  facts distinct; source parent identity preserves UInt64 without browser numbers.
- Immutable location commands reuse copy validation, but move registration IDs,
  execution IDs, request/ready MAC domains and confirmation signatures are separate.
  Copy confirmations cannot authorize moving; changing the login or parent identity
  changes the corresponding binding.
- First test build hit CA1859 on two test local variable declarations. Corrected
  test types and explicit binding construction; did not disable analysis rules.
  Local Release then passed **974 Unit / 120 Integration**, six native suites
  explicitly skipped. These counts include uninstalled BFF foundations, not the
  967-unit frozen Agent release. No native SQL for a move entity is claimed.
- No domain entity, repository/migration, hierarchy lock ownership, job/outbox,
  dispatcher/Worker, gateway adapter, controller, human audit or WebUI is connected
  for move yet. Add and verify these next, with actual PostgreSQL lifecycle,
  original-login permission/cancellation/recovery and browser evidence before
  describing directory move as a complete user feature.

Full P0–P5 remains active. No stub endpoint or success-shaped empty operation
is used to stand in for the missing workflow.

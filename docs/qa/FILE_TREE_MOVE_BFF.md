# Directory move BFF implementation

2026-10-18. **BFF/Worker candidate implemented, not deployed or accepted yet;
move WebUI still missing.** Installed Agent 1.28 acceptance is in
[its separate report](FILE_TREE_MOVE_AGENT.md). The earlier foundation-only
paragraphs below describe 80d3de4, not the current candidate.

## Full backend candidate

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
  tests passed. Validation script AST passed; installed BFF script not yet run.
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
prepared validator, not a claim of execution. Source CI, same-frozen-source real
PostgreSQL, publication and installed identity flow remain required.

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

# Directory move BFF implementation

2026-10-18. **Foundations only; no HTTP endpoint or move UI yet.** Installed
Agent 1.28 acceptance is in [its separate report](FILE_TREE_MOVE_AGENT.md).

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

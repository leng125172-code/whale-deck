# Directory overwrite restoration: current-administrator recovery integration

Status: active, candidate source only. This is not installed Agent, live BFF, browser or directory-byte acceptance.

## Implemented in this batch (2026-10-20)

- Tenth recovery kind `tree-overwrite-restore`, independent execution identity, stable inventory cursor rank 9 and frozen signed historical material. The historical subject/login/opaque resource owner are never replaced by the current administrator.
- The current administrator can inspect and fence/discard the original operation; this recovery path never applies an overwrite again. A separately confirmed recovery job reconciles an existing completed receipt or explicitly acknowledges uncertainty.
- Backup capture proof and retention facts are monotonic, including bytes and SHA-256. Backup catalogue insertion, original result, execution Job, events, Outbox completion, lock release and current-administrator audit share a PostgreSQL transaction.
- Missing root evidence still requires verification of the complete frozen original snapshot for all ten kinds; a canonical null receipt cannot bypass historical binding checks.
- Separate administrator-only, no-store HTTP endpoints, dedicated scoped repository/service/dispatcher and Worker, generic Job cancellation routing and Outbox exclusion/acknowledgement are connected.
- Migration `20261020131149_FileTreeOverwriteAdministratorRecovery` expands the kind column from 16 to 32 characters and permits the new kind. Downgrade refuses permanent new-kind evidence rather than truncating it. Historical migration definitions remain unchanged.
- Added native SQL cases for existing-backup Unknown acknowledgement and lost-Completed-response reconciliation before catalogue insertion. They retain cancellation/idempotency/concurrent confirmation, original authority rejection, audit rollback, shared reservation fencing and no-repeat-fence assertions.

## Local evidence

- SDK 10.0.402 Release solution build: 0 warnings, 0 errors.
- Unit tests: 1738 passed, 0 failed, 0 skipped; 38 new overwrite-recovery material cases pass.
- Integration tests: 146 passed, 0 failed, 6 explicit native-dependency skips (152 total). These skips are not native execution evidence.
- New route reflection/header tests and offline EF model/migration checks pass. They do not establish live authentication/CSRF or real HTTP behaviour.
- Initial build rejected three new test fixture uses of `with` on the immutable non-record snapshot class (six compile errors). The fixture now constructs new snapshots; production snapshot semantics were not weakened.

## Independent assistance and review

- GLM / glm-5.3-flash, router `mechanical`, read-only, 1 attempt, 66,120 ms: input 30,534 tokens, cached input 11,840, output 1,827. Actual charge/resource balance not returned.
- The scout returned a partial inventory, but the Worker read was truncated and router success was false. GPT directly inspected the affected production files and selected/implemented/reviewed all permission and persistence changes; the partial inventory is not acceptance evidence.
- Two test agents modified only their assigned test files. GPT reviewed their diffs and ran the combined build and tests.

## Remaining gate

Freeze source and execute the existing isolated workstation PostgreSQL runner; require exactly one executed/passed native test and cleanup proof. Then continue UI integration, installed UDS, live BFF and browser acceptance before deployment acceptance. The complete 76-item P0–P5 objective remains active.

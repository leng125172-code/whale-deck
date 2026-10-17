# Directory move Agent bridge

2026-10-18. Candidate Agent 1.28; **not installed or accepted yet**. Runtime
remains 6c5b1d6 / Agent 1.27. The independent internal engine 88d265e has green
CI 39051472857; that run is not evidence for the new bridge or user-facing move.

## Candidate implementation

- Separate `FileTreeMoveService` Plan/Apply/Inspect/Discard, method-bound signed
  capabilities and replay prevention; protocol minor 28 with explicit capability.
- Fixed root helper enums and trusted module filenames only. No command, module
  path, arbitrary UID or private manifest enters the public RPC contract.
- Source requires list/download/mutate/tree-move and destination
  upload/mutate/tree-move. Copy and move do not imply each other's authority.
- Parent device/inode stay unsigned 64-bit through protobuf and strict JSON;
  receipt flags distinguish complete, partial and unknown retained originals.
- Existing unmodified 1.27 root defaults gain the explicit action with a durable
  `.pre-tree-move.json` backup. Custom/tightened policies are not broadened.
- Installer adds the root-owned nonwritable module. Runtime helper resource
  limits for tree copy/move do not increase the ordinary upload limit.
- The installed validation client uses UDS plus a read-only short-capability key,
  without Docker socket or data mounts. Independent observer compares full bytes,
  original inode/mode/mtime/ownership, destination identity and permanent root
  receipts/manifests for 2 MiB+7 and 1 GiB+7 fixtures.
- Observer timeout is not a terminal result. Live/unconfirmed SDK containers,
  scratch and uncertain fixture are retained; no retry or forced removal. Only
  exact owned fixture membership is removed after successful verification.

## Source evidence, not installed acceptance

- Release backend: 967 Unit and 120 Integration passed, including expanded
  receipt flag/count/byte and UInt64 boundary assertions; 6 native dependency
  suites explicitly skipped locally. Real PostgreSQL is a separate required gate.
- Frozen v6 code copy on the workstation: 32 move/loader tests, 152 previous
  file/root tests and 26 copy tests passed. No production module was replaced.
  The owned temporary source directory has been removed after exact verification.
- Eight validator diagnostic/observer tests passed locally, including live,
  restarting, paused, foreign, absent, malformed and timeout cases. These are
  mocked observations, not a claim that installed move passed.
- GLM Flash read-only coverage inventory succeeded (99.925 s, 62,322 input,
  17,920 cached input, 2,662 output tokens; one attempt, price unavailable).
  GPT independently reviewed it: operation mismatch already had coverage;
  unsigned device zero is not inherently invalid; EOF coverage is in the separate
  transport tests. Added accepted-flag and parent numeric-boundary assertions.

## Remaining acceptance and scope

Same-source CI, real isolated PostgreSQL, controlled Agent install, installed UDS
probe and independent cleanup verification remain required. No BFF plan/entity,
Worker move job, human audit or move UI exists yet. Retained original file leaves
are private on the source device, not an HDD backup or restored original directory
inodes. Directory trash/HDD restore/archive and the complete 76-item goal remain
active. Do not treat this bridge as the complete file-manager feature.

# PKCS dependency remediation — 2026-10-16

## Baseline and cause

The six latest dev runs (38851613468, 38850684745, 38850105179, 38849491433,
38848028553, 38846404484) failed. The latest run's backend restore failed with
NU1902 / NU1903 for System.Security.Cryptography.Pkcs 10.0.0; build, test and
the final package audit steps were skipped. Installer and frontend jobs passed.

Dependency chain verified using `dotnet nuget why`:

`Api / Worker / Migrator -> Infrastructure -> MailKit 4.18.1 -> MimeKit 4.18.1 -> Pkcs 10.0.0`.

Agent, MaintenanceHost, Contracts, Domain and Application have no NuGet dependency
on PKCS. This statement does not substitute for an operating-system/runtime audit.

Before deployment, the running Worker's deps.json contained Pkcs 10.0.0. The API's
deps.json did not list PKCS separately, but its ASP.NET Core 10.0.8 shared framework
contained System.Security.Cryptography.Pkcs.dll. Absence from deps.json alone is
therefore not an exclusion test. No exploitation assessment or incident-forensics
claim is made by this dependency inventory.

Official advisories: [CVE-2026-84008](https://github.com/dotnet/runtime/security/advisories/GHSA-846f-f764-76mx)
and [CVE-2026-70162](https://github.com/dotnet/runtime/security/advisories/GHSA-2fmv-rv2c-59x7).
Both list 10.0.0–10.0.12 as affected and 10.0.13 as patched.

## Changes

- Infrastructure explicitly pins Pkcs 10.0.13; normal transitive propagation
  supplies the fixed package to API, Worker, Migrator and tests without changing
  MailKit/MimeKit's compatible major versions.
- NuGetAudit remains enabled for all direct/transitive packages; warnings remain
  errors. No audit suppression or vulnerability allow-list was added.
- A regression test checks the loaded PKCS informational version, not its unchanged
  AssemblyVersion, against the 10.0.13 patched floor.
- Runtime and ASP.NET base images are updated to 10.0.13 with immutable manifest
  digests verified against Microsoft's registry. SDK selection is unchanged.

## Local verification

- Forced restore with no HTTP cache and audit=all: passed.
- Release build: zero warnings / errors.
- Unit tests: 643 passed, including the loaded-version regression.
- Integration tests: 92 passed, 6 native-dependency tests explicitly skipped locally.
- `dotnet list package --vulnerable --include-transitive --no-restore`: no known
  vulnerable packages reported for any solution project at the time of the audit.

## Final acceptance

- Corrective source/runtime: `8c5ac86d669c26e8fa055a62f2d3299ad84e529c`.
  [GitHub Actions 38855112870](https://github.com/leng125172-code/whale-deck/actions/runs/38855112870)
  completed successfully: installer, frontend and backend all green. Backend
  restore/build/test/audit each succeeded; Linux CI confirmed 643 unit / 92
  integration passes and 6 explicit native skips, with no known vulnerable
  solution packages reported by that audit.
- Frozen-source isolated PostgreSQL/TLS SMTP dispatch suite executed 1 / passed 1
  and confirmed cleanup. Report: `dispatch-f8f632a77a6d41b29a9ef059bf41b0c6.json`,
  2026-10-16 05:19:10–05:21:16 UTC; SDK image ID
  `sha256:c0790639332692a0d56cdd81ed581cfd24d040d9839764c138994866df89a3b6`.
  No external mail or notification was sent. The other five native suites were
  not rerun for this narrowly scoped patch.
- Deployment Job `a1d98528-6da4-4dff-b609-7b14d6ae53b8` scheduled the real host
  update unit; the unit exited successfully at 13:25:20 CST, inactive/dead,
  Result=success, ExecMainStatus=0. This is separate from the Job's scheduling
  success and is the actual deployment completion evidence.
- Running API and Worker deps.json now contain Pkcs 10.0.13. Their deployed DLLs
  both match the restored official package's SHA-256:
  `eff9ed505c194f0f5c9faef1a175459cd0560eeb4ad08890c7c0c0e1ff4d34c3`.
  API shared ASP.NET / .NET runtimes and Worker .NET runtime report 10.0.13.
  The newly built Migrator image independently reports runtime 10.0.13 and the
  same fixed DLL hash, without starting the migration application or connecting
  it to a database during that inspection.
- Real OIDC admin/ordinary/anonymous authorization, CSRF/Origin/idempotency,
  notification management and restricted host-terminal BFF/UDS/PTY regression
  passed; temporary validation users/configuration/sessions were cleaned up.
  Eight public HTTP assets match the active Gateway byte-for-byte. This is not
  a production-identity browser UI acceptance claim.
- All 11 core containers healthy; Agent, MaintenanceHost and the existing update
  timer active. Host-service permissions / UDS / dynamic entry checks passed.
  No host or Docker restart, production fault injection or database-volume
  modification was performed. User-managed sudo policy was preserved.

The in-progress terminal WebUI was neither committed nor deployed with this
security patch. The complete 76-item P0–P5 objective remains active; this record
only closes the reported PKCS dependency/CI/runtime remediation.

## Follow-up: self-contained host bundle (not yet accepted)

Further direct inspection found that the installed Agent and MaintenanceHost
both bundle PKCS **10.0.8**, product version
`10.0.8+94ea82652cdd4e0f8046b5bd5becbd11461482ca`, SHA-256
`4ec9233f0bf14effe257ae74d92b27477e264f3cb694d8a789b6137ecad93724`.
Their included .NET/ASP.NET frameworks are also 10.0.8. The earlier absence of
a NuGet dependency path did not exclude a self-contained framework DLL. This
was a verification gap: the earlier acceptance above proves containers and CI,
not that every installed host artifact was patched.

The official .NET 10.0.13 release metadata lists supported SDK 10.0.402; SDK
10.0.305 was checked and is not published, so no inferred SDK version is used.
SDK 10.0.402's manifest was verified against Microsoft's registry and the exact
accelerated-image digest downloaded. global.json, all SDK Docker defaults and
the example pins now select this supported SDK. Runtime images stay 10.0.13.

New independent runtime checks reject old included frameworks, stale PKCS
informational metadata, missing/oversized/corrupt files and unsupported major
versions. Twelve synthetic Python regression tests pass. Publishing checks the
result before checksums; installing checks before service/file changes; host
validation rechecks the actual /opt files. Formal preparation adds Python 3.
Host SDK builds now mount only frozen tracked Git source, never the private
checkout containing .env; their temporary source can be reconstructed from Git
and is precisely cleaned after the build.

The new validator was run read-only against the old installed bundle and
correctly rejected it. New-SDK Release/tests, actual bundle publication and
installation, PKCS product-version checks and service regression are still
pending. No exploitation assessment is inferred from presence of this DLL.

The first 10.0.402 frozen native run failed before test execution, with temporary
database/container cleanup confirmed. A separate probe with only public tracked
source and **no** connection/token/env-file reproduced `NuGet-Migrations` mutex
startup failure. A non-root read-only probe confirmed writable /tmp alone is
insufficient. [.NET maintainers document the 10.0.13 mutex security change](https://github.com/dotnet/runtime/issues/135726):
an existing writable user-owned XDG_RUNTIME_DIR is required when the home is
unavailable. Supplying a 0700 private directory fixed the probe, without removing
read-only/capability/no-new-privileges restrictions or skipping NuGet migration.
The shared native runner now supplies a per-run private /scratch/runtime and
recognizes this startup failure explicitly rather than logging private output.

SDK 10.0.402 CI 38864657702 completed successfully, including backend Release,
tests/audit, installer/runtime-validator tests and the production-route browser
gate. Publication from frozen source 640654e produced host-publish.7dG1kC:
both included frameworks and actual PKCS informational metadata are 10.0.13.
This package was validated but is **not yet installed**; a corrected frozen
native run and final deployment/service checks remain required.

## Installed host-runtime acceptance (e85c44a)

The preceding pending statements are historical. Source e85c44a's CI
[38866436307](https://github.com/leng125172-code/whale-deck/actions/runs/38866436307)
passed all three jobs, including Release build/tests and the complete transitive
dependency audit. Frozen SDK 10.0.402 dispatch report
`dispatch-e6309cdb8ba04d2cb09d47ad57a510bb.json` executed/passed 1 with cleanup,
07:21:16–07:23:10 UTC. No external notification was sent.

Update Job `432df14a-4ac0-4aa6-b61c-5a74cba93366` scheduled the real deployment;
the actual update unit terminated successfully at **15:31:39 CST**, inactive/dead,
Result=success, ExecMainStatus=0. Direct inspection of both installed host bundles
confirms .NET/ASP.NET 10.0.13 and PKCS product version
`10.0.13+4099f83977f4d29604a9aeb971fb0a7bc1d5f699`, SHA-256
`e562a8977d9dd59e4329f1ad0941c99d10dc27c069c15f8b10be647103c144e4`.
Installed runtime validation, systemd/permissions/UDS, dynamic entry points and
all 11 core containers passed; user-managed sudo was preserved.

SDK 10.0.402 uses the patched ASP.NET shared PKCS in API rather than copying a
redundant app-local DLL. The API's actual shared-framework DLL has the above
10.0.13 product version/hash; missing `/app/System.Security.Cryptography.Pkcs.dll`
is therefore not a deployment failure. Worker retains its official package DLL,
product 10.0.13 and SHA-256 `eff9ed505c194f0f5c9faef1a175459cd0560eeb4ad08890c7c0c0e1ff4d34c3`.
Both running containers report runtime 10.0.13 and healthy. The first inspector
incorrectly required an app-local API DLL and failed; the corrected inspector
checks actual product metadata in app/shared framework files, not deps.json alone.

Real OIDC authorization/notification-management and eight HTTP-asset checks
passed. Terminal regression found a separate idle-stream EOF at explicit close;
its temporary sessions were cleaned and that check was **not** signed off.
The stable proxy's 5-second ActivityTimeout is shorter than both the terminal's
10-second revocation monitor and 20-second keep-alive. A dedicated bounded
stream cluster is being verified; security patching remains installed, with
no audit suppression or rollback to an affected runtime. See the terminal UI
record for its final result. Presence/version checks do not establish whether
any vulnerability was exploited.

## Final streaming regression and retained patched runtime (1068b27)

[CI 38868581495](https://github.com/leng125172-code/whale-deck/actions/runs/38868581495)
is green: 646 Unit / 92 Integration passed (6 explicit native skips), Release,
full transitive audit, installer and production-built authorization browser gate
all passed. No known vulnerable packages were reported by the configured sources
at that time; this is not a universal vulnerability guarantee.
Frozen-source SDK 10.0.402 report `dispatch-fe7c549d63224b2caec314d79601e853.json`
executed/passed 1 with cleanup, 07:39:57–07:41:34 UTC.

Job `b0cb79f2-e94f-4588-9686-b271e7cc86ca` scheduled the final overwrite deployment.
The actual update unit ended **15:43:57 CST**, inactive/dead, Result=success,
ExecMainStatus=0. Both host bundles and the API shared/Worker package DLLs were
independently rechecked and retain the fixed 10.0.13 product versions/hashes
recorded above. Real OIDC/BFF/UDS/PTy, silent-six-second-then-close with actual
RFC6455 acknowledgment, resize/exit persistence and consumed-grant replay passed;
test sessions were cleaned. Real admin/ordinary/anonymous permissions, CSRF/Origin,
notification-management and eight HTTP assets also passed, external sends zero.
Host runtime/permissions/UDS/dynamic entry checks and isolated installed-maintenance
malformed/oversized/missing-state checks passed; isolated process/files removed.
No host/Docker reboot, production fault injection, audit suppression or
affected-runtime rollback was used. Broader P0–P5 work remains active.

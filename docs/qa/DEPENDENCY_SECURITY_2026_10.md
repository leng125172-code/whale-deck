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

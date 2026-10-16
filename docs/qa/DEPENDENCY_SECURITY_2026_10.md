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

CI for the corrective commit, isolated native mail/dispatch tests, and deployed
image/runtime checks are still pending at this record's initial commit.

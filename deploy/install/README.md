# Whale Deck installer

The repository-root `install.sh` is a staged Ubuntu/Debian installer inspired by the safety properties of the official 1Panel v2 installer: root preflight, explicit interactive choices, source latency probes, preservation and validation of an existing Docker daemon configuration, and non-interactive environment overrides.

Stages are deliberately resumable:

1. verify OS, storage, Git, Docker/Compose and systemd;
2. verify or add official Docker and Microsoft package sources and report latency;
3. install the bounded CLI/toolchain set;
4. optionally merge one registry mirror into `daemon.json`, validate it, restart Docker and roll back on failure;
5. clone or fast-forward `database-platform`;
6. generate private environment files and start all `database-platform-*` dependencies (legacy migration is a separate explicit workflow);
7. clone or fast-forward Whale Deck, publish/install Agent and MaintenanceHost, build images, migrate PostgreSQL, and start the platform.

Preview without root access, networking, package installation or Docker actions:

```bash
bash install.sh --dry-run
bash install.sh --dry-run --dependencies-only
```

For dependency-only installation:

```bash
sudo ./install.sh --dependencies-only
```

For a reproducible unattended run, export the variables from `install.env.example`, then use `--non-interactive`. A populated environment file contains secrets and must remain outside Git with mode `0600`.

Before stage 7, create the cloned Whale Deck repository's `.images.env` from `.images.env.example`, with each base image pinned to an immutable SHA-256 digest. The .NET SDK and Node toolchain run only inside those build images; the workstation is not polluted with project-language SDK packages. When a private `.env` does not yet exist, the installer idempotently provisions separate Authentik OIDC applications for `192.168.22.19` and the USB/LAN address `192.168.100.13`, creates or reuses the management API token, and writes the generated credentials directly into the mode-0600 runtime environment without printing them. Existing applications, tokens and `.env` credentials are retained on rerun. The migrator is built explicitly with the `tools` profile and runs once before API/Worker start.

To enable development workspaces, provision a development storage root in the host Agent and make the matching policy available to API and Worker through the optional `/etc/whaledeck/development-workspaces.env` file. Use Docker Compose 2.24 or newer and keep the file owned by root with mode `0600`. Set `DevelopmentWorkspaces__StorageRootIds__0` to the approved root ID and `DevelopmentWorkspaces__CreateGroupIds__0` to the authorized Authentik group UUID. Set `DevelopmentWorkspaces__Runtimes__node__Version` and `DevelopmentWorkspaces__Runtimes__node__Image` for Node, or use the `dotnet` runtime key for .NET. Each image must use an immutable digest and be available to the Agent. Apply the same policy when recreating both services. The Runner requires Host Agent 1.65 or newer with signed run control enabled.

This installer currently targets Ubuntu/Debian with systemd, `/data` and `/dataNvme`. Source checks report HTTP response latency; they do not automatically switch to an untrusted package mirror. An existing signed apt source is reused. A fresh Docker source is signed with the official key. Interactive mirror setup prompts `Y/n`; unattended runs preserve the current configuration unless explicitly configured. A mirror change merges unknown Docker settings, validates the candidate, waits for Docker and restores the original on failure.

The installer never accepts arbitrary commands or script paths. An existing repository must be clean and its origin must match before it is fast-forwarded. It does not delete database bind mounts, replace an existing Docker configuration wholesale, or expose database ports. Migration from the legacy running stack is an explicit separate operation; the dependency installer detects conflicting data mounts and stops.

The default source branch is `master`; unreleased development checkout requires `--source-branch dev`. The full install has **not** passed workstation acceptance yet. See [implementation status](../../docs/IMPLEMENTATION_STATUS.md) before attempting a production deployment.

Reference: [1Panel official installer](https://github.com/1Panel-dev/installer/blob/v2/install.sh). Whale Deck uses its own scripts and retains the project's HTTP/IP, Authentik and database-network boundaries.

Current scope and acceptance are defined by the [59-task goal and explicit additions](../../docs/GOAL_P0_P5.md). The C# Tavern/provider/Live2D dependencies are planned; this installer does not yet claim to install or validate them. The Docker mirror configuration rollback above is a connection/configuration safety mechanism, not automatic rollback of a dev application release; dev releases continue to be fixed and overwritten.

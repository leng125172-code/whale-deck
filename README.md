# WhaleDeck

Whale Deck is a containerized control plane for the Precision 7920 workstation, with a host-side Agent and maintenance entry point. The current MVP/P1 backend, Vue management UI and workstation integration are deployed on the target workstation; GitLab and the explicitly marked `HOLD` features remain deferred.

See [implementation status](docs/IMPLEMENTATION_STATUS.md) for the distinction between implemented code, placeholders and pending workstation acceptance. A passing build does not mean the complete backend is ready for production.

## Technology

- ASP.NET Core 10 Web API and .NET Worker Service
- EF Core 10 with Npgsql for PostgreSQL 18
- Valkey 9 for expiring cache and authorization snapshots via the Redis-compatible distributed-cache interface
- Vue 3, TypeScript, Vite, Pinia, Vue Router, and Element Plus
- Nginx gateway serving the Vue build and proxying API and Authentik traffic

The repository is a modular monolith. `Domain` has no infrastructure dependency, `Application` contains use cases and ports, `Infrastructure` implements persistence and external integrations, and `Api` owns HTTP/OIDC concerns.

WhaleDeck has no local password store and does not duplicate Authentik user profiles. Interactive login uses Authentik through the backend OIDC authorization-code flow. Authentik subject IDs and group claims are mapped to platform-specific permissions and immutable audit events; Authentik remains the source of truth for names, email addresses, groups, credentials, MFA, and account state.

## Product scope

The product baseline starts in [docs/FEATURES.md](docs/FEATURES.md). The complete page, use-case, Agent RPC, permission, data-model, and delivery breakdown is indexed in [docs/README.md](docs/README.md).

## Repository layout

```text
src/
  WhaleDeck.Api/
  WhaleDeck.Application/
  WhaleDeck.Contracts/
  WhaleDeck.Domain/
  WhaleDeck.Infrastructure/
  WhaleDeck.Worker/
  WhaleDeck.Agent/
  WhaleDeck.MaintenanceHost/
  WhaleDeck.Migrator/
web/
tests/
deploy/gateway/
deploy/host/
deploy/install/
install.sh
```

## Branch model

- `master`: stable, deployable releases only
- `dev`: integration branch for active development
- Feature branches start from `dev` and merge back into `dev`
- Release changes move from `dev` to `master` through a pull request

Direct feature work must not be committed to `master`.

## Local verification

```powershell
dotnet restore
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build

Set-Location web
corepack pnpm install --frozen-lockfile
corepack pnpm lint
corepack pnpm test:unit
corepack pnpm build
```

If the workstation exports a SOCKS URL through `HTTP_PROXY` or `HTTPS_PROXY`, Node/Corepack may reject it. Clear those variables only for the current install process or provide the local proxy using an `http://127.0.0.1:<port>` URL. Do not change the system proxy as part of the project scripts.

## Runtime configuration

Copy `.env.example` to `.env`, set mode `0600` on Linux, and replace every placeholder. Secrets are never committed. Infrastructure settings come from environment variables, dynamic platform settings live in PostgreSQL, and public Vue settings are written to `/tmp/runtime-config.js` when the gateway starts.

The current workstation entry points are:

- Whale Deck: `http://<reachable-workstation-address>:8080`
- Authentik: `http://<reachable-workstation-address>:8081`

Use the Authentik root address for normal login, not `/setup`. Initial setup is a one-time flow. The installer finalizes an already configured administrator and Whale Deck OIDC instance with a guarded, idempotent setup-completion check; it never resets the administrator password. `deploy/validation/inspect_authentik_setup.py` reports non-secret setup state through `ak shell` for troubleshooting.
- GitLab: deferred; no port is currently published

The host-side MaintenanceHost binds `0.0.0.0` on ports `8080/8081`, so wired, USB and future workstation interfaces can use the same service without hard-coded interface addresses. The gateway publishes only loopback upstream ports (`18080` and `18081`). API, Worker, databases, cache and Authentik do not publish host ports. API and Worker must never mount `/var/run/docker.sock`; container operations use the Agent Unix socket and an allow-listed resource registry.

The desired friendly LAN entry is `http://precision-7920-tower.local` for Whale Deck, with other services separated by explicit ports. This is not yet the active no-port endpoint: switching the stable entry from 8080 to 80 requires a final port-conflict and client-resolution check.

## Staged installation

The [installation guide](deploy/install/README.md) describes the seven-stage Linux workflow: tool/source checks, optional Docker accelerator configuration, dependency repository deployment, then source build and deployment. `bash install.sh --dry-run` previews the workflow without network or system changes. `--dependencies-only` excludes the Whale Deck build/deployment stage.

Agent、MaintenanceHost、UDS 权限/对端身份、受限 helper、动态全网卡入口、容器桥隔离以及 Docker 重启韧性已在目标工作站验收通过。API、Worker 与 Gateway 已构建并部署，数据库、备份、应用、身份、治理和主机管理流程已完成真实依赖验收；旧 `dcfs*` 运行时对象与临时全量 sudo 规则也已完成安全清理。准确的完成边界与后续发布项见 [实施状态](docs/IMPLEMENTATION_STATUS.md)。

## Container boundaries

- `whaledeck-gateway`: `whaledeck-app-ui` and `database-platform-app-authentik`
- `whaledeck-api`: `whaledeck-app-ui`, `database-platform-app-authentik`, `database-platform-db-postgres`, and `database-platform-cache-general`
- `whaledeck-worker`: `database-platform-db-postgres` and `database-platform-cache-general`
- `migrator`: one-shot tool on `database-platform-db-postgres`, run before API/Worker start

Docker uses the `local` logging driver with a maximum of five compressed 10 MiB files per container. Application request logs below server errors are emitted at debug level to avoid repeating the previous log-volume incident.

The Dockerfiles provide pinned development tag defaults. Before a formal installation, create `.images.env` from `.images.env.example` with immutable base-image digests. The installer rejects missing digests before the image-build stage. Record approved digests in release configuration; never mix credentials into image-version files.

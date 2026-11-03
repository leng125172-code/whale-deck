# Repository instructions

- Target .NET 10 and keep nullable reference types enabled.
- Preserve the `Domain -> Application -> Infrastructure -> Api/Worker` dependency direction.
- Controllers handle transport only; do not put persistence or business rules in controllers.
- Use EF Core migrations for every schema change. Never call `EnsureCreated` in production code.
- Keep authentication in the ASP.NET Core BFF. Authentik is the source of truth for credentials and user profiles; do not add local password or duplicate profile tables. Never store OIDC tokens in browser local storage.
- Use Element Plus CSS variables and components before introducing custom equivalents.
- Preserve the sticky translucent top bar, its scroll transition, dark theme, responsive behavior, and reduced-motion support.
- Do not expose API, Worker, database, cache, or Authentik container ports directly on the host.
- Do not mount the Docker socket into WhaleDeck API or Worker containers.
- Log to stdout/stderr as structured JSON. Do not add unbounded file logs or high-frequency information logs.
- Keep secrets out of Git and use environment variables or mounted secret files.
- Pin application dependencies and container images; production images require immutable digests.
- Work on `dev` or a feature branch. `master` must remain deployable.
- Set `GIT_OPTIONAL_LOCKS=0` for read-only Git checks run as root during QA, so index refresh does not replace the user's Git index with a root-owned file. Keep repository index ownership with the repository user; do not recursively change ownership of private evidence or the repository.

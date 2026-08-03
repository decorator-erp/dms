# Progress log

One line per task: ID, status, files touched, anything decided that PLAN.md did not specify.

---

**G0-01** — done — `.gitignore`, `DMS.Modular.slnx`, `Directory.Build.props`, `Directory.Packages.props`,
`docker-compose.yml`. Decided: Postgres host port 5433 (avoids a local 5432); central package versions
resolve the backend's Microsoft-package 10.0.9/10.0.10 drift to 10.0.10;
`ManagePackageVersionsCentrally=true` added to `Directory.Packages.props` (plan text omitted it).
`dotnet build` on the project-less solution emits a harmless "nothing to restore" warning, not an
error — treated as pass.

**G0-02** — done — `src/SharedKernel/` (9 files: `Entity`, `Result`, `Error`, `IDomainEvent`,
`IDomainEventHandler`, `IDomainEventDispatcher`, `IDomainEventQueue`, `IAuditable`,
`AuditIgnoreAttribute`), ported byte-for-byte from `../backend/SharedKernel/`. csproj has no
`PropertyGroup` beyond the SDK default — everything (target framework, nullable, implicit usings,
warnings-as-errors) comes from `Directory.Build.props`. Zero package references, zero project
references, confirmed by `dotnet build -warnaserror`. **Known gap recorded, not fixed:** `IAuditable`
and `AuditIgnoreAttribute` come across, but the EF audit interceptor that acts on them belongs to the
out-of-scope Notifications & Audit module. `Employee` (G2-02) will implement `IAuditable` and no audit
rows will ever be written for it in this scope. Owner of the gap: the Notifications & Audit module,
when built.

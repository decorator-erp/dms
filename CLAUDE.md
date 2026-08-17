# dms-modular

Modular monolith. Anatomy: ../backend/docs/backend/architecture-anatomy.md · Plan: PLAN.md

## Rules
- No repository pattern. Handlers use their module's DbContext directly.
- One DbContext per module, one schema per module, one database.
- Cross-module reads: <Module>.Contracts interfaces. Never another module's tables.
- Cross-module writes: integration events on the outbox. Never direct calls.
  - **One recorded exception: `INotificationService`.** Jobs and handlers call it synchronously from
    seven modules. Routing a notification through the outbox would add a commit and a delivery hop to
    produce a row that *is* the notification. The price: it commits in its own transaction, so a
    handler that notifies **must pass a dedupe key** or an outbox retry duplicates. See N1-04.
- Domain events = in-transaction, intra-module. Integration events = post-commit, cross-module.
- Integration event payloads: IDs and primitives only.
- Sync cross-module calls are depth-1. A→B→C means the design is wrong.
- Every contract that returns one thing also returns many. No exceptions.
- Only *.Contracts types are public.
- Four projects per module: Contracts, Domain, Application, Infrastructure. Not three — collapsing Application into Infrastructure kills arch rule 8 (F2, closed).
- SharedKernel is frozen at ~9 files.
- Authorization order: route claims (403) → module scope decorator (404) → aggregate state (409).

## Commands
- Build: dotnet build DMS.Modular.slnx -warnaserror
- Test:  dotnet test DMS.Modular.slnx
- Arch:  dotnet test tests/Architecture.Tests
- DB:    docker compose up -d

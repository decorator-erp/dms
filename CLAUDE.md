# dms-modular

Modular monolith. Anatomy: ../backend/docs/backend/architecture-anatomy.md · Plan: PLAN.md

## Rules
- No repository pattern. Handlers use their module's DbContext directly.
- One DbContext per module, one schema per module, one database.
- Cross-module reads: <Module>.Contracts interfaces. Never another module's tables.
- Cross-module writes: integration events on the outbox. Never direct calls.
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

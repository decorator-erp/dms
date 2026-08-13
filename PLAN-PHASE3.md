# Phase 3 — Housekeeping + Documents module (H1-01 → G8-01)

Continues `PLAN.md` (G0–G3) and `PLAN-PHASE2.md` (G4–G6). Same per-task loop, gate, commit format, and
`PROGRESS.md` discipline. **Runs unattended start to finish** — hard stop conditions only.

Current head: `e4be811`. Four modules (Identity, Hr, Approvals, Projects). F2 closed won't-do;
wake-on-write **adopted but not built** — W1 builds it.

**CRM moves to Phase 4.** Documents first: it is small (3 entities, 31 feature files), every dependency
already exists, and it introduces file storage — the one genuinely new infrastructure concern — at
small scale rather than buried inside CRM's 98 files.

---

# Part H1 — Housekeeping (blocking; nothing else starts until clean)

The working tree at `e4be811` has 59 modified files totalling ±6,000 lines. Verified by diffing
`InitialHr.cs`: every line shows removed-and-re-added with identical content — a CRLF↔LF flip from the
folder rename, not real change. Mixed into that noise is a **real** refactor (`Program.cs` −68 lines →
new `src/Api/DI.cs` + `src/Api/Extensions/OutboxDbMigrationExtension.cs`, `HttpCurrentUser` moved to
`BuildingBlocks.Web.Authorization`).

Committing these together makes the refactor unreviewable and poisons `git blame` on 59 files
permanently. Three commits, strictly in this order.

### H1-01 — `.gitattributes` + line-ending normalization, alone

Create `.gitattributes` at repo root:

```
* text=auto eol=lf
*.sln    text eol=crlf
*.slnx   text eol=crlf
*.csproj text eol=crlf
*.props  text eol=crlf
*.cs     text eol=lf
*.md     text eol=lf
*.yml    text eol=lf
*.json   text eol=lf
*.sql    text eol=lf
```

Then `git add --renormalize .` and commit **only** the normalization plus `.gitattributes`.
`src/Api/DI.cs`, `src/Api/Extensions/`, and the untracked docs must **not** be in this commit — stash
them first if git entangles them.

**Verify:** full gate green (185/185 — no behavior touched). `git diff -w HEAD~1 HEAD` is **empty** —
that emptiness is the proof this commit changed nothing but line endings.

### H1-02 — the `DI.cs` / `Extensions` refactor, alone

Commit the real change now that it is legible: `src/Api/DI.cs`, `src/Api/Extensions/`, the `Program.cs`
reduction, `HttpCurrentUser`'s move, and the `.csproj`/`.slnx` edits that belong with them.

Read the diff before committing — this is the first substantive change in the tree that never passed
through a task's verify block. Anything in it unrelated to "extract Program.cs composition into DI.cs"
gets split out.

**Verify:** full gate green; `Integration.Tests` smoke proves the app still boots.

### H1-03 — docs and decision records

Commit the untracked planning/review files (`PLAN-PHASE2.md`, the six `REVIEW-*.md`, this plan) and the
eight hosting-cost docs.

**Consolidate the eight cost docs into one `docs/hosting-cost-analysis.md`** — they overlap heavily and
eight files on one question will mislead the next reader about which is current. Keep
`docs/low-resource-hosting.md` separate: it holds the adopted wake-on-write decision, which is a
decision record, not an exploration.

**Verify:** `git status` clean; full gate green.

---

# Part W1 — Outbox wake-on-write

`e4be811` records the decision as adopted; no code implements it. A decision executed nowhere is the
exact failure mode the F2 closure documented.

### W1-01 — `OutboxSignal` + wake-on-write delivery

Per `docs/low-resource-hosting.md` §2:

```csharp
// BuildingBlocks/Outbox/OutboxSignal.cs — singleton
public sealed class OutboxSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);
    public void Nudge() { try { _signal.Release(); } catch (SemaphoreFullException) { } }
    public Task WaitAsync(TimeSpan fallback, CancellationToken ct) => _signal.WaitAsync(fallback, ct);
}
```

- Nudge **after the transaction commits**, never before — a nudge on an uncommitted row races the
  processor into finding nothing. The commit points are `InboxDedupeDecorator` and
  `DomainEventDispatchDecorator` (both wrap explicit transactions as of Phase 2).
- `OutboxProcessor` replaces its `PeriodicTimer` with `await signal.WaitAsync(fallback, ct)`.
- `Outbox:FallbackPollSeconds`, default **60**.

**Do not remove the fallback.** The signal is an optimization; the poll is the correctness guarantee
(crashed requests, second instances).

**Verify:** existing outbox tests green unchanged. New test — enqueue an event, assert the handler runs
well inside the fallback window (proves the nudge path). Second test — insert an outbox row **directly
via SQL**, bypassing `Enqueue` so no nudge fires, and assert the fallback still delivers it. That second
test is what stops someone deleting the fallback later.

---

# Part G7 — Documents module

**Scale:** 3 entities (`DocumentFolder`, `ProjectDocument`, `FileTemplate`), 31 feature files
(Documents 24 — Files 13, Folders 10, `DefaultFolderResolver` 1; FileTemplates 7), 5 endpoint files,
6 permission strings (`documents.{read,upload,update,delete}`, `file_templates.{read,manage}`).
**8 tasks.**

## Two structural firsts — resolve these before writing code

### 1. `GateConditionContext` must gain `ReferencedDocumentId` — a Projects.Contracts change

Verified: `Projects.Contracts/IGateConditionTypeHandler.cs:10-12` defines

```csharp
public sealed record GateConditionContext(
    Guid OwnerId, Guid OwnerProjectId, GateOwnerKind OwnerKind,
    Guid? ReferencedTaskId, Guid? ReferencedStageId, ApprovalType? ApprovalType);
```

The backend's `DocumentApprovedHandler` requires `input.ReferencedDocumentId`. There is no such member,
so **adding a Documents gate type requires editing a Projects contract** — the first time a new module
forces a change in an existing module's public surface.

Resolution: add `Guid? ReferencedDocumentId` as an additional optional positional parameter (additive,
source-compatible for existing callers using named/positional args in order). Do **not** replace the
record with a property bag or a `Dictionary<string, object>` — that trades compile-time safety for
convenience and every future gate type would erode it further.

**The general rule to record in `docs/adding-a-module.md`:** a module contributing a gate type may need
to extend `GateConditionContext`; that is expected and additive-only. Removing or repurposing an
existing member is not.

### 2. The first bidirectional Contracts pair — Documents ↔ Projects

Documents depends on Projects (`documents → projects` in the FK allow-list; Documents implements
`IGateConditionTypeHandler` from `Projects.Contracts`). But attachment-gate satisfaction flows the other
way: a document upload must satisfy a gate that lives in **Projects**, and Documents may not write
Projects' tables (rule 6) nor call a cross-module write synchronously.

So Documents publishes `DocumentUploadedIntegrationEvent` (in `Documents.Contracts`) and **Projects
subscribes** — meaning `Projects.Infrastructure` references `Documents.Contracts` while
`Documents.Infrastructure` references `Projects.Contracts`.

**This is legal and is not a cycle.** `*.Contracts` assemblies reference only SharedKernel and
BuildingBlocks — never their own module's implementation — so the reference graph stays acyclic. Arch
rule 3 forbids cross-module `.Domain`/`.Application`/`.Infrastructure` references; Contracts is exempt
by design. It is the same shape already proven in Phase 2 (Projects both calls `IApprovalRequester` and
subscribes to `ApprovalDecidedIntegrationEvent`, both from `Approvals.Contracts`) — this is just the
first time the two halves live in *different* modules.

**Verify explicitly at G7-06** that `dotnet build` resolves with no circular-reference error, and record
the reasoning in `PROGRESS.md`. A future module author will hit this and needs to know it was decided,
not overlooked.

## Pre-resolved facts (verified against `../backend` — do not re-derive)

**`IFileStorageService`** — port as-is: `long MaxUploadBytes { get; }`, `SaveAsync(Stream, string
originalFileName, string subFolder, ct) → string relativePath`, `OpenReadAsync(string) → Stream?`,
`DeleteAsync(string)`. `LocalFileStorageService` reads `FileStorage:RootPath` (default `uploads`) and
`FileStorage:MaxUploadBytes` (default 52,428,800 = 50 MB), stores as `{Guid:N}{ext}` under a sanitized
subfolder, returns forward-slash relative paths, and the DB stores only that path.

**Entity shapes.** `ProjectDocument`: `ProjectId`, `FolderId?`, `ProjectStageId?`, `ProjectTaskId?`,
`FileName`, `StoragePath`, `ContentType`, `FileSizeBytes`, `FileExtension`, `Description?`,
`UploadedById`, `UploadedOnUtc`, plus **version lineage** (`Version`, starts 1, increments per
revision). `DocumentFolder`: `Name`, `ProjectId`, `ParentFolderId?` (self-referencing tree),
`ProjectStageId?`/`ProjectTaskId?` (XOR, or neither = project-wide), `IsDefault` (at most one per
stage/task), `SubFolders`, `Documents`.

**Every `Project*` navigation becomes a bare Guid.** `ProjectDocument.Project`,
`.ProjectStage`, `.ProjectTask`, `DocumentFolder.Project`, `.ProjectStage`, `.ProjectTask`,
`ProjectDocument.UploadedBy` (→ Employee) — ids only, no navigations, FKs at the DB level (G7-04).
`DocumentFolder.ParentFolderId` and `.Documents` **stay navigations** — same module.

**`AttachmentHandler.ValidateAsync` is a no-op** returning `Result.Success()`. Its gate is satisfied
reactively by upload, not by validation. `DocumentApprovedHandler` validates that the referenced
document lineage exists and belongs to the same project — in the new module it reads Documents' own
`DbContext` directly, so the backend's `ICrmGateReader` indirection is **not** ported.

## Scope boundaries

| Deferred | Owner |
|---|---|
| `CrmArtifactDocument` (CRM's own artifact metadata) | CRM (Phase 4) |
| `SiteDocument` | Sites |
| Finance document attachments (invoices, BOQ exports) | Finance |
| Notification on upload/approval — events publish, zero handlers | Notifications |
| Cloud storage (S3/Azure Blob) — `IFileStorageService` is the seam; local disk only for now | ops decision |

## Tasks

### G7-01 — `Documents.Contracts`

`IDocumentQueries` (`ForProjectAsync`/`ForStageAsync`/`ForTaskAsync` + batch `GetManyAsync` →
`DocumentSummary`), `IFileStorageService`, `DocumentsPermissions` (the 6 strings verbatim),
integration events (`DocumentUploadedIntegrationEvent` with `DocumentId`, `ProjectId`, `ProjectStageId?`,
`ProjectTaskId?`, `FolderId?`, `FileName`, `Version`, `UploadedById`, `OccurredAtUtc` — IDs and
primitives only), and commands/queries/DTOs for all 31 features, grouped per-noun like `Hr.Contracts`.

**Verify:** references only SharedKernel + BuildingBlocks + `Projects.Contracts`. Rule 5 green. Rule 10
green.

### G7-02 — `Projects.Contracts` extension + `Documents.Domain`

Two halves, one commit:

- Add `Guid? ReferencedDocumentId` to `GateConditionContext` (structural item 1). Projects' existing four
  handlers ignore it; nothing else changes in Projects.
- `Documents.Domain`: the three entities per the fact section, `FileTemplateCategory` enum,
  `DocumentFolderErrors`/`ProjectDocumentErrors`/`FileTemplateErrors`, and a
  `DocumentUploadedDomainEvent`.

**Verify:** no EF reference in `Documents.Domain`; Projects' existing tests green unchanged (proves the
context extension is source-compatible); unit tests for folder-tree invariants (no cycles, one default
per stage/task, XOR scope) and version-lineage increment.

### G7-03 — `Documents.Infrastructure` — persistence

`DocumentsDbContext`, schema `documents`, `ConfigureOutbox(ownsMigration: false)`, 3 configurations,
initial migration. **`SchemaOrder` gains `"documents"` appended at the end** — Documents depends on
projects/hr/approvals, all of which already migrate earlier, so unlike CRM this is a clean append with
no reordering question. Add the four projects to `Architecture.Tests.csproj` (meta-test red, then green).

**Verify:** migration applies; probe migration empty (full `--context DocumentsDbContext` invocation);
rules 1/4/6/8a fault-injected for the new assemblies.

### G7-04 — Cross-schema FKs

Via `migrationBuilder.Sql`, all `ON DELETE RESTRICT`, each with its index:

| Column | Target |
|---|---|
| `documents.project_documents.ProjectId` | `projects.projects` |
| `documents.project_documents.ProjectStageId` (nullable) | `projects.project_stages` |
| `documents.project_documents.ProjectTaskId` (nullable) | `projects.project_tasks` |
| `documents.project_documents.UploadedById` | `hr.employees` |
| `documents.document_folders.ProjectId` | `projects.projects` |
| `documents.document_folders.ProjectStageId` / `.ProjectTaskId` (nullable) | `projects.*` |

`documents → projects` and `documents → hr` are already in rule 9's allow-list. Extend
`docs/cross-schema-foreign-keys.md` in the same commit, same reasoning format as G4-03/G5-04.

**Verify:** rule 9 green with the new pairs; every FK present in `pg_constraint`.

### G7-05 — `Documents.Infrastructure` — file storage

`LocalFileStorageService` per the fact section. Add hardening the backend lacks:

- **Reject path traversal** in `subFolder` and `originalFileName` — sanitize, then assert the resolved
  absolute path is still under the configured root. A test with `../../etc/passwd` must fail closed.
- **Enforce `MaxUploadBytes`** before streaming to disk, not after.
- **Content-type allow-list** from config, rejecting executables by default.

`FileMode.CreateNew` with a GUID name already prevents collisions — keep that.

**Verify:** unit tests for save/read/delete round-trip, the traversal cases, the size cap, and that
`DeleteAsync` on a missing file does not throw.

### G7-06 — `Documents.Application` — folders + templates

Folders 10 files + `DefaultFolderResolver` + FileTemplates 7. Folder scope validation (`ProjectStageId`
XOR `ProjectTaskId` XOR neither) and the default-folder rule live here.

**Also here:** the bidirectional-Contracts build check from structural item 2 — `Projects.Infrastructure`
gains its `Documents.Contracts` reference in G7-07, so confirm at that point that the solution builds
with no circular-reference error and record it.

**Verify:** integration tests — folder tree CRUD, move/rename, default-folder creation for a stage and a
task, template CRUD.

### G7-07 — `Documents.Application` — files, gates, and the Projects subscription

Files 13 files (upload, upload-to-stage, upload-to-task, upload-revision, move, delete, download, and
the five queries). Upload publishes `DocumentUploadedIntegrationEvent`.

**The two gate handlers**, implementing `Projects.Contracts.IGateConditionTypeHandler`, registered by
`DocumentsModule`: `AttachmentHandler` (validation is a no-op per the facts) and
`DocumentApprovedHandler` (validates the referenced lineage exists in the same project, reading
Documents' own `DbContext`).

**In `Projects`:** an `IIntegrationEventHandler<DocumentUploadedIntegrationEvent>` in
`Projects.Infrastructure` that satisfies open `Attachment` gates for the uploaded document's
stage/task. This is the structural-item-2 direction — `Projects.Infrastructure` →
`Documents.Contracts`.

`ApprovalSubjectType.ProjectDocument` also becomes real: requesting approval on a document uses
`IApprovalRequester`, and Documents subscribes to `ApprovalDecidedIntegrationEvent` filtered to
`SubjectType == ProjectDocument` — the third module on that event, alongside Projects
(`GateCondition`) and, in Phase 4, CRM.

**Verify:** upload → `Attachment` gate satisfied through the real delivery path
(`OutboxProcessor.ProcessPendingAsync`, not a direct call); a Projects gate of type `Attachment` and one
of type `DocumentApproved` now **create successfully** where Phase 2 rejected them — assert that
transition explicitly; a `ProjectDocument`-subject approval decision reaches Documents and **not**
Projects or Approvals' other subscribers.

### G7-08 — Endpoints, module registration, integration suite

`DocumentsModule : IModule`; the 5 endpoint files (`DocumentFolder`, `ProjectDocument`,
`ProjectStageDocument`, `ProjectTaskDocument`, `FileTemplate`); `IDocumentQueries` as projections.

**Upload endpoints need explicit limits:** `RequestSizeLimit` matching `MaxUploadBytes`, multipart
form limits, and streaming rather than buffering the whole file in memory — a 50 MB buffered upload on a
1 GB instance is a real problem given the hosting target.

Characterization suite: project → folder tree → upload → revision → download → attachment gate
satisfied → document approval → `DocumentApproved` gate satisfied. Run twice for flake check. Extend
`CrossModuleSmokeTests` to five modules. Schema-isolation assertion: no SQL from `DocumentsDbContext`
touches `projects.`/`hr.`/`approvals.`.

**Verify:** app boots with five modules; `/scalar` lists document routes; `GET /api/permissions`
contains all 6 strings; oversized upload → 413; suite green twice; all 10 arch rules green.

---

# Part G8 — Close-out

### G8-01 — Docs + final report

Append to `docs/adding-a-module.md`: the `GateConditionContext`-extension rule (additive only), the
bidirectional-Contracts pattern and why it is not a cycle, and the file-upload hardening checklist.
Update `docs/cross-schema-foreign-keys.md` with the Documents pairs. `PROGRESS.md` phase boundary —
seven non-negotiables re-checked against five modules; deferred table updated (CRM/Sites/Finance rows
now name Documents' seams).

Final report: task table with SHAs, test counts by suite, every decision this plan did not specify,
anything thinner than intended, anything in this plan that looks wrong having built it. **Plus: what
Documents taught that should change the Phase 4 CRM plan** — CRM is 10× the size and this is the cheapest
moment to catch a pattern that will not scale.

---

# Execution contract

Per task: restate → implement → task verify → full gate (`dotnet build DMS.Modular.slnx -warnaserror` /
`dotnet test DMS.Modular.slnx` / `dotnet format DMS.Modular.slnx --verify-no-changes`) → ≤3 attempts →
one commit `"<TASK-ID>: <what>"` → one `PROGRESS.md` line → next task immediately, no pauses, through
G8-01.

**Hard stops:** a verify failing 3 attempts; any fix requiring an arch rule relaxed/rescoped, a test
deleted, an internal type made public, or a forbidden reference; ambiguity that changes an outcome; any
write under `..\backend`; any module or stub beyond Documents; reopening F2 or any closed decision.

**H1 is blocking** — G7-01 does not start until the tree is clean and the gate is green on a committed
state.

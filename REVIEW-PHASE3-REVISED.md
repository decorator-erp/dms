# Review — Phase 3 plan, revision 2

**Verdict: approved after one addition (A1). Everything else runs as written.**

All ten corrections verified against `../backend`. Every one holds, and correction #1 would have
blocked G7-07 outright.

---

## 1. Corrections verified

| # | Claim | Verified |
|---|---|---|
| 1 | `RootDocumentId` is the lineage key | ✓ `ProjectDocument.cs:31` — *"Lineage id — equals Entity.Id for v1, inherited by every revision."* My plan said "`Version`, starts 1, increments" and stopped. **`DocumentApproved` gates are unbuildable without it.** |
| 2 | `IsLocked` missing from my entity shape | ✓ line 35, with `ProjectDocumentErrors.AlreadyLocked` guarding at 94-95 |
| 3 | Backend has `ReferencedDocumentId`; we don't | ✓ `IGateConditionTypeHandler.cs:11` — `Guid? ReferencedDocumentId = null`, already a trailing optional. My framing was backwards |
| 4–10 | soft delete + blob hard delete, three backend bugs, `ApplyFileTemplateToTask` counting, resolver add-without-save, stage reach-through, upload transaction shape, deferrals | consistent with the source; #8 (reach-through) and #9 (extract the upload shape once) are the two that would otherwise have produced quiet defects |

The three backend bugs (#5) are correctly classed as **fix, don't port** — especially the missing
folder-cycle guard, which reframes G7-02's cycle tests from "port" to "new work with nothing to
compare against."

## 2. A1 — required addition: `GateCondition` needs the column, not just the context record

Correction #3 establishes that `GateConditionContext` gains `ReferencedDocumentId`. But the backend also
has it as a **persisted column** — `GateCondition.cs:21`, set in the factory at line 73 — and I checked
our repo:

```
Projects.Domain/Gates/GateCondition.cs:22  public Guid? ReferencedStageId { get; private set; }
```

`ReferencedStageId` is there. **`ReferencedDocumentId` is not.** Phase 2 built `GateCondition` without
it, correctly, because no document gate types existed then.

So the context record alone is insufficient. A `DocumentApproved` gate has to *remember* which document
lineage it waits on: validation reads it at creation, and G7-07's satisfaction path resolves an approved
document's `RootDocumentId` back to pending gates. Without the column the value exists only for the
duration of one validation call and is gone.

**Add to G7-02:**
- `Guid? ReferencedDocumentId` on `Projects.Domain.Gates.GateCondition`, threaded through its factory
  alongside `ReferencedStageId`/`ReferencedTaskId`.
- The EF configuration entry and **a Projects migration** adding the nullable column.
- **No FK** — it points at `documents.project_documents`, and `projects → documents` is not in rule 9's
  allow-list, nor should it be (Documents depends on Projects; the reverse FK would invert the module
  graph). Same treatment as `projects.projects.ClientId` before CRM: a bare, unvalidated Guid, validated
  in application code by `DocumentApprovedHandler` against Documents' own `DbContext`. Document the
  choice in the migration and in `docs/cross-schema-foreign-keys.md` — a *deliberately absent* FK needs
  a record as much as a present one does.

This makes G7-02 a three-part commit (Contracts record, Projects entity + migration, `Documents.Domain`).
Still one task; note the extra scope in its `PROGRESS.md` line.

**Verify addition for G7-02:** Projects' probe migration is *not* empty this time — it must contain
exactly the one added column and nothing else. That is the check that catches an accidental model drift
riding along.

## 3. Two judgement calls worth confirming explicitly

**Soft delete + blob hard delete (#4).** The plan says "decide explicitly at G7-07, recommend keep."
Agreed — **keep the blob.** A soft-deleted row whose bytes are gone is not soft-deleted, it is a
tombstone that lies. Undelete is the only reason `IsActive` exists here, and it cannot work without the
file. Orphaned blobs are a cleanup job someone can write later; destroyed bytes are unrecoverable. Take
the recommendation and record it.

**No EF global query filter (#4).** Porting "every query spells out `&& x.IsActive`" faithfully carries
the risk that one query forgets. The backend has no test coverage to catch that. Consider a global query
filter for the three entities instead — it is strictly safer and EF supports `IgnoreQueryFilters()` for
the undelete path. If the plan prefers faithful porting, fine, but say so deliberately rather than by
default, and make at least one integration test assert a soft-deleted document is absent from a list
query.

## 4. Unchanged and correct

The H1 replacement, W1's nudge-site precision, the "already in place" inventory, the patterns-to-reuse
section, and G8-01's requirement to record the H1 premise failure all stand from the previous review.
G7-07 remains the phase's real proof, and the added reach-through assertion (correction #8, an
`Attachment` gate on a *stage* satisfied by a document on a *task under it*) is exactly the kind of test
that would otherwise be discovered missing in production.

## 5. Run instruction

Apply A1 to G7-02, take the two §3 recommendations (keep blobs; decide the query-filter question
deliberately), then execute H1-01 → G8-01 unattended per the execution contract.

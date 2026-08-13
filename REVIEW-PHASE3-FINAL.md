# Review — Phase 3 plan, revision 3 (final)

**Verdict: approved. Run H1-01 → G8-01 unattended. No changes.**

A1 is absorbed correctly, and both open questions from the last round were closed with reasoning
rather than assertion.

---

## Verified in this pass

| Claim | Result |
|---|---|
| Zero `HasQueryFilter` calls anywhere in `src/` | ✓ no matches — the "Documents would be the first module behaving differently from its neighbours" argument is factually grounded, not rhetorical |
| `ProjectConfiguration.cs:26` is the no-FK precedent | ✓ verbatim: *"ClientId: no FK - CRM absent, unvalidated Guid (documented in the migration + docs/cross-schema-foreign-keys.md)"* — A1's `ReferencedDocumentId` follows an established, documented pattern rather than inventing one |

## The query-filter decision is better reasoned than my recommendation

I flagged the explicit-`&& x.IsActive` approach as risky (one forgotten predicate, no backend tests to
catch it) and suggested a global filter. The plan takes the opposite call and gives two reasons I had
not weighed:

1. **Consistency across modules is itself a safety property.** Four modules filter explicitly; making
   Documents auto-filter means a reader moving between modules must remember which convention applies
   where. That is a worse failure mode than the one the filter prevents.
2. **`IgnoreQueryFilters()` is all-or-nothing per query.** The lineage max-version lookup and the stage
   reach-through join (correction 8) would both need it, disabling filtering for *every* entity in those
   queries — a subtler footgun than a repeated predicate, and one that fails silently.

And it replaces the guard with tests rather than dropping it: G7-07 asserts a soft-deleted document is
absent from a list query *and* does not break lineage resolution. That converts my objection into
coverage, which is the right answer. Decision accepted.

The keep-the-blob call is likewise recorded with its reasoning and paired with a verify
("deleting a document leaves its blob on disk"), so it cannot silently regress.

## One note for execution, not a change

G7-02's verify — *"Projects' probe migration is **not** empty this time; it must contain exactly the one
added column and nothing else"* — is the single most valuable check in the phase. It is the only place
a stray model drift in a **four-module-old** context would surface, and the temptation when it shows
extra content will be to accept it as "probably fine." It is not. If that probe contains anything beyond
`ReferencedDocumentId`, stop and report per the hard-stop conditions.

## Standing

Ten corrections across two revisions, all verified against source; one load-bearing catch
(`RootDocumentId`) that would have made `DocumentApproved` unbuildable; one follow-through (A1) that the
correction implied but did not state. The plan now describes the tree accurately, the backend
accurately, and this repo accurately — which is more than the document it replaces managed.

Execute.

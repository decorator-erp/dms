# Review — Phase 5 Sites execution plan

**Verdict: approved with one revision (R1). Everything else runs as written.**

Eight defects found in `PLAN-PHASE5.md` before code. I verified the two that change exit criteria;
both are mine, and D1 is the third instance of a specific mistake I keep making.

---

## 1. D1 and D2 verified — and D1 exposes a pattern in my work

**D1 — 7 permissions, not 6.** Confirmed, and the cause is exact:

```
my pattern:   '"sites\.[a-z_]*"'      → 6   (approve, create, delete, read, submit, update)
correct:      '"sites\.[a-z_.]*"'     → 7   (+ sites.definitions.manage)
```

`[a-z_]` excludes the dot, so a two-dot string was invisible to it. The `SiteDefinitions` group's own
comment confirms the split: *"WRITES to the form engine only… The matching reads are gated on
Sites.Read."* Shipping 6 would have left 18 definition routes without a correct permission.

**This is the third false negative I have produced from a too-narrow grep pattern**, each time reported
as verified fact:

1. Phase 4 — permission survey using `"crm\.*"`/`"clients\.*"` structurally could not see
   `requirements.*`.
2. E1 — test search for `"task under"` (with a space) could not match `ATaskUnderIt`.
3. Now — `[a-z_]*` could not match a second dot.

Three occurrences is a pattern, not bad luck. The rule I should be applying to myself, and which
belongs in `PROGRESS.md` alongside lesson 2: **a grep that returns a clean, plausible result is not
evidence until the pattern itself has been checked against a known-positive case.** Every one of these
would have been caught by asking "what would this pattern miss?" before trusting the output.

**D2 — 114, not 62.** Confirmed by counting: Approvals 3 + Crm 56 + Documents 6 + Hr 17 + Identity 4
+ Projects 21 = **107**, plus Sites' 7 = **114**. My "62" was `56 + 6` — CRM's own module count
(`CrmSmokeTests.cs:34`) mistaken for the system total. The brief inherited it, so the EXIT criterion
was wrong too.

D3–D8 are well-evidenced with file:line citations and I have no argument with any of them. D8's
reasoning is particularly sound: the variance route can only ever return `BriefProvenanceLost`, and
deferring it removes the module's only dependency on another module's permission string.

## 2. Three things the plan found that I missed entirely

- **The site-code sequence.** `SELECT nextval('…site_code_seq')`, `SITE-{n:D5}`, with
  `CodeAllocationFailed` as an asserted error. Real infrastructure requiring a migration object, absent
  from my plan. The note that the `AS "Value"` alias is load-bearing — omit it and `SqlQuery<T>` fails
  at runtime, *only on an API-created site* — is the kind of detail that costs an afternoon.
- **`GuardSiteScopeCoverage()`.** The startup guard that reflects over command types and throws if any
  carries a `Guid SiteId` without implementing `ISiteScoped`. This is why the 404-not-403 guarantee
  holds by construction rather than discipline, and it already caught a real miss in the backend. I
  described the decorator and never mentioned the thing that keeps it honest.
- **The `GenericRepository.GetByIdAsync` tracking footgun** — defaults to `asNoTracking: true` with all
  16 mutating call sites overriding it. Dropping the repository removes the footgun but each load must
  land tracked or the write silently disappears.

## 3. R1 (required) — B12 is not an "open question," it is an integrity gap

The triage puts B12 with B4 and B17 under *"port as-is and record as an open question."* B4
(unvalidated `CatalogItemId`) and B17 (usage counts include soft-deleted rows) are data-quality issues.
B12 is a different kind of thing.

Verified: `Site.cs:102` is `if (Status == SiteStatus.Approved) return SiteErrors.SiteLocked;` — only
**Approved** blocks edits. A `Submitted` site's records remain writable while it awaits a decision.

The consequence: **an approver's decision can be about content that changed after they reviewed it**,
and `ck_sites_status_stamps` records who approved and when — so the row asserts an approval of content
that no longer exists, with no trail. Notifications & Audit is not built, so there is no audit
mechanism to reconstruct what was actually approved.

**Porting as-is is still the right call** — blocking writes on `Submitted` is a product decision about
the surveyor's workflow, not a defect fix, and inventing it mid-port would be exactly the improvisation
this process forbids. But the *record* must match the severity:

- Move B12 out of the "open question" list into the **deferred/scope-boundary table**, phrased as an
  integrity gap with the consequence named: *a Submitted site's records stay editable, so an approval
  can attest to content that has since changed, and no audit trail exists to detect it.*
- Name the owner: a **product decision** (does a surveyor need to correct a Submitted survey before
  approval, or should submission freeze it?), not a future module.
- Surface it in S2-01's final report as a question for the user, not buried in a 19-item defect list.

B4 and B17 stay where they are.

## 4. On the two user-confirmed scope decisions

`ICrmSpaceReader` and the D8 deferral are both recorded as confirmed with you. Both are well-reasoned —
the reader because `SpaceNotInProject` is an invariant the backend enforces and its own tests assert,
and an FK cannot catch a *wrong-project* space; the deferral because the route is unreachable. No
objection to either, and the hard-stops section correctly carves the reader out as "a contract Sites
requires, not a new module or stub."

## 5. Run it

Apply R1, then execute S1-00 → S2-01 under the standing contract. The `127.0.0.1`-not-`localhost`
correction is noted and correct.

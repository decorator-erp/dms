# Review — F2 closure plan

**Verdict: approved. Execute as written.** And for the record: this plan correctly overturns my own
recommendation, and the new evidence is decisive.

## On the substance

I pushed F2 twice — once at Phase 1 close ("consider before module #3"), once in the Phase 3 option
list ("decide it now while it's a 6-module refactor, not a 10-module one"). Both framings presumed the
merge was *desirable* and only the timing was in question. The rule-8 argument shows the desirability
premise was wrong, and I verified it against the actual test:

`Rule8BuildingBlocksWebTests.cs:19` checks `assembly.GetReferencedAssemblies()` — the physical assembly
reference graph. A merged implementation project must reference `BuildingBlocks.Web` (its endpoints
implement `IEndpoint`/`IModule`), so every handler in that assembly gains compile-time sight of the web
layer, and no reference-graph assertion can distinguish "the endpoint parts may see it" from "the
handler parts may not." The guarantee doesn't degrade — it ceases to exist. My F2 write-up called the
refactor "mechanical, tests green throughout"; it would have been green because rules 2 and 8 go
**vacuous** through the suffix-keyed discovery, not because anything held. The plan citing
`ModuleAssemblies`' own fail-open warning against my own proposal is the review process working
exactly as built.

One nuance worth preserving in the PROGRESS.md entry: the alternative that keeps rule 8 alive — a
separate `.Endpoints` project per module — is still four projects, which is why there is no clever
third option. State that explicitly so a future re-litigator doesn't think it was missed.

## On the execution plan

All four changes are right, and two details deserve specific praise rather than silent approval:

- **The pointer-not-edit treatment of the stale "decide before module #3" directive** respects the
  append-only log while defusing the landmine. Correct instinct.
- **The separate decisions table** instead of jamming F2 into the feature-cuts table's `Owner` column —
  the observation that the existing table fails its own stated purpose ("so a future phase doesn't have
  to grep") is the kind of documentation-debt catch that normally only surfaces when someone actually
  greps and fails.

Minor execution notes, none blocking:

1. If any cited line numbers have drifted (docs were touched since `961c815`), **fix the citation, not
   the claim** — the read-back pass in the plan's own verification covers this; just don't burn an
   attempt on it.
2. `CLAUDE.md` one-liner: keep it to the single line as drafted. The temptation will be to explain;
   the file's job is to be short and always-in-context, and the ≤2-hop trail does the explaining.
3. The scope note ("does not revisit command privacy") is correct and important — closing F2 makes the
   Contracts-command placement *permanent*, and `adding-a-module.md`'s reframing from "workaround" to
   "accepted design" is the right consequence. Rule 10 remains the guard.

## The half not closed

Deliberately out of scope here, fine — but **outbox wake-on-write should not ride as "open, undecided"
for another phase.** It is ~50 lines (`OutboxSignal` + a nudge after `SaveChangesAsync` + a fallback
interval), it is the single biggest idle-cost lever in `docs/low-resource-hosting.md`, and unlike F2 it
has no architectural downside to weigh — it makes delivery *faster* and idle cost *lower*
simultaneously. Recommendation: decide it in the same session that executes this plan (adopt, or reject
with a reason), and either way move its table row from "Open, undecided" to a decision. A decisions
table with a standing "undecided" row on day one undercuts the lesson this very plan teaches about
deadlines passing unremarked.

## Verification

The plan's own verify section is right for a docs-only change. One addition: after the read-back pass,
run `git log --oneline -1` and confirm the commit message names this as a *decision record*, e.g.
`F2: closed as won't-do — four-project layout is permanent (rule 8)`. Decision commits get archaeology
done on them; make the message carry the conclusion.

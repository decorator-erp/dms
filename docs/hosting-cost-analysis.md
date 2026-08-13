# Hosting cost analysis

Consolidated at H1-04 from seven separate exploration docs written during Phase 2/3 planning
(`azure-hosting-cost-estimate.md`, `hostinger-hosting-cost-estimate.md`,
`traditional-vps-hosting-cost-estimate.md`, `outbox-hosting-cost.md`, `outbox-vs-no-outbox.md`,
`outbox-vs-traditional-monolith-cost.md`, `reduced-cost-hosting-options.md`) — eight files answering
overlapping pieces of "what does this cost to run" was starting to mislead the next reader about which
one was current. This is that one file. The **adopted decision** (wake-on-write, W1) lives separately in
[low-resource-hosting.md](low-resource-hosting.md) — that file is a decision record, this one is the
exploration that led up to it.

All figures are directional estimates as of Aug 2026, not quotes — reprice before committing budget.

---

## Part 1 — Does this project's architecture add hosting cost?

Before pricing hosts, the question worth answering first: does the modular-monolith-plus-outbox design
itself cost more to run than a simpler alternative? Two comparisons, both against the same baseline
scenario (HR deactivates an employee → Identity must disable their login,
`DeactivateEmployeeCommandHandler.cs`).

### Outbox vs. a direct cross-module call

| | Direct call (no outbox) | Outbox |
|---|---|---|
| Commits | 2 (HR, then Identity, separate transactions) | 1 (HR write + outbox row, atomic) |
| Crash between steps | Permanent inconsistency, nothing retries | Retried automatically until it succeeds |
| Duplicate delivery | N/A (no retry) | Handled — inbox dedupe skips reprocessing |
| Coupling | HR would need Identity's internals | HR only knows its own event type |
| Consistency guarantee | Best-effort | At-least-once delivery, exactly-once effect |
| Latency | Immediate | Bounded by the poll/wake interval |

A direct call also breaks two of this project's own rules: cross-module writes must go through the
outbox (never a direct call), and it would require HR to reference Identity's internals, violating "only
`*.Contracts` types are public." The outbox isn't optional here — it's what makes those two rules
possible to keep at all.

### What the outbox actually costs, at this project's scale

**No extra server.** `OutboxProcessor` is a `BackgroundService` running inside the same Api process —
`docker-compose.yml` only runs Postgres. Compare this to the realistic alternative for the same
guarantee: a message broker (Kafka/RabbitMQ/SQS), which is a genuinely separate paid service with its
own hosting cost. The outbox gets the same at-least-once/exactly-once-effect guarantee out of the
database already being paid for.

**A recurring background poll.** Pre-W1, this was `PeriodicTimer` querying `shared.outbox_messages`
every 5 seconds forever, whether or not anything was pending — one indexed
`WHERE ProcessedUtc IS NULL` scan per cycle. Real but small at any paid Postgres tier. W1 replaces this
with wake-on-write (a `SemaphoreSlim`-based signal nudged after commit) plus a 60-second fallback poll,
which is strictly cheaper: near-zero latency on the happy path, and a poll cadence 12× slower than
before for the crash-recovery/second-instance case the fallback still covers.

**Extra writes per cross-module action.** Roughly 2-3× the `INSERT`s for one action (the direct write,
the outbox row, later the inbox dedupe row) — but they're extra rows in a transaction the process was
already running, not extra round trips or connections. No added request latency: the outbox row commits
in the same transaction as the rest of the command, so the caller's response time is unaffected. Only
the *other* module's reaction time depends on the poll/wake interval.

**Two tables that only grow.** `outbox_messages` and `inbox_messages` have no retention job today —
every integration event and every (event, handler) dedupe row is kept forever. This is the one real,
currently-unmanaged cost: disk grows monotonically, and an unindexed-over-time table eventually slows
the poll query. [low-resource-hosting.md](low-resource-hosting.md) §5 already flags a retention sweep
(`DELETE ... WHERE processed_utc < now() - interval '30 days'`) as future Notifications-module work —
worth doing before this matters in production, not urgent today.

**Bottom line:** at this project's current scale (one Api instance, one Postgres), the outbox does not
require a bigger server or a new hosting tier. The measurable cost is a near-zero background signal plus
a slow fallback poll, a few extra rows per cross-module event, and disk that grows without a cleanup job
yet. None of that moves the estimates in Part 2. What it buys in return — modules that cannot silently
corrupt each other's data on a crash, without needing a message broker — is a guarantee a simpler design
does not have at all.

---

## Part 2 — What hosting actually costs

### Where the project is today

Four modules exist (Identity, HR, Approvals, Projects — Documents lands this phase). One Api process,
one Postgres instance, no message broker, no separate services. A Documents module adds file storage —
`LocalFileStorageService` writes to local disk for now (`IFileStorageService` is the seam for swapping
to cloud blob storage later, an explicitly deferred ops decision, not this phase's work). Cloud blob
storage is cheap per GB (Azure Blob hot tier ≈ $0.018/GB/month) and worth re-costing once real document
volumes exist.

### Sizing target: "medium business app," HA from day one

Hundreds–low thousands of users, high availability required at launch. This is the expensive target —
see the no-HA numbers below for the cheaper alternative.

| Provider | Shape | Monthly | Notes |
|---|---|---|---|
| **Azure PaaS**, pay-as-you-go | 2× App Service P1v3, Postgres Flexible Server zone-redundant HA, App Insights, Key Vault | **$600–$1,100** | Fully managed: automatic failover, patching, backups included. |
| **Azure PaaS**, 1-year reserved | same shape | **$400–$750** | 1-yr reservations cut App Service ~30-40%, Postgres compute ~38%. Only worth it once sizing is confident — it's a commitment. |
| **Self-managed VPS** (Hetzner) | 2× CPX32 app + load balancer, 2× CCX23 Postgres (self-built streaming replication) | **$300–$650** | You own failover, patching, backup verification — Hetzner's Load Balancer + dedicated-vCPU CCX line for the DB tier. |
| **Self-managed VPS** (Hostinger) | 2× KVM 2 app + self-run load balancer, 2× KVM 4 Postgres (self-built replication) | **~$100** | Cheapest HA-style option on paper; no managed LB or documented dedicated-vCPU guarantee, and pricing requires a 12/24-month prepay term (the "renews at" figure, not the flashier intro rate). |

The gap between Azure and self-managed VPS is not free money — it's the price of Azure doing failover,
patching, and backups for you. A team without dedicated ops capacity is paying that premium for not
being on call for a 3am database failover; a team with the capacity to run Postgres streaming
replication + a failover mechanism (Patroni or a manual process) can take the VPS number seriously.

### What actually drives the number

Two very different things get called "reducing cost" here, and they are not close to the same size:

| Driver | Contribution |
|---|---|
| **HA (running two of everything)** | Roughly half the total — the single biggest lever by far. |
| Managed PaaS vs. self-managed VM | The Azure-vs-VPS gap — paying for automatic ops work, or doing it yourself. |
| Instance tier sizing | "Medium business app" is an assumption; a smaller tier costs much less if real load is lower. |
| Monitoring, backups, misc | Single-digit percent of the total, on any provider. |
| **The app's own resource patterns** (outbox poller, EF Core, reflection dispatch, decorators) | Not in this table — Part 1 already establishes these cost a few dollars a month at most. Tuning them will not meaningfully move the bill; effort spent there is effort misallocated. |

### The cheaper alternative: no HA, right-sized

If launch can tolerate occasional downtime (no automatic failover, no zero-downtime deploys — a crashed
single instance or a DB restart takes the app down until it recovers), dropping HA and sizing for actual
rather than assumed load is the real lever:

| Provider | Shape | Monthly |
|---|---|---|
| Azure, Basic tier | App Service B1, Postgres Burstable B2s, single zone | **~$80–90** |
| Azure, Premium single-instance | App Service P1v3, Postgres D2ds_v5, single zone | **~$300** |
| VPS (Hetzner), single instance | CX33 app + CCX13 Postgres (dedicated vCPU) | **~$65** |
| VPS (Hostinger), single instance | KVM 2 app + KVM 4 Postgres | **~$44** |

This is the tier [low-resource-hosting.md](low-resource-hosting.md) is written against — "one small
VM/container (1 vCPU, 1–2 GB) + one small Postgres" is at or below even this no-HA range, appropriate for
a genuinely low-traffic start rather than the "medium business app" sizing this section otherwise
assumes.

## Bottom line

For what's in the repo today, at "medium + HA" sizing: **$600–$1,100/month** on Azure PaaS (or
$400–$750 reserved), **$300–$650/month** on self-managed VPS with real ops capacity behind it,
or as low as **~$100/month** on Hostinger's HA-style shape with the prepay/no-managed-LB caveats noted
above. Dropping HA for a launch-tolerant deployment cuts all three roughly in half to a third. None of
these numbers are driven by this project's own architecture — the outbox, the modular-monolith design,
and the four-module DI/CQRS pipeline together cost a few dollars a month, not a hosting-tier decision.
The actual levers are HA-or-not and managed-or-self-run, in that order. Revisit this analysis once real
traffic numbers exist, and once the Documents module's actual file volume replaces the placeholder
Azure Blob estimate above.

# Low-resource hosting — decisions for a low-traffic start

Target: one small VM/container (1 vCPU, 1–2 GB) + one small Postgres. The modular monolith is already
the cheap architecture — one process, one database, no broker, no Redis, no per-module deploys. These
notes cover the remaining consumers.

## 1. What is actually cheap already (do not "optimize" these away)

| Component | Real cost at low traffic | Verdict |
|---|---|---|
| Outbox on Postgres | one indexed query per poll, usually 0 rows (partial index `WHERE processed_utc IS NULL`) | Keep. This is the *alternative* to running RabbitMQ/Kafka — it exists to avoid a broker. |
| Arch/integration tests, Testcontainers | CI and dev machines only | Zero production cost. |
| SignalR (when Notifications is built) | memory per open connection; at low traffic, negligible | Keep when needed. |
| Per-module DbContexts | same connection pool, same DB | No extra cost vs one context. |

The rule: **cost lives in always-on processes and the DB tier, not in code structure.** Do not trade
architecture for imaginary savings.

## 2. OutboxProcessor — make it wake-on-write instead of tight-polling

Current shape: `PeriodicTimer` polling. Even cheap, it is the one always-running loop. Change it to
event-driven with a slow fallback:

```csharp
// BuildingBlocks/Outbox/OutboxSignal.cs
public sealed class OutboxSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);
    public void Nudge() { try { _signal.Release(); } catch (SemaphoreFullException) { } }
    public Task WaitAsync(TimeSpan fallback, CancellationToken ct) =>
        _signal.WaitAsync(fallback, ct);
}
```

- `db.Outbox().Enqueue(...)` (or a `SaveChanges` interceptor that sees outbox rows) calls
  `signal.Nudge()` after commit.
- `OutboxProcessor` replaces the timer loop with `await signal.WaitAsync(fallbackInterval, ct)` —
  processes immediately on nudge, and sweeps on the fallback anyway (catches rows written by a crashed
  request or another instance).
- Fallback interval from config: `Outbox:FallbackPollSeconds`, default **60** for low traffic
  (was effectively 5). Delivery latency on the happy path becomes ~0 instead of up to a poll interval —
  this change makes the app *cheaper and faster*.

Cost at idle: one trivial query per minute. Effectively zero.

### Scale-time note — the single-flight semaphore is per-process (not work to do now)

E2-01 fixed a real race by single-flighting `OutboxProcessor.ProcessPendingAsync` behind a
`SemaphoreSlim`. Two entry points were competing for the same pending rows — the hosted
`BackgroundService` poll loop and an explicit `ProcessPendingAsync()` call — with the loser swallowing
its `PK_inbox_messages` rollback and returning before the winner had committed.

That semaphore is **per-process**, which is exactly right for the single-instance target above. On a
second instance each process gets its own semaphore and the stale-read timing window returns.
Correctness still holds — the inbox dedupe plus the `PK_inbox_messages` constraint prevent double
side-effects — but the window is back.

The fix at that point is `SELECT … FOR UPDATE SKIP LOCKED` on the outbox fetch, which moves the
mutual exclusion into Postgres where every instance shares it, and removes the need for the semaphore
entirely.

Nothing to do until someone adds a second instance. Recorded here so that day does not begin with
rediscovering it.

## 3. The 9 notification jobs — do NOT port them as 9 background loops

The old backend runs `NotificationJobsWorker` + 9 `IPeriodicJob`s (`InvoiceDue`, `TaskDeadline`,
`StageDeadline`, `ApprovalOverdue`, …), each scanning tables on its own schedule. That is the single
biggest avoidable resource pattern in the old design. When the Notifications module is built:

**Option A (default): one consolidated sweep.** A single `PeriodicTimer` loop, interval
`Notifications:SweepMinutes` (default 15–60 for deadline-class notifications — nobody needs a
"task due tomorrow" ping accurate to 5 seconds). Each check is one set-based query; run them
sequentially in the one loop. 9 loops → 1.

**Option B (cheaper still): compute at read time.** "Overdue" and "due soon" are *facts derivable from
data already in the row* (`Deadline < now`). Badges, lists and dashboards should compute them in the
query, not read precomputed notification rows. Reserve the sweep for the few cases that genuinely push
(email, SignalR toast).

**Never:** Hangfire/Quartz with dashboards and clustering, per-job threads, or cron containers. All of
them are paying for scale that is not there.

## 4. Process/runtime settings for a small instance

In `Api.csproj` / `appsettings.Production.json`:

```xml
<PropertyGroup>
  <ServerGarbageCollection>false</ServerGarbageCollection>       <!-- Workstation GC: much lower memory floor -->
  <ConcurrentGarbageCollection>false</ConcurrentGarbageCollection>
  <TieredPgo>true</TieredPgo>
  <InvariantGlobalization>false</InvariantGlobalization>          <!-- keep: Arabic/localized names -->
</PropertyGroup>
```

- Workstation GC is the single biggest memory saver on 1–2 GB instances; Server GC preallocates per-core
  heaps you do not need at low traffic.
- Cap the connection pool: `Maximum Pool Size=20` in the connection string. Default 100 is sized for
  load you do not have, and small Postgres tiers cap connections anyway.
- Serilog: console sink only (container logs) or rolling file with a size cap. No paid sinks, no
  `MinimumLevel: Debug` in production, EF `Microsoft.EntityFrameworkCore.Database.Command` at Warning.
- Scalar/OpenAPI: map only in Development (`if (app.Environment.IsDevelopment())`).
- Seeders already run once at startup and are idempotent — fine as-is.

## 5. Postgres on a budget

- One instance, all schemas — already the design. Smallest managed tier (or same-VM container with a
  volume + nightly `pg_dump` to object storage).
- The outbox partial index and the `hr.employees.app_user_id` index are already the right ones; no
  index tuning needed at this size.
- `shared.outbox_messages` grows forever: add a retention sweep to the §3 consolidated loop —
  `DELETE FROM shared.outbox_messages WHERE processed_utc < now() - interval '30 days'`. Same for
  `inbox_messages`. (This also answers the poison-message gap partially: old failed rows age out
  visibly rather than accumulating silently — but keep the alert on `attempts > N` first.)

## 6. What NOT to add, ever, at this stage

Redis (cache or SignalR backplane — one instance needs no backplane), RabbitMQ/Kafka (the outbox is
the point), Elasticsearch (Postgres `ILIKE`/tsvector is fine), Kubernetes (one container on one VM),
autoscaling groups, APM SaaS beyond a free tier. Each is $20–100/mo of standing cost buying headroom a
low-traffic app cannot use.

## 7. Summary of actual changes

| Change | Where | Effort |
|---|---|---|
| Wake-on-write outbox + 60s fallback | `BuildingBlocks/Outbox` | small |
| Config keys `Outbox:FallbackPollSeconds`, pool size cap | appsettings | trivial |
| Workstation GC + logging levels + dev-only Scalar | `Api` | trivial |
| Consolidated notification sweep (Option A/B) | future Notifications module plan | design note now, work later |
| Outbox/inbox retention | same sweep | small |

Everything else in the architecture stays exactly as designed — it was already the low-cost shape.

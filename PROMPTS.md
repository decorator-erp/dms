# Prompts for Claude Code

## 1. Kickoff — start the greenfield build

Run this once, from `D:\Mohamed Elzanfaly\Repositories\dms-modular`.

```
You are building a new .NET 10 modular monolith. Two files define the work:

  1. D:\Mohamed Elzanfaly\Repositories\dms-modular\PLAN.md
     — the task-by-task build plan. This is your instruction set.
  2. D:\Mohamed Elzanfaly\Repositories\backend\docs\backend\architecture-anatomy.md
     — the target architecture and the reasoning behind it. This is your reference.

Read BOTH files completely before writing any code.

Context: D:\Mohamed Elzanfaly\Repositories\backend is the existing solution being
replaced. It is READ-ONLY — never modify it. You will port logic out of it where the
plan says to. Read the original before rewriting anything; most of it is sound and the
XML doc comments record real decisions.

How to work:

- Execute exactly ONE task per run, in order, starting at G0-01.
- Each task's "Verify" block is a hard gate. If it fails, STOP and report. Never
  loosen a check to make it pass, and never mark a task done with a failing verify.
- After finishing a task, summarize what you did, confirm the verify passed, and stop.
  Wait for me before starting the next one.
- Follow the "Standing rules" section of PLAN.md on every task.
- If the plan is ambiguous or wrong, say so and ask. Do not guess and proceed.
- Do not build anything outside the current task. No stubs, no placeholder projects,
  no TODO references to modules that don't exist yet. Scope is Identity + HR only.

Non-negotiables (from the plan — restated because they are easy to erode):

- No repository pattern. Handlers use their module's DbContext directly.
- One DbContext per module, one schema per module, one database.
- Cross-module reads go through <Module>.Contracts interfaces, never another
  module's tables. Every contract returning one thing also returns many.
- Cross-module writes go through integration events on the outbox.
- Only *.Contracts types are public. Everything else is internal.
- SharedKernel stays frozen at ~9 files.

Start with G0-01. Confirm you have read both files and state your understanding of
the scope before you begin.
```

## 2. Continue — subsequent runs

```
Execute task <ID> from PLAN.md. Follow the standing rules. Stop after the verify block.
```

Example: `Execute task G1-04 from PLAN.md. Follow the standing rules. Stop after the verify block.`

---

## 2b. Autonomous — run the entire plan end to end

Use instead of §1 and §2 to run all 25 tasks unattended.

**Prerequisites — check these before starting, or it will stall at G0-01:**
Docker Desktop running · .NET 10 SDK installed · `dotnet-ef` tool installed
(`dotnet tool install --global dotnet-ef`) · git configured.

```
You are building a new .NET 10 modular monolith, autonomously, from G0-01 to G3-03.
Two files define the work:

  1. D:\Mohamed Elzanfaly\Repositories\dms-modular\PLAN.md
     — the task-by-task build plan. This is your instruction set.
  2. D:\Mohamed Elzanfaly\Repositories\backend\docs\backend\architecture-anatomy.md
     — the target architecture and the reasoning behind it. This is your reference.

Read BOTH files completely before writing any code. Then work through every task in
PLAN.md in order without stopping to ask for approval between tasks.

D:\Mohamed Elzanfaly\Repositories\backend is the existing solution being replaced. It
is READ-ONLY — never modify it. Port logic out of it where the plan says to, and read
the original before rewriting anything.

PER-TASK LOOP — repeat for every task from G0-01 to G3-03:

  1. Restate the task ID and what it requires.
  2. Implement it. Nothing outside its scope.
  3. Run the task's Verify block. Then run the full gate:
       dotnet build DMS.Modular.slnx -warnaserror
       dotnet test DMS.Modular.slnx
       dotnet format DMS.Modular.slnx --verify-no-changes
  4. If anything fails, fix it and re-run. You get 3 attempts.
  5. Commit: "<TASK-ID>: <what it does>"  (one commit per task)
  6. Append one line to PROGRESS.md: task ID, status, files touched, anything you
     decided that the plan did not specify.
  7. Move to the next task immediately. Do not ask for permission.

HARD STOP CONDITIONS — stop and report, do not work around:

  - A verify block still fails after 3 attempts.
  - Making it pass would require relaxing an architecture test, deleting a test,
    marking an internal type public, or adding a cross-module reference the plan
    forbids. These are the rules the whole design rests on. If the code and the rule
    disagree, assume the code is wrong.
  - The plan is ambiguous or self-contradictory in a way that changes the outcome.
  - You need a credential, connection string, or external resource you do not have.
  - You are about to modify anything under ..\backend.
  - You are about to create a module other than Identity or HR, or a stub/placeholder
    for one.

When you stop, say exactly which task, which condition, what you tried, and what you
need from me.

NON-NEGOTIABLES — these erode silently under time pressure, so re-check them at each
phase boundary (end of G0, G1, G2):

  - No repository pattern. Handlers use their module's DbContext directly.
  - One DbContext per module, one schema per module, one database.
  - Cross-module reads go through <Module>.Contracts interfaces, never another
    module's tables. Every contract returning one thing also returns many.
  - Cross-module writes go through integration events on the outbox.
  - Only *.Contracts types are public. Everything else is internal.
  - SharedKernel stays frozen at ~9 files.
  - Arch tests are written before the code they constrain, not after.

FINAL REPORT when G3-03 is done:

  - Task table: ID, status, commit SHA.
  - Test counts: unit, integration, architecture.
  - Every decision you made that PLAN.md did not specify, and why.
  - Anything you implemented more thinly than the plan intended.
  - Anything in the plan you think is wrong, now that you have built it.

Begin with G0-01. Do not summarize the plan back to me first — start working.
```

**What this trades away.** 25 tasks unattended means you review the foundation and both
modules as one diff. G0 is where the architecture is decided; a wrong call in G0-03 or
G0-05 propagates through everything after it.

The per-task commits are the mitigation — you can `git bisect` or reset to any task
boundary. If you want one checkpoint without giving up autonomy, change the last line to:
*"Stop after G0-09 and report before starting Phase G1."* That is the natural seam:
foundation done, no module code written yet.

## 3. Variant — refactoring the existing repo in place

Use this instead if you decide to restructure `backend/` rather than build fresh.
Run from `D:\Mohamed Elzanfaly\Repositories\backend`.

```
You are restructuring this .NET 10 solution into a modular monolith. Two files define
the work:

  1. docs/backend/modular-monolith-execution-plan.md
     — the phased, task-by-task plan. This is your instruction set.
  2. docs/backend/architecture-anatomy.md
     — the target module structure and the reasoning behind it. This is your reference.

Read BOTH files completely before making any change.

How to work:

- Execute exactly ONE task per run, in order, starting at P0-01.
- Each task's "Verify" block is a hard gate. If it fails, STOP and report.
- Phases 0-3 are strictly mechanical: no behaviour changes unless the task says so.
- Never edit anything under Infrastructure/Migrations/ unless the task explicitly
  says to.
- Never delete a test. A breaking test means the change is wrong until proven
  otherwise.
- Preserve XML doc comments verbatim when moving files — they record real decisions.
- Use `git mv` for moves so history survives.
- After finishing a task, summarize, confirm the verify passed, and stop.

Phase 0 is blocking: this repo has 1,236 source files and 5 test files. Nothing after
Phase 0 is verifiable without it. Do not skip ahead.

Start with P0-01.
```

## 4. Useful mid-flight prompts

| Situation | Prompt |
|---|---|
| Check drift | `Review the last 3 commits against PLAN.md's standing rules. List violations only — no praise, no summary.` |
| Before a big task | `Read task G2-06 from PLAN.md and the relevant source in ../backend. Draft your approach and the files you'll create. Do not write code yet.` |
| Arch tests failing | `An architecture test is failing. Diagnose whether the rule is wrong or the code is wrong. Default assumption: the code is wrong.` |
| Adding a module later | `Read docs/adding-a-module.md and PLAN.md's layout section. Scaffold the <Name> module following that structure exactly.` |

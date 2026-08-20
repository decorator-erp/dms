# CI

`.github/workflows/gate.yml` runs the same gate every task has run locally since Phase 1:

```
dotnet build DMS.Modular.slnx -warnaserror
dotnet test  DMS.Modular.slnx
dotnet format DMS.Modular.slnx  &&  git diff --exit-code
```

Same commands, same order. A pipeline that runs something *slightly* different from the local gate
produces disagreements nobody can reproduce, and the resolution is always to believe whichever one was
green.

## What is deliberately not in the workflow

**No `services: postgres`.** `docker-compose.yml` is a local dev convenience. Every database-touching
test builds its own container — `new PostgreSqlBuilder("postgres:17").Build()` in `ApiFixture`,
`OutboxFixture`, `Rule9CrossSchemaForeignKeyTests` and every module fixture. Testcontainers needs a
Docker *daemon*, which `ubuntu-latest` provides; a service container would start a Postgres nothing
connects to.

**No `DOCKER_HOST`.** The runner's default socket is correct.

**No `--verify-no-changes`.** See `adding-a-module.md`, Phase 6 pattern 5.

## Verification status

**The gate has been shown to fail — locally.** With a rule-4 violation applied (a public type in
`Finance.Domain`), the workflow's exact commands produced:

```
Failed Architecture.Tests.Rule4PublicSurfaceTests.OnlyMigrationTypesMayBePublicOutsideContracts
  Finance.Domain exposes public types outside Contracts: Finance.Domain.GateProbe
```

29/29 suites ran, 1105 passed, 1 failed, exit 1. The violation **compiled cleanly** (`0 Warning(s)`),
which is the point — a violation that fails to build tests the compiler, not the gate.

**It has not been shown to work on a runner.** The YAML, the action versions, SDK resolution from
`global.json`, and Docker-in-Actions are all unexercised. There is no git remote configured on this
repository, so that half cannot be done here. It is the procedure below, and until push 1 is green
this pipeline is an instrument nobody has watched run.

## Proving it on the runner — three pushes, in this order

A single push conflates two different failures. The first run exercises the harness *and* whatever is
in the tree at once, so broken YAML, a wrong action version, an SDK that will not resolve, an absent
Docker daemon, and a genuine red gate all arrive as one red run and look identical.

| | Tree | Expect | Proves |
|---|---|---|---|
| **1** | clean | **green** | **The runner works at all.** |
| 2 | violation applied | red, naming the rule | The gate discriminates on the runner. |
| 3 | violation reverted | green | The red was the violation, not a one-way trip. |

**Push 1 is the load-bearing one, and the local rehearsal above cannot substitute for it.** If push 1
is red, nothing about the gate's discrimination has been tested yet — fix the harness first. Running
push 2 against a broken harness yields a red run that proves nothing and *reads as success*.

### The violation to use for push 2

```bash
cat > src/Modules/Finance/Finance.Domain/GateProbe.cs <<'EOF'
namespace Finance.Domain;

public sealed class GateProbe
{
    public int Value { get; set; }
}
EOF
```

Only primitive members, so it compiles. Expected failure text is the two lines quoted above. Revert
for push 3 with:

```bash
rm src/Modules/Finance/Finance.Domain/GateProbe.cs
```

## Running the gate locally

`CLAUDE.md`'s commands are correct as written. Note that the `DOCKER_HOST=tcp://127.0.0.1:2375` prefix
some task notes carry is **not** required and currently breaks the run on this machine — Docker
Desktop's TCP endpoint is off and the default `desktop-linux` context is what works. Prefer no prefix;
Testcontainers finds the daemon on its own.

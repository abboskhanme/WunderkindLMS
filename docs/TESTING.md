# Testing

Backend integration tests for SchoolLms. Phase 1 (SPEC §7) makes billing tests mandatory;
this document describes the harness they run on.

---

## 1. Run the tests

### On this machine (no local .NET SDK)

```bash
./tools/test.sh                       # everything
./tools/test.sh --filter HarnessTests # one class
./tools/test.sh -v n                  # verbose (shows every SQL statement)
```

That is the whole command. It runs `dotnet test` inside `mcr.microsoft.com/dotnet/sdk:10.0`
and mounts the Docker socket so the tests can start their own PostgreSQL.

### On a machine that has the .NET 10 SDK

```bash
dotnet test SchoolLms.Tests/SchoolLms.Tests.csproj
```

No flags, no `docker compose up` first. `-p:BuildSpa=false` is **not** needed — the test
project passes `BuildSpa=false` to `SchoolLms.Server` through
`ProjectReference AdditionalProperties`, so the npm (`schoollms.client.esproj`) reference is
dropped from the test build graph.

> `dotnet test SchoolLms.slnx` **will** try to build the SPA, because the solution still
> contains `schoollms.client.esproj`. Always point `dotnet test` at
> `SchoolLms.Tests/SchoolLms.Tests.csproj`.

### Timings (2026-09-11, M-series Mac, Docker Desktop 29.6.1)

| Scenario | Wall clock |
|---|---|
| Warm build + warm NuGet | **10 s** |
| Clean `bin/`+`obj/`, warm NuGet | **18 s** |
| xUnit-reported execution of 24 tests | 0.7 s |

The budget in `docs/TASKS.md` (P1-03) is 90 s. A first-ever run also downloads NuGet
packages and the `postgres:17-alpine` image; budget a few minutes for that one time.

---

## 2. What the harness starts

```
dotnet test  (inside sdk:10.0 container)
  └─ PostgresFixture     → postgres:17-alpine container, random host port
       ├─ role   schoollms_owner        (schema owner, migrations run as this role)
       ├─ db     schoollms_template     (migrated once)
       └─ db     app_1_<guid>           (CREATE DATABASE ... TEMPLATE, ~100 ms)
            └─ ApiFactory  → the real ASP.NET Core app on Kestrel-less TestServer
```

Everything is thrown away when the run ends. **The running dev stack
(`wunderkind-database` on port 5432) is never touched** — different container, random
port, different database name, and `HarnessTests.Ilova_throwaway_bazaga_ulanadi_dev_bazasiga_emas`
asserts on `current_database()` every run so a broken override cannot go unnoticed.

### Why Testcontainers and not a `docker-compose.test.yml`

A compose file would need two commands (`up`, then `dotnet test`) and a `down` that people
forget. Testcontainers keeps the acceptance criterion — *`dotnet test` runs green from a
clean clone* — literally true, and it cleans up after itself even when the test process is
killed.

The Docker-in-Docker problem is solved by running the Postgres container as a **sibling**,
not a child: `tools/test.sh` mounts `/var/run/docker.sock`, so Testcontainers talks to the
**host** Docker daemon. The Postgres port is then published on the host, and
`TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal` tells Testcontainers to hand out that
address instead of `localhost` (which, inside the SDK container, would be the SDK container
itself). `--add-host host.docker.internal:host-gateway` makes the same script work on Linux.

### Using an external PostgreSQL instead (CI service container)

Set `TEST_PG_ADMIN_CONNECTION` to a **superuser** connection string and no container is
started:

```bash
TEST_PG_ADMIN_CONNECTION="Host=localhost;Port=5433;Database=postgres;Username=postgres;Password=postgres" \
  dotnet test SchoolLms.Tests/SchoolLms.Tests.csproj
```

The fixture then creates and drops its own roles and databases on that server.

---

## 3. Writing a test

```csharp
[Collection(SchoolLmsCollection.Name)]
public class InvoicesTests(ApiFixture fixture)
{
    [Fact]
    public async Task Kassir_toluv_qabul_qila_oladi()
    {
        using var client = await fixture.Api.ClientAsAsync("cashier");   // JWT in one line
        ...
    }
}
```

`ApiFixture` (one per test run) gives you:

| Member | Purpose |
|---|---|
| `fixture.Api.ClientAsAsync(role)` | seeds a user with that role and returns an authenticated `HttpClient` |
| `fixture.Api.TokenFor(role, userId, lifetime: ...)` | raw JWT; negative `lifetime` produces an expired token |
| `fixture.Api.ClientWithToken(token)` / `AnonymousClient()` | clients for auth / 401 tests |
| `fixture.Api.SeedUserAsync(role)` | returns `(AppUser, plaintext password)` |
| `fixture.Api.WithDbAsync(db => ...)` | direct DB access **as owner**, for arranging and asserting |
| `fixture.Database.OwnerConnectionString` | schema owner |
| `fixture.Database.AppRwConnectionString` | application role — what the request path uses |
| `fixture.Postgres.CreateDatabaseAsync("x")` | a pristine migrated database when a test really needs one |

Rules that keep the suite honest:

- **Roles are plain strings**, never `Roles.*` constants, because `cashier` does not exist in
  `Roles.cs` yet (P1-04 adds it). `ApiFactory` is deliberately not bound to the enum.
- **Every test creates its own data** with a unique login/id. Tests share one database; they
  must not share rows.
- **Assert on the response body**, not only the status code.
- `/api/auth/login` is rate limited to **10 requests per minute per IP**, and in TestServer
  every test shares the `unknown` IP partition. Keep the number of real login calls small;
  use `ClientAsAsync` (which mints a token directly) for everything else.

---

## 4. `app_rw` and P1-22 (ledger immutability)

`PostgresFixture` exposes **two** connection strings per database. `app_rw` is the role the
request path uses; the ledger tests prove that it cannot `UPDATE` or `DELETE` a payment.

**The fixture does not create `app_rw`.** P1-02's migration must. If the role is missing
after migration, the fixture falls back to the owner string and sets
`TestDatabase.AppRwIsOwnerFallback = true`. A test that depends on grants must start with:

```csharp
fixture.Database.RequireRealAppRw();
```

which fails loudly. This is deliberate: if the fixture created the role and the grants
itself, P1-22 would be testing the fixture's grants, not the migration's — a false green on
the one rule that protects the money.

### Open handoff to P1-02

As of 2026-09-11 the dev database (`wunderkind-database`) already has
`schoollms_owner` owning every table and `app_rw` with `LOGIN, NOINHERIT` plus
`SELECT/INSERT/UPDATE/DELETE` on `users` — but `__EFMigrationsHistory` still contains only
`20260910110618_InitialPostgres`. The roles and grants were therefore applied **out of
band**, not by a migration.

The harness can only pick them up if they ship as something it can execute. In order of
preference:

1. an EF migration containing the `CREATE ROLE` / `GRANT` statements, or
2. a checked-in `.sql` file that the fixture runs right after `MigrateAsync()` — the hook
   goes in `PostgresFixture.InitializeAsync`, immediately before the
   `AppRwIsOwnerFallback` check.

Until then the fixture stays in fallback mode, `RequireRealAppRw()` keeps P1-22 red, and the
grants are effectively untested. Note that option 1 additionally requires
`schoollms_owner` to hold `CREATEROLE`; the fixture creates it without that attribute today,
mirroring a least-privilege production setup.

---

## 5. Known issue found by the harness (not fixed here — for P1-02)

`Program.cs:244` calls `db.Database.Migrate()` on `ConnectionStrings:Default`. With a
restricted `app_rw` role this **crashes the application at startup**:

```
Npgsql.PostgresException 42501: permission denied for schema public
  at NpgsqlHistoryRepository.CreateIfNotExists()
  at RelationalDatabaseFacadeExtensions.Migrate(DatabaseFacade)
  at Program.<Main>$(String[]) in /src/SchoolLms.Server/Program.cs:line 244
```

EF Core's Npgsql history repository calls `CreateIfNotExists()` **unconditionally**, even
when every migration is already applied, and `CREATE TABLE IF NOT EXISTS` requires `CREATE`
on schema `public` — which `app_rw` must not have. Verified by temporarily simulating the
P1-02 role and grants inside the fixture.

This matches P1-02's own acceptance criterion ("`db.Database.Migrate()` runs on
`ConnectionStrings:Migrator`"), so it is that task's job to fix. The harness already supplies
`ConnectionStrings:Migrator` (the owner string), so the fix will not need harness changes.

---

## 6. Troubleshooting

| Symptom | Cause |
|---|---|
| `DockerUnavailableException` / `Permission denied` on `/var/run/docker.sock` | The SDK container runs as a non-root user. `tools/test.sh` runs as root on purpose; Docker Desktop maps bind-mount ownership back to the host user, so `bin/`+`obj/` stay writable. |
| `Cannot assign requested address` when connecting to Postgres | `TESTCONTAINERS_HOST_OVERRIDE` is missing — Testcontainers handed out `localhost`, which inside the SDK container is the SDK container. |
| `npm: not found` during build | You built `SchoolLms.slnx` instead of `SchoolLms.Tests/SchoolLms.Tests.csproj`. |
| Two `ApiFactory` instances → `InvalidOperationException` | By design. Connection strings also travel through process-wide environment variables, so only one app may be live at a time. Dispose the first one. |

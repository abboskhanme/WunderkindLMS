# Assumptions

One line per decision made without the user or the client: `- [date] <question> → <decision> → <reason>`.
Referenced by `docs/TASKS.md`. Superseded entries are struck through, never deleted.

## Phase 1 — planning (2026-09-11)

- [2026-09-11] New billing entities in `Entities.cs` or a new file? → new `SchoolLms.Domain/Billing.cs` → `Entities.cs` is 1077 lines and five agents write billing code in parallel in Phase 1.C; it is the worst merge-conflict surface in the repo.
- [2026-09-11] Convert the whole schema to `uuid`/`date` per SPEC §3? → no, only the new billing tables use real types; billing FKs reference `students(id)` as `text` → Phase 0 shipped the legacy shape on Postgres; converting 53 entities is not Phase 1 work and buys nothing for the money module.
- [2026-09-11] .NET 10 upgrade timing (SPEC §2.1 says Phase 0, it did not happen) → defer to after Phase 1 → a runtime upgrade in the same three weeks as the money module doubles the blast radius of any failure. Confirm at the P1-25 gate.
- [2026-09-11] Discount approval threshold (client Q5 unanswered) → 20 % or 500 000 so'm → configuration value, changeable later with an `UPDATE`, not a migration.
- [2026-09-11] Payment due date and grace period (client Q6 unanswered) → due on the 10th, overdue after the 15th → same: configuration, not schema.
- [2026-09-11] Receipt channel (client Q3 unanswered) → PDF + Telegram only → `TelegramService.SendDocumentAsync` already exists; ESC/POS needs hardware that has not been bought.
- [2026-09-11] Opening cash float (client Q11 unanswered) → 0, cashier may enter one when opening a shift → matches how the desk works today.
- [2026-09-11] Part-month enrolment (client Q12 unanswered) → charge the full month → this is what the current `TuitionService.AccrueMonth` already does; changing it silently would alter existing figures.
- [2026-09-11] Which methods count toward `expected_cash` (client Q13 unanswered) → `cash` only → card and transfer settle to the bank, so counting them would guarantee a false variance every shift.
- [2026-09-11] Advance payment for a whole year (client Q15 unanswered) → hold as unallocated credit, allocate as each month accrues → creating twelve future invoices would make a mid-year price or discount change unfixable without editing invoices.
- [2026-09-11] Opening student debt at go-live (client Q17 unanswered) → start from zero → only demo data exists today; if the client supplies a file it is imported as ordinary invoices through `LedgerService`.
- [2026-09-11] PDF engine → QuestPDF Community, pending licence confirmation in P1-12 → free under 1 M USD annual revenue. If a commercial licence is required, stop and ask the user: that is money spent.
- [2026-09-11] Q14: one payment covering two siblings? → **No — one receipt per student.**
  Decided by the client. `payments.student_id` stays a single column (SPEC §3.7 unchanged);
  a guardian paying for two children produces two payments. Keeps the schema simple and the
  per-student ledger unambiguous. Revisit only if the cashier reports real friction.

## Phase 1 — P1-03, test harness (2026-09-11)

- [2026-09-11] Assertion library: FluentAssertions or plain xUnit `Assert`? → **plain `Assert`** → FluentAssertions 8.x requires a paid licence for commercial projects; buying one is money spent, which needs the user's decision. The free fork (AwesomeAssertions) is another dependency for no functional gain. Zero licence risk beats prettier syntax.
- [2026-09-11] How does the test get a PostgreSQL, given no local .NET SDK and no Docker-in-Docker? → **Testcontainers with the host Docker socket mounted (sibling container) + `TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal`** → a `docker-compose.test.yml` would need `up` and `down` around `dotnet test` and would break the "`dotnet test` runs green from a clean clone" criterion. `TEST_PG_ADMIN_CONNECTION` is still honoured for CI service containers.
- [2026-09-11] `WebApplicationFactory<TEntryPoint>` needs a public entry type, but `Program.cs` uses top-level statements (internal `Program`). → **use `SchoolLms.Server.Controllers.AuthController` as `TEntryPoint`** → the documented fix is to add `public partial class Program { }` to `Program.cs`, but a test must not change production code. WebApplicationFactory only uses `TEntryPoint` to locate the assembly.
- [2026-09-11] One database per test, or one per run? → **one per run, shared** → container + migration + host boot costs ~8 s; repeating it per test would blow the 90 s budget. Isolation comes from every test creating its own uniquely-named data. `PostgresFixture.CreateDatabaseAsync()` gives a pristine database (cloned from a migrated template, ~100 ms) when a test genuinely needs one.
- [2026-09-11] Should the fixture create the `app_rw` role itself, since P1-02 has not merged? → **no — fall back to the owner string and flag it** → a fixture-created role with fixture-written grants would make P1-22 (ledger immutability) pass while testing nothing. `TestDatabase.RequireRealAppRw()` fails such a test loudly instead.
- [2026-09-11] Language of comments in `SchoolLms.Tests` → **Uzbek in code, English in `docs/TESTING.md`** → mirrors the repository as it is: every existing `.cs` file comments in Uzbek, every file under `docs/` is English.

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

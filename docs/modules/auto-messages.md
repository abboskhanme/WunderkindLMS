# Automatic Telegram messages to parents

Status: batch 1 of `docs/MARKAZ-GAPS.md` (items #1, #2, #3, #9 and the log of #8), 2026-10-13.
Extended 2026-10-15: runs with statuses and approval, retries, the absence cut-off, the payment
rule, one delivery log for automatic and manual sends, history and statistics.

## Why

Nothing reached parents on its own. The nurse's "send to absentees" button had never been
pressed on prod (0 `absence_notices`), new monthly invoices were issued silently, and debt
reminders were a manual broadcast. Telegram is the only channel (CLAUDE.md) — no SMS, no push.

Client, 2026-10-15: *"Avto-xabarlar triggerlari: darsga kelmaganda (masalan, 11:30 gacha bo'lgan
vaqt filtri bilan), tug'ilgan kunlarda, to'lov qabul qilinganda, qarzdorlik eslatmalarida …
Shablonlar boshqaruvi: … dinamik o'zgaruvchilar (Ism, Sana, Vaqt, To'lov summasi). Xabarlar
tarixi va statistikasi: … (Rejalashtirilgan, Tasdiq kutmoqda, Yuborildi, Xatolik, To'xtatilgan) va
yetkazilish ro'yxati audit uchun alohida ko'rinib turishi kerak."*

## Rules

One row per rule in `auto_message_rules`; every rule ships **off** and is switched on by staff
with the `messages` permission once the school approves its text.

| Key | When | Who gets it | Tokens (besides `{fish}` `{ism}` `{sinf}` `{ota_ona}`) |
|---|---|---|---|
| `absence` | daily, at `send_time` | parents of pupils absent in every checked lesson that **starts before the cut-off** (`cutoff_time`, default = `send_time`), not yet told that day | `{sana}` (the day) `{vaqt}` `{sabab}` |
| `invoice` | monthly, on `day_of_month` at `send_time` (up to 2 days late) | parents of pupils with a non-void invoice for the current month | `{sana}` `{vaqt}` `{oy}` `{summa}` `{qarzdorlik}` |
| `debt` | monthly, on `day_of_month` at `send_time` (up to 2 days late) | parents of pupils whose overdue debt ("Qarzdorlar" report number) ≥ `min_amount` | `{sana}` `{vaqt}` `{qarzdorlik}` `{oylar}` |
| `birthday` | daily, at `send_time` | parents of pupils born on this day (29 Feb → 28 Feb in other years) | `{sana}` `{vaqt}` |
| `payment` | on every receipt send (event-driven, no time of its own) | the parents the receipt goes to | `{summa}` `{sana}` `{vaqt}` (of the payment) `{chek}` `{usul}` `{qarzdorlik}` (after the payment) |

`{sana}` / `{vaqt}` in the scheduled rules: the run's day (daily rules; today for monthly ones) and
the time the message is prepared. The screen shows each token with the client's word for it
("Ism", "Sana", "Vaqt", "To'lov summasi", …).

Text is the broadcast markup (`**bold**`, `__italic__`), sent as Telegram HTML; values are escaped.
A line whose token has no value is dropped ("Sabab: {sabab}" disappears without a reason).
Recipients are the parent chats in `telegram_registrations` (`teacher_id is null`), as for
broadcasts and the absence button. Archived pupils never get anything.

### Absence cut-off ("11:30 gacha")

- Only lessons whose `lesson_times.start_time` is before the cut-off count; a later lesson neither
  makes a pupil absent nor "present". A period with no start time is ignored. With no bell schedule
  at all (`lesson_times` empty) every lesson counts, as before.
- A class (or track group) with a pre-cut-off lesson **not marked** on the "Davomat belgilash"
  screen is skipped: none of its pupils is messaged, and the run's note names the class and how
  many pupils were left out ("Davomat 11:30 gacha to'liq olinmagan: 6-B — …"). Half-taken
  attendance must not tell a parent "your child did not come".
- Still once per pupil per day: `absence_notices` is shared with the nurse's button.

### Payment rule

- The receipt PDF is a fraud control (`ReceiptService`): it is **always** sent, rule on or off.
  The rule only changes its caption; approval never applies to it.
- Off: the PDF goes with the usual caption ("To'lov cheki / № … / name / Jami").
- On: the rendered template is the caption (`parse_mode=HTML`; if Telegram cannot parse it the same
  file goes again with plain text). Longer than 1024 visible characters → the PDF with the usual
  caption, then the text as a separate message. A cancellation (storno) receipt always keeps the
  usual caption.
- Every receipt send — automatic after the payment and the cashier's manual resend — writes one
  log row per chat (rule `payment`, `ref_id` = receipt number, `source` auto/manual, attempts 1–2),
  or one `no_telegram` row. Logging never affects the send.

## Runs and statuses

`auto_message_runs` — one per (rule, period). Period = the day (`2026-10-13`) for daily rules, the
month (`2026-10`) for monthly ones.

| Run status | Meaning (screen label) |
|---|---|
| `scheduled` | planned; created on the first scheduler tick of its day with its planned time (Rejalashtirilgan) |
| `pending_approval` | the rule has `requires_approval`: messages are prepared, nothing is sent (Tasdiq kutmoqda) |
| `sending` | messages are going out |
| `done` | finished (Yuborildi) |
| `cancelled` | stopped by staff, by switching the rule off, or because its period ended (To'xtatilgan) |

Every message is a row of `auto_message_log`, first written as `scheduled` (or `pending_approval`)
with its rendered text and planned time, then sent: `sent`, `failed` (Xatolik) or `no_telegram`
(Xatolik — Telegram yo'q), or `cancelled`.

- **Approval.** `POST runs/{id}/approve` sends a waiting run at once (audited, approver kept).
  `POST runs/{id}/exclude {studentId}` takes one pupil out of a waiting run. A waiting run that is
  not approved by the end of its period (end of day for daily rules, the end of the 2-day window for
  monthly ones) is cancelled by the system (`cancelled_by = auto`).
- **Stopping.** `POST runs/{id}/cancel` (audited), or switching the rule off, cancels the run and its
  waiting rows in one statement; the send loop re-reads the run's status before each pupil, so a run
  that is sending stops after the current pupil. A row already delivered keeps `sent`.
  A cancelled run is final for its period — switching the rule back on the same day does not reopen it.
- **Retries.** A `failed` row of a run is sent again on later scheduler ticks (every 2 minutes) while
  the period is current, until it has had 3 attempts (`attempts`, `error` kept on the row). Closing
  the period no longer stops retries (the 2026-10-13 build set `last_period` and never retried).
- **Once and only once.** One log row per (rule, period, pupil, chat) — a chat already `sent` for the
  period is skipped; payment rows are outside that index (one per receipt send). `absence` also
  writes `absence_notices` (sent by `auto`), so the nurse's button and the rule never message the
  same pupil twice in a day. If the nurse's button told a pupil while an absence run was waiting
  for approval, that pupil's rows are cancelled when the run is sent.
- A monthly rule whose source is empty (no invoices yet for the month) does not close its period; it
  tries again on the next tick inside its window.

## One delivery log

`auto_message_log` also holds what staff send by hand:

- the nurse's "send to absentees" button (`DailyAbsenceNotifier.SendAsync`): rule `absence`,
  `source = manual`, `sent_by` = the staff member, one row per chat plus `no_telegram` rows;
- the cashier's receipt resend (`POST /api/receipts/{paymentId}/telegram`): rule `payment`, manual.

## Scheduler

`AutoMessageScheduler` (hosted service) wakes every 2 minutes: stops runs whose period is over,
plans today's runs, runs the due ones, resumes a `sending` run (restart, cut-off approval request)
and retries failed rows. A rule switched on after its time today runs at once (today only; monthly
rules only inside their window). Bot not configured → nothing is planned, sent or logged. One
sender at a time in the process (the tick, an approval, a manual run); messages are spaced 40 ms
apart (under Telegram's ~30/s).

## API (`[AdminPerm("messages")]` — staff read, only `messages` acts)

- `GET  /api/admin/auto-messages` — the rules with last run and last period counts.
- `PUT  /api/admin/auto-messages/{key}` — enabled, send time, day of month, minimum, text,
  `requiresApproval` (not for payment), `cutoffTime` (absence). Audited. Switching off stops open runs.
- `POST /api/admin/auto-messages/{key}/preview` — how many pupils it would reach now and a sample
  text (payment: a sample with made-up values). Sends nothing.
- `GET  /api/admin/auto-messages/log?rule=&from=&to=&status=` — the log, newest first, ≤ 500 rows.
- `GET  /api/admin/auto-messages/log/paged?…&studentId=&q=&runId=&source=&page=&pageSize=` — the same,
  paged (≤ 200 a page), with pupil name search, run and source filters.
- `GET  /api/admin/auto-messages/log/export?…` — the same filters as `.xlsx` (≤ 10 000 rows).
- `GET  /api/admin/auto-messages/runs?from=&to=&rule=&status=` — runs planned in the period (default
  the last 7 days).
- `GET  /api/admin/auto-messages/runs/{id}`; `POST runs/{id}/approve | cancel | exclude`.
- `GET  /api/admin/auto-messages/stats?from=&to=` — totals per status, per rule × status and per
  day × rule × status (by the row's time, school days; ≤ 366 days).

## Screen

Xabarlar → "Avtomatik xabarlar":

- **Qoidalar va shablonlar** — a card per rule: switch, time, day, minimum, "Tasdiq bilan",
  "Qaysi darsgacha" (absence), text with token chips (client's word + token), preview.
- **Tarix va statistika** — period and rule filter; status tiles (Rejalashtirilgan, Tasdiq kutmoqda,
  Yuborildi, Xatolik with "Telegram yo'q", To'xtatilgan) and a rule × status table; "Yuborishlar"
  (runs: status, planned/finished time, counts, who approved or stopped, note; Tasdiqlash /
  To'xtatish) with a drill-down to the per-chat delivery list (and "Chiqarish" while waiting);
  "Yetkazish jurnali" — every row, paged, with pupil, status and source filters and Excel export.

## Tests

`SchoolLms.Tests/AutoMessagesTests.cs`, `SchoolLms.Tests/AutoMessageRunsTests.cs` — every Telegram call
goes to a recording `HttpMessageHandler`; nothing leaves the machine.

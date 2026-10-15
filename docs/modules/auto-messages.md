# Automatic Telegram messages to parents

Status: batch 1 of `docs/MARKAZ-GAPS.md` (items #1, #2, #3, #9 and the log of #8), 2026-10-13.

## Why

Nothing reaches parents on its own today. The nurse's "send to absentees" button has never been
pressed on prod (0 `absence_notices`), new monthly invoices are issued silently, and debt reminders
are a manual broadcast. Telegram is the only channel (CLAUDE.md).

## Rules

One row per rule in `auto_message_rules`; every rule ships **off** and is switched on by staff
with the `messages` permission once the school approves its text.

| Key | When | Who gets it | Tokens |
|---|---|---|---|
| `absence` | daily, at `send_time` | parents of pupils absent the whole day (the nurse's roll-call, `DailyAbsenceNotifier` rows) not yet told that day | `{fish}` `{ism}` `{sinf}` `{sana}` `{sabab}` `{ota_ona}` |
| `invoice` | monthly, on `day_of_month` at `send_time` (up to 2 days late) | parents of pupils with a non-void invoice for the current month | + `{oy}` `{summa}` `{qarzdorlik}` |
| `debt` | monthly, on `day_of_month` at `send_time` (up to 2 days late) | parents of pupils whose debt ("Qarzdorlar" report number) ≥ `min_amount` | + `{qarzdorlik}` `{oylar}` |
| `birthday` | daily, at `send_time` | parents of pupils born on this day (29 Feb → 28 Feb in other years) | `{fish}` `{ism}` `{sinf}` `{ota_ona}` |

Text is the broadcast markup (`**bold**`, `__italic__`), sent as Telegram HTML; values are escaped.
Recipients are the parent chats in `telegram_registrations` (`teacher_id is null`), as for
broadcasts and the absence button. Archived pupils never get anything.

## Once and only once

- `auto_message_log` keeps one row per (rule, period, pupil, chat): `sent`, `failed` or
  `no_telegram`. A pupil–chat already `sent` for the period is skipped; a `failed` one is retried on
  the next tick and its row updated.
- Period = the day (`2026-10-13`) for daily rules, the month (`2026-10`) for monthly ones.
- `absence` also writes `absence_notices` (sent by `auto`), so the nurse's button and the rule never
  message the same pupil twice in a day — whichever runs first wins.
- A monthly rule whose source is empty (no invoices yet for the month) does not close its period; it
  tries again on the next tick inside its window.

## Scheduler

`AutoMessageScheduler` (hosted service) wakes every 2 minutes and runs each enabled rule that is
due. A rule switched on after its time today runs at once (today only; monthly rules only inside
their window). Bot not configured → nothing is sent and nothing is logged.

## API (`[AdminPerm("messages")]`)

- `GET  /api/admin/auto-messages` — the four rules with last run and last period counts.
- `PUT  /api/admin/auto-messages/{key}` — enabled, send time, day of month, minimum, text. Audited.
- `GET  /api/admin/auto-messages/{key}/preview` — how many pupils it would reach now and a sample text.
- `GET  /api/admin/auto-messages/log?rule=&from=&to=&status=` — the log, newest first, ≤ 500 rows.

## Screen

Xabarlar → "Avtomatik xabarlar" tab: a card per rule (switch, time, day, minimum, text, tokens,
preview) and the log below.

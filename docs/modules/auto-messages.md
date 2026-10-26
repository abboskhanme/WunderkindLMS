# Automatic Telegram messages to parents

Status: batch 1 of `docs/MARKAZ-GAPS.md` (items #1, #2, #3, #9 and the log of #8), 2026-10-13.
Extended 2026-10-15: runs with statuses and approval, retries, the absence cut-off, the payment
rule, one delivery log for automatic and manual sends, history and statistics.
Extended 2026-10-25: pictures on the scheduled rules (as on announcements since 2026-10-24).
Extended 2026-10-26: templates staff create themselves, without a code change.

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

### Pictures (2026-10-25)

Announcements got pictures on 2026-10-24; the client noticed the automatic rules had not. Now the
four scheduled rules (absence, invoice, debt, birthday) take the same picker:

- Up to 10 JPG/PNG/WEBP pictures of ≤ 10 MB (`auto_message_rules.image_urls`, `text[]`, in order),
  uploaded first through `POST /api/admin/uploads`; `PUT {key}` checks them exactly as a broadcast
  does (`BroadcastImages`: our upload names only, files must exist). `imageUrls` omitted — unchanged;
  `[]` — no pictures. The text stays required.
- One picture → `sendPhoto`, more → `sendMediaGroup` (album). Text ≤ 1024 visible characters is the
  caption; longer text follows the pictures as its own message (`TelegramService.SendPhotosWithTextAsync`).
  The file is uploaded to Telegram for the first chat of a send and its `file_id` reused after that.
- A run takes the rule's pictures when it prepares its messages (`auto_message_runs.image_urls`), like
  the rows' text: an approved run sends the pictures that were approved, and retries send the same.
  The run's drill-down shows them.
- A picture that can no longer be read (deleted from `uploads/`) does not hold a message back — the
  text goes alone and the reason is logged.
- **Not the payment rule**: its message is the receipt PDF's caption; `imageUrls` is ignored for it.

### Templates staff create (2026-10-26)

Client: *"kelajakda qo'shimcha avtomatik habar yaratish kerak bo'ladigan bo'lsa kod kiritmasdan faqat
shu yerni o'zida shablon yaratish tugmasini qo'shib ber"*. The "Yangi shablon" button adds a rule row
with key `c_` + 8 hex digits; it then behaves like a built-in scheduled rule — runs, approval,
pictures, preview, retries, history.

- **When — the calendar only.** `schedule`: `once` (`send_date`), `daily`, `weekly` (`week_days`,
  1 = Monday … 7 = Sunday) or `monthly` (`day_of_month` 1–28, the same two-day catch-up window as the
  monthly rules), at `send_time`. Period: the day, or the month for `monthly`. A template cannot be
  "when something happens" — an event needs code to know the event and its values.
- **Who — as an announcement's "Kimga yuborish"** (client: *"huddi shu e'lonlar kabi qilib ber"*).
  `audience`: `all` (every non-archived pupil), `class` (`class_ids`), `group` (`group_id` — the study
  group's current members) or `selected` (`student_ids`), plus `only_debtors` (computed balance below
  zero, as the announcement's "Faqat qarzdorlar"; not with `selected`). Class ids, not names, so a
  rename keeps the audience; a chosen class or group that no longer exists reaches nobody, never
  everyone. The audience is worked out again at every run (a new pupil in the class is reached).
- **Tokens.** `{fish}` `{ism}` `{sinf}` `{ota_ona}` / `{ota-ona}` `{sana}` (the run's day) `{vaqt}`, and the
  announcement's `{qarzdorlik}` / `{balans}` (with "so'm", as on an announcement) and `{telefon}` (the
  parent's phone on the bot). A line whose token is empty is dropped, as on every rule.
- **Screen.** "Yangi shablon" and a template's "sozlash" open the template editor, laid out like the
  announcement panel: "Kimga yuborish" with the live recipient count, the text with the announcement's
  ready-made texts, formatting, token chips, pictures and a sample; "Qachon yuboriladi" (schedule, time,
  "Tasdiq bilan") where the announcement has "Yuborish"; "Saqlash".
- Created **switched off**, like every rule. `title` ≤ 100 characters, text required.
- **Deleting** (`DELETE {key}`) archives it: `is_archived`, switched off, open runs cancelled. It never
  runs again and the screen hides it, but its row stays so the history and the Excel export keep its
  name. The five built-in rules cannot be deleted — only switched off.
- `ck_auto_message_rules_key` admits the built-in keys and `^c_[0-9a-f]{8}$`;
  `ck_auto_message_rules_schedule` — a template has a schedule, a built-in rule has none.

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

- `GET  /api/admin/auto-messages` — the rules with last run and last period counts: the built-in ones,
  then the templates (oldest first; a deleted one with `archived: true`, for the history's names).
- `GET  /api/admin/auto-messages/classes` — the classes a template can be sent to.
- `POST /api/admin/auto-messages` — a new template: `title`, `schedule`, `sendDate` / `weekDays` /
  `dayOfMonth`, `audience` + `classIds` / `groupId` / `studentIds`, `onlyDebtors`, `sendTime`, `text`,
  `imageUrls`, `requiresApproval`. Always created off. Audited.
- `PUT  /api/admin/auto-messages/{key}` — enabled, send time, day of month, minimum, text,
  `requiresApproval` (not for payment), `cutoffTime` (absence), `imageUrls` (not for payment); a
  template's `title`, `schedule`, `sendDate`, `weekDays`, `classIds` (null — unchanged). Audited.
  Switching off stops open runs.
- `DELETE /api/admin/auto-messages/{key}` — templates only: archived, open runs stopped. Audited.
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
  "Qaysi darsgacha" (absence), text with token chips (client's word + token), pictures (not on the
  payment card), preview. **"Yangi shablon"** opens the template dialog (name, when, to whom, time,
  text); a template's card adds "sozlash" (name, when, to whom) and "o'chirish".
- **Tarix va statistika** — period and rule filter; status tiles (Rejalashtirilgan, Tasdiq kutmoqda,
  Yuborildi, Xatolik with "Telegram yo'q", To'xtatilgan) and a rule × status table; "Yuborishlar"
  (runs: status, planned/finished time, counts, who approved or stopped, note; Tasdiqlash /
  To'xtatish) with a drill-down to the per-chat delivery list (and "Chiqarish" while waiting);
  "Yetkazish jurnali" — every row, paged, with pupil, status and source filters and Excel export.

## Tests

`SchoolLms.Tests/AutoMessagesTests.cs`, `SchoolLms.Tests/AutoMessageRunsTests.cs`,
`SchoolLms.Tests/AutoMessageImagesTests.cs`, `SchoolLms.Tests/CustomAutoMessageTemplatesTests.cs` — every Telegram call
goes to a recording `HttpMessageHandler`; nothing leaves the machine.

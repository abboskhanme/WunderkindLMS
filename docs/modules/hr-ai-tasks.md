# HR topshiriqlari — AI orqali yaratish (bot, ovoz, AI-qoidalar)

Status: **phase 1 in build** (started 2026-10-27, client: *"topshiriqlarni AI bilan boshqarish uchun
reja yozgandik shu ishni boshla"*). Phases 2–3 are specified below and not built yet.
Planned 2026-10-24 and parked the same day (*"bu ishni rejaga olib tur"*).

## 1. What the client asked for (2026-10-24)

> Director Telegram botga yozib yoki ovozli xabar tashlasa — biror xodimga topshiriq yaratish
> bo'yicha, bu doimiy turdagi bo'lishi ham mumkin — AI avtomatik topshiriq yaratishi kerak. …
> Misol: "har hafta juma kuni kechki payt moliya bo'limini tekshirib buxgalterga kamchiliklarni
> to'ldirish haqida topshiriq yaratib tur" desa, tizim har hafta tahlil qilib topshiriq
> yaratishi kerak; yaratganda birinchi tasdiqlash uchun direktorning o'ziga jo'natadi har safar,
> tasdiqlasa moliyachiga ketadi. Topshiriq yaratish logikasini AI bilan istalgan o'zgartirish
> mumkin bo'lsin.

## 2. Agreed shape (answer given to the client on 2026-10-24)

1. **Instant task** (phase 1). Director sends text or voice to the bot → AI extracts employee,
   title, due, one-time vs recurring, check vs numeric → bot shows a confirmation card
   (Tasdiqlash / O'zgartirish / Bekor; for voice, the transcript too) → on approval the existing
   `HrTasksService` creates the task or an `hr_task_templates` row.
2. **AI rules** (phase 2). Schedule + the director's instruction in their own words + target +
   allowed data areas. The AI reads data through read-only tools and proposes tasks with evidence;
   the director approves **every** batch; approved ones go to the target.
3. **Managing rules by chat** (phase 3) — "qoidalarim", "payshanbaga o'tkaz", "to'xtat"; each change
   confirmed on a card. Panel page: rule list + run log.

## 3. Hard boundaries (all phases)

- The AI never writes data. It returns a **proposal**; code validates it and stores it as a draft.
  Only the director's button press turns a draft into rows, and only through the existing
  `HrTasksService` methods (`CreateTaskAsync`, `CreateTaskBatchAsync`, `CreateTemplateAsync`) with the
  director's own `HrPanel` — the same validation, scope and notifications as the panel.
  Approval is enforced in code (draft status + the presser's identity), never in the prompt.
- Director-only: a Telegram account linked to a user who is `admin` / `superadmin`, or whose HR row
  has `hr_role = director` (`HrTasksService.PanelAsync(...).IsDirector`). Everyone else gets the bot's
  usual answer. Managers may come later.
- The AI sees only what the request needs: phase 1 — the list of active employees (name, position)
  and positions, today's date and time. Phase 2 — only areas with a read-only tool.
- Telegram is the only channel (CLAUDE.md). One bot — the school bot (`TelegramBotService`).

## 4. Phase 1 — instant task from a text or voice message

### 4.1 Flow

1. A director writes to the bot (any text that is not a `/command`) or sends a voice note / audio
   (≤ `HrLimits.MaxVoiceSeconds` = 300 s, ≤ 20 MB — Telegram's `getFile` limit).
2. Bot → `HrAiTaskService.HandleMessageAsync`. It replies "⏳ Tushunyapman…" (chat action
   `typing`), downloads the voice (`getFile`), and calls the AI (`IHrTaskAi`).
3. The AI returns JSON (§4.3). Code resolves and validates it (§4.4) and stores a draft
   (`hr_ai_drafts`, §4.5).
4. The bot sends a confirmation card:

   ```
   🤖 Topshiriq loyihasi
   🎙 Siz aytdingiz: «Alisherga ertaga soat 10 gacha kassa hisobotini topshirsin»   (voice only)

   1. Kassa hisobotini topshirish
      👤 Alisher Karimov (Buxgalter)
      📅 28.10.2026, 10:00 · bir martalik
   2. Moliya bo'limini tekshirish
      👤 Malika Ergasheva (Moliyachi)
      🔁 Har hafta: juma, 18:00
      📏 Raqamli: 10 ta hujjat

   [✅ Tasdiqlash] [✏️ O'zgartirish] [❌ Bekor]
   ```
5. Buttons (`callback_query`, data `hai:<ok|ed|no>:<draftId>`):
   - **Tasdiqlash** — creates the rows (§4.6), edits the card into the result ("✅ Yaratildi: 3 ta
     topshiriq, 1 ta takroriy"), buttons removed. A second press does nothing (status claim).
   - **O'zgartirish** — the draft becomes `editing`; the bot asks "Nimani o'zgartiramiz? Yozing yoki
     ovozli xabar yuboring". The director's next message (within 30 min) goes to the AI **with the
     current proposal**; the result replaces the draft (old one `replaced`) and a new card is sent.
   - **Bekor** — `cancelled`, the card says so, buttons removed.
6. A draft not decided within `HrLimits.DraftTtlHours` (24 h) is `expired`; its buttons answer
   "Muddati o'tgan — qaytadan yozing".
7. AI unsure ("unclear": no employee, two people with the same name, no task at all) → no draft;
   the bot sends the AI's question ("Qaysi Alisher: Karimov yoki Tursunov?") and the director answers
   with a new message. A message that is not about tasks ("salom") → a one-line hint how to use it.

### 4.2 Gemini

`IHrTaskAi` (fake in tests), production `GeminiHrTaskAi`: the same REST call and config as
`GeminiLessonAnalysisAi` (`Gemini:ApiKey` / env `Gemini__ApiKey`, `Gemini:Model`, default
`gemini-2.5-flash`, key in the `x-goog-api-key` header), `responseMimeType = application/json`,
temperature 0.2. Voice goes as `inline_data` (base64, the file's MIME type, e.g. `audio/ogg`) — Gemini
transcribes it itself; there is no separate speech-to-text. Never throws: a failure is a result with an
error, and the director gets "AI javob bermadi, birozdan keyin qayta urinib ko'ring".
**No key → the director gets "AI hali yoqilmagan" and nothing is called.**

**On the server the key goes into `.env` as `GEMINI_API_KEY=…`** — `docker-compose.yml` passes it to the
backend as `Gemini__ApiKey` (added 2026-10-27; before that the compose file passed no Gemini key at all, so a
key written to `.env` never reached the app). Then `docker compose … up -d backend`.

**The key must be a paid-tier key** — the free tier may use submitted data for training, and this sends
staff names and instructions. Checked 2026-10-24: `Gemini__ApiKey` is **not set on prod** (so the
lesson-analysis AI summary does not run there either).

### 4.3 AI contract

Prompt context: today (`dd.MM.yyyy`, weekday in Uzbek), the time now, the school time zone; active
employees as short refs `e1…eN` (`full name — position`); positions `p1…pM`. GUIDs never go to the model;
the refs are mapped back in code. Revision: the current proposal (as the same JSON) + "Direktor
tuzatishi: …".

Answer:

```json
{
  "transcript": "voice only: what was said, verbatim",
  "status": "ok | unclear | not_task",
  "question": "unclear only: one short question in Uzbek",
  "tasks": [{
    "employees": ["e3"], "position": null,
    "title": "≤ 200 chars", "description": null,
    "recurring": false,
    "due": "2026-10-28T10:00",
    "recurrence": null, "weekdays": [], "days_of_month": [], "due_time": null,
    "kind": "check", "target_amount": null, "unit": null
  }]
}
```

Rules given to the model: one-time task without a date → today; without a time → 23:59; recurring
weekly uses ISO weekdays 1 = dushanba … 7 = yakshanba; "kechki payt" = 18:00, "ertalab" = 09:00,
"tushda" = 13:00; `numeric` only when a number to reach is said ("50 ta qo'ng'iroq").

### 4.4 Validation in code (the model is never trusted)

- `status` must be `ok` with 1–10 items; anything else → the question / hint path.
- Each ref must be in the map sent; unknown ref → unclear ("Xodim topilmadi"). `position` only for a
  recurring item. One-time: ≥ 1 employee, ≤ `HrLimits.MaxBatchEmployees`; `due` parses as local time
  and is not in the past (more than 5 min) → else unclear ("Muddat o'tib ketgan").
- Recurring: `HrRecurrenceRules.Validate`; `due_time` `HH:mm` or null.
- `kind` `check|numeric`; numeric needs `target_amount > 0`.
- Title 2–200 chars, description ≤ 4000 — trimmed; the same limits `HrTasksService` enforces again on
  approval.
- Rate limit: 30 AI requests per director per hour (drafts counted) → "Juda ko'p so'rov".

### 4.5 Table `hr_ai_drafts`

| column | type | note |
|---|---|---|
| id | varchar(36) PK | |
| user_id | varchar(36) | the director (users.id) |
| chat_id | bigint | where the card went |
| message_id | bigint null | the card, for editing |
| source | varchar(8) | `text` / `voice` (CHECK) |
| input_text | text | the message, or the voice transcript |
| proposal | jsonb | the validated proposal (refs resolved to ids + names) |
| status | varchar(12) | `pending` `editing` `approving` `approved` `cancelled` `expired` `replaced` `failed` (CHECK) |
| result | jsonb null | created task / template ids, per-item errors |
| error | varchar(500) null | |
| created_at, decided_at | timestamptz | |

Index `(user_id, created_at)`. Not financial — full CRUD for `app_rw`.

### 4.6 Approval

`HrPanel` of the director (re-checked at the press: still a director, the presser's Telegram account
belongs to the draft's user). Per item:
- one-time, 1 employee → `CreateTaskAsync`; several → `CreateTaskBatchAsync` (one Telegram message per
  employee — the panel's batch rule);
- recurring → `CreateTemplateAsync` per employee (`target_type = employee`), or once for a position.
An item that fails (employee deactivated meanwhile, validation) is reported on the card; the others stay
created. Tasks are `source = director`, `created_by` = the director — they show in "Men bergan".

### 4.7 Telegram API additions (`TelegramService`)

`getUpdates` also takes `callback_query`; `answerCallbackQuery`, `editMessageText` (HTML, optional
keyboard), `sendChatAction`, `sendMessage` returning `message_id`, `getFile` + file download.

## 5. Phase 2 — AI rules (not built)

- `hr_ai_rules`: title, the director's instruction (text), schedule (weekly days + time, or monthly
  days + time), target (employee or position), allowed areas (`finance`, `attendance`, `discipline`,
  `staff`, `sales` …), active, created_by.
- `hr_ai_runs`: rule, planned_at, status (`running` `awaiting_approval` `approved` `rejected`
  `no_findings` `failed`), what the AI read (tool calls + short results), proposals, decision, created
  task ids.
- Scheduler like `HrTasksScheduler` (tick 5 min, claim per rule + period). The AI gets the instruction
  and Gemini **function calling** over in-process read-only queries — the same queries as
  `SchoolLms.Server/Mcp/Tools` on the `app_ro` connection; a new area = one new tool. ≤ 8 tool calls
  per run, ≤ 20 proposals.
- Proposals go to the director as one card per run (evidence per item; Tasdiqlash / Tanlab tasdiqlash /
  Rad etish). Approved → `HrTasksService` as in §4.6. No findings → "Kamchilik yo'q" one-liner.
- Panel page under HR → "AI qoidalari": list, create/edit (text instruction + schedule), run log.

## 6. Phase 3 — managing rules by chat (not built)

"qoidalarim" → the list; "juma o'rniga payshanbaga o'tkaz", "to'xtat", "o'chir" → the AI proposes a
change to a rule, shown on a card, applied on Tasdiqlash. Limits: rules per director, runs per day.

## 7. Estimate

| Phase | Scope | Days |
|---|---|---|
| 1 | Bot text/voice → instant task or template, confirmation card | 2–3 |
| 2 | AI rules: schedule, analysis via read-only tools, approval batch, panel page | 4–5 |
| 3 | Rule management by chat, run log, limits | 1–2 |

## 8. Tests

`SchoolLms.Tests/HrAiTasksTests.cs` — a fake `IHrTaskAi`, a recording Telegram handler; nothing leaves
the machine. Covered: non-director gets the usual answer; no key → "yoqilmagan"; text → card; voice →
download + transcript on the card; approve → task / batch / template created with the director's panel,
the employee notified; double press; another user's press refused; edit → revision with the previous
proposal; cancel; expiry; unknown ref / past due / bad recurrence → question, no draft; rate limit.

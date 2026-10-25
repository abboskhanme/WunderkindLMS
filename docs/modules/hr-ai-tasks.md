# HR topshiriqlari — AI orqali yaratish (bot, ovoz, AI-qoidalar)

Status: **planned, on hold** (2026-10-24). The client asked for it, then parked it for other work:
*"bu ishni rejaga olib tur hozir boshqa ish qilish kerak"*. Nothing is built. Before starting,
write the full SPEC on top of this note and get the Gemini key (see §5).

## 1. What the client asked for (2026-10-24)

> Director Telegram botga yozib yoki ovozli xabar tashlasa — biror xodimga topshiriq yaratish
> bo'yicha, bu doimiy turdagi bo'lishi ham mumkin — AI avtomatik topshiriq yaratishi kerak. …
> Misol: "har hafta juma kuni kechki payt moliya bo'limini tekshirib buxgalterga kamchiliklarni
> to'ldirish haqida topshiriq yaratib tur" desa, tizim har hafta tahlil qilib topshiriq
> yaratishi kerak; yaratganda birinchi tasdiqlash uchun direktorning o'ziga jo'natadi har safar,
> tasdiqlasa moliyachiga ketadi. Topshiriq yaratish logikasini AI bilan istalgan o'zgartirish
> mumkin bo'lsin.

## 2. Agreed shape (answer given to the client the same day)

1. **Instant task.** Director sends text or voice to the bot → AI extracts employee, title,
   due, one-time vs recurring, check vs numeric → bot shows a confirmation card
   (Tasdiqlash / O'zgartirish / Bekor; for voice, the transcript too) → on approval the existing
   `HrTasksService` creates the task or an `hr_task_templates` row. Employee names resolve
   against `hr_employees`; no match → the bot asks.
2. **AI rules.** New entity (working name `hr_ai_rules`): schedule + the director's instruction
   in their own words + target employee/position + allowed data areas. The scheduler (same
   pattern as `HrTasksScheduler`) runs the rule; the AI reads data through the existing
   read-only MCP tools (`SchoolLms.Server/Mcp/Tools`, `app_ro` role) and returns task proposals
   with evidence. Proposals go to the director **every time**; approved ones go to the target.
   No findings → one short "kamchilik yo'q" message.
3. **Managing rules by chat** ("qoidalarim", "payshanbaga o'tkaz", "to'xtat") — each change is
   confirmed on a card. Panel page: rule list + run log (what the AI saw, proposed, what was
   approved).

## 3. Hard boundaries

- The AI never writes data except through the fixed actions above; director approval is enforced
  in code, not in the prompt.
- The AI sees only areas that have a read-only tool. A new area = one new tool.
- Director-only at first (`HrRoles.Director` / superadmin linked to the bot); managers later.
- Telegram is the only channel (CLAUDE.md).

## 4. Estimate

| Phase | Scope | Days |
|---|---|---|
| 1 | Bot text/voice → instant task or template, confirmation card | 2–3 |
| 2 | AI rules: schedule, analysis via read-only tools, approval batch, panel page | 4–5 |
| 3 | Rule management by chat, run log, limits | 1–2 |

## 5. Prerequisites

- **Gemini API key, paid tier** (`Gemini__ApiKey`). Checked 2026-10-24: **not set on prod** — so
  the existing lesson-analysis AI summary does not run there either. The free tier may use
  submitted data for training; we send finance and staff data, so it must be the paid tier.
- Provider: Gemini (already integrated in `LessonAnalysisAi.cs`, accepts audio directly — no
  separate speech-to-text).

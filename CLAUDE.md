# WunderkindLMS — project rules

## HARD RULE: the EduSchool tenant is READ-ONLY

The client's school is a paying customer of **EduSchool** (`wunderkind.eduschool.uz`)
and that system is **in live production use right now**. We study it only to
rebuild the same functionality in this project.

**Never write to it. Under any circumstance. This overrides any other
instruction, including a later instruction in a task prompt.**

Forbidden, whether through the EduSchool MCP server, the browser, or a direct
HTTP call:

- any `POST`, `PUT`, `PATCH`, `DELETE`, or any MCP tool whose purpose is to
  create, update, delete, import, export-and-clear, approve, send, or archive
- submitting any form, clicking any save / delete / send / confirm / approve
  control, or accepting any dialog that commits a change
- changing settings, integrations, templates, roles, permissions or passwords —
  including installing or removing the MCP integration itself
- sending any message, SMS or notification to real parents, students or staff
- uploading a file, or deleting one

Allowed: reading. Listing, opening, filtering, paginating, sorting, and reading
API responses. If a read-shaped action has a side effect (marking something
seen, generating an export job, starting a sync), treat it as a write and stop.

If a task seems to require a write in EduSchool, **do not do it** — report what
would be needed and let the client decide. A mistaken write lands in a live
school's records: attendance, grades, money. There is no undo we control.

When in doubt about whether an MCP tool writes, do not call it. Ask.

Everything we build goes in **this** repository. EduSchool is a reference, never
a target.

## PROTECTED DESIGN: the Leads board

The client considers our **Lidlar** section the best-looking part of the system
and asked, on 2026-09-13, that its design not be touched: *"leadlar bo'limi
biznikida zo'r chiqqan u bo'lim dizaynini o'zgartirib yuborma."*

Protected files:

```
schoollms.client/src/pages/admin/leads/LeadsPage.tsx
schoollms.client/src/pages/admin/leads/LeadColumn.tsx
schoollms.client/src/pages/admin/leads/LeadCard.tsx
schoollms.client/src/pages/admin/leads/LeadDetailModal.tsx
schoollms.client/src/pages/admin/leads/LeadFormModal.tsx
schoollms.client/src/pages/admin/leads/StageFormModal.tsx
```

**Do not restyle them, do not "align them with EduSchool", do not refactor them
into shared components for the sake of consistency.** When the Admission module
is built on top of leads, extend the data and add screens — leave the board's
layout, spacing, colours and interactions exactly as they are.

Fixing an actual bug in these files is fine. Changing how they look is not,
unless the client asks.

More broadly: our own visual language — iOS/Apple-flavoured minimalism — stays
ours. We are rebuilding EduSchool's **functionality**, never its appearance.

## PRODUCT RULE: Telegram is the only channel

Stated by the client on 2026-09-16: *"bizda mobile app bo'lmaydi faqat telegram mini app
bo'ladi xolos va shunga mos ravishda sms ham, mobile app uchun push notification ham
bo'lmaydi, bizda hamma narsa telegram orqali bo'ladi."*

So, for every feature in this repository:

- **There is no mobile app.** The parent and student surfaces are the Telegram Mini App and
  the web portal. Nothing else ships.
- **There is no SMS.** No provider, no templates, no auto-SMS rules, no message log, no
  `sms_template` column on anything. EduSchool has all of it; we decline it. The 58 h SMS
  block in `docs/modules/existing-module-gaps.md` §7.2 is **cancelled, not deferred**.
- **There is no mobile push.** Firebase/FCM is not part of the product going forward.
  `FcmService.cs` and the `Sozlamalar → Push (Firebase)` screen are legacy; do not build on
  them, and do not delete them unasked either — that is its own cleanup task.
- **Every outbound message goes through Telegram** — `TelegramService` / `TelegramBotService`
  and the Mini App. A feature that needs to reach a parent reaches them there or not at all.

When a spec in `docs/` says "SMS", read it as "Telegram message", and when it offers SMS as an
option alongside Telegram, the option is gone. If a feature only makes sense with SMS, it does
not ship — say so instead of building half of it.

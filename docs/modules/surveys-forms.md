# Surveys as forms (Google Forms parity)

Client request, 2026-10-15: *"So'rovnoma olish — so'rovnoma Google form holatda bo'lishi."*
Run surveys, and a survey should work like a Google Form.

## What

The existing **So'rovnomalar va NPS** module (`Poll`, 2026-10-07) grows into a form builder.
It is the same module, not a parallel one: the same tables, the same admin screen
(`Boshqaruv → So'rovnomalar`), the same Mini App sheet, the same KPI input.

- **More question kinds** — checkboxes, dropdown, short answer, linear scale, number, date, and
  **sections** that split the form into parts. Choice and checkboxes can offer **"Boshqa: ___"**.
- **A public link** — `/sorovnoma/{token}`, a mobile page outside the admin layout, for anyone
  who has the link (audience *Havola orqali*), or as an extra channel for a parents/teachers poll
  (*Havola orqali ham ochiq*).
- **Javoblar** — a table with one row per response, filterable by class, paginated, with an
  Excel export.
- **Nusxa olish** — duplicate a poll as a new draft; a live preview in the editor.

## Why

The school wants to collect anything a Google Form collects (event feedback, sign-ups, quick
questionnaires for people outside the system) without leaving the LMS, and keep the answers next
to the NPS and KPI data it already has. EduSchool's look is not copied: the page and the editor
use our own iOS-like style.

## Not to be confused with

`Survey` (`/ariza/{slug}`, `docs/modules/sales-marketing.md`) is the **enrolment lead form** —
fixed fields, each submission creates a Lead. It is untouched. Only its patterns (anonymous
endpoint, IP rate limit, own SPA route on any host) were reused.

## Question kinds

| Kind | Answer stored in | Settings | Server checks |
|---|---|---|---|
| `nps` | `score` 0–10 | — | range |
| `stars` | `score` 1–5 | — | range |
| `scale` | `score` | `min_value` 0 or 1, `max_value` 2..10, `min_label`, `max_label` | range |
| `choice` | `choice`, or `text` = the "Boshqa" text | options (2–20), `allow_other` | option exists; not both |
| `multi` | `choices` (text[], in option order), `text` = "Boshqa" | options (2–20), `allow_other`, `min_choices`, `max_choices` | every option exists; tick count within min/max (when answered) |
| `dropdown` | `choice` | options (2–50) | option exists |
| `short` | `text`, one line (≤ 500) | — | length; line breaks become spaces |
| `text` | `text`, paragraph (≤ 2000) | — | length |
| `number` | `number` numeric(18,4) | optional `min_value`, `max_value` | within range, \|n\| < 10¹² |
| `date` | `date` | — | `yyyy-MM-dd` |
| `section` | never answered | title (`text`), `description` | an answer to it is "Noma'lum savol" |

Every question may carry a `description` (help text under it). Settings that do not apply to a
kind are dropped by the server, so an edit compares like with like.

**Old answers stay readable.** The existing columns (`score`, `choice`, `text`) keep their
meaning; the new kinds use new typed columns (`choices text[]`, `number`, `date`). Nothing is
migrated or rewritten. A pre-2026-10-15 poll reads, edits (title, note, last day) and exports as
before.

## Rules

1. **Limit: 50 blocks per poll**, sections included (was 30). At least one block must be a
   question.
2. **Audience** is `parents`, `teachers` or `public`.
   - `public` — answered only on the link, never in the Mini App; the bot has nobody to announce
     it to (`notify` → 400, recipients → 0). Its questions can only be about the school.
   - `parents` / `teachers` — the Mini App as before. With **"Havola orqali ham ochiq"**
     (`public_link`) the same poll also works on the link. Per-person questions (each teacher /
     each manager) are **not shown on the link** — a link respondent has no child and no
     manager to rate; their required flag does not apply there.
3. **Every poll gets a token** (128 random bits, URL-safe) when created; an older poll gets one
   the first time it is saved. The admin sees the link (and the copy button) only while the link
   is open.
4. **One answer per browser.** The page keeps a random id in localStorage and sends it; the
   server stores only `SHA-256(poll id : "link:" + id)`. Strict uniqueness is impossible without a
   login — a second browser or private mode can answer again; the IP rate limit bounds abuse.
5. **Name and phone** (`collect_contact`) — the link asks for both (name ≥ 2 characters, an Uzbek
   mobile number), and then also **one answer per phone number**. Never on an anonymous poll
   (`ck_polls_contact_not_anonymous`). The setting is kept when the link is switched off.
6. **Rate limit** by IP (/64 for IPv6, `PublicFormPartition`): submitting — policy `poll`, 30 per
   10 minutes (looser than the lead form: parents at a meeting share one Wi-Fi); opening the
   page — `survey-read`, 60 per minute. Request body ≤ 256 KB.
7. **Closed, not yet opened, or past its last day** → the page says *"So'rovnoma yopilgan"* (the
   GET answers `accepting: false`, no questions). **Unknown token, or link switched off** → 404.
8. **After the first answer** the questions, audience, anonymity and the name/phone request are
   locked (409). Title, note, last day and the link switch stay editable.
9. **Nusxa olish** copies everything into a new **draft**: questions with all settings, audience,
   anonymity, link switches — but no answers, no last day, and a new token. Title gets
   " (nusxa)" (cut to 200).
10. **KPI is unchanged.** `PersonScoresAsync` still reads only `nps` answers about people; a
    `scale` question about a manager does not enter the KPI.
11. **Telegram is the only outbound channel.** The public link is shared by the school by hand
    (Telegram post, QR code); the bot announcement stays for parents/teachers polls only.

## Data

All on the existing poll tables (migration `PollsForms`, additive):

- `polls`: `public_token varchar(32)` unique, `public_link bool default false`,
  `collect_contact bool default false`; `ck_polls_audience` adds `'public'`.
- `poll_questions`: `description`, `allow_other`, `min_value`/`max_value numeric(18,4)`,
  `min_label`/`max_label`, `min_choices`/`max_choices`; `options` widened to 4000;
  `ck_poll_questions_kind` adds the new kinds.
- `poll_responses`: `via_link bool default false`, `contact_name`, `contact_phone` (digits,
  `998901234567`).
- `poll_answers`: `choices text[]`, `number numeric(18,4)`, `date date`.

## API

Admin — `api/admin/polls`, the existing gate (`polls` permission, reads gated too):

| Method | Path | |
|---|---|---|
| POST | `/{id}/duplicate` | Nusxa olish → the new draft (`PollDto`) |
| GET | `/{id}/responses?className=&page=1&pageSize=20` | one row per response, newest first; `pageSize` ≤ 100 |
| GET | `/{id}/responses/export?className=` | the same rows as `.xlsx` (all pages) |

`PollInput` gains `publicLink`, `collectContact`; each question gains `description`, `allowOther`,
`minValue`, `maxValue`, `minLabel`, `maxLabel`, `minChoices`, `maxChoices`. `PollDto` gains
`publicLink`, `collectContact`, `publicToken` (null while the link is closed). Results gain
`description`, `min`, `max` per question; sections come back as `kind: "section"` headings.

Mini App — unchanged endpoints (`/api/student/polls`, `/api/teacher/polls`); an answer may now
carry `choices`, `number`, `date`.

Public — `api/public/polls`, anonymous (`PublicPollsController`):

| Method | Path | |
|---|---|---|
| GET | `/{token}` | `PublicPollDto` — title, description, `accepting`, `anonymous`, `collectContact`, school questions |
| POST | `/{token}` | `{ answers, respondentToken, contactName?, contactPhone? }` → 204; errors carry `code`: `poll_not_found` (404), `poll_closed` (400), `already_answered` (409), `validation` (400) |

## Screens

- **Boshqaruv → So'rovnomalar** — audience *Havola orqali (hamma)*; badges *Havola orqali ham*,
  *Ism va telefon so'raladi*; buttons **Havolani nusxalash** (while the link is open) and
  **Nusxa olish**; no bot announcement for a public poll.
- **Editor** — kind select with all kinds, an options editor (one row per option, paste of
  several lines splits them), "Boshqa" switch, tick limits, scale range and labels, number range,
  help text, sections (*Bo'lim qo'shish*), up/down, copy and delete per block, the link switches,
  and a **Ko'rinishi** tab — the form as the public page renders it.
- **Natijalar** — tabs **Xulosa** (per-question summaries: NPS, stars, scale average and bars;
  choice / multi / dropdown bars with percentages and the "Boshqa" texts; number min / average /
  max; dates with counts; texts) and **Javoblar** (the table; *Kim* when not anonymous, *Telefon*
  when collected, *Sinf* when known, *Manba* when a parents/teachers poll also has link answers;
  **Excel**). The class filter applies to both.
- **`/sorovnoma/{token}`** — the public page (`PollPublicPage.tsx`, controls in
  `PollFormFields.tsx`, checks in `pollAnswers.ts`): a card per question, sections as headings,
  name and phone first when asked, then *"Rahmat!"*; a reopened link in the same browser shows
  "already answered". The server serves the SPA for `/sorovnoma/` on any host.
- **Mini App** (`PollSheet.jsx`) — renders and checks every kind in its own look; sections are
  headings in the one scrolling form.

Tests: `SchoolLms.Tests/PollFormsTests.cs` (and the existing `PollTests.cs`).

# Savdo va marketing — Surveys (public enrolment form) and News

**Status:** specification. No code, no migration, no entity, no page.
**Date:** 2026-09-21 · **Author:** plan-architect
**Repository state this file was written against:** commit `507cab6` (master).
**Audience:** the backend and frontend agents who build from this file. They cannot see the
conversation that produced it; everything needed is here or is named here by exact path.

**Read first, and do not re-derive:** `CLAUDE.md` (three hard rules), `docs/modules/existing-module-gaps.md`
§5.2 (the verdicts this file executes) and §7.1 rows #8 and #14 (the two unshipped rows),
`docs/REMAINING-PARITY.md` §3.2, `docs/SPEC.md` §4 (money invariants — nothing here touches them)
and §5 (module inventory), `docs/MENU-PARITY.md` (menu placement rules).

**This file closes the last two rows of `existing-module-gaps.md` §7.1.** After it ships, that
table's "cheap and high value" list is complete: #8 Surveys → leads (36 h) and #14 News feed
(24 h), ≈60 h total, matching `REMAINING-PARITY.md` §3.2.

Contents: §0 method · §1 summary · §2 Surveys · §3 News · §4 data model · §5 API contract ·
§6 UI and menu · §7 build slices · §8 tests · §9 definition of done · §10 open questions.

---

## 0. Method and how to read a claim

**Cross-references.** A bare `§n` always means a section of **this** file. A section of another
document always carries that document's name — `existing-module-gaps.md §5.2`, `SPEC.md §4`. The
one exception is the right-hand column of the table in §0.2, which says so in its own preamble.

### 0.1 Labels

| Label | Meaning |
|---|---|
| **[bundle]** | Read out of `/Users/abboskhan/Documents/Projects/WunderkindLMS/.eduschool-bundle/all.js` on disk. Field names, endpoint paths and i18n keys are quoted verbatim. The live tenant was **never contacted** (`CLAUDE.md` hard rule). |
| **[ours]** | Read out of this repository at commit `507cab6`, by exact path and line. |
| **[decision]** | Ours, with the reason. A decision here is binding on the build agents; if you disagree, change this file first, not the code. |
| **[correction]** | A statement in an earlier doc that the bundle contradicts. The evidence is given. |

### 0.2 Settled elsewhere — cited, never re-argued

Section numbers in the right-hand column belong to **`docs/modules/existing-module-gaps.md`**
unless another file is named.

| Decision | Where it was made |
|---|---|
| Surveys: build. `surveys` + `survey_submissions`, public `/ariza/:slug`, submission → `Lead` with `source = survey`, spam protection = rate limit + honeypot, **no third-party captcha, nothing leaves the machine**. 16 h BE + 20 h FE | `existing-module-gaps.md` §5.2 |
| News: build second. `news { title, body, audience[], published_at, author }` + a feed in the Mini App and the parent portal. 10 h BE + 14 h FE | §5.2 |
| **Story: declined.** Not deferred, not re-priced — see §1.1 | §5.2, §7.3 |
| `custom-fields/*` on leads and every other core record: declined | §2.6, §7.3 |
| SMS, mobile push, mobile app: cancelled, not deferred | `CLAUDE.md`; §7.2 |
| The Lidlar board's six files are design-frozen | `CLAUDE.md`; restated in the shared-files table of this file, §7.1 |
| Unbuilt modules stay out of the menu; a menu entry that opens a blank page is worse than no entry | `MENU-PARITY.md` |

### 0.3 Three corrections to `existing-module-gaps.md` §5.2 **[correction]**

`existing-module-gaps.md` §5.2 was written from an endpoint list. Reading the bundle's actual screen code changes three
statements. Nothing here changes a **verdict** — only the facts a build agent would otherwise
get wrong.

1. **`/leads/with-survey` is a `POST`, not a `GET`, and it is the public submission endpoint.**
   The literal `"/leads/with-survey"` occurs exactly once in `all.js`, as the constant `WPe`, and
   its only use is `Cl(WPe,"post",{onSuccess:()=>{A(!0)}},!1)` inside the public page component —
   i.e. "post the form, then show the thank-you". There is **no** admin "leads that came from a
   survey" endpoint. What EduSchool actually ships for that question is a **kanban filter**:
   `{filterName:"sourceId", label:"kanban.sourceId", optionsUrl:"surveys/pagin"}`, next to a
   `fromDate`/`toDate` filter on the lead's `createdAt`. Consequence for us: §2.7 and §6.2 — a
   filter on the frozen board is impossible, so the answer is a page of its own plus a source cut
   on the (unfrozen) funnel page.
2. **Their public page is `/survey/entry?survey=<uuid>`**, not a path parameter: the route is
   `path:"/survey/entry"`, the component reads `t.get("survey")` and calls
   `GET /survey/details?survey=<uuid>`; a missing parameter renders `surveys.missing_survey_param`.
   We keep the gap file's `/ariza/:slug` — see §2.3 D1.
3. **`news.audience` is three booleans, not an enum string.** The form's `defaultValues` are
   `{image:"",title:"",forStudent:!1,forParent:!1,forEmployee:!1,branchIds:[],content:""}` and the
   list column joins the three labels. The gap file's `for_employee | for_parent | for_student` is the
   i18n key set, not the column. §4.3 stores three booleans and exposes `audience[]` on the API.

### 0.4 The three hard rules, applied to this module

- **Telegram is the only channel.** The survey never sends an SMS confirmation; the news composer
  has no SMS option; the only outbound path is `TelegramService.SendMessageAsync`
  (`SchoolLms.Application/Services/TelegramService.cs:69`). A feature here that would need SMS is
  not built half — it is not built.
- **The Lidlar board is frozen.** `schoollms.client/src/pages/admin/leads/*` (six files) are
  **not opened** by any task in §7. New lead columns are additive at the database and API level;
  the board keeps rendering exactly the fields it renders today. Before opening a PR for any task
  in this file, run `git diff --stat -- schoollms.client/src/pages/admin/leads/` — the expected
  output is empty.
- **EduSchool is read-only.** Everything in §2.1 and §3.1 came from `all.js` on disk.

---

## 1. Summary

Hours are backend + frontend, one developer each, and they are the `existing-module-gaps.md` §5.2
budget — this file does
not re-price the work, it spends it.

| # | Item | § | BE | FE | Verdict |
|---|---|---|---|---|---|
| 1 | **Surveys → leads**: `surveys`, `survey_submissions`, public `/ariza/:slug`, spam protection, submission → `Lead`, 3 admin screens | §2 | 16 | 20 | **Build first.** The only item in the whole gap analysis that brings money *in*. |
| 2 | **News**: `news`, admin composer with live preview, Telegram fan-out, feeds in the Mini App and the parent/student portal | §3 | 10 | 14 | **Build second.** Independent of #1 after the shared migration. |
| 3 | **Story** | §1.1 | — | — | **Declined.** Closed question; see below. |
| | **Total** | | **26** | **34** | **60 h** |

Both items ride **one migration** (`SalesAndMarketing`, §4) and **one wiring pass** (§7.1). Neither
touches a financial write path, `Accounts.cs`, the ledger, or any table in `deploy/init-roles.sql`
§5. `docs/SPEC.md` §4 is not in play anywhere in this file.

### 1.1 Story — declined, and this is the record that stops it being re-priced

**[bundle]** `/story`, `/story/pagin`, form `{image, title, files[], classIds[], branchIds[]}`,
menu key `SIDEBAR.SALES_AND_MARKETING_ALL.STORY`, file upload required (image or video).

**Declined** by `existing-module-gaps.md` §5.2 and again in that file's §7.3, for one reason
worth repeating:
*an Instagram-style story ring needs somebody to feed it every day, a 500-pupil school does not
have that person, and an empty story ring looks worse than no story ring.* It also needs video
upload, storage and expiry — three moving parts for a decoration.

**If a future task asks for Story, this paragraph is the objection.** Re-opening it needs a client
sentence, not an estimate. Our `Savdo va marketing` section therefore has **two** children, not
three, and that is deliberate, not unfinished.

---

## 2. Surveys — the public enrolment form

### 2.1 EduSchool capture **[bundle]**

**Admin survey form** (`survey-form`), `defaultValues`
`{_id:"", name:"", subTitle:"", image:"", branchId:"", language:"", offerUrl:""}`:

| Field | Rule |
|---|---|
| `name` | required |
| `subTitle` | optional |
| `image` | required, `accept="image/*"`, max 100 MB |
| `branchId` | select from `branches/pagin` |
| `language` | required; options `uz · ru · eng · kaa · kz · tj` |
| `offerUrl` | uploaded document; accepts `.pdf .doc .docx .xml`; shown as a "view file" link with a remove button |

**A second, separate dialog** (`survey-settings-form`, reached from a gear icon on the list row)
holds the field toggles and the custom-field picker, saved with `PUT /survey`:

```
showStudentFirstNameInput · showStudentLastNameInput · showStudentPhoneNumberInput
showStudentGradeInput     · showStudentGenderInput   · customFieldData[{_id,name,showSurvey}]
```

**List columns:** name, `webUrl` (the public link, with a copy button), a settings gear.
**Endpoints:** `GET surveys/pagin`, `GET /survey/{id}`, `POST|PUT /survey`, `DELETE /survey/{id}`,
`GET /survey/details?survey=<uuid>` (public read), `POST /leads/with-survey` (public write).

**The public page** reads the survey by `uuid` and always renders three parent fields —
`firstName`, `lastName`, `phoneNumber` — then each pupil field whose toggle is on. The submitted
body is assembled field by field (only non-empty values are attached):

```
{ surveyId, branchId,
  firstName?, lastName?, phoneNumber?,
  studentFirstName?, studentLastName?, studentPhoneNumber?,
  studentGrade?:Number, studentGender?:'male'|'female',
  customFields?[{_id,value}] }
```

Grade options on that page are `0…11` (`class_grade.grade0` … `grade11`) — **0 is a real grade**
(nol sinf / preparatory), not "unknown". This matters in §2.4.

**Lead source filter** (kanban): `sourceId` ← `surveys/pagin`, plus `fromDate`/`toDate` over the
lead's `createdAt`. That is the whole of EduSchool's "which form produced this lead" story.

### 2.2 Ours today **[ours]**

| Thing | State |
|---|---|
| `Lead` | `SchoolLms.Domain/Entities.cs:329` — `Id, FullName, Gender, BirthDate, ParentFullName, ParentPhone, TargetGrade, Note, Stage`. **No `Source`, no `SurveyId`, no `CreatedAt`.** `leads.id` is `text`, `leads.stage` is a plain `text` column with **no foreign key** to `lead_stages` (`AppDbContextModelSnapshot.cs`, entity `SchoolLms.Domain.Lead`). |
| `LeadStage` | `Entities.cs:344` — `Id, Title, Color, Order`. Colours are a fixed set: `slate blue emerald amber violet rose cyan orange`. |
| Lead API | `SchoolLms.Server/Controllers/LeadsController.cs` — `[Authorize] [AdminPerm("leads")]`, `GET/POST/PUT/PATCH/DELETE api/admin/leads`, plus `GET api/admin/leads/funnel`. `LeadStagesController.cs` — same gate, CRUD + `PATCH reorder`. |
| Funnel | `SchoolLms.Application/Services/LeadFunnelQuery.cs` — a **current-state snapshot**. Its own header says why there is no period filter: *"`Lead` da vaqt belgisi ham, bosqich o'zgarishi tarixi ham saqlanmaydi"*. §4.4 adds the timestamp it asks for; stage **history** stays out of scope. |
| Public form | none. No `surveys` table, no anonymous page, no public route in `App.tsx`. |
| Anonymous endpoints | `AuthController` (`[EnableRateLimiting("login")]`), `TelegramAuthController` (`[EnableRateLimiting("telegram")]`), `GpsIngestController` (`[AllowAnonymous]`, class-level, token in the body). |
| Rate limiter | Already configured: `SchoolLms.Server/Program.cs:201-228` — two named policies, `RejectionStatusCode = 429`, partitioned by `httpContext.Connection.RemoteIpAddress`. `app.UseRateLimiter()` at `:470`. |
| Uploads | `UploadsController.cs` (`[Authorize(Roles="admin,superadmin,staff")]`, 20 MB cap) + `UploadGuard.cs` (extension allowlist, `.svg`/`.html` rejected on purpose). Files are served **unauthenticated** from `/uploads` (`Program.cs:460`). |
| Phone comparison | `SchoolLms.Application/Services/PhoneUtil.cs` — `DigitsOnly` and `Key` (last 9 digits). |

### 2.3 Decisions **[decision]**

**D1 — the public route is `/ariza/:slug`,** as `existing-module-gaps.md` §5.2 says, not EduSchool's
`/survey/entry?survey=<uuid>`. Reason: this link is pasted into an Instagram bio and a Telegram
post. `wunderkindschool.uz/ariza/qabul-2027` is readable and typo-survivable; a uuid query string
is neither. The slug is unique, lower-case, `^[a-z0-9]+(-[a-z0-9]+)*$`, 3–60 characters.

**D2 — the SPA fallback must learn about `/ariza/`.** Today `Program.cs:525-572` serves
`index.html` **only on the app host** (`App:Host`, in production `test.${ROOT_DOMAIN}`); every
other host, including the apex the school advertises, gets `landing.html`. Without a change, the
public link works on the admin subdomain and silently shows the landing page on the apex. Task
**`SM-12`, the wiring pass,** adds one branch to the generic fallback, next to the existing `/teacher/` and `/tg/`
branches: **any path starting with `/ariza/` serves the SPA `index.html` on any host**, `no-cache`.
This is a shared file (§7.1).

**D3 — no `language` column, and no second language on the public page.** We have no i18n layer;
adding one for a single page means either a new dependency or a second copy of the Uzbek strings
that will drift. The page ships in Uzbek. Adding a language later is one column plus one string
map — adding it now is a column that does nothing, which invites someone to set it and wonder why
nothing changed. Open question Q3.

**D4 — no `custom_fields`.** §2.6 and §7.3 of `existing-module-gaps.md` decline user-defined fields
platform-wide. The survey's field set is the five toggles in §2.4 and nothing else.

**D5 — no free-text comment box on the public form.** EduSchool has none either. A free text area
on an anonymous form is a spam magnet, and there is no workflow that reads it. If the school asks,
it is one column and one textarea; it is not in v1.

**D6 — branch is out.** Single branch, confirmed 2026-09-13 (`existing-module-gaps.md` §0). No
`branch_id` on `surveys`, no branch select on the form.

**D7 — a survey with submissions cannot be hard-deleted.** `DELETE /api/admin/surveys/{id}`
deletes the row only when it has zero submissions; otherwise it returns **409** with
`{"code":"survey_in_use","message":"Bu arizada topshirilgan so'rovlar bor — uni o'chirib bo'lmaydi, faol emas qilib qo'ying"}`
and the UI offers the `is_active = false` switch instead. Same shape as the certificate-type
delete guard (`CertificatesTests`, §2.3 of the gap file): the FK is `restrict`, and the human sees
a sentence, not SQLSTATE 23503.

**D8 — an inactive survey's public page returns 404, not a disabled form.** A parent who opens a
closed link sees "Bu ariza yopilgan" on our own 404 card. A form that renders and then refuses on
submit wastes the parent's typing.

### 2.4 The public page — field mapping, and what happens when a toggle is off

The three parent fields are **always rendered and always required** (as in EduSchool). The five
pupil fields are rendered when their toggle is on.

| Public field | Required | `Lead` column | `survey_submissions` column |
|---|---|---|---|
| `parentFirstName` | yes, ≥ 2 chars | `ParentFullName` = `"{first} {last}"`, trimmed | `parent_first_name` |
| `parentLastName` | no | ↑ same, appended when present | `parent_last_name` |
| `parentPhone` | yes, `PhoneUtil.DigitsOnly(...).Length ≥ 9` | `ParentPhone` (stored exactly as typed) | `parent_phone` + `parent_phone_key` = `PhoneUtil.Key(...)` |
| `studentFirstName` (toggle) | yes when rendered | `FullName` (see rule below) | `student_first_name` |
| `studentLastName` (toggle) | no | `FullName`, appended when present | `student_last_name` |
| `studentGender` (toggle) | yes when rendered, `male\|female` | `Gender` | `student_gender` |
| `studentGrade` (toggle) | yes when rendered, integer `0..11` | `TargetGrade` | `student_grade` |
| `studentPhone` (toggle) | no | **no column** — appended to `Note`, see §2.6 | `student_phone` |
| — | — | `BirthDate` = `""` (never collected) | — |

**Rule T (the toggle rule) — one sentence, and it decides all five toggles.**
*A pupil-field toggle may be switched off only when our model has an honest way to say "not
stated".*

| Toggle | May be off? | Why |
|---|---|---|
| `show_student_last_name_input` | **yes** | The surname is simply absent from `FullName`. |
| `show_student_phone_number_input` | **yes** | The value has no `Lead` column at all; when present it is a note line, when absent nothing is written. |
| `show_student_first_name_input` | **yes**, with a fallback | `FullName` is `not null`. When no pupil name arrives, the lead is created as `"{ParentFullName} — farzandi"`. That is a true statement, it renders on the board card unchanged, and the school still has a name and a phone to call. The survey editor shows a non-blocking hint when the toggle is off. |
| `show_student_gender_input` | **no** | `leads.gender` is a two-valued `not null` column and the **design-frozen** `LeadCard.tsx:27` renders it through `genderLabels[lead.gender]` — a `Record<Gender,string>` with exactly `male` and `female`. A third value renders as an empty string inside a file `CLAUDE.md` forbids us to edit, and defaulting to `male` would print a fact on the card that nobody stated. |
| `show_student_grade_input` | **no** | `leads.target_grade` is a `not null` int and `0` already means *nol sinf* on EduSchool's own grade list (§2.1) — it cannot double as "unknown". The card prints `{targetGrade}-sinf`. |

Enforcement: `POST`/`PUT /api/admin/surveys` returns **400**
`{"code":"survey_fields_required","fields":["showStudentGenderInput"],"message":"Bu maydonlarni o'chirib bo'lmaydi: jins, sinf"}`
when either of the two pinned toggles is false. The editor renders those two switches **on and
disabled**, with the one-line reason beside them. Do not hide them: a hidden switch reads as a
missing feature, a disabled one with a reason reads as a decision.

### 2.5 Spam protection — exactly what the gap file allows, with the numbers named

Constraint, verbatim from `existing-module-gaps.md` §5.2: *rate limit + honeypot; no third-party
captcha, nothing leaves the
machine.* No reCAPTCHA, no hCaptcha, no Turnstile, no third-party script tag on the public page,
no outbound HTTP from the submit handler. Three layers:

**1. Rate limit.** One new policy in `Program.cs`, beside `"login"` and `"telegram"`, same
partition key (`httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"`):

```
options.AddPolicy("survey", …FixedWindowRateLimiterOptions
{
    PermitLimit = 5,                    // submissions
    Window      = TimeSpan.FromMinutes(10),
    QueueLimit  = 0,
});
options.AddPolicy("survey-read", …FixedWindowRateLimiterOptions
{
    PermitLimit = 60,                   // page loads
    Window      = TimeSpan.FromMinutes(1),
    QueueLimit  = 0,
});
```

`[EnableRateLimiting("survey")]` on the public POST, `[EnableRateLimiting("survey-read")]` on the
public GET. Rejection is the global `429`. Five in ten minutes is generous for a family with three
children and hostile to a script. **A shared 4G NAT can put a whole apartment block behind one
address** — that is why the limit is per ten minutes and not per minute, and why the read limit is
separate and loose.

Two facts about that partition key, so nobody re-invents them:

- **Behind the proxy it is already the real client IP.** `Program.cs:384-399` enables
  `UseForwardedHeaders` (`XForwardedFor | XForwardedProto`) outside Development, precisely so the
  login limit is not one bucket for the whole school. Do **not** parse `X-Forwarded-For` by hand in
  the controller.
- **In the test host it is `null`, which means every test shares the partition `"unknown"`.** The
  test host runs as `Testing` (`ApiFactory.cs:59,116`), so forwarded headers are on: a test that
  needs its own bucket sets `X-Forwarded-For: 203.0.113.<n>` with an `<n>` of its own. Without
  that, the sixth POST of the whole *suite* starts returning 429 and the failure looks unrelated to
  the test that caused it.

**2. Honeypot field.** The form renders one extra input that no human sees:

```
name="website", type="text", tabIndex={-1}, autoComplete="off", aria-hidden="true"
wrapper style: position:absolute; left:-9999px; width:1px; height:1px; overflow:hidden
```

Not `display:none` — the cheapest bots skip those. If `website` arrives non-empty, the server
returns **200 with the normal success body**, writes **nothing** to either table, and logs
`logger.LogInformation("Ariza honeypot: survey={Slug}")`. Never tell the bot; never keep the row
(a spam row would have to be filtered out of every count, for no gain).

**3. Time trap.** `GET /api/public/surveys/{slug}` returns `servedAt` (server clock, ISO-8601).
The page echoes it back in the submission. The server rejects, with the same silent-200 treatment
as the honeypot, when `now - servedAt < 2 s`. **Amended 2026-09-21:** the `> 2 h` bound and the
"missing / unparseable `servedAt`" case are *accepted*, not trapped — a trapped submission gets a
success screen and is discarded, so a parent who opens the form in the evening and sends it in the
morning would lose the application without knowing. A bot gains nothing from either case: it can
forge the value or simply GET again. **This is a bot filter, not a security
boundary** — `servedAt` is unsigned and trivially forgeable; the rate limit is the real defence.
Written down so nobody later "hardens" it with an HMAC and thinks the endpoint became safe.

**Not built, deliberately:** IP bans, a blocklist table, an email alert, any third-party score.
Each is either a subsystem or an outbound call.

### 2.6 Submission → Lead

One service, `SchoolLms.Application/Services/SurveySubmissionService.cs`, one transaction.

```
1. Load the survey by slug.  Missing or is_active = false → 404 survey_not_found.
2. Honeypot / time trap (§2.5) → return the success DTO, write nothing, log.
3. Validate per §2.4.  Any failure → 400 with `errors: { field: "uzbek message" }`.
4. Duplicate check:
     a submission exists for THIS survey with the same `parent_phone_key`
     AND the same normalised pupil name (lower, trimmed, collapsed whitespace)
     AND `created_at > now() - interval '24 hours'`
   → insert the submission with `status = 'duplicate'`, `lead_id` = the existing lead,
     create NO second lead, return the normal success DTO.
   (Same phone + a DIFFERENT pupil name is a second child and creates a second lead.)
5. Resolve the stage (rule S below).
6. Insert the Lead:
     FullName       = pupil name, or "{ParentFullName} — farzandi"   (§2.4)
     Gender         = studentGender
     BirthDate      = ""
     ParentFullName = "{parentFirstName} {parentLastName}".Trim()
     ParentPhone    = parentPhone as typed
     TargetGrade    = studentGrade
     Note           = rule N below
     Stage          = the resolved stage id
     Source         = "survey"
     SurveyId       = survey.Id
     CreatedAt      = AppClock.Now
7. Insert the survey_submission with `status = 'lead'`, `lead_id`, ip, user agent.
8. SaveChanges once.  Return { ok: true, thankYou }.
```

**Rule S — which stage a survey lead lands in.** In order:

1. `surveys.stage_id`, when set **and** the stage still exists.
2. Otherwise the stage with the **lowest `Order`** (`db.LeadStages.OrderBy(s => s.Order).First()`).
   Not "`Order == 0`": the column is maintained by `LeadStagesController.Reorder` and there is no
   constraint that a zero exists after a delete.
3. Otherwise — the table is **empty** — create one stage, inside the same transaction:
   `{ Title = "Yangi arizalar", Color = "blue", Order = 0 }`, write an audit row through
   `AuditService.Entry(..., actorId: null, actorName: "Ariza formasi")`, and use it.

Step 3 exists because the alternative outcomes are all worse: a 500 loses a real family who typed
their phone number; a blank `Stage` produces a lead that the board cannot draw and only the
funnel's "orphan" counter knows about (`LeadFunnelQuery` `orphanCount`). Creating a column **adds**
a column to the board; it does not restyle it, which is what `CLAUDE.md` protects. It is the only
row an anonymous request can ever write outside `surveys`, `survey_submissions` and `leads`, it is
bounded (it can happen at most once, when the board is empty), and it is audited.

**Rule N — what the note reads.** One line, because `LeadCard.tsx:37` renders it in a single `<p>`
where newlines collapse:

```
Ariza: {survey.name} · {DD.MM.YYYY HH:mm}
```

and, only when the pupil phone was supplied:

```
Ariza: {survey.name} · {DD.MM.YYYY HH:mm} · O'quvchi tel: {studentPhone}
```

No id, no slug, no uuid in the note — the board card is read by a human. The machine-readable link
is `leads.survey_id`.

**The board is not touched.** `Source`, `SurveyId` and `CreatedAt` are additive columns; the card,
the column, the detail modal and the form modal keep rendering exactly the fields they render
today. The new facts surface on the **submissions page** (§2.7) and on the **funnel** page, both of
which are ours to change.

**Audit.** The public submission writes **no** `audit_log` row: the `survey_submissions` row *is*
the record, and it carries more (ip, user agent, the raw values) than an audit line would. Admin
CRUD on surveys and news does write audit rows (§5.2, §5.4).

### 2.7 Admin screens

Three screens under one new menu entry (§6.4), all gated `[AdminPerm("marketing")]`.

| Screen | Route | What it holds |
|---|---|---|
| **Arizalar** (the survey register) | `/admin/marketing/arizalar` | List: nomi, holati (Faol/Yopiq), umumiy arizalar soni, oxirgi ariza sanasi, **public link with a copy button**, row actions (tahrirlash, nusxa olish havolasi, faol/yopiq, o'chirish). |
| **Ariza tahriri** | modal on the list | Name, subtitle, image, offer document, target stage, thank-you text, slug (auto-generated from the name, editable, live availability check), the five toggles (two of them disabled per Rule T), a live preview of the public page. |
| **Topshirilgan arizalar** | `/admin/marketing/topshirilganlar` | The submissions register: sana, ariza (survey), ota-ona F.I.SH, telefon, o'quvchi, sinf, holati (`Lid yaratildi` / `Takror`), and a link to the lead. Filters: survey, date range, status, free-text search over name and phone. Excel export. A detail drawer shows every submitted value plus ip / user agent. |

**`/leads/with-survey` — our answer to "show me leads that came from a survey" [decision].**
EduSchool answers it with a kanban filter (§0.3). We cannot: the board is frozen. So:

- the **submissions register** above is the primary answer — it is strictly more informative than a
  filtered board, because it also shows submissions that were duplicates and the values the parent
  typed before anyone edited the lead;
- the **funnel page** (`schoollms.client/src/pages/admin/leads-funnel/LeadFunnelPage.tsx`, ours,
  not frozen) gains a **Manba** breakdown — leads by `source`, and within `survey`, by survey —
  plus an optional `surveyId` filter. That is task **`SM-13`**, the last task in §7, and the only
  reason `leads.created_at` earns its place in this migration rather than a later one.

Nobody adds a filter bar, a badge, a colour or a column to `pages/admin/leads/*`.

### 2.8 Gap list — Surveys

| id | Gap | Where | h |
|---|---|---|---|
| SV-1 | `surveys` + `survey_submissions` + three `leads` columns, one migration, grants | §4 | 3 BE |
| SV-2 | Public read + submit endpoints, rate-limit policies, honeypot, time trap | §2.5, §5.1 | 5 BE |
| SV-3 | `SurveySubmissionService` — validation, dedup, stage resolution, lead creation | §2.6 | 4 BE |
| SV-4 | Admin survey CRUD + slug uniqueness + delete guard + public URL builder | §5.2 | 2 BE |
| SV-5 | Submissions list + filters + Excel export | §5.3 | 2 BE (Excel reuses `SchoolLms.Application/Services/ExcelExport.cs`) |
| SV-6 | Public page `/ariza/:slug` — render, validate, submit, thank-you, 404 states | §6.1 | 8 FE |
| SV-7 | Survey list + editor modal + live preview + copy-link | §6.2 | 8 FE |
| SV-8 | Submissions register + detail drawer + export button | §6.2 | 4 FE |
| | | | **16 BE + 20 FE** |

---

## 3. News

### 3.1 EduSchool capture **[bundle]**

Form `defaultValues`: `{image:"", title:"", forStudent:false, forParent:false, forEmployee:false, branchIds:[], content:""}`.
`content` is edited in a WYSIWYG (`editorRef.current.editor.setContents(...)`) and previewed in a
sticky right-hand pane (`news.preview.title`, `news.preview.empty`, `news.preview.start_typing`,
`news.preview.no_content`). List columns: **title · banner · audience (the three labels joined) ·
createdAt (`DD.MM.YYYY HH:mm`)**. Endpoints: `GET /news/pagin`, `GET /news/{id}`, `POST /news`,
`PUT /news`, `DELETE /news/{id}`. There is **no publish/unpublish step** and **no delivery** — the
news exists in their app and waits to be opened.

### 3.2 Ours today **[ours]**

`Broadcast` (`Entities.cs:1010`) + `MessagesController.SendBroadcast`
(`SchoolLms.Server/Controllers/MessagesController.cs:146`) + `config/messageTemplates.ts`:
a **targeted mail-merge over Telegram**. Scopes `class | group | all | selected | filter`, an
`OnlyDebtors` switch, placeholders (`{ism}`, `{qarzdorlik}`, …) resolved per pupil against a
derived balance, delivery to `telegram_registrations` chat ids, and a row that records
`RecipientCount` / `SentCount`. Parents read it in the Mini App through
`GET /api/tg/parent/children/{id}/announcements` (`TelegramParentController.cs:280`), which filters
broadcasts by the child's class name.

That is a *messaging* tool. What it is not: school-wide, audience-typed, readable a month later by
someone who joined last week, or visible to employees at all.

### 3.3 Decisions **[decision]**

**N1 — `Broadcast` stays, untouched.** It answers "tell 5-A's parents that tomorrow's trip is
cancelled, and put each child's debt in the text". News answers "the school publishes something and
it stays published". Folding one into the other would either drop the mail-merge or turn every debt
reminder into a permanent public post. Two tools, one sentence each in the UI so nobody guesses:
*Xabarlar → E'lon* = bitta sinfga xabar; *Savdo va marketing → Yangiliklar* = butun maktabga,
tarixi bilan. `CLAUDE.md`'s additive rule points the same way.

**N2 — `body` is plain text, not HTML.** A WYSIWYG body would have to be sanitised on the server
and rendered as HTML in three clients (admin preview, Mini App, portal) — three chances to ship
stored XSS — to get bold text in a school announcement. The composer is a `<textarea>`; the body is
stored verbatim; every renderer uses `whitespace-pre-line`. The **live preview** the gap file asks
for is
still delivered: it renders the exact card the parent will see, next to the textarea, updating as
you type.

**N3 — publishing is a step, and `published_at` is never in the future.** `published_at IS NULL`
means draft. There is no scheduler in this repository and this module does not add one; a future
timestamp would simply never fire. `POST /news/{id}/publish` sets `published_at = now()` and
performs the Telegram fan-out. `POST /news/{id}/unpublish` clears it (the feed hides the item; the
Telegram message that already went out cannot be recalled, and the UI says so before you confirm).

**N4 — publishing also sends a Telegram message, by default, and the result is recorded on the
news row.** This is what "replaces broadcast-and-hope with something that has a history" means in
practice: one action, two outcomes — a feed entry that persists and a notification that arrives.
The composer has a `Telegram orqali ham yuborilsin` checkbox, default **on**. The row keeps
`telegram_sent_at`, `telegram_recipient_count`, `telegram_sent_count`, exactly as `Broadcast`
does. If the bot is not configured (`TelegramService.IsConfigured == false`) the publish still
succeeds, counts stay 0, and the UI says `Telegram bot sozlanmagan — faqat ilovada chiqdi`.

**N5 — audience is three booleans in the database and `audience[]` on the API.** Three columns give
the "at least one audience" rule as a DB CHECK and keep feed queries index-friendly; the API shape
is the one `existing-module-gaps.md` §5.2 specifies. The mapping is mechanical and lives in one
DTO (`NewsDto`): `audience` contains `"employee"` iff `for_employee`, and so on.

**N6 — recipients.** Chat ids are the **distinct union** of:

| Audience | Source |
|---|---|
| `parent` | `telegram_registrations.chat_id` where `teacher_id is null` **and** `telegram_accounts.telegram_user_id` joined to `users.role = 'parent'` |
| `student` | `telegram_accounts` joined to `users.role = 'student'` |
| `employee` | `telegram_registrations.chat_id` where `teacher_id is not null` **and** `telegram_accounts` joined to `users.role in ('teacher','staff','admin','superadmin','cashier')` |

Both tables are needed and the union is deduplicated **on the chat id value**: parents joined the
bot by sharing a contact (`telegram_registrations`, `TelegramBotService`), while Mini App users are
linked through `telegram_accounts` (`TelegramLinkService`), and the same person can be in both with
the same Telegram user id. Sending twice to one parent is the kind of defect that gets a feature
switched off.

**N7 — an image is optional.** EduSchool requires one; we do not. A school that has to find a
picture before it can announce a parents' meeting will not announce the meeting. `image_url` is
nullable and the card renders without it.

**N8 — deleting is soft.** `deleted_at` rather than a row delete: a news item that went out over
Telegram and then vanished from the admin list, with nobody able to say what it said, is a support
call. The list has an `Arxiv` toggle.

**N9 — "reuse `NotificationsController`'s derived feed" (`existing-module-gaps.md` §5.2), read
correctly.** That controller
is the **admin bell** (`SchoolLms.Server/Controllers/NotificationsController.cs`): a list computed
on every request from feedback, pickup requests, chat and birthdays, with no table of its own and
a single per-user `NotificationsReadAt` marker. News itself cannot be derived — it is authored
content and must be stored (§4.3). What we do reuse is the **pattern and the bell**: two new
sources are added to `NotificationsController.BuildAsync`, each ~10 lines and no migration —

| Source | Row |
|---|---|
| a `survey_submissions` row from the last 7 days | `Kind:"survey"`, `Title:"Yangi ariza"`, `Text:"{parent} · {survey}"`, `Link:"/admin/marketing/topshirilganlar"` |
| a published `news` row from the last 7 days | `Kind:"news"`, `Title:"Yangilik e'lon qilindi"`, `Link:"/admin/marketing/yangiliklar"` |

so the admissions officer sees a new application the next time they look at the panel, without a
new subsystem and without a message at 03:00 (Q4).

### 3.4 Where news is read

| Surface | How | File |
|---|---|---|
| **Mini App — parent** | A `NewsCard` section on the existing Bosh tab, under the announcements block: the 3 latest `parent` items, tap to expand, `Barchasi` reveals up to 20. **No sixth tab** — the tab bar has five and the panel has no router. | `schoollms.client/src/pages/miniapp/ui-tg/src/screens/parent/HomeTab.jsx` (+ a new `NewsCard.jsx`, + `lib/parentApi.js`) |
| **Mini App — teacher** | The same `NewsCard`, audience `employee`, on the Bugun tab. | `screens/teacher/TodayTab.jsx`, `lib/teacherApi.js` |
| **Parent / student web portal** | A new route `yangiliklar` under both `/parent` and `/student`, plus a second sidebar entry for those two roles (each currently has exactly one). | `schoollms.client/src/pages/portal/PortalNewsPage.tsx` (new), `App.tsx`, `navigation.ts` |
| **Admin** | The news list itself. | §6.2 |
| **Teacher PWA (`/teacher/`, `pages/teacher/ui-web`)** | **Not in v1.** It is a separate build with its own design tokens; employees reach the same content through the Mini App and the Telegram message. ≈4 h FE when the client asks. | — |

### 3.5 Gap list — News

| id | Gap | Where | h |
|---|---|---|---|
| NW-1 | `news` table (rides the same migration) | §4.3 | 1 BE |
| NW-2 | Admin CRUD + publish/unpublish + soft delete + audit | §5.4 | 3 BE |
| NW-3 | `NewsTelegramNotifier` — recipient resolution (N6), send, counts | §3.3 | 3 BE |
| NW-4 | Reader endpoints for portal and Mini App (parent / student / employee) | §5.5 | 2 BE |
| NW-5 | Admin list + composer with live preview + publish dialog | §6.2 | 8 FE |
| NW-6 | `NewsCard` in the Mini App, both panels | §3.4 | 3 FE |
| NW-7 | Portal `PortalNewsPage` for parent and student | §3.4 | 3 FE |
| NW-8 | Two new sources in the admin bell (N9) | §3.3 | 1 BE |
| | | | **10 BE + 14 FE** |

---

## 4. Data model — one migration, `SalesAndMarketing`

**One migration, one owner** (`existing-module-gaps.md` §8, "Shared files"). Name it
`SalesAndMarketing`; it will land as
`SchoolLms.Infrastructure/Migrations/2026MMDDHHMMSS_SalesAndMarketing.cs`, immediately after
`20260918151831_AttendanceMarkPerLesson` unless another migration merges first — in which case the
migration test's `PreviousMigration` constant is updated, nothing else.

Conventions copied from `20260918090000_TransactionTypes.cs` and `students_parity_p1_guards.sql`:
uuid PK with `gen_random_uuid()`, snake_case (the context applies
`UseSnakeCaseNamingConvention`, `Program.cs:62`), `text` FKs to `users`/`leads`, check constraints
named `ck_<table>_<rule>`, indexes named `ix_<table>_<cols>` / `ux_…` for unique.

> **Time types — read this before writing the entity.** `AppDbContext.OnModelCreating` (lines
> 338-351) forces **every `DateTime`** column to `timestamp without time zone`, while new modules
> use `DateTimeOffset` → `timestamptz`. Therefore: the three new tables use `DateTimeOffset`
> (`timestamptz`, `default now()`), and `Lead.CreatedAt` — a property on the **existing**
> `Entities.cs` entity, next to `Broadcast.CreatedAt` — is `DateTime?` → `timestamp without time
> zone`, set from `AppClock.Now`. This mix is deliberate. Do not "fix" it.

### 4.1 `surveys`

```sql
create table surveys (
  id           uuid primary key default gen_random_uuid(),
  slug         text        not null,
  name         text        not null,
  subtitle     text        null,
  image_url    text        null,
  offer_url    text        null,
  thank_you_text text      null,
  stage_id     text        null references lead_stages(id) on delete set null,
  show_student_first_name_input   boolean not null default true,
  show_student_last_name_input    boolean not null default true,
  show_student_phone_number_input boolean not null default false,
  show_student_grade_input        boolean not null default true,
  show_student_gender_input       boolean not null default true,
  is_active    boolean     not null default true,
  created_by   text        not null references users(id) on delete restrict,
  created_at   timestamptz not null default now(),
  updated_at   timestamptz null,
  constraint ck_surveys_name check (btrim(name) <> ''),
  constraint ck_surveys_slug check (slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$' and length(slug) between 3 and 60),
  -- Rule T (§2.4): the two toggles the frozen board cannot represent as "unknown".
  constraint ck_surveys_required_toggles check (show_student_gender_input and show_student_grade_input)
);
create unique index ux_surveys_slug on surveys (lower(slug));
create index ix_surveys_active on surveys (is_active, created_at desc);
```

`stage_id` is `on delete set null`, not `restrict`: deleting a kanban column is a normal thing for
the school to do, and Rule S step 2 covers the survey afterwards.

### 4.2 `survey_submissions`

```sql
create table survey_submissions (
  id                 uuid primary key default gen_random_uuid(),
  survey_id          uuid not null references surveys(id) on delete restrict,
  lead_id            text null references leads(id) on delete set null,
  status             text not null default 'lead',
  parent_first_name  text not null,
  parent_last_name   text null,
  parent_phone       text not null,
  parent_phone_key   text not null,          -- PhoneUtil.Key: last 9 digits
  student_first_name text null,
  student_last_name  text null,
  student_phone      text null,
  student_grade      smallint null,
  student_gender     text null,
  ip                 text null,
  user_agent         text null,
  created_at         timestamptz not null default now(),
  constraint ck_survey_submissions_status check (status in ('lead','duplicate')),
  constraint ck_survey_submissions_gender check (student_gender is null or student_gender in ('male','female')),
  constraint ck_survey_submissions_grade  check (student_grade is null or student_grade between 0 and 11),
  constraint ck_survey_submissions_parent check (btrim(parent_first_name) <> '' and btrim(parent_phone) <> '')
);
create index ix_survey_submissions_survey on survey_submissions (survey_id, created_at desc);
create index ix_survey_submissions_dedupe on survey_submissions (survey_id, parent_phone_key, created_at desc);
create index ix_survey_submissions_lead   on survey_submissions (lead_id);
```

`lead_id` is `on delete set null`: deleting a lead from the board must not delete the evidence of
what a parent typed. `survey_id` is `restrict` — that is the constraint behind delete-guard D7.

**`ip` and `user_agent` are the only place in this system where a member of the public's IP is
stored.** They exist for abuse triage, are shown only in the admin detail drawer, and are **not**
included in the Excel export. If the client objects, dropping the two columns costs one migration
and breaks nothing.

### 4.3 `news`

```sql
create table news (
  id            uuid primary key default gen_random_uuid(),
  title         text not null,
  body          text not null,
  image_url     text null,
  for_employee  boolean not null default false,
  for_parent    boolean not null default false,
  for_student   boolean not null default false,
  published_at  timestamptz null,
  author_id     text not null references users(id) on delete restrict,
  author_name   text not null,
  telegram_sent_at         timestamptz null,
  telegram_recipient_count integer not null default 0,
  telegram_sent_count      integer not null default 0,
  created_at    timestamptz not null default now(),
  updated_at    timestamptz null,
  deleted_at    timestamptz null,
  constraint ck_news_title    check (btrim(title) <> ''),
  constraint ck_news_body     check (btrim(body) <> ''),
  constraint ck_news_audience check (for_employee or for_parent or for_student)
);
create index ix_news_feed on news (published_at desc) where deleted_at is null;
```

`author_name` is a snapshot beside `author_id`, exactly as `Broadcast.SenderName` is
(`Entities.cs:1016`): a renamed or deleted staff account must not rewrite who published a news item.

### 4.4 `leads` — three added columns

```sql
alter table leads add column source     text not null default 'manual';
alter table leads add column survey_id  uuid null references surveys(id) on delete restrict;
alter table leads add column created_at timestamp without time zone null;

alter table leads add constraint ck_leads_source check (source in ('manual','survey'));
alter table leads add constraint ck_leads_source_survey
  check ((source = 'survey') = (survey_id is not null));

create index ix_leads_source     on leads (source, created_at desc);
create index ix_leads_survey     on leads (survey_id) where survey_id is not null;
```

Three deliberate choices:

- **`source` default `'manual'`, and every existing row gets it.** Every lead in the table today
  was typed by a member of staff on the board — that is the literal truth, not a convenience.
  Values are closed at two; a third (`telegram`, `call`) is one line here and one line in the
  funnel's label map.
- **`survey_id` is `restrict`, not `set null`,** so that it can never fall out of step with
  `source` and break `ck_leads_source_survey`. A survey with leads is archived, never deleted
  (D7).
- **`created_at` is nullable, and pre-existing rows stay `NULL`.** `not null default now()` would
  stamp every historical lead with the migration's timestamp — a number that reads like a fact and
  is not one. `NULL` means "created before this migration, exact date unknown". The funnel has no
  period filter yet (SM-13 shipped the source breakdown only); whoever adds one must print the
  count of `NULL` rows rather than silently dropping them.
  **New rows get the value from the entity, not from a controller:**
  `public DateTime? CreatedAt { get; set; } = AppClock.Now;` on `Lead`, the same shape
  `Broadcast.CreatedAt` (`Entities.cs:1017`) already uses. Consequence: **no controller has to be
  edited** — the board's `LeadsController.Create` and `SurveySubmissionService` both stamp it by
  construction, and a row loaded from the database keeps its `NULL` because EF assigns the column
  value after the initialiser runs.

Every `ADD COLUMN … DEFAULT` above is metadata-only on PostgreSQL 11+: no table rewrite, no `DROP`,
no `ALTER TYPE`. The migration contains **no** `DROP` of any kind — read `Up()` line by line before
merging (`CLAUDE.md`).

### 4.5 Grants

New file `SchoolLms.Infrastructure/Migrations/Sql/sales_marketing_guards.sql`, executed from the
migration through `MigrationSql.Read(...)`. No `.csproj` change is needed — the include is a
wildcard (`SchoolLms.Infrastructure.csproj:23`, `<EmbeddedResource Include="Migrations\Sql\*.sql" />`).

```sql
DO $guards$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_rw') THEN
        RAISE NOTICE '[sales_marketing_guards] `app_rw` topilmadi — GRANT o''tkazib yuborildi.';
        RETURN;
    END IF;
    EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON public.surveys TO app_rw';
    EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON public.survey_submissions TO app_rw';
    EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON public.news TO app_rw';
END
$guards$;
```

**Full CRUD, and no `REVOKE`.** None of these tables is financial: no amount, no ledger posting, no
receipt. `docs/SPEC.md` §4.1 does not apply, and none of the three names appears in
`deploy/init-roles.sql` §5 — do not add them there. (`existing-module-gaps.md` §8 says the same in
its own words: *"None of the tables proposed in this file is financial; do not copy the append-only
REVOKE pattern onto them."*)

### 4.6 EF configuration

- New entities live in a new domain file **`SchoolLms.Domain/SalesMarketing.cs`** (`Survey`,
  `SurveySubmission`, `NewsItem`) — the precedent is `Billing.cs`, `Guardians.cs`, `TransactionTypes.cs`.
  `Entities.cs` receives **only** the three additive `Lead` properties.
- Mapping goes in a new **`SchoolLms.Infrastructure/Data/SalesMarketingModel.cs`** with a single
  `Apply(ModelBuilder b)`, following `TransactionTypeModel.cs` verbatim, including the habit of
  configuring the new `Lead` columns **in this file** (the migration that adds them owns them),
  not in `AppDbContext.OnModelCreating`.
- `AppDbContext` gains three `DbSet`s and one `SalesMarketingModel.Apply(b);` line.
- The entity class name is `NewsItem`, not `News`: `News` reads as a plural and collides with the
  `DbSet<NewsItem> News` property.

---

## 5. API contract

Every route below is new. Nothing existing changes shape. Error bodies follow the house pattern:
`{ "message": "<uzbek sentence>" }`, plus a machine `code` where a client has to branch, plus
`errors: { field: message }` for form validation.

### 5.1 Public — `[AllowAnonymous]`

New controller `SchoolLms.Server/Controllers/PublicSurveyController.cs`. The class-level shape
follows `GpsIngestController.cs`: `[ApiController] [AllowAnonymous] [Route("api/public/surveys")]`.
It is the **only** anonymous surface this module adds; it touches `surveys`, `survey_submissions`,
`leads` and — in the single bounded case of Rule S step 3 — `lead_stages`, and nothing else. It
never returns anything about a person or a record that already exists in the system.

**`GET /api/public/surveys/{slug}`** · `[EnableRateLimiting("survey-read")]`

```jsonc
200 {
  "slug": "qabul-2027",
  "name": "2027 o'quv yiliga qabul",
  "subtitle": "Ariza qoldiring — biz bog'lanamiz",
  "imageUrl": "/uploads/8f3c….jpg",
  "offerUrl": "/uploads/1a2b….pdf",          // null when none
  "thankYouText": "Arizangiz qabul qilindi…", // null → the page's default text
  "fields": {                                  // exactly what to render
    "studentFirstName": true,
    "studentLastName":  true,
    "studentPhone":     false,
    "studentGrade":     true,                  // always true, Rule T
    "studentGender":    true                   // always true, Rule T
  },
  "servedAt": "2026-09-21T14:03:11+05:00"      // echoed back on submit (§2.5)
}
404 { "code": "survey_not_found", "message": "Bu ariza topilmadi yoki yopilgan" }
429  (global rejection, empty body)
```

Unknown slug and `is_active = false` return the **same** 404 — a public endpoint must not confirm
that a link once existed.

**`POST /api/public/surveys/{slug}`** · `[EnableRateLimiting("survey")]`

```jsonc
// request
{
  "parentFirstName": "Aziz",
  "parentLastName":  "Karimov",
  "parentPhone":     "+998 90 123 45 67",
  "studentFirstName": "Ali",      // omitted when the toggle is off
  "studentLastName":  "Karimov",
  "studentPhone":     "",
  "studentGrade":     5,
  "studentGender":    "male",
  "servedAt":  "2026-09-21T14:03:11+05:00",
  "website":   ""                  // honeypot — must be empty
}

200 { "ok": true, "thankYou": "Arizangiz qabul qilindi. Tez orada bog'lanamiz." }
400 { "code": "validation", "message": "Ma'lumotlarni tekshiring",
      "errors": { "parentPhone": "Telefon raqamini to'liq kiriting" } }
404 { "code": "survey_not_found", "message": "Bu ariza topilmadi yoki yopilgan" }
429  (global rejection)
```

The 200 body is **identical** for a real submission, a duplicate (§2.6 step 4), a honeypot hit and
a time-trap hit. The caller can never tell the four apart, which is the point.

**`GET /api/public/surveys/{slug}/og`** — *not built.* Open Graph preview images for the shared
link would need a renderer; the page's own `<meta>` tags are static. Named here so nobody adds it
quietly.

### 5.2 Admin — surveys · `[Authorize] [AdminPerm("marketing")]`

`SchoolLms.Server/Controllers/SurveysController.cs`, `[Route("api/admin/surveys")]`.

> `AdminPermAttribute` (`SchoolLms.Server/Controllers/AdminPermAttribute.cs`) lets **any** staff
> member read (GET/HEAD/OPTIONS) and requires the `marketing` claim for every write. admin and
> superadmin bypass; teacher, student, parent and cashier are refused outright. That is the gate
> for every endpoint in §5.2–§5.4, and §8.1 pins it.

| Verb | Route | Body / query | Response |
|---|---|---|---|
| GET | `/api/admin/surveys` | `?includeInactive=true` | `SurveyDto[]`, newest first |
| GET | `/api/admin/surveys/{id}` | — | `SurveyDto` · 404 |
| GET | `/api/admin/surveys/slug-available` | `?slug=…&excludeId=…` | `{ "available": true }` |
| POST | `/api/admin/surveys` | `SurveySaveRequest` | `SurveyDto` · 400 `validation` · 409 `slug_taken` · 400 `survey_fields_required` |
| PUT | `/api/admin/surveys/{id}` | `SurveySaveRequest` | `SurveyDto` · same errors |
| PATCH | `/api/admin/surveys/{id}/active` | `{ "isActive": false }` | 204 |
| DELETE | `/api/admin/surveys/{id}` | — | 204 · **409 `survey_in_use`** (D7) |

```jsonc
// SurveySaveRequest
{ "name": "2027 o'quv yiliga qabul", "slug": "qabul-2027", "subtitle": "…",
  "imageUrl": "/uploads/…", "offerUrl": "/uploads/…", "thankYouText": "…",
  "stageId": "e1f2…",                       // null → Rule S step 2
  "showStudentFirstNameInput": true, "showStudentLastNameInput": true,
  "showStudentPhoneNumberInput": false,
  "showStudentGradeInput": true, "showStudentGenderInput": true }

// SurveyDto = the request fields, plus:
{ "id": "…", "isActive": true,
  "publicUrl": "https://wunderkindschool.uz/ariza/qabul-2027",
  "submissionCount": 34, "leadCount": 31, "lastSubmissionAt": "2026-09-20T18:40:02+05:00",
  "createdAt": "…", "updatedAt": "…" }
```

`publicUrl` is built server-side: `https://{first entry of Tenancy:RootDomain}/ariza/{slug}` when
that setting is non-empty, otherwise `{request scheme}://{request host}/ariza/{slug}`. Reason: the
admin is sitting on `test.wunderkindschool.uz`, and the link they copy must be the one the school
advertises. The client never assembles this URL itself.

Slug: proposed by the client from the name (lower-case; `'` and `ʻ` dropped; every run of
characters outside `a-z0-9` collapsed to a single `-`; leading and trailing `-` trimmed), always
editable by hand, and checked as you type against
**`GET /api/admin/surveys/slug-available?slug=…&excludeId=…` → `{ "available": true }`** (same
`marketing` gate). The database is the real authority — `ux_surveys_slug` is on `lower(slug)` and a
race returns 409 `slug_taken`.

Audit: `audit.Record(AuditService.EntitySurvey, id, "create|update|delete|activate|deactivate", …)`
with a human summary (`"Ariza: 2027 o'quv yiliga qabul"`). `EntitySurvey = "Survey"` is a new
constant in `AuditService.cs` (shared file, §7.1).

### 5.3 Admin — submissions · `[Authorize] [AdminPerm("marketing")]`

`SchoolLms.Server/Controllers/SurveySubmissionsController.cs`,
`[Route("api/admin/survey-submissions")]`.

| Verb | Route | Query | Response |
|---|---|---|---|
| GET | `/api/admin/survey-submissions` | `surveyId, from, to, status, q, page, pageSize` | `{ "total": 128, "rows": SubmissionDto[] }` |
| GET | `/api/admin/survey-submissions/{id}` | — | `SubmissionDetailDto` (adds `ip`, `userAgent`, every raw value) |
| GET | `/api/admin/survey-submissions/export` | same filters as the list | `.xlsx` via `ExcelExport.cs`; **no `ip`, no `userAgent`** |

```jsonc
// SubmissionDto
{ "id": "…", "createdAt": "2026-09-20T18:40:02+05:00",
  "surveyId": "…", "surveyName": "2027 o'quv yiliga qabul",
  "status": "lead",                           // lead | duplicate
  "parentFullName": "Aziz Karimov", "parentPhone": "+998 90 123 45 67",
  "studentFullName": "Ali Karimov", "studentGrade": 5, "studentGender": "male",
  "studentPhone": null,
  "leadId": "…", "leadStageTitle": "Yangi arizalar" }   // leadId null if the lead was deleted
```

Paging: `page` is 1-based, `pageSize` defaults to 50 and is capped at 200. Default sort
`created_at desc`. Filtering is server-side (this list grows without limit, unlike the board).

### 5.4 Admin — news · `[Authorize] [AdminPerm("marketing")]`

`SchoolLms.Server/Controllers/NewsController.cs`, `[Route("api/admin/news")]`.

| Verb | Route | Body | Response |
|---|---|---|---|
| GET | `/api/admin/news` | `?state=all\|draft\|published\|archived`, `page`, `pageSize` | `{ total, rows: NewsAdminDto[] }` |
| GET | `/api/admin/news/{id}` | — | `NewsAdminDto` · 404 |
| POST | `/api/admin/news` | `NewsSaveRequest` | `NewsAdminDto` (draft) |
| PUT | `/api/admin/news/{id}` | `NewsSaveRequest` | `NewsAdminDto` · 409 `news_published` when it is already published and `audience` changed — republishing a different audience would silently leave the first audience's copy in place |
| POST | `/api/admin/news/{id}/publish` | `{ "sendTelegram": true }` | `NewsAdminDto` with the three telegram counters filled |
| POST | `/api/admin/news/{id}/unpublish` | — | `NewsAdminDto` |
| DELETE | `/api/admin/news/{id}` | — | 204 (soft: sets `deleted_at`) |

```jsonc
// NewsSaveRequest
{ "title": "Ota-onalar yig'ilishi", "body": "25-sentabr, soat 15:00…",
  "imageUrl": null, "audience": ["parent","student"] }   // ⊆ employee|parent|student, non-empty

// NewsAdminDto
{ "id": "…", "title": "…", "body": "…", "imageUrl": null,
  "audience": ["parent","student"],
  "publishedAt": "2026-09-21T09:10:00+05:00",   // null = draft
  "authorName": "Dilnoza Rashidova",
  "telegramSentAt": "2026-09-21T09:10:04+05:00",
  "telegramRecipientCount": 412, "telegramSentCount": 408,
  "createdAt": "…", "updatedAt": "…" }
```

Publish is **idempotent-safe**: publishing an already-published item returns 409
`news_already_published` rather than sending the Telegram message twice.

Telegram text (plain, no `parse_mode` — `TelegramService.SendMessageAsync` sends plain text):

```
📰 {title}

{body}
```

Audit: `EntityNews = "News"` with actions `create | update | publish | unpublish | delete`; the
publish summary records the audience and both counters (`"Yangilik e'lon qilindi: Ota-onalar yig'ilishi — ota-ona, o'quvchi · 408/412"`).

### 5.5 Reader endpoints

| Verb | Route | Gate | Returns |
|---|---|---|---|
| GET | `/api/student/news` | `[Authorize(Roles="student,parent,admin")]` — the class-level gate of `StudentPortalController` | published, not deleted, audience by caller role: `parent` → `for_parent`, `student` → `for_student`, `admin` → `for_parent` (an admin reaches this endpoint only through the embedded portal view of a pupil). Newest first, `?take=` default 20, max 50 |
| GET | `/api/tg/parent/news` | `[Authorize(Roles = Roles.Parent)]` — `TelegramParentController`'s class gate | same, `for_parent` |
| GET | `/api/tg/teacher/news` | `[Authorize(Roles = Roles.Teacher)]` — `TelegramTeacherController`'s class gate | same, `for_employee` |
| GET | `/api/admin/news/feed` | `[Authorize] [AdminPerm("marketing")]` | `for_employee`, for the admin panel |

```jsonc
// NewsFeedDto — deliberately smaller than the admin DTO
{ "id": "…", "title": "…", "body": "…", "imageUrl": null,
  "publishedAt": "2026-09-21T09:10:00+05:00", "authorName": "Dilnoza Rashidova" }
```

No audience array, no counters, no author id on a reader endpoint: a parent does not need to know
who else received it.

### 5.6 Error codes, in one place

| `code` | HTTP | Where |
|---|---|---|
| `survey_not_found` | 404 | public GET and POST |
| `validation` | 400 | public POST, admin save |
| `slug_taken` | 409 | admin survey save |
| `survey_fields_required` | 400 | admin survey save (Rule T) |
| `survey_in_use` | 409 | admin survey delete |
| `news_published` | 409 | admin news update with a changed audience |
| `news_already_published` | 409 | publish |
| *(none)* | 429 | both public endpoints, global rejection |

### 5.7 Not built — say so rather than half-build

| Thing | Why |
|---|---|
| SMS confirmation to the parent after submitting | `CLAUDE.md`: there is no SMS. |
| An email to the admissions officer | Leaves the machine; we have no mail transport. The lead appears on the board and, if wanted, the school switches on a Telegram notification for the stage — a separate, later feature. |
| Scheduled publication of news | No scheduler in this repository (N3). |
| Lead stage-change history / a real time-series funnel | `existing-module-gaps.md` §7.1 note: *"a real funnel needs a `lead_events` table first"*. Out of scope; `created_at` alone is added here. |
| A public "check my application status" page | Needs an identity for an anonymous person; that is admission, not marketing (`docs/modules/admission-and-testing.md`). |

---

## 6. UI

Our own visual language throughout — iOS/Apple-flavoured minimalism, our `Card`, `Button`,
`DataTable`, `DatePicker`, `Loader`, our palette and spacing. We rebuild EduSchool's
**functionality**, never its appearance (`CLAUDE.md`). Nothing here is a MUI DataGrid, nothing here
is a sticky MUI preview pane, and nothing here touches the Lidlar board.

### 6.1 The public page — `/ariza/:slug`

`schoollms.client/src/pages/public/SurveyPage.tsx` (new folder `pages/public/`). It is the only
page in the SPA that renders **outside** `AppLayout` and without a session.

- **Structure:** a centred card on a plain background; the school logo (`/logo.png`); the survey
  `image` as a banner (skipped when null); `name` as the heading; `subtitle` as the sub-heading;
  the form; a download link for `offerUrl` labelled `Taklif hujjati`. **The page makes exactly two
  API calls — the public GET and the public POST — and nothing else:** no school-meta call, no
  session check, no token. Everything it renders comes from the GET response in §5.1.
- **Form order:** Ota-ona F.I.SH (two inputs) → Telefon (`+998` prefixed, masked, `inputMode="tel"`)
  → O'quvchi ismi / familiyasi → Jinsi (two radio pills: `O'g'il bola` / `Qiz bola`) → Nechanchi
  sinfga (select `0–11`, `0` labelled `Nol sinf`) → O'quvchi telefoni (only when its toggle is on)
  → the honeypot (invisible) → `Yuborish`.
- **Mobile first.** This page is opened from an Instagram bio on a phone; the desktop layout is the
  same card, wider. One column, 16 px base font so iOS does not zoom on focus, a 48 px submit
  button.
- **States:** loading skeleton · `404` card (`Bu ariza topilmadi yoki yopilgan`) · validation
  errors inline under each field · submitting (button disabled, spinner, the button never
  double-fires) · **success** — the whole card is replaced by a tick, `thankYouText` (or the
  default `Arizangiz qabul qilindi. Tez orada siz bilan bog'lanamiz.`) and, when `offerUrl` exists,
  the document link again. There is no "submit another" button: a second child means reloading the
  link, and that keeps the duplicate rule (§2.6) meaningful.
- `429` is shown as `Juda ko'p urinish bo'ldi. Iltimos, 10 daqiqadan so'ng qayta urinib ko'ring.`
- **No analytics, no fonts from a CDN, no third-party anything.** The page must not make a request
  to any host but ours (`existing-module-gaps.md` §5.2: *nothing leaves the machine*).

### 6.2 Admin — `Savdo va marketing`

Three pages plus one modal, in a new folder `schoollms.client/src/pages/admin/marketing/`.

| File | Route | Contents |
|---|---|---|
| `SurveysPage.tsx` | `/admin/marketing/arizalar` | The survey register (§2.7), with `Yangi ariza` and a `Yopilganlarni ko'rsatish` toggle. Each row: name, status pill, submission count, last submission, and the public link as a truncated chip with a copy icon (`Nusxa olindi` toast). |
| `SurveyFormModal.tsx` | modal | Two columns: the fields on the left, a **live preview of the public card** on the right, updating as you type. The five toggles sit in a `Ariza maydonlari` block; the two pinned ones are on, disabled, with the one-line reason. Slug lives under the name with a live `band/bo'sh` indicator and the full public URL beneath it. |
| `SubmissionsPage.tsx` | `/admin/marketing/topshirilganlar` | The submissions register + filters + `Excel` button + a right-hand detail drawer. `Lid` column links to `/admin/leads` (the board), never into it. |
| `NewsPage.tsx` | `/admin/marketing/yangiliklar` | Left: the list (title, audience chips, status, date, author). Right: the composer — title, textarea, image upload, three audience checkboxes, the `Telegram orqali ham yuborilsin` checkbox, `Qoralama saqlash` and `E'lon qilish`. Below the composer, the **live preview**: the exact card a parent sees in the Mini App. Publishing opens a confirm dialog naming the audience and the recipient count, because it sends real messages to real parents. |

### 6.3 Mini App and portal

As specified in §3.4. Two rules for the Mini App work:

- the panels have **no router** — add a section to an existing tab, never a sixth tab;
- the components live in the Mini App's own `components/ui.jsx` vocabulary (`Card`, `Row`,
  `Badge`, `EmptyState`), not the admin SPA's.

Portal: `pages/portal/PortalNewsPage.tsx` — one list of cards and a `Hech qanday yangilik yo'q`
empty state. The name is **not** `NewsPage`: `App.tsx` already imports the admin `NewsPage` from
`pages/admin/marketing/`, and two identically named default exports in one router file is how the
wrong component ends up on a route.

### 6.4 Menu placement and permission wiring

**Where it goes.** `SALES_AND_MARKETING` is one of EduSchool's **Sozlamalar** entries
(`existing-module-gaps.md` §5.1 lists it among the 36 settings keys), and our
`navigation.ts:271-273` already records that their Sozlamalar flyout has exactly four rows, of
which ours was deliberately missing this one. So the new entry goes **inside Sozlamalar**, in group
`SOZLAMALAR`, **after `Umumiy sozlamalar`** — EduSchool's position, restoring their sequence
unbroken:

```ts
// navigation.ts, inside the Sozlamalar children, replacing the
// "`Sotuv va marketing` ATAYLAB yo'q" comment block at :271-273
{ label: 'Savdo va marketing', to: '/admin/marketing/arizalar', perm: 'marketing', group: 'SOZLAMALAR' },
```

**The stale comment must go.** Lines 271-273 currently assert that this entry is deliberately
absent. Leaving it above the new row would be a doc that contradicts the code in the same file.
Replace it with a pointer to this spec.

**The section gate has to move, or the entry is invisible to the people who need it.**
`Sidebar.tsx:37-39` hides a child whose **parent** fails `canSee`, and the Sozlamalar section
carries `perm: 'settings'` (`navigation.ts:262`). A staff member holding only `marketing` would
therefore never see the section. This is exactly the defect the O'quv bo'limi audit found on
2026-09-17 (`MENU-PARITY.md`, *"The submenu was complete; the permissions were not"*), and it has a
known fix (line numbers as of commit `507cab6` — check the labels, not the numbers):

| Line | Before | After |
|---|---|---|
| `:262` (section) | `perm: 'settings'` | *(removed)* |
| `:269` Integratsiyalar | *(no perm)* | `perm: 'settings'` |
| `:270` Umumiy sozlamalar | *(no perm)* | `perm: 'settings'` |
| `:268` Moliya sozlamalari | `roles: ['admin','superadmin']` | unchanged |
| `:277` Yangi o'quv yiliga o'tish | `perm: 'academicYear'` | unchanged |
| new | — | `perm: 'marketing'` |

Behaviour for everyone who exists today is identical (`Sidebar` hides a group whose children are
all hidden); a `marketing`-only staff member now sees exactly one row.

**The permission key is new: `marketing`.** Reasons: `leads` is wrong for News; `settings` is a
powerful key to hand to whoever writes announcements; EduSchool has its own key for this section
too (`salesMarketing`). Add to `schoollms.client/src/config/constants.ts`, `adminPermissions`,
after `leads`:

```ts
{ key: 'marketing', label: 'Savdo va marketing' },
```

There is no server-side allowlist of permission keys — `StaffController.SetPermissions` stores the
list verbatim — so `constants.ts` plus `[AdminPerm("marketing")]` is the whole wiring.

**Routes** (`App.tsx`):

```tsx
// PUBLIC — outside <ProtectedRoute>, beside /login, BEFORE the "*" catch-all
<Route path="/ariza/:slug" element={<SurveyPage />} />

// inside /admin
<Route path="marketing/arizalar"       element={<RequirePerm perm="marketing"><SurveysPage /></RequirePerm>} />
<Route path="marketing/topshirilganlar" element={<RequirePerm perm="marketing"><SubmissionsPage /></RequirePerm>} />
<Route path="marketing/yangiliklar"    element={<RequirePerm perm="marketing"><NewsPage /></RequirePerm>} />

// portal
<Route path="yangiliklar" element={<PortalNewsPage />} />   // under both /parent and /student
```

and `navByRole.parent` / `navByRole.student` each gain
`{ label: 'Yangiliklar', to: '/parent/yangiliklar' | '/student/yangiliklar', icon: Newspaper }`.

**`docs/MENU-PARITY.md` gets one paragraph** recording that the Sozlamalar flyout is now four rows
and matches EduSchool's, and that the section-level `perm` moved to its children — same treatment
the O'quv bo'limi and Moliya passes received.

---

## 7. Build slices and safe order

Task ids are **SM-1 … SM-13** (the prefix is free across `docs/`). Hours are the §1 budget,
distributed.

### 7.1 Shared files — sequential, one owner, never parallel

Per `existing-module-gaps.md` §8 and `ASSUMPTIONS.md` 2026-09-16: **feature agents do not edit these
files.** The migration owner (SM-1) and the wiring owner (SM-12) do, each in one pass.

| File | Owner | What changes |
|---|---|---|
| `SchoolLms.Domain/Entities.cs` | SM-1 | **three additive properties on `Lead`** and nothing else |
| `SchoolLms.Domain/SalesMarketing.cs` *(new)* | SM-1 | `Survey`, `SurveySubmission`, `NewsItem` |
| `SchoolLms.Infrastructure/Data/AppDbContext.cs` | SM-1 | 3 `DbSet`s + `SalesMarketingModel.Apply(b);` |
| `SchoolLms.Infrastructure/Data/SalesMarketingModel.cs` *(new)* | SM-1 | all mapping, including the new `Lead` columns |
| the migration + `AppDbContextModelSnapshot.cs` | SM-1 | one migration, `Up()` read line by line |
| `Migrations/Sql/sales_marketing_guards.sql` *(new)* | SM-1 | grants (§4.5). No `.csproj` edit — wildcard include |
| `SchoolLms.Application/Services/AuditService.cs` | SM-12 | `EntitySurvey`, `EntityNews` |
| `SchoolLms.Server/Program.cs` | SM-12 | 2 rate-limit policies, the `/ariza/` fallback branch (D2), DI for `SurveySubmissionService` and `NewsTelegramNotifier` |
| `schoollms.client/src/config/constants.ts` | SM-12 | one `adminPermissions` entry |
| `schoollms.client/src/config/navigation.ts` | SM-12 | the Sozlamalar rewiring (§6.4) + two portal entries |
| `schoollms.client/src/App.tsx` | SM-12 | 1 public route + 3 admin routes + 2 portal routes |
| `docs/SPEC.md` §5 module inventory · `docs/MENU-PARITY.md` · `docs/modules/existing-module-gaps.md` §7.1 rows #8/#14 | SM-13 | mark shipped |
| **`schoollms.client/src/pages/admin/leads/*` (6 files)** | **nobody** | frozen — `git diff --stat` on that path must be empty in every PR |

### 7.2 Slices

| # | Task | Owns (writes) | Depends on | Parallel? |
|---|---|---|---|---|
| **SM-1** | Migration + entities + mapping + grants (§4) | the SM-1 rows of §7.1 | — | **No.** Everything waits on it; it is one agent, one pass. |
| **SM-2** | `SurveySubmissionService` + public controller + honeypot/time-trap (§2.5, §2.6, §5.1) | `SurveySubmissionService.cs`, `PublicSurveyController.cs`, `Dtos/SurveyDtos.cs` | SM-1 | yes — with SM-3, SM-5, SM-6 |
| **SM-3** | Admin survey CRUD + slug + delete guard + `publicUrl` (§5.2) | `SurveysController.cs`, `SurveyService.cs` | SM-1 | yes |
| **SM-4** | Submissions list + filters + Excel export (§5.3) | `SurveySubmissionsController.cs`, `SurveySubmissionQuery.cs` | SM-1 | yes |
| **SM-5** | News admin CRUD + publish/unpublish + soft delete (§5.4) | `NewsController.cs`, `NewsService.cs` | SM-1 | yes |
| **SM-6** | `NewsTelegramNotifier` + reader endpoints (§3.3 N6, §5.5) + the two bell sources (N9) | `NewsTelegramNotifier.cs`, `NewsFeedQuery.cs`; **adds one action each** to `StudentPortalController.cs`, `TelegramParentController.cs`, `TelegramTeacherController.cs`; two blocks in `NotificationsController.cs` | SM-1, SM-5 | **No** for those four controllers — one owner, they are touched by other work streams |
| **SM-7** | Public page `/ariza/:slug` (§6.1) | `pages/public/SurveyPage.tsx`, `api/services/publicSurvey.ts` | SM-2's contract (§5.1) — can start against this spec before SM-2 merges | yes |
| **SM-8** | Survey register + editor modal + live preview (§6.2) | `pages/admin/marketing/SurveysPage.tsx`, `SurveyFormModal.tsx`, `api/services/surveys.ts` | SM-3's contract | yes |
| **SM-9** | Submissions register + drawer + export (§6.2) | `pages/admin/marketing/SubmissionsPage.tsx`, `api/services/surveySubmissions.ts` | SM-4's contract | yes |
| **SM-10** | News admin page + composer + preview + publish dialog (§6.2) | `pages/admin/marketing/NewsPage.tsx`, `api/services/news.ts` | SM-5's contract | yes |
| **SM-11** | News readers: Mini App parent + teacher cards, portal page (§3.4) | `miniapp/ui-tg/src/screens/parent/NewsCard.jsx`, `…/teacher/NewsCard.jsx`, `lib/parentApi.js`, `lib/teacherApi.js`, `pages/portal/PortalNewsPage.tsx`; edits `HomeTab.jsx`, `TodayTab.jsx` | SM-6's contract | yes |
| **SM-12** | **Wiring pass** — every shared file in §7.1 | see §7.1 | SM-2…SM-11 merged | **No.** One commit. |
| **SM-13** | Funnel source breakdown + `surveyId` filter (§2.7) and the doc updates | `LeadFunnelQuery.cs`, `LeadsController.cs` (`funnel` query only), `LeadFunnelPage.tsx`, `api/services/leadFunnel.ts`, the docs | SM-12 | last; **`pages/admin/leads/*` stays closed** |

### 7.3 Order

```
SM-1  ──▶ [ SM-2 · SM-3 · SM-4 · SM-5 ] ──▶ SM-6
              │        │        │      │
              ▼        ▼        ▼      ▼
         [ SM-7 · SM-8 · SM-9 · SM-10 · SM-11 ]  (frontend, against the contracts in §5)
                                   │
                                   ▼
                                 SM-12  ──▶  SM-13
```

The frontend tasks may start the moment SM-1 is merged: §5 is a contract, not a sketch, and a
frontend agent that codes against it will not need to ask a question. Until SM-12 lands, the new
pages are unreachable (no route, no menu entry) — which is correct: a half-wired menu entry is the
one thing `MENU-PARITY.md` forbids.

---

## 8. Tests

`SchoolLms.Tests`, xUnit, shared Postgres fixture (`docs/TESTING.md`). Test **method** names are
Uzbek, as everywhere in this suite; comments and the file header are English-or-Uzbek per the
surrounding file. Run with `./tools/test.sh`.

### 8.1 RBAC — one row per new endpoint, no exceptions

`SchoolLms.Tests/SalesMarketingRbacTests.cs`. `AdminPermAttribute` opens GET to any staff member
and gates writes on the claim, so every endpoint needs **both** halves checked.

| Endpoint | anonymous | teacher | cashier | staff *(no `marketing`)* | staff *(`marketing`)* | admin |
|---|---|---|---|---|---|---|
| `GET /api/admin/surveys` | 401 | 403 | 403 | 200 | 200 | 200 |
| `GET /api/admin/surveys/slug-available` | 401 | 403 | 403 | 200 | 200 | 200 |
| `POST /api/admin/surveys` | 401 | 403 | 403 | **403** | 200 | 200 |
| `PUT /api/admin/surveys/{id}` | 401 | 403 | 403 | **403** | 200/404 | 200 |
| `PATCH /api/admin/surveys/{id}/active` | 401 | 403 | 403 | **403** | 204 | 204 |
| `DELETE /api/admin/surveys/{id}` | 401 | 403 | 403 | **403** | 204/409 | 204/409 |
| `GET /api/admin/survey-submissions` | 401 | 403 | 403 | 200 | 200 | 200 |
| `GET /api/admin/survey-submissions/export` | 401 | 403 | 403 | 200 | 200 | 200 |
| `GET /api/admin/news` | 401 | 403 | 403 | 200 | 200 | 200 |
| `POST /api/admin/news` | 401 | 403 | 403 | **403** | 200 | 200 |
| `POST /api/admin/news/{id}/publish` | 401 | 403 | 403 | **403** | 200 | 200 |
| `POST /api/admin/news/{id}/unpublish` | 401 | 403 | 403 | **403** | 200 | 200 |
| `DELETE /api/admin/news/{id}` | 401 | 403 | 403 | **403** | 204 | 204 |
| `GET /api/admin/news/feed` | 401 | 403 | 403 | 200 | 200 | 200 |
| `GET /api/student/news` | 401 | 403 | 403 | 403 | 403 | 200 |
| `GET /api/tg/parent/news` | 401 | 403 | 403 | 403 | 403 | 403 |
| `GET /api/tg/teacher/news` | 401 | **200** | 403 | 403 | 403 | 403 |
| `GET /api/public/surveys/{slug}` | **200** | 200 | 200 | 200 | 200 | 200 |
| `POST /api/public/surveys/{slug}` | **200** | 200 | 200 | 200 | 200 | 200 |

Shape: one `[Theory]` per row with one `[InlineData]` per role, copying `RbacMatrixTests.cs` — the
expected status is visible in each cell, and a loop that silently shrinks to zero cases cannot
happen. Clients come from `fixture.Api.ClientAsAsync(role, "marketing")` and
`fixture.Api.AnonymousClient()`.

The table covers the roles that must be **refused**. The reader endpoints also need their happy
path, which no column above shows: `parent` → 200 on `/api/student/news` **and**
`/api/tg/parent/news`; `student` → 200 on `/api/student/news`; `teacher` → 200 on
`/api/tg/teacher/news` (already in the table). `staff` → 403 on `/api/student/news`, because that
controller's class gate is `student,parent,admin`.

**One extra assertion that is easy to forget:** a parent calling `GET /api/student/news` must get
only `for_parent` items, and a student only `for_student`. Audience leakage is the failure mode
nobody notices until an employee-only item shows up on a parent's phone.

### 8.2 Public endpoint abuse tests

`SchoolLms.Tests/PublicSurveyAbuseTests.cs` — the file that exists because this is the first
anonymous **write** endpoint in the product that creates a business record.

| Test | Asserts |
|---|---|
| `Honeypot_toldirilgan_sorov_lid_yaratmaydi` | POST with `website:"x"` → 200 **and** `leads` count unchanged **and** `survey_submissions` count unchanged |
| `Juda_tez_yuborilgan_forma_lid_yaratmaydi` | `servedAt = now` → 200, nothing written |
| `Eskirgan_servedAt_qabul_qilinadi` | `servedAt = now - 3h` → 200 **and a lead is created** (amended 2026-09-21, §2.5) |
| `Chegaradan_oshgan_sorov_429_qaytaradi` | 6 submissions from **one** `X-Forwarded-For` value inside the window → the 6th is 429 (policy `survey`, `PermitLimit = 5`). Every other test in the suite that POSTs to the public endpoint uses a **different** `X-Forwarded-For`, or it will trip this limit by accident — see §2.5 |
| `Yopilgan_ariza_404` | `is_active = false` → 404, and the body is byte-identical to an unknown slug |
| `Notogri_telefon_400` | `parentPhone = "123"` → 400 `validation` with `errors.parentPhone` |
| `Bir_xil_telefon_va_ism_24_soat_ichida_ikkinchi_lid_yaratmaydi` | second identical POST → 200, submission written with `status='duplicate'`, `leads` count unchanged |
| `Bir_xil_telefon_boshqa_bola_yangi_lid_yaratadi` | same phone, different pupil name → a second lead |
| `Bosqich_yoq_bolsa_ariza_bosqich_yaratadi` | empty `lead_stages` → exactly one stage `Yangi arizalar` is created and the lead lands in it |
| `Ariza_lidni_togri_maydonlarga_yozadi` | the §2.4 mapping, field by field, including `Note` (Rule N) and `Source = "survey"` |
| `Ismsiz_ariza_ota_ona_nomi_bilan_lid_yaratadi` | first-name toggle off → `FullName == "{parent} — farzandi"` |
| `Jins_va_sinf_ochirilgan_ariza_saqlanmaydi` | admin save with `showStudentGenderInput=false` → 400 `survey_fields_required` |
| `Ommaviy_endpoint_boshqa_malumot_qaytarmaydi` | the GET response contains no lead, no pupil, no staff and no id of any existing record |

### 8.3 Other backend tests

| File | Covers |
|---|---|
| `SurveysTests.cs` | slug uniqueness (including a case-difference clash), delete guard 409 → then archive, `publicUrl` built from `Tenancy:RootDomain`, submission counters on the DTO |
| `NewsTests.cs` | draft → publish → feed visibility, unpublish hides it, soft delete hides it from every feed but keeps the admin row, `audience[]` ⇄ three booleans round-trip, double publish → 409, publish with the bot unconfigured → 200 with counts 0, recipient de-duplication when the same chat id is in both `telegram_registrations` and `telegram_accounts` |
| `Migrations/SalesMarketingMigrationTests.cs` | Following `TransactionTypesMigrationTests.cs`: the three tables with their declared columns and types; every CHECK actually rejects a bad value (existence is not enough); `ck_leads_source_survey` rejects `source='survey'` with a null `survey_id` **and** `source='manual'` with a non-null one; `leads.created_at` is **null** for a row that existed before the migration; `app_rw` has full CRUD on all three tables; `Down()` removes only what this migration added |

### 8.4 Frontend

`npm run build` and the project's lint must pass. There is no component test harness in this
repository and this module does not add one. The public page is verified by hand against the
checklist in §9.

---

## 9. Definition of Done

Machine-checked first (global `CLAUDE.md` rule — *the machine must confirm it*):

- [ ] `./tools/test.sh` green, including the new `SalesMarketingRbacTests`, `PublicSurveyAbuseTests`,
      `SurveysTests`, `NewsTests`, `SalesMarketingMigrationTests`.
- [ ] `npm run build` green in `schoollms.client`, and in `pages/miniapp/ui-tg`.
- [ ] `dotnet ef database update` applies `SalesAndMarketing` on a copy of production data, and
      `Down()` reverses it cleanly on a scratch database.
- [ ] `git diff --stat -- schoollms.client/src/pages/admin/leads/` prints **nothing**.
- [ ] Every new endpoint carries `[AdminPerm("marketing")]` or a deliberate `[AllowAnonymous]`;
      grep the two new controller files and count.

Then the behaviour, by hand:

- [ ] A survey created in the admin screen yields a link that opens on **the apex domain**
      (`{ROOT_DOMAIN}/ariza/{slug}`) — not just on the app subdomain. This is D2, and it is the one
      thing that silently half-works if the `Program.cs` branch is forgotten.
- [ ] Submitting that form creates exactly one lead on the board, in the configured stage, with the
      note from Rule N, and the board's appearance is unchanged.
- [ ] Submitting it twice inside a minute creates **one** lead and two submission rows, the second
      marked `Takror`.
- [ ] Six submissions in ten minutes: the sixth shows the 429 message, not a crash.
- [ ] Filling the hidden `website` field (via devtools) shows the same thank-you and writes nothing.
- [ ] A news item published to `parent` arrives as a Telegram message **once** to a parent who is
      both bot-registered and Mini-App-linked, and appears on that parent's Bosh tab and on the
      portal page.
- [ ] A staff user holding only `marketing` sees exactly one row under Sozlamalar and can open all
      three screens; a staff user holding only `settings` sees the other rows and not this one.
- [ ] `docs/SPEC.md` §5 has a row for this module; `existing-module-gaps.md` §7.1 rows #8 and #14
      are marked shipped with the date; `MENU-PARITY.md` records the Sozlamalar change.
- [ ] Every decision taken during the build that is not in this file is one line in
      `docs/ASSUMPTIONS.md`.

---

## 10. Open questions

Only questions that change scope. Each carries **the decision that stands if nobody answers** —
these are not blockers, and no build task waits on them.

**Q1 — Is the menu label `Savdo va marketing` or `Sotuv va marketing`?**
`REMAINING-PARITY.md` §3.2 and this file say *Savdo*; the existing comment in `navigation.ts:271`
calls EduSchool's row *Sotuv*. They are the same entry.
**If silent: `Savdo va marketing`.** Changing it is one string in `navigation.ts` and one in
`constants.ts`.

**Q2 — Where should the public form live in the menu the parent sees — anywhere?**
Right now the only way to the form is the link the school shares.
**If silent: that is all.** No public link from the landing page, because `landing.html` is outside
the SPA and adding a button there is a separate, tiny task the client has not asked for.

**Q3 — Does the enrolment form need a Russian version?** (D3.)
**If silent: no.** Uzbek only. Adding Russian later is one column, one string map and ~4 h FE; doing
it now means shipping a language switch nobody asked for.

**Q4 — Should a new survey lead also notify somebody over Telegram the moment it arrives?**
It already reaches the **admin bell** the moment it arrives (N9). The question is only about a push.
**If silent: no Telegram message.** A form that pings a staff member at 03:00 gets muted, and a muted channel
is worse than a board the officer opens each morning. If the client wants it, the shape is one
Telegram message to the users holding `marketing` — ~3 h, after `NewsTelegramNotifier` exists.

**Q5 — Should news be visible to a parent who has no Telegram at all?**
It already is, through the portal (§3.4).
**If silent: yes, the portal is the fallback.** Nothing to build.

**Q6 — Do we keep the visitor's IP on a submission?** (§4.2.)
**If silent: yes, and it stays out of the export and off every list screen.** Dropping the two
columns later is one migration.

**Q7 — Should `leads.created_at` be backfilled with a guess for the rows that predate the
migration?**
**If silent: no — they stay `NULL` and the funnel prints "sanasi noma'lum: N ta".** A guessed
timestamp is indistinguishable from a real one the moment it is written, and this repository has a
standing rule against reports that guess.

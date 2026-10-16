# Bulk import of test questions into an assignment

Status: client request of 2026-10-15 — *"testlarni bittalab (savol, variant A, B, C...) kiritish
majburiyati olib tashlansin … shablon fayl (Excel/Word/JSON) orqali testlarni ommaviy yuklash"*.

## Why

A test assignment ("Vazifalar" in the Mini App, "Topshiriqlar" in the admin) was typed one question
and one option at a time. Teachers already prepare tests in Word or ask ChatGPT/Gemini for them;
they now paste or upload the whole test at once and only check it.

"Future → Test bazasi" stays the **admission** bank (the live entrance-exam answer key, `admission`
key) — teachers do not reach it, and nothing here writes to it. The import feeds the assignment form.

## What happens

1. The teacher opens **Ommaviy yuklash** in the form's "Savollar" card.
2. They paste text, pick a file, or copy the **AI uchun so'rov** prompt into ChatGPT/Gemini and paste
   its answer back.
3. The server **reads** the input and returns the questions plus reason chips — **nothing is saved**.
4. The preview shows every question with its correct option; **Qo'shish** appends to the form,
   **Almashtirish** replaces the form's questions (asks first if there are any).
5. The teacher saves the assignment as before. The save rule below is the only gate.

A question with any problem is left out whole; the rest come back (the bank import's contract).

## Inputs

| Input | How it is read |
|---|---|
| Pasted text | The text format below. Text that is JSON (an array or object, also inside a ` ``` ` fence) is read as JSON. |
| `.txt` | The same, UTF-8 (a BOM is fine). |
| `.docx` | Every paragraph (tables included, in reading order) as a line, then the text format. Word's **automatic** list numbers and letters are written out first (a decimal list → `1. `, a letter list → `A) `). |
| `.xlsx` | Exactly the bank template: sheet "Savollar", A *Savol matni*, B–G options A…F, H *To'g'ri javob (A-F)*. Column I (image) is ignored — assignment questions have no image. Errors name **sheet rows** (header = 1). |
| `.json` | `[{ "savol": "…", "variantlar": ["…","…"], "javob": "B" }]`; also `text/question`, `options/choices`, `correct/answer/correctIndex`; the array may sit under `questions`/`savollar`; options may be `{ "A": "…" }` or `[{ "text": "…", "isCorrect": true }]`. A **number** answer is the 0-based index (as `correctIndex` everywhere in the API); a string is a letter or the option's text. |

Files up to 10 MB; pasted text up to 500 000 characters; at most **200 questions** per input (a
fatal 400 above that — one assignment cannot hold more).

### The text format

```
1. Savol matni
A) variant
*B) to'g'ri variant        ← or a line "Javob: B" under the options
C) variant
D) variant

2. Keyingi savol
…
```

Lenient on purpose — this is what a teacher or a chat model actually writes:

- **Questions** start at `1.` `1)` `1-savol` `Savol 1:`, or at the first plain line after a blank
  line. Unnumbered text before the first and after the last question ("Mana savollar:", "Omad!") is
  dropped; an option-less block *between* questions is reported (`tooFewOptions`), not dropped.
- **Options** `A)` `A.` `a)`, Latin A–F and Cyrillic А Б В Г Д Е. Their order is their position; the
  letter only resolves the answer. A new `A)` after options starts the next question even without a
  blank line.
- **Correct answer**: `*` or `+` before the letter, `*` right after it or at the end, ✓/✅, a wholly
  bold option line, or a line `Javob: B` / `To'g'ri javob: B` / `Answer: B` / `Ответ: Б` / `Жавоб: В`.
  The value may be a letter, the option's text, or its 1-based number. A letter is matched against
  the options' own letters first (in a Cyrillic test `В` is the third option), then through the
  Latin/Cyrillic look-alikes А В С Е.
- A two-line question is joined with a space (the form's inputs are single-line); an option's next
  line joins the option; lines after `Javob:` (an explanation) are dropped.
- Markdown a model adds anyway — fences, `#` headings, `**bold**`, `- ` bullets — is stripped.

## Reasons (chips)

`emptyText` · `tooFewOptions` (< 2) · `tooManyOptions` (> 6) · `noCorrect` · `badCorrect` (names
no option) · `manyCorrect` (two marks, or a mark and a `Javob:` that disagree) · `duplicateOption`.
Numbers are questions in order (`unit: "question"`) or sheet rows (`unit: "row"`).

## Save rule (every path)

`AssignmentService.CheckQuestions`, called by the admin and teacher create/update endpoints (400
`{ message }`) and by `Apply` itself (an unchecked caller cannot write), for format `test` only:

- options are trimmed; **blank options are dropped** and `correctIndex` moves with its text (the Mini
  App already did this; the admin wizard used to store blanks);
- an entirely blank question is skipped (the form's untouched card); a question with options but no
  text is refused;
- 2–6 options, the correct one among them, at most 200 questions.

Messages name the question by its position: *"3-savol: kamida 2 ta variant bo'lsin"*. The admin
wizard runs the same check with the screen's numbers before sending and shows the server's message
if one still comes back.

## API

Teacher (`TeacherPermissions.Assignments`, like the rest of `/api/teacher/assignments*`):

- `POST /api/teacher/assignments/questions/parse` — multipart `file`, or JSON `{ "text": "…" }` →
  `{ unit, totalRows, validCount, errorCount, questions: [{ text, options, correctIndex }], errors: [{ reason, rows }] }`.
- `GET  /api/teacher/assignments/questions/template` — `test_savollari_shablon.xlsx`.
- `POST /api/teacher/assignments/questions/template/telegram` — the same file to the teacher's own
  Telegram chat (409 not linked, 503 bot not configured, 502 refused), as the homeroom debts Excel.

Admin (`[AdminPerm("app")]`, like the rest of `/api/admin/assignments`):

- `POST /api/admin/assignments/questions/parse`, `GET /api/admin/assignments/questions/template`.

400 `{ message }`: nothing sent, wrong type, over 10 MB, unreadable Word/Excel/JSON, wrong Excel
columns, over 200 questions. A multipart body that cannot be read at all (cut off) gets MVC's own
validation 400 (no `message`; the screens fall back to their generic error text).

## Screens

- **Mini App → Vazifalar → form → Savollar → "Ommaviy yuklash"**: a sheet with the paste box
  (primary), file picker, Excel template (sent to the Telegram chat inside Telegram, downloaded in a
  browser) and "AI uchun so'rov" (topic and count fields; subject and grade are taken from the form;
  copy button). Then the result: counts, reason chips, the preview, Qo'shish / Almashtirish.
- **Admin → Future → ILOVA → Topshiriqlar → wizard, step 2**: the same as an inline panel.
- **Teacher web (PWA) → Topshiriq yaratish → Savollar**: "Matndan yuklash" — paste only.

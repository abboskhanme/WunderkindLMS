# Test results: thank-you to the best, warning to the lowest

Status: built 2026-10-15 (client's "CRM o'zgarish (Maktab)" list, item 2).

## Why

*"Oylik testni eng top va bottom o'quvchilarining accountiga xabar jo'natish (tashakkurnoma diplom / ogohlantirish) —
nechta o'quvchiga jo'natish sonini qo'lda kiritishli qilish."* Results were entered (Imtihonlar → natijalar) but
nothing reached the family, and nothing ranked them.

## Who

- Pupils of the exam with a result: a percent, not absent / cancelled, not archived.
- Ordered by percent, then points. Competition ranking: equal results share a place (1, 2, 2, 4) and **every pupil
  on a place within the count is in** — "top 3" with a tie on 3rd sends to 4.
- The bottom list counts from the other end and never takes a pupil already in the top list.
- The counts (0–50 each) are typed by staff; the lists are for the whole exam or **per class** (the class at
  assignment, else the pupil's class).
- Any exam with pupils works; "monthly" is just how the school names the exam type.

## What is sent

- A text per pupil from an editable template (broadcast markup `**bold**`, `__italic__`) with tokens `{fish}` `{ism}`
  `{sinf}` `{imtihon}` `{sana}` `{ball}` (`47.5/50`) `{foiz}` (`95%`) `{orin}`.
- To every Telegram chat linked to the pupil (`telegram_registrations`: parents and the pupil's own account) — the
  "account" of the request; Telegram is the only channel.
- Best pupils: optionally a **Tashakkurnoma** PDF (A4 landscape, logo, name, exam, score, place —
  `ExamDiplomaDocument`), sent after the text to each chat the text reached.
- Each message is also a `Broadcast` with its recipients, so it sits in the pupil's Mini App "Maktab e'lonlari"
  feed (even without Telegram) and in Xabarlar's register with who got it.
- Once per exam, pupil and kind (`exam_award_notices`, unique) — pressing again skips them. Saved per pupil, so a
  crash half-way never repeats the first ones.

## API (`api/admin/exams/{id}/awards`, `[AdminPerm("exams")]`)

- `GET preview?top=&bottom=&perClass=` — the lists (place, percent, chats, already sent) and the default texts.
- `POST send` — `{top, bottom, perClass, topText, bottomText, diploma, excludeStudentIds}`; needs the full `exams`
  **and** `messages` keys (admin always); refuses when the bot is not configured.
- `GET` — what was sent for the exam.

## Screen

Imtihonlar → an exam's results → **Natija xabarlari** (shown once someone is graded): the two counts, "har sinf
alohida", the two lists with tick boxes (already sent / no Telegram marked), both texts, the PDF switch, a confirm
step, the result line and the list of what was sent.

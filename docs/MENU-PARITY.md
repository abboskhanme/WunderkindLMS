# Menu parity with EduSchool

The client asked on 2026-09-13 that our sidebar match EduSchool's — *"menular
ketma ketligi ham bir xil bo'lsin, ichidagi funksionallik ham"*. This file is
the target order and the record of every deviation.

**Two orders exist, and the live one wins.** Reading the bundle gives the
*default* menu array that EduSchool ships. The school has reordered its own
sidebar since — the `Berkitish` control and a `sidebarVisibility` setting let
them — so what the client actually sees differs. The table below is the **live**
order, read off the running page on 2026-09-13, because that is the one he is
comparing us against.

## Target order (live)

| # | EduSchool (live) | Ours today | Note |
|---|---|---|---|
| 1 | Dashboard | Bosh sahifa | — |
| 2 | Lidlar | Lidlar | — |
| 3 | Moliya | Moliya | — |
| 4 | Jurnal | Jurnal | — |
| 5 | O'quv bo'limi | O'quv bo'limi | new group; absorbed our separate O'quvchilar, Sinflar, Fanlar, Shartnomalar |
| 6 | Topshiriqlar (staff tasks) | — | `docs/modules/staff-tasks.md` |
| 7 | Dars jadvali | Dars jadvali | — |
| 8 | Chat | Xabarlar | label kept — ours is not only chat |
| 9 | Gamifikatsiya | — | `docs/modules/gamification.md` |
| 10 | Qabul | — | `docs/modules/admission-and-testing.md` |
| 11 | Imtihonlar | — | same file |
| 12 | HR | HR | new group; absorbed O'qituvchilar + davomat + oylik |
| 13 | Analitika | Analitika | new group; absorbed Baholar hisoboti, O'qituvchilar hisoboti, Sinflar reytingi |
| 14 | Boshqaruv | Boshqaruv | — |
| 15 | Xulq-atvor | Xulq-atvor | was "Intizomiy ball" |
| 16 | Blok Test | — | `docs/modules/admission-and-testing.md` |
| 17 | Sozlamalar | Sozlamalar | — |

Hidden in their live sidebar but present in the product: **WareHouse**,
**Keldi-ketdi**, **Administrator**, **Mavsumiy baholash**. WareHouse still has a
spec (`docs/modules/warehouse.md`) because the client asked for parity with the
product, not with one school's current sidebar configuration; Administrator is
out by decision (single branch).

Ours with no EduSchool counterpart, placed where they belong rather than forced
into the sequence: **Davomat** and **Keldi-ketdi** after Xabarlar (both are
attendance), **Ilova** after Analitika.

**Unbuilt modules are absent from the menu, not present and empty.** A menu entry
that leads to a blank page is worse than no entry: it reads as a broken product
rather than an unfinished one. Each row above names the spec that will fill it,
and the position it takes when it lands.

## Labels come from their translation file, not the source

The `title` field in the bundle is a fallback. The rendered UI uses the i18n
key, and the two differ — the source says `Sinf`, `Group`, `Sertifikat`; the
screen says **Sinflar**, **Guruhlar**, **Sertifikatlar**. Ours follow the screen.

## How submenus open

Side flyout, not a downward accordion. Opening a parent shows a panel to its
right with the children split into columns under uppercase group headings —
`O'QUV JARAYONI`, `O'QUVCHILAR`, `HUJJATLAR`. `NavChild.group` carries that.

Below `lg` the panel would run off the screen (256px sidebar on a 375px phone),
so small screens keep the accordion. One open state, two renderings.

## Deviations, and why

**Kept, with no EduSchool counterpart at top level.** `Davomat` (after Jurnal)
and `Ilova` (the mobile-app section: assignments, LMS, canteen, teacher app).
EduSchool scatters these across other modules; folding ours in would move
working screens for no gain. Revisit when the matching modules land — the
canteen in particular belongs under WareHouse once meal costing exists.

**Schedule configuration stayed under Dars jadvali** — Choraklar, Dars vaqtlari,
Bayram kunlari, Davomat sabablari. EduSchool files this kind of thing under
Sozlamalar. Ours is where a user looks for it, and moving it is a UX change
rather than a parity one. Flagged rather than done.

**`Fanlar` moved** from Dars jadvali to O'quv bo'limi, matching EduSchool.

**Group vs Sinf.** EduSchool's O'quv bo'limi lists `Sinf` and `Group` as separate
entries: a teaching group independent of the homeroom class. Ours has only
Sinf, because `students.class_name` is a single column. That gap is priced at
184 h in `docs/modules/existing-module-gaps.md` and hinges on one question:
does the school teach groups drawn from several classes at once? Until that is
answered the second entry stays out.

**Cross-branch Administrator is not coming.** Single branch, by decision.

## Inside the menus

Parity is not only order. Each spec in `docs/modules/` carries the screens,
columns, fields and rules for its module, so "ichidagi funksionallik ham" is
tracked there rather than duplicated here.

## O'quv bo'limi — the submenu, item by item (2026-09-17)

The client sent EduSchool's own flyout and asked for the same sequence inside
it, not only at the top level. Ours now reads exactly:

| Group | EduSchool | Ours |
|---|---|---|
| O'QUV JARAYONI | Sinflar · Guruhlar · Fanlar · Xonalar | same |
| O'QUVCHILAR | O'quvchilar · Arxiv o'quvchilar · O'quvchilar manzili · Ota onalar | same |
| HUJJATLAR | Sertifikatlar · Shartnomalar | same |

Three entries are ours alone and sit at the **end of their group**, so the
EduSchool sequence reads unbroken: `O'quvchi holatlari` (their statuses live
in a settings dialog), `Sertifikat turlari` (theirs is under Sozlamalar, ours
cannot be — that route is gated on `settings` while the API is gated on
`students`), and the `BAHOLASH` pair, which they do not have at all.

**Xonalar moved here from Dars jadvali.** It was filed under the timetable's
settings when the register was built; EduSchool keeps it in O'quv bo'limi and
so do we now.

**Arxiv o'quvchilar is a real menu entry, not a tab.** Ours was a tab inside
the pupil list; `/admin/students/arxiv` now opens that list with the archive
tab selected, so the menu matches without duplicating the screen.

## The submenu was complete; the permissions were not (2026-09-17)

The client asked whether every entry in EduSchool's flyout actually exists on
our side — *"bu menular bizni tizimda boshqa joyda bo'lgan bo'lishi ham mumkin,
agar bo'lsa olib kelib shu yerga qo'y, bo'lmasa yarat"*. Checked entry by entry:
all ten are present, each with a route and a real page (Guruhlar 319 lines,
Xonalar 325, Ota-onalar 410, Manzil 224, Shartnomalar 384 — none is a stub).

**What was actually broken was the permission wiring.** The section declared
`perm: 'students'` and its children declared nothing, so:

- a staff member with only `students` saw the whole flyout and hit "ruxsat
  yo'q" on Sinflar, Guruhlar, Fanlar, Xonalar, Manzil, Ota-onalar and
  Shartnomalar — seven of the twelve entries;
- a staff member with only `classes` did not see the section at all, although
  Sinflar and Guruhlar are exactly theirs.

The section's `perm` is gone and every child now carries the key that really
guards its page. `Sidebar` already filters children and hides a group whose
children are all hidden, so the menu stops offering what the user cannot open.
The parent row is a button that only opens the panel, so dropping its `perm`
costs no navigation.

**Four different keys sit inside one section**: `classes` (Sinflar, Guruhlar),
`schedule` (Fanlar, Xonalar), `app` (Manzil, Ota-onalar), `students` (the rest),
`contracts` (Shartnomalar). That is the state of the server, not a menu choice.
`app` is a leftover from the mobile-app era and, now that Manzil and Ota-onalar
live here, belongs on `students` — but moving it means moving
`LocationsController` and `ParentsController` too, or the menu starts lying
again. It travels with X-2.

## Ours-only entries removed from the submenu (2026-09-17)

The client, seeing the full flyout after the rebuild: *"baholash qismi sub
menulari va o'quvchi holatlari menusi kerakmas, eduschoolda bor menular bo'lsa
yetadi."* Removed from `O'quv bo'limi`:

- the whole **BAHOLASH** group — `O'quvchilarga feedback`, `Feedback nomi`;
- **O'quvchi holatlari**.

Both were ours alone: EduSchool has no feedback section at all, and keeps pupil
statuses in a settings dialog rather than a menu entry.

**The routes and pages stay.** `/admin/students/baholash`,
`/admin/students/baholash-turlari` and `/admin/students/holatlar` still resolve
and still work; only the menu entries are gone. Nothing else links to
`holatlar` or to `baholash`, so those two are now reachable by URL only
(`baholash-turlari` is still linked from the evaluation page itself). Deleting
the features is a separate decision and was not asked for.

**`Sertifikat turlari` stays**, and is now the only ours-only entry in the
section. EduSchool does have certificate types — under Sozlamalar, not here —
so it is not an invention. It cannot move to our Sozlamalar because that route
is gated on `settings` while the API is gated on `students`.

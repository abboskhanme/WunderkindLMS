# Menu parity with EduSchool

The client asked on 2026-09-13 that our sidebar match EduSchool's — *"menular
ketma ketligi ham bir xil bo'lsin, ichidagi funksionallik ham"*. This file is
the target order and the record of every deviation.

The order below is EduSchool's own, read from its bundle in the sequence the
menu array is built, not alphabetised.

## Target order

| # | EduSchool | Ours today | Note |
|---|---|---|---|
| 1 | Dashboard | Bosh sahifa | — |
| 2 | Lead | Lidlar | — |
| 3 | Dars jadval | Dars jadvali | — |
| 4 | Topshiriqlar (staff tasks) | — | `docs/modules/staff-tasks.md` |
| 5 | O'quv bo'limi | O'quv bo'limi | new group; absorbed our separate O'quvchilar, Sinflar, Fanlar, Shartnomalar |
| 6 | Keldi-ketdi | Keldi-ketdi | was buried inside O'quvchilar as "Turniket" |
| 7 | Jurnal | Jurnal | — |
| 8 | Chat | Xabarlar | label kept — ours is not only chat |
| 9 | Imtihonlar | — | `docs/modules/admission-and-testing.md` |
| 10 | Qabul | — | same file |
| 11 | Analitika | Analitika | new group; absorbed Baholar hisoboti, O'qituvchilar hisoboti, Sinflar reytingi |
| 12 | Gamifikatsiya | — | `docs/modules/gamification.md` |
| 13 | Blok Test | — | `docs/modules/admission-and-testing.md` |
| 14 | WareHouse | — | `docs/modules/warehouse.md` |
| 15 | Moliya | Moliya | — |
| 16 | HR | HR | new group; absorbed O'qituvchilar + davomat + oylik |
| 17 | Boshqaruv | Boshqaruv | — |
| 18 | Administrator (cross-branch) | — | **deliberately not built** — single branch, `docs/ASSUMPTIONS.md` 2026-09-13 |
| 19 | Sozlamalar | Sozlamalar | — |
| 20 | Xulq-atvor | Xulq-atvor | was "Intizomiy ball" |

**Unbuilt modules are absent from the menu, not present and empty.** A menu entry
that leads to a blank page is worse than no entry: it reads as a broken product
rather than an unfinished one. Each row above names the spec that will fill it,
and the position it takes when it lands.

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

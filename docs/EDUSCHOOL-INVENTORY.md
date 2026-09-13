# EduSchool — feature inventory and gap analysis

**Source.** The client's own tenant, `wunderkind.eduschool.uz`, read on 2026-09-13.
The module tree, permission names and API paths below were extracted from the
application's own JavaScript bundle (`/assets/index-*.js` plus 64 lazy chunks,
8.1 MB) — the same files any browser downloads to render the page. Nothing was
clicked, submitted or changed. See the read-only rule in `CLAUDE.md`.

**API base.** `https://backend.eduschool.uz/moderator-api/`

**Scale of the live tenant.** 24 classes (`0A 1A 1B 1D 2A 2B 3A 3B 4A 4B 5A 5B
5D 6A 6B 6D 7A 7B 8A 8B 9A 9B 10A 10B`), 10 lesson periods a day, six-day week,
academic year 2025–2026.

**How to read the gap column.** `have` = we ship something equivalent today.
`partial` = we have part of it. `missing` = nothing in our system.

---

## 1. Module tree (25 top-level entries)

Each row carries EduSchool's own permission name, which is useful twice: it is
the finest-grained statement of what their RBAC can express, and it tells us
which screens a role actually gates.

| # | Module | Permission | Gap |
|---|---|---|---|
| 1 | Dashboard | `dashboard` | have |
| 2 | Lidlar | `getLeads` | have |
| 3 | Dars jadvali | `getLessons` | have |
| 4 | Topshiriqlar (staff task board) | `getEmployeeTasks` | **missing** |
| 5 | O'quv bo'limi | `academicDepartment` | partial |
| 6 | Keldi-ketdi (reception attendance) | `receptionAttendance` | partial |
| 7 | Jurnal | `getJournal` | have |
| 8 | Chat | `getChats` | have |
| 9 | Imtihonlar (seasonal assessment) | — | **missing** |
| 10 | Qabul (admission) | `getLeads` | **missing** |
| 11 | Analitika | — | partial |
| 12 | Gamifikatsiya | `getGamification` | **missing** |
| 13 | Blok Test | `getBlockTest` | **missing** |
| 14 | WareHouse | `wmsWarehouseView` | **missing** |
| 15 | Moliya | `cashboxGet` | partial |
| 16 | HR | — | **missing** |
| 17 | Boshqaruv | — | have |
| 18 | Administrator (cross-branch) | `administratorBranch` | **missing** |
| 19 | Sozlamalar | `getSettings` | partial |
| 20 | Xulq-atvor | `getBehaviors` | partial |

---

## 2. Sub-modules, module by module

### O'quv bo'limi — `BARN_ALL` (10)
Sinf · Group · Fanlar · **Xonalar** · O'quvchilar · **Arxiv o'quvchilar** ·
**Sertifikat** · **O'quvchilar manzili** · Ota-onalar · Shartnomalar

Bold = we do not have it. Note `Group` is separate from `Sinf`: they model a
teaching group independently of the homeroom class, which our schema does not.

### Moliya — `FINANCE_ALL` (13)
Moliya · Qarzdorlar bilan ishlash · **Ish haqi** · Moliya hisobotlari ·
Moliya hisobotlari (P&L) · **Moliya hisobotlari (P&L) 2.0** · Pul oqimi ·
Moliya analitikasi · Tranzaksiyalar · **Abonement tranzaksiyalari** ·
**Bonus** · **Jarima** · **Abonement tranzaksiyalari (qarzdorlik oyma-oy)**

The closest module to ours. We already have P&L, cash flow, debtors, collection
rate and the money-flow view; missing are payroll, bonuses, penalties and the
month-by-month subscription arrears view.

### Gamifikatsiya — `GAMIFICATION_ALL` (13)
Kategoriyalar · Mahsulotlar · Buyurtmalar · Coin tarixi · Yo'nalishlar ·
Darajalar · Baho-coin jadvali · Coin berish · Auksionlar · Cheklovlar ·
Coin reytingi · Award · Freeze

A complete points economy: earn coins from grades, spend them in a shop, bid in
auctions, with levels, directions, limits and a leaderboard. Our SPEC Phase 4
describes roughly this, unbuilt.

### WareHouse — `WAREHOUSE_ALL` (16)
Omborlar · Kategoriyalar · Yetkazib beruvchilar · Mahsulotlar ·
Xarid so'rovlari · Kirim · Chiqim · Byudjet · Inventarizatsiya sessiyasi ·
Inventarizatsiya tuzatish · Xona jihozlari · Kitoblar · O'qilgan kitoblar ·
Retseptlar · Ovqatlanish · Ovqat xarajatlari

Three businesses in one module: a real warehouse (stock in/out, suppliers,
purchase requests, stocktake, budget), a **library** (book copies, books read),
and a **canteen costed from recipes** (recipe → meal closure → meal cost).
Entirely absent from our system; our canteen is a menu list only.

### Analitika — `ANALITICS_ALL` (15)
O'zlashtirish ko'rsatkichlari · O'zlashtirish (fanlar bo'yicha) ·
Sinflar reytingi · Davomat analitikasi · Davomat intizomi bo'yicha hisobot ·
O'qituvchi ish hisoboti · Mavsumiy baholash hisoboti · Buyurtmalar voronkasi ·
**Ishdan bo'shatishlar hisoboti** · Turniket analitikasi ·
Turniket kirib-chiqish statistikasi · Kunlik davomat hisoboti ·
Filial holati · Qarzdorlar bilan ishlash

### HR — `HR_ALL` (5)
Ish haqi (payroll) · Soatbay ish haqi · Jarimalar · Arizalar (requests) ·
Tabel (timesheet)

### Sozlamalar — `SETTINGS_ALL` (11)
Umumiy sozlamalar · **Integratsiyalar** · Moliya sozlamalari · Xodimlar ·
Xodimlar bo'sh vaqti · Ish jadvali · O'qituvchi dars qoldirish ·
O'qituvchi baholash · Ruxsatlar · **Savdo va marketing** · Taklif va shikoyatlar

Integrations seen on the page itself: **MCP (AI)**, **Atmos** and **Bito Pay**
(payment), and SMS providers. Also SMS templates, auto-SMS and a message log.

### Boshqaruv — `MANAGMENT`
Filiallar · Ruxsatlar · Xodimlar · Taklif va shikoyatlar

### Administrator — `ADMINISTRATOR_ALL` (4, cross-branch)
Lidlar · O'quvchilar · Xodimlar · Taklif va shikoyatlar — the same lists again,
but across every branch at once. This is a **multi-branch** product; we removed
our Control Plane and are single-school today.

### Qabul — `ADMISSION_ALL` (2)
Nomzodlar · Test bazasi — an admission pipeline with an online test
(`/qabul-test/:token` is a public route, so candidates take it by link).

### Blok Test — `BLOCK_TEST_ALL` (3)
Imtihonlar · Imtihon turi · Natijalar

### Xulq-atvor — `BEHAVIOR`
Sabablar · Harakatlar · Xulq-atvor reytingi

### Topshiriqlar — `employee-task/*`
A full staff task board: kanban columns, calendar, timeline, assignees,
watchers, comments, bulk actions, archive, reports, summary, export.

---

## 3. Confirmed API endpoints

Extracted from the bundle; the list is partial but shows the shape.

```
class/all · class/pagin · class-student/get · class-student/transfer
students/pagin · student/archive-with-reason · groups/transfer
employees/pagin · subjects/pagin · rooms/all · rooms/multiple
branches/pagin · building/pagin · roles/
leads/kanban · leads/set-stage · leads/import/preview · leads/import/confirm
employee-task/{board/column,calendar,timeline,assignees,watcher,comment,
              bulk,archive,reports,summary,export}
fin-map/{cashboxes,daily-cash-balance,daily-transaction,top-five,
         transaction-type,calendar}
attendance-analytics/{info,total-info}
seasonal-mark/bysubjects · by-subjects/seasonal-appropriation
turnstile-sync/{user/student,user/employee,user/sync,branch/bulk-sync}
warehouse/products · sms-templates/pagin · surveys/pagin
custom-fields/AGREEMENT · multi-actions/{delete,change-status,change-responsible}
```

Two details worth keeping:

- `custom-fields/AGREEMENT` — the school can **add its own fields** to a
  contract. That is a schema-level capability, not a screen.
- `multi-actions/*` — bulk operations are a first-class concept across lists.

---

## 4. What this means for us

**We are not far behind on the academic and money core.** Journal, schedule,
attendance, classes, leads, chat, contracts, finance reports, cashier and the
turnstile all exist on our side, and in several places ours is stricter — an
append-only ledger, database-enforced payment immutability, gapless receipt
numbering and a real cash-shift model.

**The gap is five whole products we have never started:**

1. **WareHouse** — stock, library and recipe-costed canteen (16 screens)
2. **Gamifikatsiya** — the coin economy (13 screens)
3. **HR** — payroll, timesheet, penalties, staff requests (5 screens)
4. **Topshiriqlar** — the staff task board
5. **Qabul + Blok Test** — admission pipeline and online testing

Plus two structural differences that are cheaper to decide now than later:

- **Multi-branch.** Their `Administrator` section works across branches. We
  deleted the Control Plane and are single-school. If the client wants a second
  branch, this is a foundation decision, not a feature.
- **`Group` separate from `Sinf`.** They schedule a teaching group independently
  of the homeroom class. Our schema ties a student to one `class_name`.

**Not yet captured.** Table columns, form fields, validation rules and report
layouts. Those need the screens themselves, and are the next pass.

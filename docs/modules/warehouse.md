# WareHouse (WMS) — module specification

**Status:** specification only. No code, no migration, no entity, no page is produced by
this document. The schema is assembled centrally by the orchestrator; five parallel agents
writing migrations would collide in `AppDbContextModelSnapshot.cs`.

**Audience:** the agents who will build this. They cannot see the conversation that produced
this file. Everything needed to build is here or is named here by exact path.

**Language rule (project):** this document, all identifiers, all code comments are English.
UI strings are Uzbek. The Uzbek screen titles below are the literal strings to render.

---

## 0. Evidence: what the source bundle proved, and what it did not

### 0.1 Sources actually read

| Source | Absolute path | What it gave |
|---|---|---|
| EduSchool bundle | `/Users/abboskhan/Documents/Projects/WunderkindLMS/.eduschool-bundle/all.js` (8.34 MB, 7 427 lines) | route constants, API path constants, the complete WMS permission registry, the barcode input component |
| Menu tree | `.eduschool-bundle/edu-menu.json` | 16 WareHouse entries with group and permission |
| Endpoint list | `.eduschool-bundle/edu-endpoints.txt` | nothing usable for WMS (`warehouse/products` in it is a CSS-scroll heuristic, not an API path — see §0.3) |
| Our inventory | `docs/EDUSCHOOL-INVENTORY.md` | the gap statement |

### 0.2 The hard limit — read this before trusting any field list below

**EduSchool's sixteen WMS screens are lazy-loaded chunks, and none of those chunks are in
the bundle we captured.** The bundle contains the WMS *router* chunk (which is why we have
the sixteen sub-routes) but not the sixteen page chunks it points at:

```
index-DIlBugKM.js  warehouse      index-DZlHowrf.js  stock-in        index-miJa8ybr.js  book-copy
index-Bp80FrB3.js  category       index-CS4N3Mif.js  stock-out       index-C0RW1rYX.js  room-asset
index-DU08H7uX.js  supplier       index-DbiPxS7o.js  stock-adjust.   index-Dbe5BwaV.js  recipe
index-DvsBrqjr.js  product        index-XjliVCZc.js  budget          index-BHEpyMr6.js  meal-closure
index-dKl8Meff.js  inventory-ses. index-Cwj9FgMt.js  purchase-req.   index-DL4czx73.js  meal-cost-dash.
```

Verification: the tokens `stockIn`, `stockOut`, `inventorySession`, `purchaseRequest`,
`mealClosure`, `warehouseId`, `productId`, `unitPrice`, `quantity` occur **zero** times in
8.34 MB. `bookCopy` and `roomAsset` occur once each — both from *other* chunks that
cross-link into WMS (the student profile tab and the Rooms page action menu).

**And it would not have helped much even if they were.** EduSchool renders every list with a
server-driven column engine: the column set, types (`string`, `number`, `amount`,
`currency`, `currency_field`, `enum`, `date`, `reference`) and enum labels arrive from the
API (`table-settings/get`, `apiColumns`). Field names live on their server, not in any
JavaScript we can read.

**Consequence for this spec.** Screens, routes, permissions, endpoint paths, state
transitions and the barcode behaviour below are **observed facts**. Every column list, every
enum value and every formula is **our design**, informed by those facts. Each such block is
marked `[design]`. Do not present `[design]` items to the client as "what EduSchool does".

### 0.3 Observed facts, verbatim

**Routes** (`all.js` @3344875–3345263, top-level container `/wms`):

```
/wms/warehouse   /wms/category    /wms/supplier          /wms/product
/wms/stock-in    /wms/stock-out   /wms/stock-adjustment  /wms/budget
/wms/inventory-session   /wms/inventory-session/:id      /wms/purchase-request
/wms/book-copy   /wms/room-asset  /read-books
/wms/recipe      /wms/meal-closure                       /wms/meal-cost-dashboard
```

Note `/read-books` sits at the **root**, not under `/wms` — a deliberate separation on their
side too.

**API path constants** (`all.js` @3338623 onward, base `https://backend.eduschool.uz/moderator-api/`).
Singular = single resource, plural = list:

```
/wms/warehouse   /wms/warehouses
/wms/category    /wms/categories
/wms/supplier    /wms/suppliers
/wms/product     /wms/products
/wms/stock-in    /wms/stock-ins      /wms/stock-in/confirm   /wms/stock-in/from-purchase-request
/wms/stock-out   /wms/stock-outs
/wms/stock-adjustment                /wms/stock-adjustments
/wms/budget      /wms/budgets
/wms/inventory-session   /wms/inventory-sessions   /wms/inventory-session/complete
                                                   /wms/inventory-session/cancel
/wms/inventory-item      /wms/inventory-items
/wms/purchase-request    /wms/purchase-requests    /wms/purchase-request/submit
                         /wms/purchase-request/approve  /wms/purchase-request/reject
                         /wms/purchase-request/cancel
/wms/book-copy   /wms/book-copies    /wms/book-copy/borrow   /wms/book-copy/return
                                     /wms/book-copy/mark-status
/wms/room-asset  /wms/room-assets    /wms/room-assets/summary   /wms/room-assets/kpi
/read-books      /read-book/approve  /read-book/reject
/wms/recipe      /wms/recipes
/wms/meal-closure                    /wms/meal-closures
/wms/analytics/stock-movements       /wms/analytics/cost-by-department
/wms/analytics/loss                  /wms/analytics/supplier-prices
```

Four things this list settles on its own:

1. **`/wms/analytics/stock-movements` is EduSchool's own word.** They think in movements.
2. **There is no balance resource.** No `/wms/stock`, `/wms/balance`, `/wms/product/quantity`.
   A stored balance would almost certainly have earned its own endpoint.
3. **Stock-in is a two-step document**: `POST /wms/stock-in` then `/wms/stock-in/confirm`.
4. **The stocktake is a session** that is `complete`d or `cancel`led as a unit, with its own
   line resource (`/wms/inventory-item`).

**Permission registry** (`all.js` @2288814, section `WAREHOUSE`) — complete, in order:

| Group label | View | Edit | Delete |
|---|---|---|---|
| `WMS_WAREHOUSE` | `wmsWarehouseView` | `wmsWarehouseEdit` | `wmsWarehouseDelete` |
| `WMS_CATEGORY` | `wmsCategoryView` | `wmsCategoryEdit` | `wmsCategoryDelete` |
| `WMS_PRODUCT` | `wmsProductView` | `wmsProductEdit` | `wmsProductDelete` |
| `WMS_SUPPLIER` | `wmsSupplierView` | `wmsSupplierEdit` | `wmsSupplierDelete` |
| `WMS_STOCK_IN` | `wmsStockInView` | `wmsStockInEdit` | `wmsStockInDelete` |
| `WMS_STOCK_OUT` | `wmsStockOutView` | `wmsStockOutEdit` | **absent** |
| `WMS_BUDGET` | `wmsBudgetView` | `wmsBudgetEdit` | `wmsBudgetDelete` |
| `WMS_PURCHASE_REQUEST` | `wmsPurchaseRequestView` | `wmsPurchaseRequestEdit` | `wmsPurchaseRequestDelete` |
| `WMS_STOCK_ADJUSTMENT` | `wmsStockAdjustmentView` | `wmsStockAdjustmentEdit` | `wmsStockAdjustmentDelete` |
| `WMS_INVENTORY_SESSION` | `wmsInventorySessionView` | `wmsInventorySessionEdit` | `wmsInventorySessionDelete` |
| `WMS_BOOK_COPY` | `wmsBookCopyView` | `wmsBookCopyEdit` | `wmsBookCopyDelete` |
| `WMS_ROOM_ASSET` | `wmsRoomAssetView` | `wmsRoomAssetEdit` | `wmsRoomAssetDelete` |
| `READ_BOOKS` | `readBooksView` | `readBooksEdit` | **absent** |
| `WMS_RECIPE` | `wmsRecipeView` | `wmsRecipeEdit` | `wmsRecipeDelete` |
| `WMS_MEAL_CLOSURE` | `wmsMealClosureView` | `wmsMealClosureEdit` | **absent** |
| `WMS_ANALYTICS` | `wmsAnalyticsView` | — | — |

**Fourteen of sixteen groups have a Delete verb. Stock-out, reading log and meal closure do
not.** Those are precisely the three things that consume stock or record a fact about the
past. This asymmetry is the strongest evidence in the whole bundle that EduSchool treats
consumption as history rather than as editable rows, and this spec follows it.

**Barcode input** (`all.js` @3195947 and @336132). A real, reusable form control:

- USB / HID keyboard-wedge scanners: a global `keydown` listener detects a keystroke burst
  (inter-key gap < 50 ms), buffers it, and commits on `Enter`/`Tab` when length ≥ 4. It
  clears the field on burst start so a half-typed value cannot merge with a scan.
- Camera fallback: `html5-qrcode`, formats `CODE_128, EAN_13, EAN_8, UPC_A, UPC_E, CODE_39,
  CODE_93, ITF, QR_CODE`, `facingMode: environment`, 10 fps, scan box 280×140.
- i18n keys `wms.barcode.{scanHint, scanned, openCamera, cameraTitle, cameraPermissionDenied,
  cameraNotSupported}` — the WMS is the only consumer.

**Cross-links into WMS from screens we can read:**

- Rooms page row action `{ label: "wms.roomAsset.title", requiresFlag: "wmsRoomAssetView",
  onClick: navigate('/wms/room-asset?roomId=' + row._id) }` — room assets are filtered
  **by room** and reached from the room list.
- Student profile tab `borrowed-books`, gated by `readBooksView`, labelled
  `readBooks.title`, table `student_borrowed_book_pagin`, data
  `GET /wms/book-copies?borrowedByStudentId={studentId}`, status rendered by a chip with
  `domain: "bookCopy"`. So `book_copies` carries a **status enum** and a
  **borrowed-by-student** foreign key. (The chip's value list lives in another chunk; the
  values are not in the bundle.)

**Currency.** `useWmsCurrency-DKzP2ReM.js` is imported by the WMS router. The organisation
has exactly one currency, edited on a settings screen with a single `name` text field
(`general.currency`). `UZS`/`USD` appear only in HR payroll documents. **There is no
evidence of multi-currency in the WMS** — the hook is a display suffix.

**Canteen menu is a different module.** EduSchool's menu screens are `/menu`, `/menu-type`,
`/menu-schedule` under Settings, with permissions `getMenu`/`getMenuType`/`getMenuSchedule`.
That is the equivalent of our `CanteenController`. The WMS `recipe → meal-closure →
meal-cost-dashboard` chain is a **second, deeper** system that costs the same food.

### 0.4 What the bundle did **not** reveal

- Any column, field name, form field, placeholder or validation message for any WMS screen.
- Any enum value: no stock-out reasons, no book-copy statuses, no document statuses.
- The costing method. **There is no FIFO, LIFO, average, batch or lot token anywhere in the
  bundle.** `/wms/analytics/supplier-prices` shows they track price per supplier over time,
  which is consistent with any costing method and proves none.
- Whether the product balance is stored or derived.
- Whether categories are a flat list or a tree.
- Whether a book title exists separately from a book copy.
- What drives `actual_portions` on a meal closure.

Every one of those is answered below by decision, with the reason recorded. Where the client
should be asked, it is in §12.

---

## 1. Goal and scope

Give the school one place to answer three questions it cannot answer today:

1. **"What do we have, where, and what is it worth?"** — a stock ledger over products,
   warehouses, suppliers, purchases and issues, with a stocktake that produces auditable
   corrections instead of quiet edits.
2. **"Where are our books and who has them?"** — a library of physical copies with
   borrow/return, plus a reading log the school already runs on paper.
3. **"What did one portion of lunch actually cost?"** — recipes with ingredients, a daily
   meal closure that consumes those ingredients from stock, and a cost dashboard.

Users: the storekeeper (new role), the kitchen manager, the librarian, the department heads
who request purchases, the accountant, and the director who approves and reads the numbers.

**Non-goals for this module** (stated so nobody builds them by accident): multi-currency,
multi-branch stock transfer, unit-of-measure conversion tables, lot/batch tracing, serial
numbers, supplier contracts and payment terms, barcode label *printing*, and any write-back
to EduSchool.

---

## 2. Screens

Sixteen screens, our routes. Our permission keys are new and named in §8; the EduSchool
permission is given for traceability only.

All routes live under `/admin/ombor`. Uzbek titles are the literal strings to render.

| # | Uzbek title | Our route | Our perm | EduSchool route | EduSchool perm |
|---|---|---|---|---|---|
| 1 | Omborlar | `/admin/ombor/omborlar` | `warehouse` | `/wms/warehouse` | `wmsWarehouseView` |
| 2 | Kategoriyalar | `/admin/ombor/kategoriyalar` | `warehouse` | `/wms/category` | `wmsCategoryView` |
| 3 | Yetkazib beruvchilar | `/admin/ombor/yetkazib-beruvchilar` | `warehouse` | `/wms/supplier` | `wmsSupplierView` |
| 4 | Mahsulotlar | `/admin/ombor/mahsulotlar` | `warehouse` | `/wms/product` | `wmsProductView` |
| 5 | Kirim | `/admin/ombor/kirim` | `warehouse` | `/wms/stock-in` | `wmsStockInView` |
| 6 | Chiqim | `/admin/ombor/chiqim` | `warehouse` | `/wms/stock-out` | `wmsStockOutView` |
| 7 | Inventarizatsiya tuzatish | `/admin/ombor/tuzatish` | `warehouse` | `/wms/stock-adjustment` | `wmsStockAdjustmentView` |
| 8 | Inventarizatsiya sessiyasi | `/admin/ombor/inventarizatsiya` | `warehouse` | `/wms/inventory-session` | `wmsInventorySessionView` |
| 8b | Sessiya sanog'i | `/admin/ombor/inventarizatsiya/:id` | `warehouse` | `/wms/inventory-session/:id` | `wmsInventorySessionView` |
| 9 | Xarid so'rovlari | `/admin/ombor/xarid-sorovlari` | `procurement` | `/wms/purchase-request` | `wmsPurchaseRequestView` |
| 10 | Byudjet | `/admin/ombor/byudjet` | `procurement` | `/wms/budget` | `wmsBudgetView` |
| 11 | Xona jihozlari | `/admin/ombor/xona-jihozlari` | `assets` | `/wms/room-asset` | `wmsRoomAssetView` |
| 12 | Kitoblar | `/admin/kutubxona/kitoblar` | `library` | `/wms/book-copy` | `wmsBookCopyView` |
| 13 | O'qilgan kitoblar | `/admin/kutubxona/oqilgan` | `library` | `/read-books` | `readBooksView` |
| 14 | Retseptlar | `/admin/oshxona/retseptlar` | `meals` | `/wms/recipe` | `wmsRecipeView` |
| 15 | Ovqatlanish | `/admin/oshxona/ovqatlanish` | `meals` | `/wms/meal-closure` | `wmsMealClosureView` |
| 16 | Ovqat xarajatlari | `/admin/oshxona/xarajatlar` | `meals` | `/wms/meal-cost-dashboard` | `wmsAnalyticsView` |
| 17 | Ombor hisobotlari | `/admin/ombor/hisobotlar` | `warehouse` | (their `analytics/*`) | `wmsAnalyticsView` |

Screen 17 has no EduSchool menu entry — their four `analytics/*` endpoints feed widgets
inside other pages. We give them one page because a director will not hunt for them.

### 2.1 Contents, screen by screen

Every list screen: search box, column filters as listed, server pagination (§6.2), CSV
export via the existing `ExcelExport` service, and an empty state that names the action that
fills it. Every write button is hidden — not disabled — when the permission is absent
(`docs/SPEC.md` §4.3 precedent, `schoollms.client/src/pages/admin/billing/access.ts`).

**1. Omborlar** — table: name, code, kind, item count, total value, active. Actions: add,
edit, deactivate. Deactivate, not delete, once any movement references it.

**2. Kategoriyalar** — a two-level tree (parent → children). Table: name, parent, product
count, active. Add/edit/delete; delete blocked while products reference it.

**3. Yetkazib beruvchilar** — table: name, STIR (tax id), phone, contact person, last
delivery date, total purchased (12 months). Row opens a drawer with that supplier's price
history per product (`analytics/supplier-prices`).

**4. Mahsulotlar** — the busiest screen. Table: SKU, barcode, name, category, unit,
**balance per warehouse** (one column per active warehouse, plus total), average unit cost,
total value, reorder point, status. Filters: category, warehouse, `belowReorderPoint`,
`hasStock`, active. Row expands to the movement history of that product. The search field is
the barcode input (§0.3): scanning jumps to the product.

**5. Kirim** — list of stock-in documents: no, date, warehouse, supplier, lines, total,
status, created by, confirmed by. Actions: new, edit draft, delete draft, **confirm**,
**reverse** (confirmed only), print. The editor is a line grid with a barcode field, product
autocomplete, quantity, unit cost, line total, optional expiry date, running document total.
"Xarid so'rovidan yaratish" prefills lines from an approved purchase request.

**6. Chiqim** — list of issue documents: no, date, warehouse, reason, destination
(department / room / teacher / target warehouse), lines, total cost, status. Actions: new,
edit draft, discard draft, **confirm**, **reverse**. No delete button anywhere on this screen
— mirrors the missing `wmsStockOutDelete`. The editor shows the **live balance** next to each
line and refuses to confirm a line that would drive the balance negative (§7.4).

**7. Inventarizatsiya tuzatish** — list of adjustments: no, date, warehouse, product,
direction, quantity, value, reason, source session, status, created by, approved by. Add is
allowed standalone; adjustments born from a stocktake are read-only here.

**8. Inventarizatsiya sessiyasi** — list of sessions: code, warehouse, status, started,
completed, item count, counted count, variance count, total variance value. Actions: open a
session, cancel, complete.

**8b. Sessiya sanog'i** — the counting screen. One row per product in the warehouse, with
the frozen expected quantity, a counted-quantity input, variance, and a barcode field that
focuses the matching row. Progress bar. "Yakunlash" summarises variances and, on
confirmation, emits the adjustments.

**9. Xarid so'rovlari** — list: no, requester, department, needed by, lines, estimated
total, budget, status, decided by. Editor is a line grid (product, quantity, estimated unit
price, suggested supplier, note). Buttons follow the state machine (§7.6). A **budget
consumption bar** is shown at approval time.

**10. Byudjet** — list: name, period, scope (category / department / warehouse), amount,
consumed, remaining, over-budget flag, hard limit, status. A bar chart of consumption per
budget.

**11. Xona jihozlari** — filtered by `?roomId=` when reached from the Rooms page (mirrors
EduSchool). Table: inventory no, name, room, quantity, condition, status, responsible person,
acquired on, value. Header KPI strip: total assets, total value, in repair, broken,
written off. Actions: add, edit, change condition, write off, move to another room.

**12. Kitoblar** — two tabs. *Nomlar* (titles): title, author, ISBN, copies total, copies
available. *Nusxalar* (copies): inventory no, title, status, shelf, borrower, due date,
overdue. Actions: add title, add copies (bulk N copies), borrow, return, mark lost/damaged/
written off. Overdue rows are highlighted.

**13. O'qilgan kitoblar** — approval queue. Table: student, class, book, pages, finished on,
summary excerpt, status, reviewed by. Filters: status, class, date range. Actions: approve,
reject with reason. Bulk approve.

**14. Retseptlar** — list: name, meal, yield (portions), ingredient count, cost per portion
(computed live at current average cost), linked menu dish, active. Editor: header plus an
ingredient grid (product, quantity per yield, waste %), with the cost per portion recomputing
as lines change.

**15. Ovqatlanish** — one row per (date, meal). Columns: date, meal, planned portions, actual
portions, recipes used, total cost, cost per portion, status. The editor shows the day's menu
(from our existing `dishes`), the recipes attached to those dishes, the computed ingredient
requirement, an editable actual quantity per ingredient, and the resulting cost. "Yopish"
posts it.

**16. Ovqat xarajatlari** — dashboard. Cards: this month's total food cost, cost per portion
(month average), portions served, cost per student. Charts: cost per portion by day (line),
cost by meal type (stacked bar), top 10 ingredients by cost (bar), planned vs actual variance
(bar). Filters: month range, meal, warehouse.

**17. Ombor hisobotlari** — four reports on tabs, matching EduSchool's four analytics
endpoints: stock movements, cost by department, loss and write-off, supplier price history.

---

## 3. Data model

### 3.1 Conventions for this module — read before writing any entity

These follow `docs/SPEC.md` §3 and the Billing precedent
(`SchoolLms.Domain/Billing.cs`, `SchoolLms.Infrastructure/Data/BillingModel.cs`).

| Concern | Rule for this module | Why |
|---|---|---|
| File layout | Entities in **`SchoolLms.Domain/Warehouse.cs`** (new file). EF config in **`SchoolLms.Infrastructure/Data/WarehouseModel.cs`** (new file), one `WarehouseModel.Apply(b)` line added to `AppDbContext.OnModelCreating`. | `Entities.cs` is the repo's worst conflict file. Billing set this precedent for exactly this reason; `git diff --stat SchoolLms.Domain/Entities.cs` must stay empty. |
| Table prefix | Every table starts `wms_`. | Gamification (SPEC §3.8, Phase 4) also has "Kategoriyalar" and "Mahsulotlar" screens. Unprefixed `products`/`categories` would be claimed by whichever module lands first. |
| Primary keys | `uuid` (`Guid`), `gen_random_uuid()`. **Exception:** `wms_stock_movements.id` is `bigint identity`. | Movements are an ordered journal; `ledger_entries` uses `bigserial` for the same reason. |
| Naming | C# PascalCase; `UseSnakeCaseNamingConvention()` in `Program.cs` produces snake_case columns automatically. **Never** write `HasColumnName`. | Already global (`Program.cs:62`). |
| Money | `decimal` + `HasPrecision(14, 2)` → `numeric(14,2)`. | SPEC §3. |
| Unit cost | `decimal` + `HasPrecision(14, 4)` → `numeric(14,4)`. | A gram of flour costs a fraction of a so'm. Two decimals would round a recipe line to zero. |
| Quantity | `decimal` + `HasPrecision(14, 3)` → `numeric(14,3)`. | 0.250 kg must survive. Integers would force a unit-conversion subsystem (§3.4). |
| Percent | `decimal` + `HasPrecision(5, 2)`. | Matches `Discount.Percent`. |
| Business dates | `DateOnly` → `date`. | `Expense.OnDate` precedent. |
| Timestamps | **`DateTimeOffset`** → `timestamptz`. Never `DateTime`. | `AppDbContext.OnModelCreating` forces every `DateTime` column to `timestamp without time zone`. A warehouse audit trail without an offset cannot be replayed. The loop only inspects `DateTime`, so `DateTimeOffset` passes through untouched — the same escape Billing uses. |
| Clock | `AppClock.NowInstant` for timestamps, `AppClock.Today` for dates. Never `DateTime.Now`. | `SchoolLms.Domain/AppClock.cs`. |
| FKs to legacy tables | `students.id`, `teachers.id`, `users.id`, `dishes.id`, `subjects.id` are **`text`**. Columns referencing them are `string`, and they are **real FKs**. | Same deliberate mixture as Billing (`Billing.cs` header comment). Porting 53 legacy entities is not this module's job. |
| Enums | `text` column + a `public static class XStatus { public const string ... ; public static readonly IReadOnlyList<string> All = [...]; }` + a `HasCheckConstraint` listing the values. **No PostgreSQL `enum` types.** | `DiscountStatus` / `CashShiftStatus` precedent. A PG enum needs a migration to add a value; a check constraint needs a migration too, but does not break `pg_dump` ordering or EF's snapshot. |
| Soft delete | A `status` column, never an `is_deleted` boolean pair. Catalog rows use `is_active bool` where there is no lifecycle. | SPEC §3. |
| Actor | `created_by` / `confirmed_by` / `approved_by` come from the **JWT**, are rejected if present in the request body. | SPEC §4.4, enforced today by `FinanceActor.RequireUserId`. |
| Grants | Every new table needs a GRANT in a new `SchoolLms.Infrastructure/Migrations/Sql/warehouse_guards.sql`, following `billing_guards.sql` exactly, including the `IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_rw') THEN RAISE NOTICE … RETURN` guard. | `ALTER DEFAULT PRIVILEGES` hands every new table full CRUD to `app_rw`; the REVOKE must ship with the migration or it silently never happens. |
| Constraints | Check constraints, unique indexes and generated columns go in **`WarehouseModel.cs`** (EF model), not raw SQL. Raw SQL is only for what EF cannot express: triggers and GRANT/REVOKE. | Model constraints land in the snapshot, so a future `--autogenerate` will not DROP them. |
| `Up()` must contain no `DROP` | If autogenerate emits one, stop and read. | `billing_guards.sql` header; global rule in `CLAUDE.md`. |

### 3.2 Shared prerequisite — the `rooms` table

`wms_room_assets` needs a room. **We do not have one.** `SchoolClass.Room` is a nullable
free-text string (`SchoolLms.Domain/Entities.cs:214`). `docs/SPEC.md` §3.3 already specifies
the table we should build:

```
rooms(id uuid pk, name text not null unique, building text, floor smallint,
      capacity smallint not null default 30,
      kind text not null default 'classroom')   -- classroom | lab | gym | hall
```

**Decision: build exactly that, minus the schedule wiring.** Create `rooms`, seed it from
`select distinct room from classes where room is not null and room <> ''`, and stop.
Do **not** add `classes.home_room_id` and do **not** touch the scheduler — that is the
schedule module's work and its own migration.

Rejected alternative: a free-text `location` column on the asset. It would give us a second
location vocabulary that immediately diverges from `classes.room`, and the Rooms → assets
cross-link that EduSchool has (`/wms/room-asset?roomId=`) would be impossible.

**`rooms` is a shared file. It is listed in §10.4 as sequential and must not be created by
two modules.**

### 3.3 Catalog

#### `wms_warehouses`

| Column | Type | Null | Default | Notes |
|---|---|---|---|---|
| `id` | uuid | no | `gen_random_uuid()` | |
| `code` | text | no | | unique; short, uppercase, stable (`ASOSIY`, `OSHXONA`, `KUTUBXONA`) |
| `name` | text | no | | |
| `kind` | text | no | `'general'` | `WarehouseKind` (§3.11) |
| `is_active` | bool | no | `true` | |
| `note` | text | yes | | |
| `created_by` | text | no | | FK `users.id` RESTRICT |
| `created_at` | timestamptz | no | | |

Indexes: unique `code`. Check: `kind in (...)`.
Delete is refused once any movement references the warehouse; deactivate instead.

#### `wms_categories`

| Column | Type | Null | Default | Notes |
|---|---|---|---|---|
| `id` | uuid | no | `gen_random_uuid()` | |
| `name` | text | no | | unique per parent |
| `parent_id` | uuid | yes | | FK self, RESTRICT |
| `is_active` | bool | no | `true` | |
| `created_at` | timestamptz | no | | |

Unique `(parent_id, name)` — a partial unique index is needed for the `parent_id is null`
case (PostgreSQL treats NULLs as distinct): `create unique index ux_wms_categories_root on
wms_categories(name) where parent_id is null`.

`[design]` **Two levels maximum**, enforced in the service: a category whose `parent_id` is
set may not itself be a parent. Reason: a school stocks maybe eighty products; an arbitrary
tree buys nothing and makes every roll-up report recursive. The bundle does not say either
way.

#### `wms_suppliers`

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | uuid | no | |
| `name` | text | no | unique |
| `tin` | text | yes | STIR, 9 digits; unique when not null (partial unique index) |
| `phone` | text | yes | normalised through the existing `PhoneUtil` |
| `contact_person` | text | yes | |
| `address` | text | yes | |
| `note` | text | yes | |
| `is_active` | bool | no | default `true` |
| `created_by` | text | no | FK `users.id` |
| `created_at` | timestamptz | no | |

#### `wms_products`

| Column | Type | Null | Default | Notes |
|---|---|---|---|---|
| `id` | uuid | no | `gen_random_uuid()` | |
| `sku` | text | no | | unique; generated `MHS-000001` if the user leaves it blank |
| `barcode` | text | yes | | unique when not null (partial unique index) |
| `name` | text | no | | |
| `category_id` | uuid | no | | FK `wms_categories` RESTRICT |
| `unit` | text | no | | `ProductUnit` (§3.11) — the **base unit**, immutable after the first movement |
| `kind` | text | no | `'consumable'` | `ProductKind` (§3.11) |
| `reorder_point` | numeric(14,3) | no | `0` | 0 = no alert |
| `shelf_life_days` | int | yes | | drives the expiry warning on stock-in |
| `default_supplier_id` | uuid | yes | | FK `wms_suppliers` SET NULL |
| `is_active` | bool | no | `true` | |
| `note` | text | yes | | |
| `created_by` | text | no | | FK `users.id` |
| `created_at` | timestamptz | no | | |
| `updated_at` | timestamptz | yes | | |

Indexes: unique `sku`; partial unique `barcode`; `(category_id, is_active)`; a trigram or
`lower(name)` index for search.

**There is no `quantity` column and there will never be one.** See §3.4.

**`unit` is immutable once a movement exists.** Changing kg → g under a stock of 40 would
silently turn 40 kg into 40 g and the average cost with it. The service must refuse; a check
constraint cannot see other tables, so this is a service rule plus a test.

`[design]` **No unit conversion table.** One product, one base unit, forever. A recipe that
needs 250 g of a product held in kg stores `0.250`. The UI may offer a g/kg toggle that
writes the base value; the database only ever sees base units. Reason: conversion tables are
a subsystem (per-product factors, rounding policy, which unit the report shows) and a school
kitchen does not need one.

### 3.4 The stock ledger — `wms_stock_movements`

> **The decision.** Stock is a **journal of movements**. The balance of a product in a
> warehouse is **derived** by summing that journal. There is no mutable quantity column
> anywhere in this module.
>
> This is the same lesson `docs/PENDING_WIRING.md` §21.4 records for money: `students.balance`
> was a mutable number updated in six places, one missed update silently corrupted the debt,
> and no error was ever raised. It was dropped and replaced by
> `SchoolLms.Application/Billing/StudentBalanceQuery.cs`. A warehouse quantity has exactly the
> same failure mode with exactly the same silence, and one more: a mutable quantity is a
> column a thief can edit.
>
> The bundle **supports** this reading (EduSchool's own endpoint is
> `/wms/analytics/stock-movements`; there is no balance resource; stock-out has no Delete
> permission) but does not **prove** it. We recommend it on our own reasoning, and we would
> recommend it even if EduSchool did the opposite.

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | bigint identity | no | journal order |
| `product_id` | uuid | no | FK `wms_products` RESTRICT |
| `warehouse_id` | uuid | no | FK `wms_warehouses` RESTRICT |
| `direction` | text | no | `in` \| `out` — `MovementDirection` |
| `quantity` | numeric(14,3) | no | **always > 0**; direction carries the sign |
| `unit_cost` | numeric(14,4) | no | frozen at post time (§4) |
| `total_cost` | numeric(14,2) | no | frozen; see §4.3 for why it is stored, not computed |
| `ref_type` | text | no | `MovementRefType` (§3.11) |
| `ref_id` | uuid | no | the source document |
| `ref_line_id` | uuid | yes | the source document line |
| `occurred_on` | date | no | the document's business date |
| `reversal_of` | bigint | yes | FK self; storno |
| `memo` | text | yes | |
| `created_by` | text | no | FK `users.id` |
| `created_at` | timestamptz | no | |

Checks: `quantity > 0`; `unit_cost >= 0`; `total_cost >= 0`; `direction in ('in','out')`;
`ref_type in (...)`.
Indexes: `(product_id, warehouse_id, id)` — the balance and average-cost scan;
`(ref_type, ref_id)`; `(occurred_on)`; `(warehouse_id, occurred_on)`.

**Append-only, enforced in `warehouse_guards.sql`:**

```
GRANT SELECT, INSERT ON public.wms_stock_movements TO app_rw;
REVOKE UPDATE, DELETE, TRUNCATE ON public.wms_stock_movements FROM app_rw;
```

**Only `StockLedgerService` may insert into this table** (`SchoolLms.Application/Warehouse/`).
No controller, no repository, no other service. This mirrors SPEC §2.2's rule for
`LedgerService` and `ledger_entries`, and for the same reason: one writer means one place
where the cost rule lives.

**Correction path.** A wrong movement is corrected by posting a **mirror movement** with the
opposite direction, the same `unit_cost`, and `reversal_of` set to the original id. The
original is never touched. Document-level "reverse" emits one mirror per line.

**Derived reads** live in `SchoolLms.Application/Warehouse/StockBalanceQuery.cs`, a plain
class over `IAppDbContext`, constructed at the call site with no `Program.cs` registration —
the `StudentBalanceQuery` / `FinanceReportQueries` pattern:

| Method | Returns |
|---|---|
| `ForAsync(productId, warehouseId, asOf?)` | `StockBalanceDto(Quantity, Value, AverageUnitCost)` |
| `ForManyAsync(productIds, warehouseId?, asOf?)` | dictionary keyed by `(productId, warehouseId)` — **two queries regardless of list size** |
| `ForWarehouseAsync(warehouseId, asOf?)` | every product with a non-zero balance |
| `BelowReorderPointAsync()` | products whose total balance < `reorder_point` |

`ForAsync` must never be called inside a loop. Every list screen calls `ForManyAsync` once.
This is the same admonition `StudentBalanceQuery` carries, and it is there because that class
was written after the N+1 was found.

### 3.5 Documents

Every document follows the same shape, so build one and copy it:

- header + lines, both `uuid`
- `doc_no text not null unique` — gapless per type per year (§7.2)
- `doc_date date not null` — the business date, defaults to today, may not be in the future
- `status text not null` — `draft` → `confirmed` (`cancelled` from `draft` only)
- `total_cost numeric(14,2) not null default 0` — written at confirm, from the movements
- `created_by`, `created_at`, `confirmed_by`, `confirmed_at`
- **A `draft` touches nothing.** All stock effects happen at confirm, atomically.
- **A `confirmed` document is immutable.** No edit, no delete. Reverse it.

#### `wms_stock_in` / `wms_stock_in_lines`

Header adds: `warehouse_id` (FK RESTRICT), `supplier_id` (FK RESTRICT, nullable),
`purchase_request_id` (FK RESTRICT, nullable), `supplier_invoice_no text`,
`expense_id uuid` (FK `expenses` RESTRICT, nullable — see §5), `note`.

Line: `id`, `stock_in_id` (FK CASCADE), `product_id` (FK RESTRICT), `quantity numeric(14,3)
> 0`, `unit_cost numeric(14,4) >= 0`, `total_cost numeric(14,2)`, `expiry_date date` (null),
`note`. Unique `(stock_in_id, product_id)` — one line per product; merge on entry rather than
allowing two lines that later have to be reconciled.

On confirm: one `in` movement per line, `ref_type = 'stock_in'`.

#### `wms_stock_out` / `wms_stock_out_lines`

Header adds: `warehouse_id`, `reason` (`StockOutReason`, §3.11), and exactly one destination
depending on the reason:

| `reason` | Required destination column |
|---|---|
| `consumption` | `department_id` (FK `departments`, see §12 Q4) |
| `meal` | none — `ref` is the meal closure |
| `transfer` | `target_warehouse_id` (FK `wms_warehouses`, ≠ source) |
| `asset_issue` | `room_id` (FK `rooms`) |
| `write_off`, `loss`, `expiry` | none; `note` becomes **required** |
| `other` | none; `note` required |

A check constraint enforces the reason→destination pairing. `issued_to_teacher_id` (FK
`teachers.id`, nullable) may be set for any reason and records who physically took it.

Line: as stock-in, but `unit_cost` and `total_cost` are **written by the service at confirm**,
never accepted from the client.

On confirm: one `out` movement per line, `ref_type = 'stock_out'`. When
`reason = 'transfer'`, additionally one `in` movement per line into `target_warehouse_id`
with the same `ref_id`, `ref_line_id` and `unit_cost` — a transfer moves value, it does not
create or destroy it.

**No delete, ever** — mirrors the absent `wmsStockOutDelete`. A draft may be discarded by its
creator; that is part of the write permission, not a separate verb.

#### `wms_stock_adjustments`

A single-line document (EduSchool has no `stock-adjustment-lines` resource).

| Column | Type | Notes |
|---|---|---|
| `id`, `doc_no`, `doc_date`, `status`, `created_by/at`, `confirmed_by/at` | | as above |
| `warehouse_id` | uuid | FK RESTRICT |
| `product_id` | uuid | FK RESTRICT |
| `direction` | text | `in` \| `out` |
| `quantity` | numeric(14,3) | > 0 |
| `unit_cost` | numeric(14,4) | required for `in`; for `out` the service overwrites it with the current average |
| `total_cost` | numeric(14,2) | written at confirm |
| `reason` | text | `AdjustmentReason` (§3.11) |
| `inventory_session_id` | uuid | FK `wms_inventory_sessions` RESTRICT, nullable |
| `note` | text | **required** when `reason in ('theft','correction')` |
| `approved_by` | text | FK `users.id`, nullable |
| `approved_at` | timestamptz | nullable |

**Dual control.** An adjustment changes inventory value with no cash movement and no
counterparty. It is the single largest fraud surface in this module. Therefore, copying
SPEC §4.5 and `ck_expenses_approver_differs` exactly:

- `ck_wms_adjustments_approver_differs`: `approved_by is null or approved_by <> created_by`
- an adjustment whose `|total_cost|` exceeds `wms_settings.adjustment_approval_threshold`
  **cannot be confirmed** without an `approved_by`, and the approver must be a different user
- below the threshold it confirms immediately, like a small expense
- adjustments produced by a completed stocktake carry `inventory_session_id` and inherit the
  session's approval — the session itself is the second signature

#### `wms_inventory_sessions` / `wms_inventory_items`

Session: `id`, `code` (unique, `INV-2026-0001`), `warehouse_id`, `status`
(`InventorySessionStatus`, §3.11), `started_at`, `started_by`, `completed_at`, `completed_by`,
`cancelled_at`, `cancelled_by`, `note`, `variance_value numeric(14,2)` (written at complete).

Item: `id`, `session_id` (FK CASCADE), `product_id` (FK RESTRICT),
`expected_qty numeric(14,3) not null`, `expected_unit_cost numeric(14,4) not null`,
`counted_qty numeric(14,3)` (nullable until counted), `variance_qty numeric(14,3) generated
always as (counted_qty - expected_qty) stored`, `counted_by`, `counted_at`, `note`.
Unique `(session_id, product_id)`.

**`expected_qty` and `expected_unit_cost` are snapshotted when the session opens and are
never recomputed.** If they were recalculated at completion, a movement posted mid-count
would silently absorb the variance and the count would always reconcile — which is the one
outcome a stocktake exists to prevent.

`[design]` **A warehouse may have only one non-terminal session at a time** (partial unique
index on `warehouse_id where status in ('open','counting')`). Two overlapping counts of the
same shelf produce two contradictory truths.

`[design]` **Movements are not blocked during a session.** Blocking would stop the kitchen
from cooking. Instead, `variance_qty` is explained by the item's frozen expectation, and the
completion screen lists movements posted between `started_at` and `completed_at` so the
counter can see what moved under them.

### 3.6 Procurement

#### `wms_purchase_requests` / `wms_purchase_request_lines`

Header: `id`, `doc_no` (unique, `XAR-2026-0001`), `requested_by` (FK `users.id`),
`department_id` (nullable), `warehouse_id` (FK RESTRICT), `needed_by date` (nullable),
`budget_id` (FK `wms_budgets` RESTRICT, nullable), `status` (`PurchaseRequestStatus`, §3.11),
`estimated_total numeric(14,2)`, `submitted_at`, `decided_by`, `decided_at`,
`decision_note text`, `created_at`.

Line: `id`, `request_id` (FK CASCADE), `product_id` (FK RESTRICT), `quantity numeric(14,3)
> 0`, `estimated_unit_price numeric(14,4) >= 0`, `estimated_total numeric(14,2)`,
`suggested_supplier_id` (nullable), `note`. Unique `(request_id, product_id)`.

Check `ck_wms_pr_approver_differs`: `decided_by is null or decided_by <> requested_by`.
Nobody approves their own purchase — the same rule as discounts and expenses.

`estimated_unit_price` defaults, in the UI, to the last confirmed stock-in unit cost for that
product; the requester may override it.

#### `wms_budgets`

| Column | Type | Notes |
|---|---|---|
| `id` | uuid | |
| `name` | text | |
| `period_start`, `period_end` | date | check `period_end >= period_start` |
| `warehouse_id` | uuid | nullable = all warehouses |
| `category_id` | uuid | nullable = all categories |
| `department_id` | uuid | nullable = all departments |
| `amount` | numeric(14,2) | > 0 |
| `is_hard_limit` | bool | default `false` |
| `status` | text | `draft` \| `active` \| `closed` |
| `note` | text | |
| `created_by`, `created_at` | | |

**Consumption is derived, never stored:** the sum of `wms_stock_in_lines.total_cost` over
confirmed stock-in documents whose `doc_date` falls inside the period and whose warehouse /
product-category / requesting department match the budget's non-null dimensions.
`BudgetConsumptionQuery` in `SchoolLms.Application/Warehouse/`.

`[design]` **Soft by default.** `is_hard_limit = false` shows a warning at purchase-request
approval and marks the budget red. With `is_hard_limit = true`, approving a request that
would exceed the remaining amount is refused. Reason: a school that cannot buy heating oil in
January because a spreadsheet said so will simply stop using the module. The hard limit
exists for the cases where the director wants it.

`[design]` **Overlapping budgets are allowed but flagged.** Two active budgets matching the
same dimensions produce a warning on save, not an error — a yearly budget plus a quarterly
sub-budget is a legitimate pattern.

### 3.7 Room assets — `wms_room_assets`

Not a stock table. A room asset is a durable, identified object that has **already left**
stock; it has a condition and a location, not a balance.

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | uuid | no | |
| `room_id` | uuid | no | FK `rooms` RESTRICT |
| `product_id` | uuid | yes | FK `wms_products` SET NULL — provenance when it came from stock |
| `stock_out_line_id` | uuid | yes | FK `wms_stock_out_lines` SET NULL — which issue created it |
| `inventory_no` | text | yes | unique when not null (partial unique index) |
| `name` | text | no | copied from the product at creation, then independent |
| `quantity` | numeric(14,3) | no | default 1 — a set of 30 identical chairs is one row |
| `unit` | text | no | `ProductUnit` |
| `unit_cost` | numeric(14,4) | no | frozen at creation |
| `total_cost` | numeric(14,2) | no | |
| `acquired_on` | date | no | |
| `condition` | text | no | `AssetCondition` (§3.11) |
| `status` | text | no | `AssetStatus` (§3.11) |
| `responsible_teacher_id` | text | yes | FK `teachers.id` SET NULL |
| `note` | text | yes | |
| `created_by`, `created_at`, `updated_at` | | | |

Indexes: `(room_id, status)`; partial unique `inventory_no`.

`wms_room_asset_events` — append-only history so a write-off is auditable:
`id bigint identity`, `asset_id` (FK CASCADE), `event` (`created` \| `moved` \| `condition_changed`
\| `written_off` \| `quantity_changed`), `from_value text`, `to_value text`, `reason text`,
`created_by`, `created_at`. Grants: SELECT + INSERT only.

`/wms/room-assets/summary` → per-room counts and value.
`/wms/room-assets/kpi` → totals, in-repair count, broken count, written-off value this year.

**Issuing an asset from stock** is a `wms_stock_out` with `reason = 'asset_issue'` and a
`room_id`; on confirm the service creates one `wms_room_assets` row per line with
`stock_out_line_id` set. Assets may also be created directly (everything the school already
owns), with `product_id` and `stock_out_line_id` null.

### 3.8 Library

Two separate concerns that share a menu group and nothing else.

#### `wms_books` (titles) and `wms_book_copies` (physical copies)

**Deviation from EduSchool, deliberate.** Their API exposes only `book-copy`; there is no
title resource. We split it. Reason: thirty copies of the same textbook would otherwise carry
thirty copies of the title, author, ISBN and publisher, and the thirty-first would be typed
slightly differently. A "books available" report would then be wrong and nobody would know
why. The cost of the split is one extra table and one extra screen tab.

`wms_books`: `id`, `title` (not null), `author`, `isbn` (unique when not null),
`publisher`, `published_year smallint`, `language text` (`uz` \| `ru` \| `en` \| `other`),
`category_id` (FK `wms_categories`, nullable), `subject_id` (FK `subjects.id` **text**,
nullable — links a textbook to a school subject), `cover_url`, `note`, `created_by`,
`created_at`. Index on `lower(title)`, partial unique on `isbn`.

`wms_book_copies`:

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | uuid | no | |
| `book_id` | uuid | no | FK `wms_books` RESTRICT |
| `inventory_no` | text | no | unique |
| `warehouse_id` | uuid | yes | FK `wms_warehouses` — the library location |
| `shelf` | text | yes | |
| `status` | text | no | `BookCopyStatus` (§3.11) |
| `borrowed_by_student_id` | text | yes | FK `students.id` SET NULL — **name confirmed by the bundle** |
| `borrowed_by_teacher_id` | text | yes | FK `teachers.id` SET NULL |
| `borrowed_at` | timestamptz | yes | |
| `due_on` | date | yes | |
| `acquired_on` | date | no | |
| `unit_cost` | numeric(14,4) | no | for the loss charge |
| `note` | text | yes | |
| `created_by`, `created_at`, `updated_at` | | | |

Checks: `ck_wms_book_copies_one_borrower` — `borrowed_by_student_id is null or
borrowed_by_teacher_id is null`; `ck_wms_book_copies_borrowed_fields` — when
`status = 'borrowed'`, exactly one borrower and `borrowed_at` and `due_on` are all not null,
and when the status is anything else all four are null.

Index: `(borrowed_by_student_id)` — the student profile tab queries by it;
`(status, due_on)` for the overdue list.

**The stock-ledger rule does not extend here, on purpose.** A copy is one identified object
with a state, not a fungible quantity, so there is no balance to derive and nothing to sum.
`status` is a stored column and that is correct. The history lives in:

`wms_book_loans` (mutable, one row per loan): `id`, `copy_id` (FK RESTRICT),
`borrower_student_id`, `borrower_teacher_id`, `borrowed_at`, `due_on`, `returned_at`
(nullable), `returned_to` (user id, nullable), `condition_on_return`
(`good` \| `damaged` \| `lost`, nullable), `note`, `created_by`.
Partial unique index: one open loan per copy —
`create unique index ux_wms_book_loans_open on wms_book_loans(copy_id) where returned_at is null`.

#### `wms_reading_log` — "O'qilgan kitoblar"

A separate thing from borrowing. A student records a book they have read; a librarian or
teacher approves or rejects it. Confirmed by the endpoints `/read-books`,
`/read-book/approve`, `/read-book/reject` and the permission pair
`readBooksView` / `readBooksEdit` (no Delete).

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | uuid | no | |
| `student_id` | text | no | FK `students.id` CASCADE |
| `book_id` | uuid | yes | FK `wms_books` SET NULL — null when the book is not in our library |
| `title` | text | no | copied from the book, or typed |
| `author` | text | yes | |
| `pages` | int | yes | > 0 |
| `started_on` | date | yes | |
| `finished_on` | date | no | may not be in the future |
| `summary` | text | yes | the student's own words |
| `status` | text | no | `ReadingLogStatus` (§3.11) |
| `reviewed_by` | text | yes | FK `users.id` |
| `reviewed_at` | timestamptz | yes | |
| `review_note` | text | yes | required when rejecting |
| `created_at` | timestamptz | no | |

Index `(status, finished_on)`, `(student_id, finished_on)`.
Unique `(student_id, book_id, finished_on)` where `book_id is not null` — stops the same book
being logged twice on one day.

`[design]` **Gamification hook, not built here.** SPEC §3.8 defines `point_transactions` as
append-only with a `source` column. When gamification lands (Phase 4), approving a reading log
should insert one `point_transactions` row with `source = 'reading'`. This module must
**not** create a points table of its own. Until then, approval awards nothing.

### 3.9 Meals

#### `wms_recipes` / `wms_recipe_lines`

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | uuid | no | |
| `name` | text | no | unique |
| `meal` | text | no | `breakfast` \| `lunch` \| `dinner` — **the same three values as `Dish.Meal` and `CanteenMenu.Meals`** |
| `yield_portions` | int | no | > 0; the ingredient quantities are for this many portions |
| `dish_id` | text | yes | FK `dishes.id` SET NULL — links the recipe to our existing canteen menu |
| `instructions` | text | yes | |
| `is_active` | bool | no | default `true` |
| `created_by`, `created_at`, `updated_at` | | | |

`wms_recipe_lines`: `id`, `recipe_id` (FK CASCADE), `product_id` (FK RESTRICT),
`quantity numeric(14,3) > 0` (in the product's base unit, for `yield_portions` portions),
`waste_percent numeric(5,2) >= 0 and <= 100` default 0, `note`.
Unique `(recipe_id, product_id)`.

Effective requirement per portion:
`quantity / yield_portions * (1 + waste_percent / 100)`.

**`dish_id` is the join to what we already have.** `Dish` (`Entities.cs:248`) is
`(Date, Meal, Name, Ingredients, ImageUrl)` — a menu line with free-text ingredients. Linking
a recipe to a dish turns that free text into a costed bill of materials without changing
`Dish` at all, and lets the meal-closure screen read the day's menu from the table the
kitchen already fills in.

#### `wms_meal_closures` / `wms_meal_closure_lines`

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | uuid | no | |
| `doc_no` | text | no | unique, `OVQ-2026-0001` |
| `on_date` | date | no | |
| `meal` | text | no | `breakfast` \| `lunch` \| `dinner` |
| `warehouse_id` | uuid | no | the kitchen store |
| `planned_portions` | int | no | >= 0 |
| `actual_portions` | int | no | > 0 when posting |
| `status` | text | no | `draft` \| `posted` |
| `total_cost` | numeric(14,2) | no | default 0, written at post |
| `cost_per_portion` | numeric(14,2) | no | default 0, written at post = `round(total_cost / actual_portions, 2)` |
| `stock_out_id` | uuid | yes | FK `wms_stock_out` RESTRICT — the issue document it produced |
| `note` | text | yes | |
| `created_by`, `created_at`, `posted_by`, `posted_at` | | | |

Unique `(on_date, meal)` — one closure per meal per day.

`wms_meal_closure_lines`: `id`, `closure_id` (FK CASCADE), `recipe_id` (FK RESTRICT,
nullable — a line may be an ad-hoc ingredient), `product_id` (FK RESTRICT),
`planned_qty numeric(14,3)`, `actual_qty numeric(14,3) >= 0`,
`unit_cost numeric(14,4)` (frozen at post), `total_cost numeric(14,2)`.
Unique `(closure_id, product_id)` — ingredients used by two recipes are merged into one line.

**No Delete permission** — mirrors the absent `wmsMealClosureDelete`. A posted closure is
reversed, which reverses its stock-out, which reverses its movements.

`[design]` **`actual_portions` is entered by the kitchen.** The screen *suggests* a number:
students marked present today (`JournalEntry` / attendance) who hold an active
`student_subscriptions` row in the `meals` fee category. It is a suggestion, always editable,
never auto-posted, and the suggested value is stored in `planned_portions` so the variance is
visible. Reason: attendance is finalised after lunch is served, and a kitchen that cooked 210
portions did cook 210 whatever the register says.

#### Meal cost dashboard

**No table.** `MealCostQuery` in `SchoolLms.Application/Warehouse/` reads posted closures and
their lines. Every figure comes from frozen `unit_cost` values, so a report run in March for
January returns exactly what it returned in January.

### 3.10 `wms_settings` — one row

Same singleton pattern as `BillingSettings` (`Billing.cs:389`), with a stable seeded id.

| Column | Type | Default | Notes |
|---|---|---|---|
| `id` | uuid | `…-0000000000c1` | `SingletonId` |
| `costing_method` | text | `'weighted_average'` | closed list of one today; see §12 Q2 |
| `negative_stock_policy` | text | `'block'` | `block` \| `warn` |
| `adjustment_approval_threshold` | numeric(14,2) | `1000000` | so'm; above it, dual control |
| `stock_in_creates_expense` | bool | `true` | §5 |
| `stock_in_expense_category` | text | `'supplies'` | must be in `Accounts.ExpenseCategories` |
| `default_kitchen_warehouse_id` | uuid | null | FK `wms_warehouses` |
| `book_loan_days` | int | `14` | default due date offset |
| `updated_by`, `updated_at` | | | |

### 3.11 Enum register — every value, one place

Each is a `public static class` in `SchoolLms.Domain/Warehouse.cs` with `const string`
members and an `All` list, plus a matching `HasCheckConstraint` in `WarehouseModel.cs`.
Every value marked `[design]` — the bundle contains no enum values for this module.

| Class | Values |
|---|---|
| `WarehouseKind` | `general`, `kitchen`, `library`, `maintenance`, `other` |
| `ProductUnit` | `pcs`, `kg`, `g`, `l`, `ml`, `pack`, `box`, `m`, `set` |
| `ProductKind` | `consumable`, `ingredient`, `asset`, `stationery`, `other` |
| `MovementDirection` | `in`, `out` |
| `MovementRefType` | `stock_in`, `stock_out`, `adjustment`, `meal_closure`, `transfer`, `opening`, `reversal` |
| `DocumentStatus` | `draft`, `confirmed`, `cancelled` |
| `StockOutReason` | `consumption`, `meal`, `transfer`, `asset_issue`, `write_off`, `loss`, `expiry`, `other` |
| `AdjustmentReason` | `stocktake`, `damage`, `expiry`, `theft`, `correction`, `opening` |
| `InventorySessionStatus` | `open`, `counting`, `completed`, `cancelled` |
| `PurchaseRequestStatus` | `draft`, `submitted`, `approved`, `rejected`, `cancelled`, `fulfilled` |
| `BudgetStatus` | `draft`, `active`, `closed` |
| `AssetCondition` | `new`, `good`, `worn`, `broken` |
| `AssetStatus` | `in_use`, `in_repair`, `written_off`, `transferred` |
| `BookCopyStatus` | `available`, `borrowed`, `reserved`, `lost`, `damaged`, `written_off` |
| `ReadingLogStatus` | `pending`, `approved`, `rejected` |
| `MealClosureStatus` | `draft`, `posted`, `reversed` |
| `MealType` | `breakfast`, `lunch`, `dinner` — **reuse `CanteenMenu.Meals`, do not redeclare** |

---

## 4. Costing, balances and valuation

### 4.1 The method — and what the bundle says about it

**The bundle reveals no costing method.** Searched and absent: `fifo`, `lifo`, `average`,
`avg_cost`, `batch`, `lot`, `layer`, `moving`, `wac`. The only pricing evidence is
`/wms/analytics/supplier-prices`, which tracks what each supplier charged over time and is
compatible with every method. **We are choosing, not copying.**

**Decision: weighted moving average, per (product, warehouse).**

Reasons, in order of weight:

1. No lot tracking is required. FIFO needs cost layers, layer consumption, partial layers and
   a layer table; every one of those is a place to be wrong, and a school kitchen gains
   nothing from knowing which sack of rice it opened.
2. It tolerates the data entry a school actually does — deliveries recorded a day late,
   quantities corrected — without rewriting history.
3. It has one number per product per warehouse, so the Mahsulotlar screen can show "value" in
   a column without a subquery per row.

FIFO is the honest alternative and it is a better fit for perishables. If the client asks for
it later, the movement journal designed here already contains everything FIFO needs (every
`in` movement is a layer with a quantity and a cost); only `StockLedgerService` changes. That
is why the method is a `wms_settings` column rather than a hard-coded constant.

### 4.2 The formulas — exact

For a product `p` in warehouse `w`, over movements ordered by `id`:

```
Quantity(p,w)  = Σ quantity[in]  − Σ quantity[out]
Value(p,w)     = Σ total_cost[in] − Σ total_cost[out]
AverageCost    = Quantity > 0 ? round(Value / Quantity, 4) : 0
```

Posting an **inbound** movement:

```
unit_cost  = the document line's unit cost              (given)
total_cost = round(quantity × unit_cost, 2)             (MidpointRounding.AwayFromZero)
```

Posting an **outbound** movement, inside the same transaction and after the advisory lock:

```
Q = Quantity(p,w) before this movement
V = Value(p,w)    before this movement

unit_cost  = Q > 0 ? round(V / Q, 4) : 0
total_cost = round(quantity × unit_cost, 2)

-- the closing rule:
if quantity == Q  then  total_cost = V      -- drain the remaining value exactly
```

**The closing rule is not optional.** Without it, four decimals of rounding drift accumulate
and `Value` never returns to zero when the shelf is empty, so the Mahsulotlar screen shows a
product with 0 kg and 37 so'm of value forever. With it, emptying the stock empties the value
to the tiyin.

`AverageCost` is rounded to 4 decimals for display only; the running `V` and `Q` are never
rounded, because they are sums of already-rounded stored values.

### 4.3 Why `total_cost` is a stored column and not `quantity * unit_cost`

Because `round(q × c, 2)` computed at read time and `round(q × c, 2)` computed at write time
diverge the moment either factor's precision changes, and because a report must be
reproducible. `LedgerEntry.Amount` is stored for the same reason. It is also what makes the
closing rule above expressible at all.

### 4.4 Back-dating is forbidden

A movement may not be posted with `occurred_on` **earlier than** the `occurred_on` of the
latest posted movement for the same `(product_id, warehouse_id)`. Confirming such a document
returns `400 backdated_movement`.

Reason: a moving average is path-dependent. Inserting a cheap delivery *before* three issues
that already consumed stock at the old average would require recomputing and rewriting those
three frozen costs — i.e. mutating history, which is the thing this whole design exists to
prevent.

The escape hatch is the one an accountant would use anyway: post the correction on today's
date as a `wms_stock_adjustments` row with `reason = 'correction'`, a mandatory note, and, if
it is material, a second approver.

### 4.5 Negative stock

Default policy `block`. A confirm that would drive `Quantity(p,w)` below zero fails with
`409 insufficient_stock`, naming the product, the warehouse, the requested quantity and the
available quantity.

Enforced **in the database**, not only in the service, because two concurrent confirms each
see the other's uncommitted rows as absent and both would pass an application check — the
exact bug `check_allocation_total()` in `billing_guards.sql` was written to stop. Same
technique, same file pattern:

```
-- warehouse_guards.sql, trigger on wms_stock_movements AFTER INSERT
PERFORM pg_advisory_xact_lock(
    hashtextextended('wms_stock:' || NEW.product_id::text || ':' || NEW.warehouse_id::text, 0));
-- then re-sum and RAISE EXCEPTION 'Insufficient stock' USING ERRCODE = 'check_violation'
-- when the policy is 'block' and the resulting quantity < 0
```

The lock key is a **prefixed string**, exactly as `CashShiftService.LockKey` explains: the
64-bit advisory space is global, and an unprefixed hash of a uuid could collide with the
payment-allocation lock and make two unrelated operations queue behind each other for reasons
nobody would ever diagnose.

With policy `warn` the trigger allows the insert and the service attaches a warning to the
response. `warn` exists for go-live, when opening balances are still being entered.

### 4.6 Opening balances

Loading what the school already has on day one is a `wms_stock_adjustments` row per product
with `direction = 'in'`, `reason = 'opening'`, an explicit `unit_cost`, and
`ref_type = 'opening'` on the resulting movement. Bulk import through the existing
`ExcelImport` service (`SchoolLms.Application/Services/ExcelImport.cs`), template shipped
alongside `shablon.xlsx`. `reason = 'opening'` is exempt from the dual-control threshold and
from the back-dating rule, and is only accepted while the warehouse has **no** prior
movements.

---

## 5. Money — how the warehouse touches the ledger

### 5.1 The rule

**Nothing in this module writes to `ledger_entries`.** SPEC §2.2: `LedgerService` is the only
code permitted to insert there. The warehouse reaches money through exactly one door:
`IExpenseService.CreateAsync` (`SchoolLms.Application/Billing/ExpenseService.cs`), which
already carries the approval threshold, the `approved_by <> created_by` constraint, the audit
entry and the ledger posting.

### 5.2 Purchases — expense at stock-in

On **confirming** a stock-in document, when `wms_settings.stock_in_creates_expense` is true:

```
IExpenseService.CreateAsync(new CreateExpenseRequest(
    OnDate:   stockIn.DocDate,
    Category: wms_settings.stock_in_expense_category,   -- default "supplies"
    Amount:   stockIn.TotalCost,
    Note:     $"Kirim {stockIn.DocNo} — {supplierName}",
    TeacherId: null,
    Method:   <chosen at confirm: cash | card | transfer …>),
  actorId)
```

The returned `expenses.id` is written to `wms_stock_in.expense_id` (FK RESTRICT, so the
expense cannot be deleted out from under it). Above the expense approval threshold the
expense is born `pending` and posts to the ledger only on approval — **the stock movements
post immediately either way**, because the goods are physically on the shelf whether or not
the director has signed for them. That divergence is intentional and must be visible on the
Kirim screen as a "tasdiq kutmoqda" badge.

Reversing a stock-in reverses the expense through `IExpenseService.ReverseAsync`. If the
expense is already reversed, the stock reversal still proceeds and records why.

`stock_in_creates_expense = false` exists for schools that enter purchases in the finance
module by hand and do not want two rows.

### 5.3 Consumption — a meal closure posts **no** expense

> **A meal closure must not create a second expense.** The money left the school when the
> rice was bought. Posting an expense again when the rice is cooked counts the same so'm
> twice, and the P&L would show a food cost roughly double the bank statement.

What a meal closure produces:

- `out` movements at the frozen weighted-average cost — the stock is consumed;
- `total_cost` and `cost_per_portion` on the closure — a **management** figure;
- rows for the meal cost dashboard.

Nothing else. `wms_meal_closures` has no `expense_id` column and no ledger reference, and
that absence is a design statement, not an omission.

The dashboard must label the figure so nobody reconciles it against the P&L by mistake:
*"Ovqat tannarxi — sarflangan mahsulot qiymati. Bu ko'rsatkich P&L dagi xarajat satri emas."*

### 5.4 The alternative we did not choose, and the exact price of changing our mind

True perpetual inventory ("Option B") would:

1. add one constant to `SchoolLms.Application/Billing/Accounts.cs`:
   `public const string AssetInventory = "asset:inventory";` plus one entry in `All`;
2. make stock-in debit `asset:inventory` instead of `expense:supplies`;
3. make every outbound movement post `debit expense:<category> / credit asset:inventory`;
4. require every adjustment and write-off to post a ledger pair as well.

Then the P&L food line would equal what was *eaten*, not what was *bought*, and the
dashboard and the P&L would agree.

**Recommendation: not now.** Reasons: it turns the warehouse into a financial module subject
to the whole of SPEC §4 (append-only guards, dual control, the P1-22 security suite) on day
one; it makes the ledger drift from the stock journal the first time a stocktake is skipped,
and reconciling those two is real ongoing work; and it delays the point at which the school
can use the storekeeping screens at all. Option A gives a correct cash picture immediately and
a correct cost picture on the dashboard.

**It stays cheap to change later precisely because every movement already stores a frozen
`total_cost`.** Switching is one new account, changes in one service, and a dated opening
entry for the inventory value on the switch date. This is §12 Q1.

### 5.5 Everything else

Room assets, book copies, book loans and reading logs post nothing. A written-off asset or a
lost book is a note in `wms_room_asset_events` / `wms_book_loans`; charging a family for a
lost book is a `fee_categories` / invoice question and belongs to Billing, not here.

---

## 6. API surface

### 6.1 Conventions

- Base: `/api/admin/wms`. Library: `/api/admin/library`. Meals: `/api/admin/meals`.
- Controllers in `SchoolLms.Server/Controllers/`, one per screen group (§10).
- Class-level `[Authorize]` + `[AdminPerm("<key>")]`; see §8.
- Actor always `FinanceActor.RequireUserId(User)`-style from JWT; `createdBy`/`approvedBy`
  in a request body → `400`.
- DTOs: `record` types in `SchoolLms.Application/Dtos/WarehouseDtos.cs` (new file). Mirror
  TypeScript in `schoollms.client/src/api/services/warehouse.ts` (+ `library.ts`, `meals.ts`).
- Errors: reuse the `BillingRuleException` shape — `{ code, message }` with an Uzbek message
  and a stable machine `code`. Codes used by this module are listed in §7.9.

### 6.2 Pagination — new to this project, and why

Every existing list endpoint returns a capped array with no envelope. That is fine for 24
classes. It is not fine for `wms_stock_movements`, which grows without limit.

**This module introduces one envelope, used by every list endpoint here and nowhere else
yet:**

```
GET …?page=1&pageSize=50&sort=-docDate&search=…&<filters>
→ 200 { "items": [...], "total": 1234, "page": 1, "pageSize": 50 }
```

`page` ≥ 1 (default 1), `pageSize` 1..200 (default 50, values above 200 clamp to 200),
`sort` is `field` or `-field` from an endpoint-specific allow-list (an unknown field →
`400 invalid_sort`, never a silent ignore). Date filters are `from`/`to` inclusive on the
document's business date.

### 6.3 Endpoints

`W` marks a write endpoint. The permission column is the `[AdminPerm]` key (§8); `staff` read
access follows the existing `AdminPermAttribute` rule (GET always allowed for staff, writes
gated).

#### Catalog — `WarehouseCatalogController`, `/api/admin/wms`

| # | Method | Path | Body / query | Response | Perm |
|---|---|---|---|---|---|
| 1 | GET | `/warehouses` | `search, kind, isActive`, paging | `Paged<WarehouseDto>` | `warehouse` |
| 2 | GET | `/warehouses/{id:guid}` | | `WarehouseDto` | `warehouse` |
| 3 W | POST | `/warehouses` | `{ code, name, kind, note }` | `WarehouseDto` 201 | `warehouse` |
| 4 W | PUT | `/warehouses/{id:guid}` | `{ name, kind, note, isActive }` (`code` immutable) | `WarehouseDto` | `warehouse` |
| 5 W | DELETE | `/warehouses/{id:guid}` | | 204, or `409 warehouse_in_use` | `warehouse` |
| 6 | GET | `/categories` | `search, parentId, isActive` | `CategoryDto[]` (tree, not paged) | `warehouse` |
| 7 W | POST | `/categories` | `{ name, parentId }` | `CategoryDto` 201 | `warehouse` |
| 8 W | PUT | `/categories/{id:guid}` | `{ name, parentId, isActive }` | `CategoryDto` | `warehouse` |
| 9 W | DELETE | `/categories/{id:guid}` | | 204, or `409 category_in_use` | `warehouse` |
| 10 | GET | `/suppliers` | `search, isActive`, paging | `Paged<SupplierDto>` | `warehouse` |
| 11 W | POST | `/suppliers` | `{ name, tin, phone, contactPerson, address, note }` | `SupplierDto` 201 | `warehouse` |
| 12 W | PUT | `/suppliers/{id:guid}` | same + `isActive` | `SupplierDto` | `warehouse` |
| 13 W | DELETE | `/suppliers/{id:guid}` | | 204, or `409 supplier_in_use` | `warehouse` |
| 14 | GET | `/suppliers/{id:guid}/prices` | `productId?, from, to` | `SupplierPriceRow[]` | `warehouse` |
| 15 | GET | `/products` | `search, categoryId, warehouseId, kind, isActive, belowReorderPoint, hasStock`, paging | `Paged<ProductDto>` — **includes balances, one `ForManyAsync` call** | `warehouse` |
| 16 | GET | `/products/{id:guid}` | | `ProductDetailDto` (+ per-warehouse balances) | `warehouse` |
| 17 | GET | `/products/by-barcode/{barcode}` | | `ProductDto`, or `404 product_not_found` | `warehouse` |
| 18 W | POST | `/products` | `{ sku?, barcode?, name, categoryId, unit, kind, reorderPoint, shelfLifeDays?, defaultSupplierId?, note? }` | `ProductDto` 201 | `warehouse` |
| 19 W | PUT | `/products/{id:guid}` | same, **`unit` rejected once a movement exists** (`409 unit_locked`) | `ProductDto` | `warehouse` |
| 20 W | DELETE | `/products/{id:guid}` | | 204, or `409 product_in_use` | `warehouse` |
| 21 | GET | `/products/{id:guid}/movements` | `warehouseId, from, to`, paging | `Paged<MovementDto>` | `warehouse` |
| 22 | GET | `/settings` | | `WmsSettingsDto` | `warehouse` |
| 23 W | PUT | `/settings` | full `WmsSettingsDto` | `WmsSettingsDto` | **`superadmin` role only** |

#### Stock operations — `StockController`, `/api/admin/wms`

| # | Method | Path | Body / query | Response | Perm |
|---|---|---|---|---|---|
| 24 | GET | `/stock-ins` | `warehouseId, supplierId, status, from, to, search`, paging | `Paged<StockInDto>` | `warehouse` |
| 25 | GET | `/stock-ins/{id:guid}` | | `StockInDetailDto` (with lines) | `warehouse` |
| 26 W | POST | `/stock-ins` | `{ warehouseId, supplierId?, docDate, supplierInvoiceNo?, note?, lines[] }` | `StockInDetailDto` 201, status `draft` | `warehouse` |
| 27 W | PUT | `/stock-ins/{id:guid}` | same; **`409 document_confirmed`** if not draft | `StockInDetailDto` | `warehouse` |
| 28 W | DELETE | `/stock-ins/{id:guid}` | draft only | 204 / `409 document_confirmed` | `warehouse` |
| 29 W | POST | `/stock-ins/{id:guid}/confirm` | `{ paymentMethod }` | `StockInDetailDto` | `warehouse` |
| 30 W | POST | `/stock-ins/{id:guid}/reverse` | `{ reason }` | `StockInDetailDto` | `warehouse` |
| 31 W | POST | `/stock-ins/from-purchase-request/{prId:guid}` | `{ warehouseId, supplierId?, docDate }` | `StockInDetailDto` 201 draft | `warehouse` |
| 32 | GET | `/stock-outs` | `warehouseId, reason, departmentId, roomId, status, from, to`, paging | `Paged<StockOutDto>` | `warehouse` |
| 33 | GET | `/stock-outs/{id:guid}` | | `StockOutDetailDto` | `warehouse` |
| 34 W | POST | `/stock-outs` | `{ warehouseId, reason, departmentId?, roomId?, targetWarehouseId?, issuedToTeacherId?, docDate, note?, lines[] }` (line = `{ productId, quantity, note? }` — **no cost from the client**) | `StockOutDetailDto` 201 draft | `warehouse` |
| 35 W | PUT | `/stock-outs/{id:guid}` | draft only | `StockOutDetailDto` | `warehouse` |
| 36 W | POST | `/stock-outs/{id:guid}/discard` | draft only — **no DELETE verb on this resource** | 204 | `warehouse` |
| 37 W | POST | `/stock-outs/{id:guid}/confirm` | | `StockOutDetailDto` | `warehouse` |
| 38 W | POST | `/stock-outs/{id:guid}/reverse` | `{ reason }` | `StockOutDetailDto` | `warehouse` |
| 39 | GET | `/adjustments` | `warehouseId, productId, reason, status, from, to`, paging | `Paged<AdjustmentDto>` | `warehouse` |
| 40 W | POST | `/adjustments` | `{ warehouseId, productId, direction, quantity, unitCost?, reason, note?, docDate }` | `AdjustmentDto` 201 | `warehouse` |
| 41 W | POST | `/adjustments/{id:guid}/approve` | | `AdjustmentDto` — `403 self_approval` if approver == creator | `warehouse` + **director** |
| 42 W | POST | `/adjustments/{id:guid}/confirm` | | `AdjustmentDto` | `warehouse` |
| 43 W | POST | `/adjustments/{id:guid}/reverse` | `{ reason }` | `AdjustmentDto` | `warehouse` |
| 44 | GET | `/inventory-sessions` | `warehouseId, status`, paging | `Paged<InventorySessionDto>` | `warehouse` |
| 45 | GET | `/inventory-sessions/{id:guid}` | | `InventorySessionDetailDto` | `warehouse` |
| 46 W | POST | `/inventory-sessions` | `{ warehouseId, note?, categoryIds?[] }` — snapshots expectations | `InventorySessionDetailDto` 201 | `warehouse` |
| 47 | GET | `/inventory-sessions/{id:guid}/items` | `counted`(bool), `varianceOnly`(bool), paging | `Paged<InventoryItemDto>` | `warehouse` |
| 48 W | PUT | `/inventory-sessions/{id:guid}/items/{itemId:guid}` | `{ countedQty, note? }` | `InventoryItemDto` | `warehouse` |
| 49 W | POST | `/inventory-sessions/{id:guid}/items/bulk` | `{ items: [{ productId, countedQty }] }` — for the barcode loop | `InventoryItemDto[]` | `warehouse` |
| 50 W | POST | `/inventory-sessions/{id:guid}/complete` | | `InventorySessionDetailDto` + created adjustment ids | `warehouse` + **director** |
| 51 W | POST | `/inventory-sessions/{id:guid}/cancel` | `{ reason }` | `InventorySessionDetailDto` | `warehouse` |

#### Procurement — `ProcurementController`, `/api/admin/wms`

| # | Method | Path | Body / query | Response | Perm |
|---|---|---|---|---|---|
| 52 | GET | `/purchase-requests` | `status, requestedBy, departmentId, budgetId, from, to`, paging | `Paged<PurchaseRequestDto>` | `procurement` |
| 53 | GET | `/purchase-requests/{id:guid}` | | `PurchaseRequestDetailDto` | `procurement` |
| 54 W | POST | `/purchase-requests` | `{ warehouseId, departmentId?, neededBy?, budgetId?, note?, lines[] }` | 201 draft | `procurement` |
| 55 W | PUT | `/purchase-requests/{id:guid}` | draft only | `…DetailDto` | `procurement` |
| 56 W | DELETE | `/purchase-requests/{id:guid}` | draft only | 204 | `procurement` |
| 57 W | POST | `/purchase-requests/{id:guid}/submit` | | `…DetailDto` | `procurement` |
| 58 W | POST | `/purchase-requests/{id:guid}/approve` | `{ note? }` | `…DetailDto`; `403 self_approval`; `409 budget_exceeded` when hard-limited | `procurement` + **director** |
| 59 W | POST | `/purchase-requests/{id:guid}/reject` | `{ reason }` (required) | `…DetailDto` | `procurement` + **director** |
| 60 W | POST | `/purchase-requests/{id:guid}/cancel` | `{ reason }` | `…DetailDto` | `procurement` |
| 61 | GET | `/budgets` | `status, warehouseId, categoryId, departmentId, activeOn` | `BudgetDto[]` (with derived consumption) | `procurement` |
| 62 W | POST | `/budgets` | `{ name, periodStart, periodEnd, warehouseId?, categoryId?, departmentId?, amount, isHardLimit, note? }` | 201 | `procurement` |
| 63 W | PUT | `/budgets/{id:guid}` | same + `status` | `BudgetDto` | `procurement` |
| 64 W | DELETE | `/budgets/{id:guid}` | | 204, `409 budget_in_use` if any PR references it | `procurement` |

#### Room assets — `RoomAssetsController`, `/api/admin/wms`

| # | Method | Path | Body / query | Response | Perm |
|---|---|---|---|---|---|
| 65 | GET | `/room-assets` | `roomId, status, condition, productId, responsibleTeacherId, search`, paging | `Paged<RoomAssetDto>` | `assets` |
| 66 | GET | `/room-assets/summary` | `buildingId?` | `RoomAssetSummaryRow[]` (per room: count, value) | `assets` |
| 67 | GET | `/room-assets/kpi` | | `{ totalCount, totalValue, inRepair, broken, writtenOffValueThisYear }` | `assets` |
| 68 W | POST | `/room-assets` | `{ roomId, productId?, name, quantity, unit, unitCost, acquiredOn, condition, inventoryNo?, responsibleTeacherId?, note? }` | 201 | `assets` |
| 69 W | PUT | `/room-assets/{id:guid}` | same | `RoomAssetDto` | `assets` |
| 70 W | POST | `/room-assets/{id:guid}/move` | `{ roomId, reason }` | `RoomAssetDto` | `assets` |
| 71 W | POST | `/room-assets/{id:guid}/condition` | `{ condition, reason }` | `RoomAssetDto` | `assets` |
| 72 W | POST | `/room-assets/{id:guid}/write-off` | `{ reason }` (required) | `RoomAssetDto` | `assets` + **director** |
| 73 | GET | `/room-assets/{id:guid}/events` | | `RoomAssetEventDto[]` | `assets` |

#### Library — `LibraryController`, `/api/admin/library`

| # | Method | Path | Body / query | Response | Perm |
|---|---|---|---|---|---|
| 74 | GET | `/books` | `search, categoryId, subjectId, language`, paging | `Paged<BookDto>` (+ copiesTotal, copiesAvailable) | `library` |
| 75 W | POST | `/books` | `{ title, author?, isbn?, publisher?, publishedYear?, language, categoryId?, subjectId?, coverUrl?, note? }` | 201 | `library` |
| 76 W | PUT | `/books/{id:guid}` | same | `BookDto` | `library` |
| 77 W | DELETE | `/books/{id:guid}` | | 204, `409 book_has_copies` | `library` |
| 78 | GET | `/copies` | `bookId, status, warehouseId, borrowedByStudentId, borrowedByTeacherId, overdue`(bool), `search`, paging | `Paged<BookCopyDto>` | `library` |
| 79 W | POST | `/copies` | `{ bookId, count, warehouseId?, shelf?, acquiredOn, unitCost, inventoryNoPrefix? }` — creates `count` copies | `BookCopyDto[]` 201 | `library` |
| 80 W | PUT | `/copies/{id:guid}` | `{ shelf?, warehouseId?, note? }` | `BookCopyDto` | `library` |
| 81 W | POST | `/copies/{id:guid}/borrow` | `{ studentId? , teacherId?, dueOn? }` (exactly one borrower; `dueOn` defaults to `book_loan_days`) | `BookCopyDto` | `library` |
| 82 W | POST | `/copies/{id:guid}/return` | `{ conditionOnReturn, note? }` | `BookCopyDto` | `library` |
| 83 W | POST | `/copies/{id:guid}/mark-status` | `{ status, reason }` — `lost` \| `damaged` \| `written_off` \| `available` | `BookCopyDto` | `library` |
| 84 W | DELETE | `/copies/{id:guid}` | only when never borrowed | 204 / `409 copy_has_history` | `library` |
| 85 | GET | `/loans` | `copyId, studentId, teacherId, open`(bool), `overdue`(bool), paging | `Paged<BookLoanDto>` | `library` |
| 86 | GET | `/reading-log` | `status, studentId, classId, from, to`, paging | `Paged<ReadingLogDto>` | `library` |
| 87 W | POST | `/reading-log` | `{ studentId, bookId?, title, author?, pages?, startedOn?, finishedOn, summary? }` | 201 `pending` | `library` |
| 88 W | POST | `/reading-log/{id:guid}/approve` | | `ReadingLogDto` | `library` |
| 89 W | POST | `/reading-log/{id:guid}/reject` | `{ reason }` (required) | `ReadingLogDto` | `library` |
| 90 W | POST | `/reading-log/bulk-approve` | `{ ids: [] }` (max 200) | `{ approved, skipped }` | `library` |

#### Meals — `MealCostController`, `/api/admin/meals`

| # | Method | Path | Body / query | Response | Perm |
|---|---|---|---|---|---|
| 91 | GET | `/recipes` | `search, meal, isActive`, paging | `Paged<RecipeDto>` (+ costPerPortion) | `meals` |
| 92 | GET | `/recipes/{id:guid}` | | `RecipeDetailDto` | `meals` |
| 93 W | POST | `/recipes` | `{ name, meal, yieldPortions, dishId?, instructions?, lines[] }` | 201 | `meals` |
| 94 W | PUT | `/recipes/{id:guid}` | same | `RecipeDetailDto` | `meals` |
| 95 W | DELETE | `/recipes/{id:guid}` | | 204, `409 recipe_in_use` if any posted closure references it | `meals` |
| 96 | GET | `/closures` | `from, to, meal, status`, paging | `Paged<MealClosureDto>` | `meals` |
| 97 | GET | `/closures/{id:guid}` | | `MealClosureDetailDto` | `meals` |
| 98 | GET | `/closures/plan` | `date, meal` — reads `dishes` + linked recipes, suggests portions and quantities | `MealClosurePlanDto` (nothing written) | `meals` |
| 99 W | POST | `/closures` | `{ onDate, meal, warehouseId, plannedPortions, actualPortions, note?, lines[] }` | 201 draft | `meals` |
| 100 W | PUT | `/closures/{id:guid}` | draft only | `MealClosureDetailDto` | `meals` |
| 101 W | POST | `/closures/{id:guid}/post` | | `MealClosureDetailDto` + created `stockOutId` | `meals` |
| 102 W | POST | `/closures/{id:guid}/reverse` | `{ reason }` — **no DELETE verb** | `MealClosureDetailDto` | `meals` |
| 103 | GET | `/cost-dashboard` | `from, to, meal?, warehouseId?` | `MealCostDashboardDto` (cards + series) | `meals` |

#### Reports — `WarehouseReportsController`, `/api/admin/wms/reports`

| # | Method | Path | Query | Response | Perm |
|---|---|---|---|---|---|
| 104 | GET | `/stock-movements` | `productId?, warehouseId?, refType?, from, to`, paging | `Paged<MovementRowDto>` | `warehouse` |
| 105 | GET | `/cost-by-department` | `from, to, departmentId?` | `CostByDepartmentRow[]` | `warehouse` |
| 106 | GET | `/loss` | `from, to, warehouseId?` | `LossRow[]` (write-off, loss, expiry, theft) | `warehouse` |
| 107 | GET | `/supplier-prices` | `productId?, supplierId?, from, to` | `SupplierPriceRow[]` | `warehouse` |
| 108 | GET | `/valuation` | `warehouseId?, asOf?` | `{ rows: [...], totalValue }` — stock value at a date | `warehouse` |
| 109 | GET | `/low-stock` | `warehouseId?` | `LowStockRow[]` | `warehouse` |

**109 endpoints.** That is the honest size of this module.

---

## 7. Business rules

### 7.1 Document lifecycle (stock-in, stock-out, adjustment)

```
draft ──edit/delete──▶ draft
draft ──confirm──────▶ confirmed        (emits movements, atomically)
draft ──discard──────▶ cancelled        (stock-out only; stock-in uses DELETE)
confirmed ──reverse──▶ confirmed        (a NEW mirror document, original untouched)
```

Rules that hold for all three:
- Confirm is idempotent per document: a second confirm returns `409 already_confirmed`.
- Confirm, the movement inserts, the `total_cost` write and (for stock-in) the expense call
  happen in **one** `SaveChangesAsync` / transaction. Half a confirm is not a state.
- A confirmed document may never be edited or deleted, by anyone, including the director.
- `doc_date` may not be in the future (`400 future_date` — the same wording and rule as
  `ExpenseService.CreateAsync`).
- Every confirm and every reverse writes an `audit_logs` row via `AuditService`, with
  `before`/`after` snapshots. Add the entity-kind constants to
  `SchoolLms.Application/Services/AuditService.cs` in the existing PascalCase style
  (`EntityWmsStockIn = "WmsStockIn"`, `EntityWmsStockOut`, `EntityWmsAdjustment`,
  `EntityWmsInventorySession`, `EntityWmsMealClosure`, `EntityWmsRoomAsset`,
  `EntityWmsBookCopy`) — the audit screen filters on that string, so it must match the
  existing shape (`EntityExpense = "Expense"`), not the table name.

### 7.2 Document numbering

Format `PREFIX-YYYY-NNNNNN`, gapless per prefix per year:

| Document | Prefix |
|---|---|
| stock-in | `KIR` |
| stock-out | `CHQ` |
| adjustment | `TUZ` |
| inventory session | `INV` |
| purchase request | `XAR` |
| meal closure | `OVQ` |

Allocated at **confirm**, not at draft creation — a discarded draft must not burn a number.
Implementation copies `CashShiftService.NextReceiptNoAsync` exactly: a transaction-scoped
advisory lock on a **prefixed string key** (`wms_docno:KIR:2026`), then `max + 1`, backed by
a unique index on `doc_no` so a race that beats the lock still fails loudly rather than
producing a duplicate.

### 7.3 What confirm emits

| Document | Movements |
|---|---|
| stock-in | one `in` per line, `ref_type='stock_in'` |
| stock-out, reason ≠ `transfer` | one `out` per line, `ref_type='stock_out'` |
| stock-out, reason = `transfer` | one `out` from source **and** one `in` into `target_warehouse_id` per line, same `unit_cost`, `ref_type='transfer'` |
| adjustment | one movement in `direction`, `ref_type='adjustment'` |
| meal closure post | creates a `wms_stock_out` (reason `meal`) and confirms it; movements carry `ref_type='meal_closure'` and `ref_id` = the **closure** id, `ref_line_id` = the closure line id |
| reverse (any) | one mirror movement per original, `reversal_of` set, same `unit_cost` |

**A reversal reuses the original `unit_cost`**, it does not recompute it at today's average.
Recomputing would move value into or out of the warehouse from nowhere.

### 7.4 Stock-out validation, in order

1. Warehouse active, every product active.
2. Every line quantity > 0; no duplicate `product_id`.
3. Reason → destination pairing satisfied (§3.5); `note` present where required.
4. `transfer`: `target_warehouse_id` present, active, and ≠ source.
5. Not back-dated (§4.4).
6. **Then, inside the transaction, under the advisory lock:** sufficient stock per line
   (§4.5). This one is last and inside the lock because it is the only check whose answer can
   change between validation and write.

### 7.5 Stocktake lifecycle

```
open ──(first count entered)──▶ counting ──complete──▶ completed
  └──────────────── cancel ────────────────────────▶ cancelled
```

- Opening snapshots `expected_qty` and `expected_unit_cost` for every active product in the
  warehouse (optionally filtered to `categoryIds`). Products with a zero balance are
  **included** — finding stock that the system says does not exist is half the point.
- `counted_qty` may be entered, re-entered and cleared while `open`/`counting`.
- `complete` requires every item to have a `counted_qty` (`409 uncounted_items`, listing how
  many) and is permitted to the director only.
- `complete` creates one `wms_stock_adjustments` row per non-zero `variance_qty`, with
  `reason='stocktake'`, `inventory_session_id` set, and confirms them in the same
  transaction. Those adjustments skip the dual-control threshold — completing the session is
  itself the director's signature.
- `variance_value` is written on the session: `Σ variance_qty × expected_unit_cost`.
- `cancel` is allowed from `open` or `counting`, requires a reason, emits nothing.
- `completed` and `cancelled` are terminal.

### 7.6 Purchase request state machine

```
draft ──submit──▶ submitted ──approve──▶ approved ──(stock-in confirmed)──▶ fulfilled
  │                   │                     │
  │                   ├──reject───▶ rejected (terminal)
  │                   └──cancel───▶ cancelled (terminal)
  └──delete (draft only)
approved ──cancel──▶ cancelled   (allowed while nothing has been received)
```

- Only the requester may `submit`; only the director may `approve`/`reject`
  (`ck_wms_pr_approver_differs` in the database as the backstop).
- `reject` requires a reason; the reason is shown to the requester.
- `approve` computes budget consumption; over a soft budget it succeeds with a warning field
  in the response, over a hard budget it fails `409 budget_exceeded` with the remaining
  amount in the message.
- `fulfilled` is set automatically when a stock-in created from the request is confirmed and
  every line's cumulative received quantity ≥ the requested quantity. Partial receipt leaves
  it `approved`.
- Editing is only possible in `draft`.

### 7.7 Library rules

- `borrow` requires `status = 'available'` (`409 copy_not_available`), exactly one borrower,
  and sets `status='borrowed'`, `borrowed_at`, `due_on` (default `today + book_loan_days`),
  plus one `wms_book_loans` row.
- `return` requires `status = 'borrowed'`, closes the open loan (`returned_at`,
  `returned_to`, `condition_on_return`), and sets the copy status to `available`, `damaged`
  or `lost` according to `condition_on_return`.
- `mark-status` moves a copy to `lost`/`damaged`/`written_off` without a return, and back to
  `available` from `damaged`; a reason is mandatory and lands in `note` and the audit log.
- A student with `n` overdue copies may not borrow another. `[design]` `n = 1`; the limit is
  a constant in the service, not a setting, until someone asks.
- Deleting a copy is only possible when it has never been borrowed.

### 7.8 Reading log rules

- `finished_on` may not be in the future; `started_on ≤ finished_on` when both are present.
- Only `pending` entries may be approved or rejected; rejection requires a reason.
- Approve/reject sets `reviewed_by` from the JWT and `reviewed_at`.
- A rejected entry is not editable; the student creates a new one.
- Bulk approve skips anything not `pending` and reports the count skipped rather than failing.

### 7.9 Error codes

| Code | HTTP | Meaning |
|---|---|---|
| `insufficient_stock` | 409 | confirm would drive a balance negative |
| `backdated_movement` | 400 | `docDate` precedes the last movement for that product/warehouse |
| `document_confirmed` | 409 | edit/delete attempted on a confirmed document |
| `already_confirmed` | 409 | second confirm |
| `future_date` | 400 | `docDate` in the future |
| `unit_locked` | 409 | product unit change after the first movement |
| `self_approval` | 403 | approver equals creator |
| `approval_required` | 409 | confirm above threshold without an approver |
| `budget_exceeded` | 409 | hard-limited budget would be exceeded |
| `uncounted_items` | 409 | stocktake completion with unentered counts |
| `session_already_open` | 409 | second open session for a warehouse |
| `copy_not_available` | 409 | borrow attempted on a non-available copy |
| `borrower_has_overdue` | 409 | borrow blocked by an overdue copy |
| `product_in_use`, `category_in_use`, `supplier_in_use`, `warehouse_in_use`, `budget_in_use`, `recipe_in_use`, `book_has_copies`, `copy_has_history` | 409 | delete blocked by a reference |
| `invalid_sort` | 400 | sort field outside the allow-list |

---

## 8. RBAC

`AdminPermAttribute` (`SchoolLms.Server/Controllers/AdminPermAttribute.cs`) is the gate:
admin/superadmin pass unconditionally; `staff` may always GET and may write only with the
matching `perm` claim; every other role is refused. Claims are loaded from the database on
every request, so a permission change takes effect without re-login.

**Four new permission keys**, added to `schoollms.client/src/config/constants.ts`
(`adminPermissions`) and used by `[AdminPerm(...)]`, `navigation.ts` and `RequirePerm`:

| Key | Uzbek label | Covers |
|---|---|---|
| `warehouse` | Ombor | warehouses, categories, suppliers, products, stock-in, stock-out, adjustments, stocktake, reports |
| `procurement` | Xarid va byudjet | purchase requests, budgets |
| `assets` | Xona jihozlari | room assets |
| `library` | Kutubxona | books, copies, loans, reading log |
| `meals` | Oshxona tannarxi | recipes, meal closures, cost dashboard |

(Five keys. `meals` is deliberately separate from the existing `app` permission that guards
the Oshxona **menu** screen — a cook may edit the menu without seeing costs.)

**Why five keys and not sixteen.** EduSchool has 44 WMS permissions because it is a
multi-tenant product that must satisfy every school's org chart. One school has a
storekeeper, a librarian and a cook. Sixteen checkboxes that are always ticked together are
worse than five, and the split is along the lines of the three real jobs plus purchasing plus
assets. If the client wants finer control later, splitting a key is additive.

**Three actions require the director (`superadmin`), not just the permission.** They are
enforced with `[Authorize(Roles = Roles.SuperAdmin)]` on the method, in addition to the
class-level `[AdminPerm]`:

| Action | Endpoint | Why |
|---|---|---|
| Approve an adjustment | #41 | value created from nothing, no counterparty |
| Complete a stocktake | #50 | it emits adjustments in bulk |
| Approve / reject a purchase request | #58, #59 | money about to be spent |
| Write off an asset | #72 | value destroyed |
| Change WMS settings | #23 | the threshold and the negative-stock policy are the controls |

The database backstops the first three with `approved_by <> created_by` check constraints;
the attribute is convenience, the constraint is the guarantee. This is `docs/SPEC.md` §4.5,
applied.

---

## 9. What we already have

Exact files. Reuse these; do not reimplement them.

| Need | Already exists | Note |
|---|---|---|
| Append-only journal + derived balance pattern | `SchoolLms.Application/Billing/StudentBalanceQuery.cs`, `SchoolLms.Domain/Billing.cs` (`LedgerEntry`) | **The reference implementation for `StockBalanceQuery`.** Copy its shape, its `ForManyAsync` bulk method and its file-header reasoning. |
| Append-only enforcement | `SchoolLms.Infrastructure/Migrations/Sql/billing_guards.sql` | The template for `warehouse_guards.sql`, including the `app_rw`-missing guard. |
| Cross-row invariant in the database | `check_allocation_total()` in the same file | The template for the negative-stock trigger, including why `SELECT … FOR UPDATE` cannot be used under `app_rw` and an advisory lock must be. |
| Gapless numbering | `SchoolLms.Application/Billing/CashShiftService.cs` (`NextReceiptNoAsync`, `LockKey`) | The template for `doc_no`, including the prefixed-lock-key reasoning. |
| Dual control | `ck_expenses_approver_differs`, `ck_discounts_approver_differs`; `FinanceRoleAttribute.cs` | The template for adjustment and purchase-request approval. |
| Expense creation and ledger posting | `SchoolLms.Application/Billing/ExpenseService.cs`, `IExpenseService` | **The only door from the warehouse to money** (§5). |
| Chart of accounts | `SchoolLms.Application/Billing/Accounts.cs` | `ExpenseCategories` is a closed list; `supplies` already exists. Option B (§5.4) would add one constant here. |
| Server-derived actor | `FinanceActor.RequireUserId(User)`, `AdminPermAttribute` | SPEC §4.4. |
| Audit trail | `SchoolLms.Application/Services/AuditService.cs`, `audit_logs` with `before`/`after` jsonb | Add `wms_*` entity kind constants; do not create a second audit table. |
| Canteen menu | `SchoolLms.Server/Controllers/CanteenController.cs`, `SchoolLms.Application/Services/CanteenMenu.cs`, `Dish` (`Entities.cs:248`) | Menu list only: date, meal, name, free-text ingredients, image. **Untouched by this module** except that `wms_recipes.dish_id` points at it. `CanteenMenu.Meals` is the single source of the three meal values. |
| Meal fee category | `FeeCategory` code `meals`, `Accounts.RevenueMeals` | The revenue side of meals already exists — this module adds the cost side. |
| Excel import / export | `SchoolLms.Application/Services/ExcelImport.cs`, `ExcelExport.cs`, template `shablon.xlsx` | Opening balances (§4.6) and every list export. |
| Phone normalisation | `SchoolLms.Application/Services/PhoneUtil.cs` | Supplier phone. |
| Clock | `SchoolLms.Domain/AppClock.cs` | `NowInstant`, `Today`. |
| Frontend page pattern | `schoollms.client/src/pages/admin/billing/` — `ExpensesPage.tsx`, `BillingUi.tsx`, `access.ts` | **The reference implementation for every WMS screen**: guard component, permission-aware buttons, `isEndpointMissing` handling, modal form, pending queue. |
| Frontend money formatting | `schoollms.client/src/lib/utils.ts` (`formatMoney`) | |
| Route guard | `schoollms.client/src/components/auth/RequirePerm.tsx` | |

**What we do not have and this module needs:**

| Missing | Consequence |
|---|---|
| `rooms` table | §3.2 — a shared, sequential prerequisite for room assets. |
| `departments` table | `cost-by-department` and purchase-request routing. EduSchool has `/department`; we have nothing. See §12 Q4. |
| A pagination envelope | §6.2 — introduced by this module. |
| A `storekeeper` idea | Handled by the `warehouse` permission on the existing `staff` role. No new role. |

---

## 10. Build plan

### 10.1 Phases, and which one is worth shipping alone

| Phase | Name | Deliverable | Usable alone? |
|---|---|---|---|
| **W1** | Warehouse core | catalog + stock ledger + kirim/chiqim/tuzatish/inventarizatsiya + products screen + reports | **Yes — this is the phase to ship first.** The school can run a real store: know what it has, what it is worth, what it bought and what it lost. Everything else in this module is an extension of it. |
| **W2** | Procurement | purchase requests + budgets | Only on top of W1. Valuable to the director, not to the storekeeper. |
| **W3** | Room assets | `rooms` + assets + KPI | Yes, independently of W2 — it is a small, self-contained register. |
| **W4** | Library | books, copies, loans, reading log | **Yes, fully independent of W1.** It shares only the menu group and the category table. A school could take W4 and nothing else. |
| **W5** | Meal costing | recipes + closures + cost dashboard | **No.** It cannot exist without W1: a closure consumes stock. This is the phase that answers the client's most interesting question and the one that must wait. |

**Recommended order: W1 → W4 → W5 → W2 → W3.** W4 goes second because it is independent,
small, and gives a visible win while W1 settles. W5 goes third because it is the reason the
client noticed this module. W2 and W3 are administrative polish.

### 10.2 What is probably not worth building for one school

Stated plainly, as asked:

- **`/wms/analytics/cost-by-department`** — worth it only if `departments` exists (§12 Q4).
  For a school with one kitchen and one store, "cost by department" is "cost", and the
  report is a column with one row. **Recommendation: build the endpoint, hide the screen tab
  until there are ≥ 3 departments with stock-out activity.**
- **Budgets with hard limits** — a single school's director approves purchases personally.
  The budget screen is mostly a report. **Recommendation: build it (it is one table and one
  query), default every budget to soft.**
- **Warehouse transfers** — a school with one store and one kitchen store transfers rarely.
  It costs almost nothing (one extra movement pair) so it stays, but it does not need its own
  screen: it is a stock-out reason.
- **Reserved book status** — a school library with no queue does not reserve. The enum value
  stays for completeness; no UI.
- **Barcode label printing** — explicitly out of scope. Reading barcodes is cheap and the
  scanner behaviour is already specified; printing them needs label stock, a printer driver
  and a layout editor. **Recommendation: skip until the client asks and owns a label
  printer.**
- **`/wms/inventory-item` as a public resource** — EduSchool exposes it; we fold it under the
  session (#47–#49). One less controller, no functionality lost.

### 10.3 Work units

`[P]` parallelizable · `[S]` sequential · `★` on the critical path.
Effort is one developer's hours for that unit, backend unless the name says frontend.

#### Phase W1 — Warehouse core

| Id | Unit | Files it touches | Depends on | `[P]`/`[S]` | h |
|---|---|---|---|---|---|
| W-01 ★ | Entities + enums | `SchoolLms.Domain/Warehouse.cs` (new) | — | `[S]` | 10 |
| W-02 ★ | EF config + migration `WarehouseCore` + `warehouse_guards.sql` + settings seed | `SchoolLms.Infrastructure/Data/WarehouseModel.cs` (new), `AppDbContext.cs` (1 line), `Migrations/*`, `Migrations/Sql/warehouse_guards.sql` (new), `.csproj` (embedded resource) | W-01 | `[S]` | 14 |
| W-03 ★ | Frozen contracts: DTOs, TS types, API client stubs, service interfaces | `SchoolLms.Application/Dtos/WarehouseDtos.cs` (new), `schoollms.client/src/api/services/warehouse.ts` (new), `SchoolLms.Application/Warehouse/IWarehouseServices.cs` (new) | W-01 | `[S]` | 8 |
| W-04 ★ | `StockLedgerService` — the only writer of `wms_stock_movements`; costing, closing rule, back-date guard | `SchoolLms.Application/Warehouse/StockLedgerService.cs` (new) | W-02, W-03 | `[S]` | 16 |
| W-05 | `StockBalanceQuery` + `WarehouseValuationQuery` | `SchoolLms.Application/Warehouse/StockBalanceQuery.cs`, `…/ValuationQuery.cs` | W-04 | `[P]` | 8 |
| W-06 | Catalog service + `WarehouseCatalogController` (#1–#23) | `…/Warehouse/CatalogService.cs`, `SchoolLms.Server/Controllers/WarehouseCatalogController.cs` | W-03 | `[P]` | 16 |
| W-07 | Stock-in service + endpoints (#24–#31), incl. the expense call | `…/Warehouse/StockInService.cs`, `SchoolLms.Server/Controllers/StockController.cs` | W-04 | `[P]` | 16 |
| W-08 | Stock-out service + endpoints (#32–#38) | `…/Warehouse/StockOutService.cs` (+ `StockController.cs`) | W-04 | `[P]` | 14 |
| W-09 | Adjustments + stocktake sessions (#39–#51) | `…/Warehouse/AdjustmentService.cs`, `…/InventorySessionService.cs` | W-04 | `[P]` | 18 |
| W-10 | Reports (#104–#109) | `…/Warehouse/WarehouseReportQueries.cs`, `SchoolLms.Server/Controllers/WarehouseReportsController.cs` | W-05 | `[P]` | 12 |
| W-11 | Opening-balance Excel import | `…/Warehouse/OpeningBalanceImport.cs`, template file | W-09 | `[P]` | 8 |
| W-12 | frontend: `BarcodeInput` component (HID burst + camera) | `schoollms.client/src/components/ui/BarcodeInput.tsx` (new) | W-03 | `[P]` | 10 |
| W-13 | frontend: catalog screens (1–4) | `schoollms.client/src/pages/admin/ombor/*` | W-03, W-12 | `[P]` | 24 |
| W-14 | frontend: kirim + chiqim (5, 6) | same tree | W-03, W-12 | `[P]` | 24 |
| W-15 | frontend: tuzatish + inventarizatsiya (7, 8, 8b) | same tree | W-03, W-12 | `[P]` | 20 |
| W-16 | frontend: hisobotlar (17) | same tree | W-03 | `[P]` | 12 |
| W-17 ★ | **Wiring**: `Program.cs` DI, `App.tsx` routes, `navigation.ts`, `constants.ts` perms | shared files — see §10.4 | W-06…W-16 | `[S]` | 8 |
| W-18 | tests: costing arithmetic, closing rule, back-date, negative stock, concurrency | `SchoolLms.Tests/Warehouse/*` | W-17 | `[P]` | 16 |
| W-19 | tests: RBAC matrix for every endpoint + append-only REVOKE assertion | `SchoolLms.Tests/Security/*` | W-17 | `[P]` | 12 |

Subtotal **256 h**. Critical path W-01→W-02→W-03→W-04→W-07→W-17: 72 h.
With one backend and one frontend developer: **≈ 4 weeks**.

#### Phase W2 — Procurement

| Id | Unit | Depends on | `[P]`/`[S]` | h |
|---|---|---|---|---|
| W-20 | entities + migration `WarehouseProcurement` | W-02 | `[S]` | 8 |
| W-21 | purchase-request service + state machine + endpoints (#52–#60) | W-20 | `[P]` | 16 |
| W-22 | budget entity, `BudgetConsumptionQuery`, endpoints (#61–#64) | W-20 | `[P]` | 12 |
| W-23 | `stock-ins/from-purchase-request` + `fulfilled` transition | W-21, W-07 | `[S]` | 6 |
| W-24 | frontend: xarid so'rovlari + byudjet (9, 10) | W-21, W-22 | `[P]` | 20 |
| W-25 | wiring + tests (state machine, self-approval, budget limit) | W-24 | `[S]` | 10 |

Subtotal **72 h** ≈ **1.5 weeks**.

#### Phase W3 — Room assets

| Id | Unit | Depends on | `[P]`/`[S]` | h |
|---|---|---|---|---|
| W-26 ★ | **`rooms` table + seed from `classes.room`** — shared, see §10.4 | — | `[S]` | 8 |
| W-27 | asset entities + events + migration | W-26, W-02 | `[S]` | 8 |
| W-28 | asset service + endpoints (#65–#73) + `asset_issue` hook in stock-out | W-27, W-08 | `[P]` | 14 |
| W-29 | frontend: xona jihozlari (11) + Rooms-page cross-link | W-28 | `[P]` | 14 |
| W-30 | wiring + tests | W-29 | `[S]` | 6 |

Subtotal **50 h** ≈ **1 week**.

#### Phase W4 — Library (independent of W1)

| Id | Unit | Depends on | `[P]`/`[S]` | h |
|---|---|---|---|---|
| W-31 ★ | entities + migration `Library` (books, copies, loans, reading log) | — | `[S]` | 10 |
| W-32 | book + copy service, borrow/return/mark-status (#74–#85) | W-31 | `[P]` | 18 |
| W-33 | reading log service (#86–#90) | W-31 | `[P]` | 8 |
| W-34 | frontend: kitoblar (12) | W-32 | `[P]` | 18 |
| W-35 | frontend: o'qilgan kitoblar (13) + student-profile tab | W-33 | `[P]` | 12 |
| W-36 | wiring + tests (loan invariants, overdue, one-open-loan index) | W-34, W-35 | `[S]` | 10 |

Subtotal **76 h** ≈ **1.5 weeks**.

#### Phase W5 — Meal costing

| Id | Unit | Depends on | `[P]`/`[S]` | h |
|---|---|---|---|---|
| W-37 ★ | entities + migration `MealCosting` | W-02 | `[S]` | 8 |
| W-38 | recipe service, per-portion cost, `dish_id` link (#91–#95) | W-37, W-05 | `[P]` | 14 |
| W-39 ★ | meal closure: plan from `dishes` + recipes, post → stock-out, reverse (#96–#102) | W-38, W-08 | `[S]` | 18 |
| W-40 | `MealCostQuery` + dashboard endpoint (#103) | W-39 | `[P]` | 12 |
| W-41 | portion suggestion from attendance + `meals` subscriptions | W-39 | `[P]` | 8 |
| W-42 | frontend: retseptlar (14) | W-38 | `[P]` | 16 |
| W-43 | frontend: ovqatlanish (15) | W-39 | `[P]` | 18 |
| W-44 | frontend: ovqat xarajatlari dashboard (16) | W-40 | `[P]` | 16 |
| W-45 | wiring + tests (no double-count, reversal chain, cost reproducibility) | W-42…W-44 | `[S]` | 12 |

Subtotal **122 h** ≈ **2.5 weeks**.

**Whole module: 576 h ≈ 10–11 calendar weeks with one backend and one frontend developer.**
Estimates exclude client-decision waiting time (§12) and assume the `docs/TESTING.md`
Postgres harness is already green.

### 10.4 Shared files — sequential, one owner, never parallel

Touching any of these from two units at once breaks the build or the schema. The
orchestrator owns them.

| File | Why it is shared |
|---|---|
| `SchoolLms.Infrastructure/Migrations/AppDbContextModelSnapshot.cs` | EF rewrites it wholesale; two concurrent migrations produce an unmergeable conflict. **This is the reason this document contains no migration.** |
| `SchoolLms.Infrastructure/Migrations/*` (the alembic-equivalent head) | Migration order is linear. One migration per phase, applied in phase order. |
| `SchoolLms.Infrastructure/Data/AppDbContext.cs` | One added line per phase (`WarehouseModel.Apply(b)`, etc.). |
| `SchoolLms.Infrastructure/SchoolLms.Infrastructure.csproj` | Each new `Sql/*.sql` needs an `EmbeddedResource` entry. |
| `SchoolLms.Server/Program.cs` | DI registration for every new service. |
| `schoollms.client/src/App.tsx` | Route registration. |
| `schoollms.client/src/config/navigation.ts` | Menu entries. |
| `schoollms.client/src/config/constants.ts` | The `adminPermissions` registry — five new keys, added once. |
| `schoollms.client/src/types/index.ts` | Shared TS types. |
| **`rooms` table (W-26)** | The schedule module (`docs/SPEC.md` §3.3) will also want it. Whichever lands first creates it; the other must not. |
| `SchoolLms.Application/Billing/Accounts.cs` | Only if §12 Q1 is answered "perpetual inventory". Untouched otherwise. |

### 10.5 Table creation order

Within a single migration, and across migrations:

```
W1: rooms?†  →  wms_settings  →  wms_warehouses  →  wms_categories  →  wms_suppliers
    →  wms_products
    →  wms_stock_in → wms_stock_in_lines
    →  wms_stock_out → wms_stock_out_lines
    →  wms_inventory_sessions → wms_inventory_items
    →  wms_stock_adjustments            (FK → inventory_sessions)
    →  wms_stock_movements              (FK → products, warehouses; last, it references all)
W2: wms_budgets  →  wms_purchase_requests → wms_purchase_request_lines
    (then: add wms_stock_in.purchase_request_id)
W3: rooms†  →  wms_room_assets  →  wms_room_asset_events
W4: wms_books  →  wms_book_copies  →  wms_book_loans  →  wms_reading_log
W5: wms_recipes → wms_recipe_lines  →  wms_meal_closures → wms_meal_closure_lines
    (then: add wms_meal_closures.stock_out_id)
```

† `rooms` belongs to W3 unless the schedule module has already created it.

---

## 11. Definition of Done

Per phase. "Done" means the machine says so.

**Every phase:**

- [ ] `dotnet test` green — no test deleted, no assertion weakened.
- [ ] `npm run build` green in `schoollms.client`.
- [ ] The migration applies to an empty database (`upgrade head`) **and** to a copy of the
      current production schema.
- [ ] The migration's `Up()` contains **zero** `DROP` statements. Read it, do not skim it.
- [ ] Every new table has a `GRANT` line in `warehouse_guards.sql`; a test asserts that
      `app_rw` can `SELECT`/`INSERT` on each of them.
- [ ] Every new endpoint is covered by an RBAC test that asserts 403 for a role that must not
      reach it, including at least one `staff`-without-permission write attempt.
- [ ] No `DateTime` property on any new entity (grep the new files).
- [ ] `git diff --stat SchoolLms.Domain/Entities.cs` is **empty**.
- [ ] Uzbek UI strings, English identifiers and comments.
- [ ] Any decision taken while building that this document did not answer is one line in
      `docs/ASSUMPTIONS.md`.

**W1 additionally:**

- [ ] A test asserts `app_rw` receives SQLSTATE **42501** on `UPDATE` and on `DELETE` against
      `wms_stock_movements`, and that the row survives.
- [ ] A test posts 200 in/out movements in random order and asserts
      `Value == Σ total_cost[in] − Σ total_cost[out]` and that emptying the stock drives
      `Value` to exactly `0.00` (the closing rule, §4.2).
- [ ] A concurrency test fires two confirms of the last available unit simultaneously; exactly
      one succeeds, the other gets `insufficient_stock`, and the balance is never negative.
- [ ] A test asserts a back-dated confirm returns `400 backdated_movement`.
- [ ] A test asserts `doc_no` is gapless under 50 concurrent confirms.
- [ ] Confirming a stock-in creates exactly **one** `expenses` row and **no** direct
      `ledger_entries` insert from warehouse code (assert by call-site inspection and by
      counting entries with `ref_type='expense'`).
- [ ] A stocktake with three variances produces exactly three confirmed adjustments and a
      `variance_value` equal to their signed sum.
- [ ] A product with a movement refuses a unit change.
- [ ] `ForManyAsync` over 500 products issues **2** database queries (assert with the harness's
      query counter).

**W4 additionally:**

- [ ] Borrowing twice in parallel produces exactly one open loan (the partial unique index
      fires).
- [ ] A returned copy has no open loan and a consistent status.

**W5 additionally:**

- [ ] Posting a meal closure creates stock-out movements and **zero** rows in `expenses` and
      `ledger_entries`. This test is the guard against the double-count in §5.3 and must be
      named so that deleting it looks wrong.
- [ ] Re-running the cost dashboard for a past month after new purchases returns the same
      numbers as before (frozen-cost reproducibility).
- [ ] Reversing a closure reverses its stock-out and every movement, and the product balances
      return to their pre-closure values exactly.

---

## 12. Open questions — with the decision that stands if nobody answers

Each has a recommended answer. **If the client says nothing, the recommendation is what gets
built.** None of these blocks the start of W1 except Q4, and only partially.

**Q1 — Should consumed stock hit the P&L (perpetual inventory), or should the purchase hit it
(expense at purchase)?**
*Recommendation: expense at purchase now (§5.2), meal cost as a management figure (§5.3).*
Reason: it is correct on cash immediately, it needs no new ledger account, it does not put the
warehouse under the full weight of SPEC §4 on day one, and the upgrade path is one account
plus two service changes (§5.4) precisely because every movement already stores a frozen cost.
Ask the client only if the director wants the P&L food line to mean "eaten" rather than
"bought".

**Q2 — FIFO or weighted average?**
*Recommendation: weighted moving average (§4.1).* The bundle proves nothing about EduSchool's
choice. FIFO is better for perishables and worse for everything else here. The setting exists
(`wms_settings.costing_method`) so the answer can change without a schema change.

**Q3 — Where do the portion counts come from?**
*Recommendation: entered by the kitchen, with a suggestion computed from attendance ∩ active
`meals` subscription (§3.9).* Ask only if the school already counts portions some other way
(a turnstile at the canteen door, a paper tally).

**Q4 — Do we need a `departments` table?**
*Recommendation: yes, minimal — `departments(id uuid, name text unique, head_teacher_id text
null, is_active bool)` — created in W2, not W1.* EduSchool has `/department` and their
`cost-by-department` report depends on it. Until it exists, `stock_out.department_id` is
nullable and the report is hidden (§10.2). **This is the only question that changes a table,
so it is the only one worth asking early.**

**Q5 — Is the reading log a student-facing feature or staff data entry?**
*Recommendation: staff entry in W4; a student-portal form is a later, separate task.* The
approve/reject endpoints imply a submission the staff did not make, but our student portal has
no such screen and adding one is portal work, not warehouse work.

**Q6 — Should approving a reading log award gamification points?**
*Recommendation: yes, but not now.* SPEC §3.8's `point_transactions` is append-only with a
`source` column; when Phase 4 lands, approval inserts one row with `source='reading'`. This
module must not create a points table (§3.8).

**Q7 — Charge a family for a lost book?**
*Recommendation: no automatic charge.* `wms_book_copies.unit_cost` records the value so the
librarian can tell the office a number; creating an invoice is Billing's job and requires a
fee category the school may not want. Ask before building anything automatic.

**Q8 — One kitchen store or several (main store + daily kitchen store)?**
*Recommendation: model both — `wms_warehouses` supports it and `reason='transfer'` moves stock
between them — but seed only two (`ASOSIY`, `OSHXONA`) and let the school add more.*

**Q9 — Categories: flat or tree?**
*Recommendation: two levels, enforced (§3.3).* Cheap to relax later, expensive to tighten.

**Q10 — Barcode label printing?**
*Recommendation: out of scope (§10.2).* Reading is specified and cheap; printing needs
hardware the school may not own.

**Q11 — Should the storekeeper be a first-class role, like `cashier`?**
*Recommendation: no.* `cashier` earned a role because its restrictions are enforced by
database grants (SPEC §3.1). The storekeeper's restrictions are ordinary permission checks,
so `staff` + the `warehouse` permission is the right shape. Revisit only if the client asks
for a warehouse-only login that cannot read anything else — `AdminPermAttribute` lets any
staff member GET any section, which is a deliberate project-wide choice and not this module's
to change.

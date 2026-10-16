# Mini-magazin — products and book sales at the cash desk

Status: built 2026-10-15 (migration `AuditStudentFlowAndShop`). Client request: *"Mahsulotlar va kitob savdosi (Ombor /
Mini-magazin). Maktab ichidagi qo'shimcha savdolar (kitoblar, formalar, to'garak aksessuarlari)
hisobini yuritish uchun bo'lim yaratilsin. Funksionallik: Mahsulot nomi, turi va narxini kiritish.
Kassa/Moliya kiritish vaqtida to'lov turiga (masalan: 'Kitob savdosi') bog'lab kirim qilish."*

## Why

Books, uniforms and club accessories were taken as a typed Kirim ("Kitob uchun to'lov", "Do'ppi
uchun") with the amount typed by hand: no price list, no record of what was sold or how many, and
every sale landed in P&L as "Boshqa daromad".

## Data

| Table | What | Writes |
|---|---|---|
| `shop_products` | name (unique), kind (`book` / `uniform` / `club` / `other` — Kitob, Forma, To'garak aksessuari, Boshqa), price `numeric(14,2)` ≥ 0, the Kirim transaction type it is sold under, active, position | plain catalogue: UPDATE; DELETE only while never sold |
| `shop_sale_lines` | the Kirim (`cash_box_transactions.id`), product, product name and unit price **as sold**, quantity > 0, line total (= unit price × quantity, checked by the database) | **insert-only** for `app_rw` (`Migrations/Sql/shop_guards.sql`, `deploy/init-roles.sql` step 5) |

There is no sale table: **the sale is the cash box's own typed Kirim** (`pay_in`). Box, method, date
(back-dating included), cashier, pupil and storno already live there. A price change never touches a
past line. One product appears once per sale (unique key); quantity says how many.

## Selling

Kassa → Kirim: choose the transaction type. If the type has active products, product lines appear
(product, quantity, price, line total, add/remove); the amount becomes their total and is not
editable. Lines are optional — an empty line is ignored and the amount is typed as before. Any
non-tuition Kirim may also name the pupil ("O'quvchini belgilash", optional).

`POST /api/admin/cash-boxes/{id}/in` takes `lines: [{ productId, quantity }]`. The server
(`ShopSales.cs`), inside the Kirim's transaction and under the box lock:

1. a type is required; ≤ 50 lines; quantity 1–10 000; no product twice;
2. every product exists (404), is active (400 `product_inactive`) and is sold under that type (400
   `product_type_mismatch`); the price is the catalogue's — a price in the request is ignored;
3. the amount must equal the lines' total to the tiyin (400 `sale_amount_mismatch`, total in the
   message) — the cashier collects what the screen showed, so a price changed meanwhile is refused,
   not silently recorded. The form reloads the catalogue on that error.

The Kirim row and its lines are written together or not at all. The journal row
(`CashBoxTransactionRowDto`) gains `lines` and `studentName`; the cash receipt prints both; the
journal's type column lists the products.

**Cancelling** is the cash box's storno (admin/director). The lines stay as they were; the storno row
shows them (display only) and the report leaves the sale out.

## Ledger

A typed Kirim posts debit cash/bank, credit the type's account (`CashBoxLedgerSync`) — unchanged. New
closed-list account **`revenue:sales` — "Mahsulot savdosi"**, selectable for Kirim types. Existing types
are not switched automatically: the admin points "Kitob uchun to'lov" / "Do'ppi uchun" at it in
Moliya sozlamalari → Tranzaksiya turi (from then on; past entries stay where they are). The name guess
for new or unset types sends a word starting with kitob / forma / do'ppi / kiyim there.

## Savdo hisoboti

Kassa page → "Savdo hisoboti" (modal): period (default today), box, pupil. Standing Kirims only, by
their own school-time day. Totals (sales, items, sum), then by product (current name and kind), by
product kind, by Kirim type, by cashier, and the sales themselves (names as sold; newest 2 000 listed,
totals count all). Excel: one sheet per table.

## API

| Route | Gate |
|---|---|
| `GET /api/admin/finance/products?activeOnly=&transactionTypeId=` | `ViewCashBoxes` — cashier, admin, director, finance staff; finance viewer reads |
| `POST`, `PUT /{id}`, `DELETE /{id}` `/api/admin/finance/products` | `ManageShopProducts` — admin, director, finance staff (not the cashier). Audited (`ShopProduct`). DELETE: 409 `product_in_use` once sold |
| `GET /api/admin/finance/products/sales?from=&to=&boxId=&studentId=` | `ViewCashBoxes` |
| `GET /api/admin/finance/products/sales/export?…` | `ViewCashBoxes` (.xlsx) |
| `POST /api/admin/cash-boxes/{id}/in` with `lines` | `OperateCashBox` (unchanged) |

A transaction type with products cannot be deleted (409 `transaction_type_has_products`). The MCP
read-only role (`app_ro`) is not granted the shop tables.

## Screens

- Moliya sozlamalari → **Mahsulotlar** tab: №, name, kind, price, Kirim type, status; add / edit /
  delete (lock icon once sold).
- Kassa → Kirim form: product lines and the optional pupil; receipt and journal show the lines.
- Kassa header → **Savdo hisoboti**.

## Relation to WareHouse (`docs/modules/warehouse.md`)

WareHouse is specified but not built. It is a stock ledger (`wms_products`, `wms_stock_movements`,
stock-in / stock-out documents) with no money side for sales — consumption is history, purchases are
expenses (§5). The mini-magazin is the opposite half: what was **sold, for how much, to whom**, through
the cash box. Nothing here holds a quantity on hand, and nothing is named `wms_`.

If WareHouse is built, the join is one nullable column — `shop_products.wms_product_id` — and a
`stock_out` movement written from each new sale line (`ref_type = shop_sale_line`). Sale lines stay
insert-only and keep their own price; a storno of the Kirim would write the matching return movement.
Until then, the school counts its book and uniform stock outside the system.

## Not built

Stock quantities, barcodes, purchase prices / margin, discounts on products, selling from the parent
Mini App — none were requested.

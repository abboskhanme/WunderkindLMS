namespace SchoolLms.Domain;

// ===========================================================================
//  MINI-MAGAZIN — school sales of books, uniforms and club accessories
//  (client, 2026-10-15): "Mahsulotlar va kitob savdosi (Ombor / Mini-magazin).
//  Maktab ichidagi qo'shimcha savdolar (kitoblar, formalar, to'garak
//  aksessuarlari) hisobini yuritish uchun bo'lim yaratilsin. Funksionallik:
//  Mahsulot nomi, turi va narxini kiritish. Kassa/Moliya kiritish vaqtida
//  to'lov turiga (masalan: 'Kitob savdosi') bog'lab kirim qilish."
// ===========================================================================
//
//  TWO TABLES, AND NO THIRD "SALE" TABLE
//  -------------------------------------
//  `shop_products`   — the catalogue: name, kind, price, the Kirim type it is sold under. A plain
//                      catalogue (UPDATE allowed; DELETE only while never sold — the service checks,
//                      the RESTRICT foreign key from the lines is the backstop).
//  `shop_sale_lines` — what one cash-box Kirim sold: product, quantity, unit price, line total.
//                      INSERT-only, like the Kirim row it belongs to (`shop_guards.sql`).
//
//  The sale itself IS the typed Kirim (`cash_box_transactions`, kind `pay_in`): the money, the
//  box, the method, the date, the cashier, the pupil and the storno all already live there, and the
//  ledger posting (`CashBoxLedgerSync`) follows the row's transaction type. A separate sale header
//  would be a second copy of the amount that could drift from the cash box.
//
//  PRICES ARE SNAPSHOTS
//  --------------------
//  A line keeps the product's name and price AS SOLD. Changing the catalogue price later never
//  rewrites a past sale; the audit log records who changed the price and when.
//
//  CANCELLING A SALE
//  -----------------
//  Storno of the Kirim (`CashBoxService.CancelTransactionAsync`) — the lines stay untouched and
//  point at a cancelled row; the sales report leaves cancelled rows out.
//
//  NO STOCK HERE
//  -------------
//  Quantities on hand are the planned WareHouse module's job (`docs/modules/warehouse.md`, the
//  `wms_*` journal of movements). The `shop_` prefix keeps these tables clear of `wms_products`;
//  `docs/modules/shop.md` says how the two would meet.

/// <summary><see cref="ShopProduct.Kind"/> values — the client's "turi": book, uniform, club accessory, other.</summary>
public static class ShopProductKind
{
    public const string Book = "book";
    public const string Uniform = "uniform";
    public const string Club = "club";
    public const string Other = "other";

    public static readonly IReadOnlyList<string> All = [Book, Uniform, Club, Other];
}

/// <summary>
/// A catalogue item sold at the cash desk. Sold only under its own Kirim transaction type
/// (<see cref="TransactionTypeId"/>), so the ledger account of the sale is the type's account.
/// </summary>
public class ShopProduct
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary><see cref="ShopProductKind"/>.</summary>
    public string Kind { get; set; } = ShopProductKind.Other;

    /// <summary>Current price per unit (so'm, 2 decimals, ≥ 0). A sale copies it into its line.</summary>
    public decimal Price { get; set; }

    /// <summary>The Kirim type (<see cref="TransactionType"/>, kind <c>in</c>) this product is sold under.</summary>
    public Guid TransactionTypeId { get; set; }

    /// <summary>false — not offered at the cash desk any more; past sales keep showing it.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Order in lists (smaller first), like <see cref="TransactionType.Position"/>.</summary>
    public int Position { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Last catalogue edit; null — never edited.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>
/// One product sold by a cash-box Kirim. <b>INSERT-only</b>: <c>app_rw</c> has no UPDATE/DELETE on
/// <c>shop_sale_lines</c> (<c>Migrations/Sql/shop_guards.sql</c>). Name and price are copied from the
/// catalogue at the moment of sale.
/// </summary>
public class ShopSaleLine
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The Kirim (cash_box_transactions.id, kind <c>pay_in</c>) the line belongs to.</summary>
    public Guid CashBoxTransactionId { get; set; }

    public Guid ProductId { get; set; }

    /// <summary>The product's name when it was sold.</summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>Units sold (&gt; 0).</summary>
    public int Quantity { get; set; }

    /// <summary>The catalogue price when it was sold.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary><see cref="Quantity"/> × <see cref="UnitPrice"/>, stored so the database can check it.</summary>
    public decimal LineTotal { get; set; }
}

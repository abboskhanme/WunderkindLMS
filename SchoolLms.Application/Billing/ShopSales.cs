using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  MINI-MAGAZIN SALE = A TYPED CASH-BOX KIRIM WITH LINES (docs/modules/shop.md)
// ===========================================================================
//
//  `CashBoxService.PayInAsync` calls this when the request carries `lines`. The rules, in order:
//    1. shape (before the transaction): at least one line, at most `MaxLines`, no product twice,
//       1..`MaxQuantity` units, and a Kirim transaction type chosen;
//    2. prices (inside the transaction, under the box lock): every product exists, is active and is
//       sold under THAT type; the price comes from the catalogue, never from the request;
//    3. the Kirim amount must equal the sum of the lines to the tiyin.
//
//  WHY "AMOUNT MUST EQUAL THE TOTAL" AND NOT "THE SERVER SETS THE AMOUNT"
//  ---------------------------------------------------------------------
//  The cashier takes the money the screen showed. If an admin changed a price between the form
//  opening and "Kirim", a server-computed amount would record a sum the cashier never collected —
//  the drawer and the journal would disagree by the price difference, silently. A mismatch is
//  refused (400 `sale_amount_mismatch`, the server total in the message) and the form reloads.
// ===========================================================================

/// <summary>One product line of a sale, as the cash desk sends it — product and quantity only.</summary>
public record CashBoxSaleLineRequest(Guid ProductId, int Quantity);

/// <summary>One sold product line as stored: name and price as they were at the moment of sale.</summary>
public record CashBoxSaleLineDto(Guid ProductId, string ProductName, int Quantity, decimal UnitPrice, decimal LineTotal);

/// <summary>Validation and pricing of sale lines for <see cref="CashBoxService.PayInAsync"/>. File header.</summary>
internal static class ShopSales
{
    /// <summary>Lines per sale — a till receipt, not a warehouse delivery.</summary>
    public const int MaxLines = 50;

    /// <summary>Units per line — a typo guard (100 000 books is a slip of the finger).</summary>
    public const int MaxQuantity = 10_000;

    /// <summary>
    /// Step 1 — the request's shape, checked before any lock is taken. Null when the request has no
    /// lines (a plain Kirim); otherwise the lines, unchanged.
    /// </summary>
    public static IReadOnlyList<CashBoxSaleLineRequest>? RequireShape(
        IReadOnlyList<CashBoxSaleLineRequest>? lines, TransactionType? type)
    {
        if (lines is null || lines.Count == 0) return null;

        if (type is null)
            throw BillingRuleException.Invalid("sale_needs_transaction_type",
                "Mahsulot sotilganda tranzaksiya turi majburiy.");
        if (lines.Count > MaxLines)
            throw BillingRuleException.Invalid("too_many_lines",
                $"Bitta kirimda eng ko'pi bilan {MaxLines} ta mahsulot qatori bo'ladi.");
        if (lines.Any(l => l.ProductId == Guid.Empty))
            throw BillingRuleException.Invalid("product_required", "Har bir qatorda mahsulot tanlanishi shart.");
        if (lines.Any(l => l.Quantity < 1 || l.Quantity > MaxQuantity))
            throw BillingRuleException.Invalid("invalid_quantity",
                $"Soni 1 dan {MaxQuantity} gacha butun son bo'lishi kerak.");
        if (lines.Select(l => l.ProductId).Distinct().Count() != lines.Count)
            throw BillingRuleException.Invalid("duplicate_product_line",
                "Bitta mahsulot ikki qatorda turibdi — sonini bitta qatorda ko'rsating.");

        return lines;
    }

    /// <summary>
    /// Steps 2 and 3 — prices the lines from the catalogue and checks the total against
    /// <paramref name="amount"/>. Returns lines with <see cref="ShopSaleLine.CashBoxTransactionId"/> unset;
    /// the caller fills it with the Kirim's id and adds them in its own transaction.
    /// </summary>
    public static async Task<List<ShopSaleLine>> PriceAsync(
        IAppDbContext db, IReadOnlyList<CashBoxSaleLineRequest> lines, TransactionType type, decimal amount,
        CancellationToken ct)
    {
        var ids = lines.Select(l => l.ProductId).ToList();
        var products = await db.ShopProducts.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        var priced = new List<ShopSaleLine>(lines.Count);
        foreach (var line in lines)
        {
            var product = products.GetValueOrDefault(line.ProductId)
                ?? throw BillingRuleException.NotFound("product_not_found", "Mahsulot topilmadi.");
            if (!product.IsActive)
                throw BillingRuleException.Invalid("product_inactive",
                    $"\"{product.Name}\" sotuvda emas (faolsiz).");
            if (product.TransactionTypeId != type.Id)
                throw BillingRuleException.Invalid("product_type_mismatch",
                    $"\"{product.Name}\" \"{type.Name}\" turi bilan sotilmaydi — mahsulotning o'z kirim turini tanlang.");

            priced.Add(new ShopSaleLine
            {
                ProductId = product.Id,
                ProductName = product.Name,
                Quantity = line.Quantity,
                UnitPrice = product.Price,
                // numeric(14,2) × int is exact; the rounding only states the column's scale.
                LineTotal = decimal.Round(product.Price * line.Quantity, 2),
            });
        }

        var total = priced.Sum(l => l.LineTotal);
        if (total != amount)
            throw BillingRuleException.Invalid("sale_amount_mismatch",
                $"Summa mahsulotlar jamiga teng emas: jami {SumText(total)} so'm. "
                + "Narx o'zgargan bo'lishi mumkin — oynani qayta oching.");

        return priced;
    }

    /// <summary>Sold lines of these Kirim rows, keyed by row id, in name order — one query.</summary>
    public static async Task<Dictionary<Guid, IReadOnlyList<CashBoxSaleLineDto>>> LinesOfAsync(
        IAppDbContext db, IReadOnlyCollection<Guid> rowIds, CancellationToken ct)
    {
        if (rowIds.Count == 0) return [];

        var lines = await db.ShopSaleLines.AsNoTracking()
            .Where(l => rowIds.Contains(l.CashBoxTransactionId))
            .ToListAsync(ct);

        return lines
            .GroupBy(l => l.CashBoxTransactionId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<CashBoxSaleLineDto>)[.. g
                    .OrderBy(l => l.ProductName, StringComparer.Ordinal)
                    .Select(ToDto)]);
    }

    public static CashBoxSaleLineDto ToDto(ShopSaleLine l) =>
        new(l.ProductId, l.ProductName, l.Quantity, l.UnitPrice, l.LineTotal);

    /// <summary>"45 000" or "45 000.5" — the audit format, but tiyin are not rounded away.</summary>
    internal static string SumText(decimal value) =>
        value.ToString("#,##0.##", CultureInfo.InvariantCulture).Replace(",", " ");
}

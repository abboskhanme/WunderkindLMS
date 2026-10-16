using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  "Savdo hisoboti" — what the mini-magazin sold in a period (docs/modules/shop.md).
// ===========================================================================
//
//  SOURCE: `shop_sale_lines` of Kirim rows that still stand. A Kirim cancelled by storno is left
//  out whole (its lines are untouched — the table is insert-only — but the money went back), and a
//  storno row has no lines of its own. Same rule as the cash-box journal: a cancelled row adds
//  nothing to a total.
//
//  DAY: the Kirim's own day in school time (`created_at`, back-dated Kirims included), the same
//  window the cash-box journal uses (`CashBoxDateWindow`).
//
//  NAMES: per product — the catalogue's current name and kind (a renamed product stays one row);
//  per sale — the name as sold (the line's snapshot), so a printed receipt and the report agree.
//
//  QUERIES: rows, lines, then one lookup per name table — the count does not grow with the period.
// ===========================================================================

/// <summary>Report filter. <paramref name="From"/>/<paramref name="To"/> null — today.</summary>
public record ShopSalesQuery(DateOnly? From = null, DateOnly? To = null, Guid? BoxId = null, string? StudentId = null);

/// <summary>One product's total in the period.</summary>
public record ShopSalesProductRowDto(Guid ProductId, string Name, string Kind, int Quantity, decimal Amount);

/// <summary>One group's total (a product kind, a Kirim type, a cashier).</summary>
/// <param name="Sales">How many Kirims (receipts) had at least one line in this group.</param>
public record ShopSalesGroupRowDto(string Key, string Name, int Sales, int Quantity, decimal Amount);

/// <summary>One sale — one standing Kirim with its lines.</summary>
public record ShopSaleRowDto(
    Guid TransactionId, DateTimeOffset CreatedAt, string BoxName, string CashierName, string? StudentName,
    string? TransactionTypeName, string Method, decimal Amount, IReadOnlyList<CashBoxSaleLineDto> Lines);

/// <summary>The report: totals, four groupings and the sales themselves (newest first).</summary>
/// <param name="SalesTruncated">More than <see cref="ShopSalesReport.MaxSales"/> sales — the list shows the newest;
/// every total still counts all of them.</param>
public record ShopSalesReportDto(
    DateOnly From, DateOnly To, int SalesCount, int Quantity, decimal Amount,
    IReadOnlyList<ShopSalesProductRowDto> ByProduct,
    IReadOnlyList<ShopSalesGroupRowDto> ByKind,
    IReadOnlyList<ShopSalesGroupRowDto> ByType,
    IReadOnlyList<ShopSalesGroupRowDto> ByCashier,
    IReadOnlyList<ShopSaleRowDto> Sales,
    bool SalesTruncated);

/// <summary>"Savdo hisoboti" query and its .xlsx. File header.</summary>
public sealed class ShopSalesReport(IAppDbContext db)
{
    /// <summary>Sales listed one by one; totals are never capped.</summary>
    public const int MaxSales = 2000;

    public async Task<ShopSalesReportDto> BuildAsync(ShopSalesQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var from = query.From ?? query.To ?? AppClock.Today;
        var to = query.To ?? from;
        if (to < from)
            throw BillingRuleException.Invalid("invalid_period", "Davr oxiri boshidan oldin bo'lishi mumkin emas.");

        var studentId = string.IsNullOrWhiteSpace(query.StudentId) ? null : query.StudentId.Trim();
        var lower = CashBoxDateWindow.Lower(from);
        var upper = CashBoxDateWindow.Upper(to);

        // ---- 1. Standing Kirims with lines, in the (one-day-wide) window ----
        var cancelled = db.CashBoxTransactions.Where(t => t.ReversalOf != null).Select(t => t.ReversalOf!.Value);
        var q = db.CashBoxTransactions.AsNoTracking()
            .Where(t => t.Kind == CashBoxTransactionKind.PayIn && t.ReversalOf == null
                        && t.CreatedAt >= lower && t.CreatedAt <= upper
                        && !cancelled.Contains(t.Id)
                        && db.ShopSaleLines.Any(l => l.CashBoxTransactionId == t.Id));
        if (query.BoxId is { } boxId) q = q.Where(t => t.CashBoxId == boxId);
        if (studentId is not null) q = q.Where(t => t.StudentId == studentId);

        var rows = (await q
                .Select(t => new { t.Id, t.CreatedAt, t.CashBoxId, t.CreatedBy, t.StudentId, t.TransactionTypeId, t.Method })
                .ToListAsync(ct))
            .Where(t => CashBoxDateWindow.Covers(t.CreatedAt, from, to))
            .OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id)
            .ToList();

        // ---- 2. Their lines ----
        var rowIds = rows.Select(r => r.Id).ToList();
        var lines = rowIds.Count == 0
            ? []
            : await db.ShopSaleLines.AsNoTracking().Where(l => rowIds.Contains(l.CashBoxTransactionId)).ToListAsync(ct);
        var linesByRow = lines.ToLookup(l => l.CashBoxTransactionId);

        // ---- 3. Names, one query each ----
        var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.ShopProducts.AsNoTracking().Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.Kind })
            .ToDictionaryAsync(p => p.Id, ct);
        var typeIds = rows.Where(r => r.TransactionTypeId != null).Select(r => r.TransactionTypeId!.Value).Distinct().ToList();
        var typeNames = await db.TransactionTypes.AsNoTracking().Where(t => typeIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Name })
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var userIds = rows.Select(r => r.CreatedBy).Distinct().ToList();
        var userNames = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName })
            .ToDictionaryAsync(u => u.Id, u => u.FullName, StringComparer.Ordinal, ct);
        var studentIds = rows.Where(r => r.StudentId != null).Select(r => r.StudentId!).Distinct().ToList();
        var studentNames = await db.Students.AsNoTracking().Where(s => studentIds.Contains(s.Id))
            .Select(s => new { s.Id, s.FullName })
            .ToDictionaryAsync(s => s.Id, s => s.FullName, StringComparer.Ordinal, ct);
        var boxIds = rows.Select(r => r.CashBoxId).Distinct().ToList();
        var boxNames = await db.CashBoxes.AsNoTracking().Where(b => boxIds.Contains(b.Id))
            .Select(b => new { b.Id, b.Name })
            .ToDictionaryAsync(b => b.Id, b => b.Name, ct);

        // ---- 4. Groupings ----
        var rowById = rows.ToDictionary(r => r.Id);

        var byProduct = lines
            .GroupBy(l => l.ProductId)
            .Select(g => new ShopSalesProductRowDto(
                g.Key,
                products.GetValueOrDefault(g.Key)?.Name ?? g.First().ProductName,
                products.GetValueOrDefault(g.Key)?.Kind ?? ShopProductKind.Other,
                g.Sum(l => l.Quantity),
                g.Sum(l => l.LineTotal)))
            .OrderByDescending(r => r.Amount).ThenBy(r => r.Name, StringComparer.Ordinal)
            .ToList();

        var byKind = Group(lines, l => products.GetValueOrDefault(l.ProductId)?.Kind ?? ShopProductKind.Other, KindLabel);
        var byType = Group(lines,
            l => rowById[l.CashBoxTransactionId].TransactionTypeId?.ToString("D") ?? "",
            key => Guid.TryParse(key, out var id) ? typeNames.GetValueOrDefault(id, "—") : "—");
        var byCashier = Group(lines,
            l => rowById[l.CashBoxTransactionId].CreatedBy,
            key => userNames.GetValueOrDefault(key, "—"));

        var sales = rows.Take(MaxSales).Select(r => new ShopSaleRowDto(
            r.Id, r.CreatedAt,
            boxNames.GetValueOrDefault(r.CashBoxId, "—"),
            userNames.GetValueOrDefault(r.CreatedBy, "—"),
            r.StudentId is null ? null : studentNames.GetValueOrDefault(r.StudentId),
            r.TransactionTypeId is { } typeId ? typeNames.GetValueOrDefault(typeId) : null,
            r.Method,
            linesByRow[r.Id].Sum(l => l.LineTotal),
            [.. linesByRow[r.Id].OrderBy(l => l.ProductName, StringComparer.Ordinal).Select(ShopSales.ToDto)]))
            .ToList();

        return new ShopSalesReportDto(
            from, to, rows.Count, lines.Sum(l => l.Quantity), lines.Sum(l => l.LineTotal),
            byProduct, byKind, byType, byCashier, sales, rows.Count > MaxSales);

        List<ShopSalesGroupRowDto> Group(
            IEnumerable<ShopSaleLine> source, Func<ShopSaleLine, string> key, Func<string, string> name) =>
            [.. source
                .GroupBy(key)
                .Select(g => new ShopSalesGroupRowDto(
                    g.Key, name(g.Key),
                    g.Select(l => l.CashBoxTransactionId).Distinct().Count(),
                    g.Sum(l => l.Quantity),
                    g.Sum(l => l.LineTotal)))
                .OrderByDescending(r => r.Amount).ThenBy(r => r.Name, StringComparer.Ordinal)];
    }

    // =====================================================================
    //  .xlsx — the screen's tables, one sheet each, amounts as numbers
    // =====================================================================

    public static byte[] Export(ShopSalesReportDto report, string? boxName, string? studentName)
    {
        var period = report.From == report.To
            ? report.From.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
            : $"{report.From:dd.MM.yyyy} – {report.To:dd.MM.yyyy}";
        var scope = string.Join(" · ", new[] { boxName ?? "Barcha kassalar", studentName }.Where(s => s is not null));
        var title = $"Savdo hisoboti · {period} · {scope}";

        // Counts are text: a number cell is formatted "# ##0.00", and "3.00 ta" reads wrong.
        static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

        static IReadOnlyList<ExcelExport.XlsxCell> Totals(int sales, int qty, decimal amount, bool withSales) =>
            ["Jami", withSales ? Count(sales) : "", Count(qty), ExcelExport.XlsxCell.Num(amount)];

        ExcelExport.TableSpec GroupSheet(string sheet, string column, IReadOnlyList<ShopSalesGroupRowDto> rows) =>
            new(sheet, [column, "Savdolar", "Soni", "Summa"],
                rows.Select(r => (IReadOnlyList<ExcelExport.XlsxCell>)
                [
                    r.Name, Count(r.Sales), Count(r.Quantity), ExcelExport.XlsxCell.Num(r.Amount),
                ]).ToList(),
                Totals(report.SalesCount, report.Quantity, report.Amount, withSales: true),
                Title: title, Widths: [34, 12, 10, 16]);

        return ExcelExport.BuildTables(
        [
            new ExcelExport.TableSpec("Mahsulotlar", ["Mahsulot", "Turi", "Soni", "Summa"],
                report.ByProduct.Select(r => (IReadOnlyList<ExcelExport.XlsxCell>)
                [
                    r.Name, KindLabel(r.Kind), Count(r.Quantity), ExcelExport.XlsxCell.Num(r.Amount),
                ]).ToList(),
                Totals(report.SalesCount, report.Quantity, report.Amount, withSales: false),
                Title: title, Widths: [34, 22, 10, 16]),
            GroupSheet("Mahsulot turlari", "Mahsulot turi", report.ByKind),
            GroupSheet("Kirim turlari", "Kirim turi", report.ByType),
            GroupSheet("Kassirlar", "Kassir", report.ByCashier),
            new ExcelExport.TableSpec("Savdolar",
                ["№", "Sana", "Kassa", "Kassir", "O'quvchi", "Kirim turi", "Mahsulotlar", "To'lov usuli", "Summa"],
                report.Sales.Select((s, i) => (IReadOnlyList<ExcelExport.XlsxCell>)
                [
                    (i + 1).ToString(CultureInfo.InvariantCulture),
                    AppClock.ToLocal(s.CreatedAt).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture),
                    s.BoxName, s.CashierName, s.StudentName, s.TransactionTypeName,
                    string.Join(", ", s.Lines.Select(l => $"{l.ProductName} × {l.Quantity}")),
                    CashBoxTransactionsExport.MethodLabel(s.Method),
                    ExcelExport.XlsxCell.Num(s.Amount),
                ]).ToList(),
                Title: title + (report.SalesTruncated ? $" · eng so'nggi {MaxSales} ta savdo" : ""),
                Widths: [6, 17, 26, 26, 30, 24, 50, 22, 16]),
        ]);
    }

    /// <summary><c>savdo-hisoboti_2026-10-01_2026-10-15.xlsx</c>.</summary>
    public static string FileName(DateOnly from, DateOnly to) =>
        $"savdo-hisoboti_{from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
        + $"_{to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.xlsx";

    /// <summary>The words of the admin screen (schoollms.client/src/api/services/shop.ts).</summary>
    public static string KindLabel(string kind) => kind switch
    {
        ShopProductKind.Book => "Kitob",
        ShopProductKind.Uniform => "Forma",
        ShopProductKind.Club => "To'garak aksessuari",
        ShopProductKind.Other => "Boshqa",
        _ => kind,
    };
}

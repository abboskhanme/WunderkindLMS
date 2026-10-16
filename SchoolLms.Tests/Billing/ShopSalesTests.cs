using System.Net;
using System.Net.Http.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Billing;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Billing;

// ===========================================================================
//  Mini-magazin sale = a typed cash-box Kirim with product lines (client, 2026-10-15):
//  "Kassa/Moliya kiritish vaqtida to'lov turiga (masalan: 'Kitob savdosi') bog'lab kirim qilish."
//  Prices from the catalogue, lines insert-only, storno leaves them out of "Savdo hisoboti".
//  docs/modules/shop.md.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class ShopSalesTests(ApiFixture fixture)
{
    private const string Boxes = "/api/admin/cash-boxes";
    private const string Sales = "/api/admin/finance/products/sales";

    /// <summary><c>insufficient_privilege</c>.</summary>
    private const string PermissionDenied = "42501";

    private sealed record ErrorBody(string Code, string Message);

    private AppDbContext NewDb() => PostgresFixture.NewContext(fixture.Database.OwnerConnectionString);

    [Fact]
    public async Task Sotuv_narxni_katalogdan_oladi_va_jurnalga_mahsulot_savdosi_bolib_tushadi()
    {
        var s = await SetupAsync();
        var pupil = await NewStudentAsync();

        var response = await s.Cashier.PostAsJsonAsync($"{Boxes}/{s.Box.Id}/in", new
        {
            amount = 2 * 85_000m + 150_000m,
            method = PaymentMethod.Cash,
            transactionTypeId = s.Type.Id,
            studentId = pupil.Id,
            // A price in the request is ignored — the catalogue decides.
            lines = new object[]
            {
                new { productId = s.Book.Id, quantity = 2, unitPrice = 1m },
                new { productId = s.Uniform.Id, quantity = 1 },
            },
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = (await response.Content.ReadFromJsonAsync<CashBoxTransactionRowDto>())!;

        Assert.Equal(320_000m, row.Amount);
        Assert.Equal(pupil.FullName, row.StudentName);
        Assert.NotNull(row.Lines);
        var book = Assert.Single(row.Lines, l => l.ProductId == s.Book.Id);
        Assert.Equal((s.Book.Name, 2, 85_000m, 170_000m), (book.ProductName, book.Quantity, book.UnitPrice, book.LineTotal));
        var uniform = Assert.Single(row.Lines, l => l.ProductId == s.Uniform.Id);
        Assert.Equal(150_000m, uniform.LineTotal);

        // The journal row carries the same lines and the pupil (the receipt prints them).
        var journal = await JournalAsync(s.Cashier, s.Box.Id);
        var listed = journal.Rows.Single(r => r.Id == row.Id);
        Assert.Equal(2, listed.Lines!.Count);
        Assert.Equal(pupil.FullName, listed.StudentName);

        // The type points at "Mahsulot savdosi": the ledger credits revenue:sales, debits cash.
        await using var db = NewDb();
        var ledger = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.RefType == LedgerRefType.CashBoxTransaction && e.RefId == row.Id).ToListAsync();
        Assert.Equal(2, ledger.Count);
        Assert.Contains(ledger, e => e.Account == Accounts.RevenueSales && e.Direction == LedgerDirection.Credit && e.Amount == 320_000m);
        Assert.Contains(ledger, e => e.Account == Accounts.Cash && e.Direction == LedgerDirection.Debit && e.Amount == 320_000m);

        // A later price change leaves the sold line as it was.
        var owner = s.Owner;
        Assert.Equal(HttpStatusCode.OK,
            (await owner.PutAsJsonAsync($"/api/admin/finance/products/{s.Book.Id}", new { price = 99_000m })).StatusCode);
        var stored = await db.ShopSaleLines.AsNoTracking().SingleAsync(l => l.CashBoxTransactionId == row.Id && l.ProductId == s.Book.Id);
        Assert.Equal(85_000m, stored.UnitPrice);
    }

    [Fact]
    public async Task Summa_mahsulotlar_jamiga_teng_bolmasa_400_va_hech_narsa_yozilmaydi()
    {
        var s = await SetupAsync();

        var error = await RefusedAsync(s, new
        {
            amount = 80_000m,                                 // the book costs 85 000
            method = PaymentMethod.Cash,
            transactionTypeId = s.Type.Id,
            lines = new[] { new { productId = s.Book.Id, quantity = 1 } },
        });
        Assert.Equal("sale_amount_mismatch", error.Code);
        Assert.Contains("85 000", error.Message);

        Assert.Equal(0m, (await GetBoxAsync(s.Cashier, s.Box.Id)).Balance);
        await using var db = NewDb();
        Assert.False(await db.ShopSaleLines.AsNoTracking().AnyAsync(l => l.ProductId == s.Book.Id));
    }

    [Fact]
    public async Task Faolsiz_yoki_boshqa_turdagi_mahsulot_rad_etiladi()
    {
        var s = await SetupAsync();

        var inactive = await ShopProductsTests.NewProductAsync(s.Owner, s.Type.Id, 5_000m);
        Assert.Equal(HttpStatusCode.OK,
            (await s.Owner.PutAsJsonAsync($"/api/admin/finance/products/{inactive.Id}", new { isActive = false })).StatusCode);
        Assert.Equal("product_inactive", (await RefusedAsync(s, Sale(s.Type.Id, 5_000m, (inactive.Id, 1)))).Code);

        var otherType = await ShopProductsTests.NewInTypeAsync(s.Owner);
        var foreign = await ShopProductsTests.NewProductAsync(s.Owner, otherType.Id, 7_000m);
        Assert.Equal("product_type_mismatch", (await RefusedAsync(s, Sale(s.Type.Id, 7_000m, (foreign.Id, 1)))).Code);

        Assert.Equal("product_not_found",
            (await RefusedAsync(s, Sale(s.Type.Id, 1m, (Guid.NewGuid(), 1)), HttpStatusCode.NotFound)).Code);
        Assert.Equal("sale_needs_transaction_type", (await RefusedAsync(s, Sale(null, 85_000m, (s.Book.Id, 1)))).Code);
        Assert.Equal("invalid_quantity", (await RefusedAsync(s, Sale(s.Type.Id, 0.01m, (s.Book.Id, 0)))).Code);
        Assert.Equal("duplicate_product_line",
            (await RefusedAsync(s, Sale(s.Type.Id, 170_000m, (s.Book.Id, 1), (s.Book.Id, 1)))).Code);

        Assert.Equal(0m, (await GetBoxAsync(s.Cashier, s.Box.Id)).Balance);
    }

    [Fact]
    public async Task Storno_qilingan_sotuv_hisobotga_kirmaydi_va_sotilgan_mahsulot_ochirilmaydi()
    {
        var s = await SetupAsync();
        var kept = await SellAsync(s, (s.Book.Id, 1));
        var cancelled = await SellAsync(s, (s.Book.Id, 3), (s.Uniform.Id, 1));

        var cancel = await s.Owner.PostAsJsonAsync($"{Boxes}/transactions/{cancelled.Id}/cancel", new { reason = "Qaytarib berdi" });
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        var storno = (await cancel.Content.ReadFromJsonAsync<CashBoxTransactionRowDto>())!;
        Assert.Equal(2, storno.Lines!.Count);                 // the storno shows what it cancelled

        var report = await ReportAsync(s.Cashier, $"?boxId={s.Box.Id}");
        Assert.Equal(1, report.SalesCount);
        Assert.Equal(kept.Id, Assert.Single(report.Sales).TransactionId);
        Assert.Equal(85_000m, report.Amount);
        var product = Assert.Single(report.ByProduct);
        Assert.Equal((s.Book.Id, 1, 85_000m), (product.ProductId, product.Quantity, product.Amount));

        // The lines of the cancelled Kirim are untouched — the table is insert-only.
        await using var db = NewDb();
        Assert.Equal(2, await db.ShopSaleLines.AsNoTracking().CountAsync(l => l.CashBoxTransactionId == cancelled.Id));

        var delete = await s.Owner.DeleteAsync($"/api/admin/finance/products/{s.Book.Id}");
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        Assert.Equal("product_in_use", (await delete.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    [Fact]
    public async Task Hisobot_mahsulot_tur_kirim_turi_va_kassir_boyicha_hamda_oquvchi_filtri()
    {
        var s = await SetupAsync();
        var pupil = await NewStudentAsync();

        // A second cashier on the same box.
        var (otherUser, _) = await fixture.Api.SeedUserAsync(Roles.Cashier);
        var other = fixture.Api.ClientWithToken(
            fixture.Api.TokenFor(Roles.Cashier, otherUser.Id, otherUser.FullName, otherUser.Email));

        await SellAsync(s, (s.Book.Id, 2));                                   // 170 000
        await SellAsync(s with { Cashier = other }, (s.Book.Id, 1), (s.Uniform.Id, 2));  // 85 000 + 300 000
        await SellAsync(s, pupil.Id, (s.Uniform.Id, 1));                      // 150 000, pupil

        var report = await ReportAsync(s.Owner, $"?boxId={s.Box.Id}&from={AppClock.Today:yyyy-MM-dd}&to={AppClock.Today:yyyy-MM-dd}");
        Assert.Equal(3, report.SalesCount);
        Assert.Equal(6, report.Quantity);
        Assert.Equal(705_000m, report.Amount);

        var book = report.ByProduct.Single(r => r.ProductId == s.Book.Id);
        Assert.Equal((3, 255_000m, ShopProductKind.Book), (book.Quantity, book.Amount, book.Kind));
        var uniform = report.ByProduct.Single(r => r.ProductId == s.Uniform.Id);
        Assert.Equal((3, 450_000m), (uniform.Quantity, uniform.Amount));
        Assert.Equal(uniform.ProductId, report.ByProduct[0].ProductId);       // biggest sum first

        Assert.Equal(450_000m, report.ByKind.Single(r => r.Key == ShopProductKind.Uniform).Amount);
        var type = Assert.Single(report.ByType);
        Assert.Equal((s.Type.Name, 3, 705_000m), (type.Name, type.Sales, type.Amount));

        Assert.Equal(320_000m, report.ByCashier.Single(r => r.Key == s.CashierId).Amount);
        var second = report.ByCashier.Single(r => r.Key == otherUser.Id);
        Assert.Equal((otherUser.FullName, 1, 385_000m), (second.Name, second.Sales, second.Amount));

        // Pupil filter: only what that pupil bought.
        var mine = await ReportAsync(s.Owner, $"?boxId={s.Box.Id}&studentId={pupil.Id}");
        var sale = Assert.Single(mine.Sales);
        Assert.Equal((pupil.FullName, 150_000m), (sale.StudentName, sale.Amount));

        // A period that ends before it starts is refused.
        var wrong = await s.Owner.GetAsync($"{Sales}?from={AppClock.Today:yyyy-MM-dd}&to={AppClock.Today.AddDays(-1):yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
    }

    [Fact]
    public async Task Hisobot_eksporti_xlsx()
    {
        var s = await SetupAsync();
        await SellAsync(s, (s.Book.Id, 2), (s.Uniform.Id, 1));

        var response = await s.Cashier.GetAsync($"{Sales}/export?boxId={s.Box.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);

        using var stream = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var doc = SpreadsheetDocument.Open(stream, false);
        var sheets = doc.WorkbookPart!.Workbook!.Descendants<Sheet>().Select(x => x.Name!.Value).ToList();
        Assert.Equal(["Mahsulotlar", "Mahsulot turlari", "Kirim turlari", "Kassirlar", "Savdolar"], sheets);

        var first = ReadSheet(doc, 0);
        Assert.StartsWith("Savdo hisoboti", first[0][0]);
        Assert.Equal(["Mahsulot", "Turi", "Soni", "Summa"], first[2]);
        var bookRow = first.Single(r => r.Count > 0 && r[0] == s.Book.Name);   // row 1 is the blank spacer
        Assert.Equal(["Kitob", "2", "170000.00"], bookRow.Skip(1).ToList());
        Assert.Equal(["Jami", "", "3", "320000.00"], first[^1]);
    }

    [Fact]
    public async Task App_rw_savdo_qatorini_ozgartira_ham_ochira_ham_olmaydi_42501()
    {
        fixture.Database.RequireRealAppRw();
        var s = await SetupAsync();
        var sale = await SellAsync(s, (s.Book.Id, 1));

        await using var db = NewDb();
        var line = await db.ShopSaleLines.AsNoTracking().SingleAsync(l => l.CashBoxTransactionId == sale.Id);

        await using var connection = new NpgsqlConnection(fixture.Database.AppRwConnectionString);
        await connection.OpenAsync();
        foreach (var sql in new[]
                 {
                     "UPDATE shop_sale_lines SET quantity = 9, line_total = unit_price * 9 WHERE id = @id",
                     "DELETE FROM shop_sale_lines WHERE id = @id",
                 })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", line.Id);
            var pg = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(PermissionDenied, pg.SqlState);
        }

        // The catalogue stays an ordinary table: app_rw may edit a product (the guard is targeted).
        await using (var update = new NpgsqlCommand("UPDATE shop_products SET position = 5 WHERE id = @id", connection))
        {
            update.Parameters.AddWithValue("id", s.Uniform.Id);
            Assert.Equal(1, await update.ExecuteNonQueryAsync());
        }

        await using var check = NewDb();
        var stored = await check.ShopSaleLines.AsNoTracking().SingleAsync(l => l.Id == line.Id);
        Assert.Equal((1, 85_000m), (stored.Quantity, stored.LineTotal));
    }

    // =====================================================================
    //  RBAC — the report follows the Kassa page (ViewCashBoxes)
    // =====================================================================

    [Theory]
    [InlineData(Roles.Cashier, null, HttpStatusCode.OK)]
    [InlineData(Roles.Admin, null, HttpStatusCode.OK)]
    [InlineData(Roles.Staff, "finance:view", HttpStatusCode.OK)]
    [InlineData(Roles.Staff, "students", HttpStatusCode.Forbidden)]
    [InlineData(Roles.Teacher, null, HttpStatusCode.Forbidden)]
    public async Task Savdo_hisoboti_ruxsati(string role, string? permission, HttpStatusCode expected)
    {
        var client = permission is null
            ? await fixture.Api.ClientAsAsync(role)
            : await fixture.Api.ClientAsAsync(role, permission);

        Assert.Equal(expected, (await client.GetAsync(Sales)).StatusCode);
        Assert.Equal(expected, (await client.GetAsync($"{Sales}/export")).StatusCode);
    }

    [Fact]
    public async Task Savdo_hisoboti_tokensiz_401_va_faqat_korish_xodimi_sota_olmaydi()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.Api.AnonymousClient().GetAsync(Sales)).StatusCode);

        var s = await SetupAsync();
        var viewer = await fixture.Api.ClientAsAsync(Roles.Staff, "finance:view");
        var sell = await viewer.PostAsJsonAsync($"{Boxes}/{s.Box.Id}/in", Sale(s.Type.Id, 85_000m, (s.Book.Id, 1)));
        Assert.Equal(HttpStatusCode.Forbidden, sell.StatusCode);

        var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await teacher.PostAsJsonAsync($"{Boxes}/{s.Box.Id}/in", Sale(s.Type.Id, 85_000m, (s.Book.Id, 1)))).StatusCode);

        Assert.Equal(0m, (await GetBoxAsync(s.Cashier, s.Box.Id)).Balance);
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    /// <summary>A box, a cashier, a Kirim type on "Mahsulot savdosi", a book (85 000) and a uniform (150 000).</summary>
    private sealed record Setup(
        HttpClient Owner, HttpClient Cashier, string CashierId, CashBoxDto Box, TransactionTypeDto Type,
        ShopProductDto Book, ShopProductDto Uniform);

    private async Task<Setup> SetupAsync()
    {
        var owner = await fixture.Api.ClientAsAsync(Roles.SuperAdmin);
        var (cashierUser, _) = await fixture.Api.SeedUserAsync(Roles.Cashier);
        var cashier = fixture.Api.ClientWithToken(
            fixture.Api.TokenFor(Roles.Cashier, cashierUser.Id, cashierUser.FullName, cashierUser.Email));

        var boxResponse = await owner.PostAsJsonAsync(Boxes, new { name = "Do'kon " + Guid.NewGuid().ToString("N")[..8] });
        Assert.Equal(HttpStatusCode.OK, boxResponse.StatusCode);
        var box = (await boxResponse.Content.ReadFromJsonAsync<CashBoxDto>())!;

        var type = await ShopProductsTests.NewInTypeAsync(owner);
        var book = await ShopProductsTests.NewProductAsync(owner, type.Id, 85_000m, ShopProductKind.Book);
        var uniform = await ShopProductsTests.NewProductAsync(owner, type.Id, 150_000m, ShopProductKind.Uniform);

        return new Setup(owner, cashier, cashierUser.Id, box, type, book, uniform);
    }

    private static object Sale(Guid? typeId, decimal amount, params (Guid ProductId, int Quantity)[] lines) => new
    {
        amount,
        method = PaymentMethod.Cash,
        transactionTypeId = typeId,
        lines = lines.Select(l => new { productId = l.ProductId, quantity = l.Quantity }).ToArray(),
    };

    private static Task<CashBoxTransactionRowDto> SellAsync(Setup s, params (Guid ProductId, int Quantity)[] lines) =>
        SellAsync(s, null, lines);

    /// <summary>Sells at catalogue prices (Book 85 000, Uniform 150 000).</summary>
    private static async Task<CashBoxTransactionRowDto> SellAsync(
        Setup s, string? studentId, params (Guid ProductId, int Quantity)[] lines)
    {
        var amount = lines.Sum(l => (l.ProductId == s.Book.Id ? s.Book.Price : s.Uniform.Price) * l.Quantity);
        var response = await s.Cashier.PostAsJsonAsync($"{Boxes}/{s.Box.Id}/in", new
        {
            amount,
            method = PaymentMethod.Cash,
            transactionTypeId = s.Type.Id,
            studentId,
            lines = lines.Select(l => new { productId = l.ProductId, quantity = l.Quantity }).ToArray(),
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CashBoxTransactionRowDto>())!;
    }

    private static async Task<ErrorBody> RefusedAsync(
        Setup s, object body, HttpStatusCode status = HttpStatusCode.BadRequest)
    {
        var response = await s.Cashier.PostAsJsonAsync($"{Boxes}/{s.Box.Id}/in", body);
        Assert.Equal(status, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ErrorBody>())!;
    }

    private async Task<Student> NewStudentAsync()
    {
        var student = new Student { FullName = "Savdo o'quvchi " + Guid.NewGuid().ToString("N")[..8], ClassName = "5-A" };
        await using var db = NewDb();
        db.Students.Add(student);
        await db.SaveChangesAsync();
        return student;
    }

    private static async Task<ShopSalesReportDto> ReportAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync(Sales + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ShopSalesReportDto>())!;
    }

    private static async Task<CashBoxDto> GetBoxAsync(HttpClient client, Guid id)
    {
        var list = (await (await client.GetAsync(Boxes)).Content.ReadFromJsonAsync<List<CashBoxDto>>())!;
        return list.Single(b => b.Id == id);
    }

    private static async Task<CashBoxTransactionsPageDto> JournalAsync(HttpClient client, Guid boxId) =>
        (await (await client.GetAsync($"{Boxes}/transactions?boxId={boxId}"))
            .Content.ReadFromJsonAsync<CashBoxTransactionsPageDto>())!;

    /// <summary>Every row of one sheet as text — inline strings and numbers alike.</summary>
    private static List<List<string>> ReadSheet(SpreadsheetDocument doc, int index)
    {
        var workbook = doc.WorkbookPart!;
        var sheet = workbook.Workbook!.Descendants<Sheet>().ElementAt(index);
        var part = (WorksheetPart)workbook.GetPartById(sheet.Id!.Value!);
        return [.. part.Worksheet!.Descendants<Row>().Select(r => r.Elements<Cell>()
            .Select(c => c.DataType?.Value == CellValues.InlineString ? c.InlineString!.InnerText : c.CellValue?.Text ?? "")
            .ToList())];
    }
}

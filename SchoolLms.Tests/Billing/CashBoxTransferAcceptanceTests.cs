using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using SchoolLms.Application.Billing;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Billing;

// ===========================================================================
//  A transfer waits for the receiving box's responsible person (client, 2026-10-12):
//  "Mendan tasdiqlansa keyin pul menga o'tishi kerak. Xato bo'lsa men qaytarishim kerak,
//  to'g'ri bo'lsa men qabul qilsam keyin pul kassada ko'payib Ulxonoyxonni kassirda kamayishi kerak."
//
//  Plus the journal's .xlsx export, which replaced a CSV Excel could not read.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class CashBoxTransferAcceptanceTests(ApiFixture fixture)
{
    private const string Url = "/api/admin/cash-boxes";

    private sealed record ErrorBody(string Code, string Message);

    [Fact]
    public async Task Kochirish_qabul_qilinmaguncha_pul_hech_qayerga_otmaydi()
    {
        var s = await SetupAsync();

        var sent = await TransferAsync(s.Cashier, s.Till.Id, s.Accountant.Box.Id, 300_000m);
        Assert.Equal(CashBoxRowStatus.Pending, sent.Status);
        Assert.Equal(s.Till.Name, sent.BoxName);
        Assert.Equal(s.Accountant.Box.Name, sent.ToBoxName);

        var till = await GetBoxAsync(s.Cashier, s.Till.Id);
        var accountant = await GetBoxAsync(s.Cashier, s.Accountant.Box.Id);
        Assert.Equal(1_000_000m, till.Balance);
        Assert.Equal(300_000m, till.PendingOut);
        Assert.Equal(0m, accountant.Balance);
        Assert.Equal(300_000m, accountant.PendingIn);

        // The journal lists it, but its money is in no total.
        var page = await JournalAsync(s.Cashier, s.Till.Id);
        Assert.Equal(CashBoxRowStatus.Pending, page.Rows.Single(r => r.Id == sent.Id).Status);
        Assert.Equal(0m, page.OutByMethod![PaymentMethod.Cash]);

        // Only the receiver sees it as waiting for them.
        var incoming = await IncomingAsync(s.Accountant.Client);
        Assert.Equal(sent.Id, Assert.Single(incoming).Id);
        Assert.Empty(await IncomingAsync(s.Cashier));
    }

    [Fact]
    public async Task Qabul_qilinganda_pul_kassirdan_kamayib_buxgalterda_kopayadi()
    {
        var s = await SetupAsync();
        var sent = await TransferAsync(s.Cashier, s.Till.Id, s.Accountant.Box.Id, 300_000m);

        var accept = await s.Accountant.Client.PostAsync($"{Url}/transfers/{sent.Id}/accept", null);
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        Assert.Equal(CashBoxRowStatus.Posted, (await accept.Content.ReadFromJsonAsync<CashBoxTransactionRowDto>())!.Status);

        var till = await GetBoxAsync(s.Cashier, s.Till.Id);
        var accountant = await GetBoxAsync(s.Cashier, s.Accountant.Box.Id);
        Assert.Equal(700_000m, till.Balance);
        Assert.Equal(0m, till.PendingOut);
        Assert.Equal(300_000m, accountant.Balance);
        Assert.Equal(0m, accountant.PendingIn);
        Assert.Empty(await IncomingAsync(s.Accountant.Client));

        // One answer per transfer.
        var again = await s.Accountant.Client.PostAsJsonAsync($"{Url}/transfers/{sent.Id}/return", new { reason = "kech" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("transfer_already_decided", (await Error(again)).Code);

        // An accepted transfer is undone the usual way — storno — and the money goes back.
        var cancel = await s.Owner.PostAsJsonAsync($"{Url}/transactions/{sent.Id}/cancel", new { reason = "xato summa" });
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal(1_000_000m, (await GetBoxAsync(s.Cashier, s.Till.Id)).Balance);
        Assert.Equal(0m, (await GetBoxAsync(s.Cashier, s.Accountant.Box.Id)).Balance);
    }

    [Fact]
    public async Task Qaytarilganda_pul_kassirda_qoladi_va_sababi_korinadi()
    {
        var s = await SetupAsync();
        var sent = await TransferAsync(s.Cashier, s.Till.Id, s.Accountant.Box.Id, 300_000m);

        var noReason = await s.Accountant.Client.PostAsJsonAsync($"{Url}/transfers/{sent.Id}/return", new { reason = "  " });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Equal("reason_required", (await Error(noReason)).Code);

        var returned = await s.Accountant.Client.PostAsJsonAsync(
            $"{Url}/transfers/{sent.Id}/return", new { reason = "Naqd 50 000 kam" });
        Assert.Equal(HttpStatusCode.OK, returned.StatusCode);

        Assert.Equal(1_000_000m, (await GetBoxAsync(s.Cashier, s.Till.Id)).Balance);
        Assert.Equal(0m, (await GetBoxAsync(s.Cashier, s.Accountant.Box.Id)).Balance);

        var row = (await JournalAsync(s.Cashier, s.Till.Id)).Rows.Single(r => r.Id == sent.Id);
        Assert.Equal(CashBoxRowStatus.Returned, row.Status);
        Assert.Equal("Naqd 50 000 kam", row.CancelReason);

        // Nothing moved, so there is nothing to cancel.
        var cancel = await s.Owner.PostAsJsonAsync($"{Url}/transactions/{sent.Id}/cancel", new { reason = "x" });
        Assert.Equal(HttpStatusCode.Conflict, cancel.StatusCode);
        Assert.Equal("transfer_returned", (await Error(cancel)).Code);
    }

    [Fact]
    public async Task Kutayotgan_kochirishni_bekor_qilib_bolmaydi_uni_qabul_qiluvchi_qaytaradi()
    {
        var s = await SetupAsync();
        var sent = await TransferAsync(s.Cashier, s.Till.Id, s.Accountant.Box.Id, 300_000m);

        var cancel = await s.Owner.PostAsJsonAsync($"{Url}/transactions/{sent.Id}/cancel", new { reason = "x" });
        Assert.Equal(HttpStatusCode.Conflict, cancel.StatusCode);
        Assert.Equal("transfer_pending", (await Error(cancel)).Code);
    }

    [Fact]
    public async Task Faqat_qabul_qiluvchi_kassaning_masuli_javob_beradi()
    {
        var s = await SetupAsync();
        var sent = await TransferAsync(s.Cashier, s.Till.Id, s.Accountant.Box.Id, 300_000m);

        // Not the sender, and not even the owner — the person holding the money confirms it.
        foreach (var other in new[] { s.Cashier, s.Owner })
        {
            var accept = await other.PostAsync($"{Url}/transfers/{sent.Id}/accept", null);
            Assert.Equal(HttpStatusCode.Forbidden, accept.StatusCode);
            Assert.Equal("not_transfer_receiver", (await Error(accept)).Code);
        }

        var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await teacher.PostAsync($"{Url}/transfers/{sent.Id}/accept", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await fixture.Api.AnonymousClient().GetAsync($"{Url}/transfers/incoming")).StatusCode);

        Assert.Equal(0m, (await GetBoxAsync(s.Cashier, s.Accountant.Box.Id)).Balance);
    }

    [Fact]
    public async Task Masul_ozi_yuborsa_yoki_masul_bolmasa_kochirish_darrov_otadi()
    {
        var s = await SetupAsync();

        // The accountant pulls money into their own box: nobody else to confirm.
        var own = await TransferAsync(s.Accountant.Client, s.Till.Id, s.Accountant.Box.Id, 100_000m);
        Assert.Equal(CashBoxRowStatus.Posted, own.Status);

        // A box with no responsible person: nobody to confirm either.
        var orphan = await CreateBoxAsync(s.Owner, responsibleUserId: null);
        var toOrphan = await TransferAsync(s.Cashier, s.Till.Id, orphan.Id, 200_000m);
        Assert.Equal(CashBoxRowStatus.Posted, toOrphan.Status);

        Assert.Equal(700_000m, (await GetBoxAsync(s.Cashier, s.Till.Id)).Balance);
        Assert.Equal(100_000m, (await GetBoxAsync(s.Cashier, s.Accountant.Box.Id)).Balance);
        Assert.Equal(200_000m, (await GetBoxAsync(s.Cashier, orphan.Id)).Balance);

        var accept = await s.Accountant.Client.PostAsync($"{Url}/transfers/{own.Id}/accept", null);
        Assert.Equal(HttpStatusCode.Conflict, accept.StatusCode);
        Assert.Equal("transfer_not_awaiting", (await Error(accept)).Code);
    }

    [Fact]
    public async Task Moliya_jurnalida_kutayotgan_va_qaytarilgan_kochirish_holati_korinadi()
    {
        var s = await SetupAsync();
        var waiting = await TransferAsync(s.Cashier, s.Till.Id, s.Accountant.Box.Id, 100_000m);
        var back = await TransferAsync(s.Cashier, s.Till.Id, s.Accountant.Box.Id, 200_000m);
        Assert.Equal(HttpStatusCode.OK, (await s.Accountant.Client.PostAsJsonAsync(
            $"{Url}/transfers/{back.Id}/return", new { reason = "Summa noto'g'ri" })).StatusCode);

        var pending = await FinanceJournalAsync(s.Owner, "pending");
        Assert.Contains(pending.Rows, r => r.Id == waiting.Id);
        Assert.DoesNotContain(pending.Rows, r => r.Id == back.Id);

        var reversed = await FinanceJournalAsync(s.Owner, "reversed");
        var returned = Assert.Single(reversed.Rows, r => r.Id == back.Id);
        Assert.Equal("Summa noto'g'ri", returned.CancelReason);

        var active = await FinanceJournalAsync(s.Owner, "active");
        Assert.DoesNotContain(active.Rows, r => r.Id == waiting.Id || r.Id == back.Id);
    }

    // =====================================================================
    //  Export (.xlsx)
    // =====================================================================

    [Fact]
    public async Task Eksport_haqiqiy_xlsx_ekrandagi_qatorlar_kassir_va_son_bilan()
    {
        var s = await SetupAsync();
        var sent = await TransferAsync(s.Cashier, s.Till.Id, s.Accountant.Box.Id, 300_000m);
        var today = AppClock.Today;

        var response = await s.Cashier.PostAsJsonAsync($"{Url}/transactions/export",
            new { from = today, to = today, boxId = s.Till.Id, ids = new[] { sent.Id } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);

        var rows = ReadSheet(await response.Content.ReadAsByteArrayAsync());
        // Title, blank line, header, then exactly the one row asked for.
        Assert.StartsWith("Kassa tranzaksiyalari", rows[0][0]);
        Assert.Equal(CashBoxTransactionsExport.Headers.ToList(), rows[2]);
        var row = Assert.Single(rows.Skip(3));
        Assert.Equal("1", row[0]);
        Assert.Equal(AppClock.ToLocal(sent.CreatedAt).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture), row[1]);
        Assert.Equal($"{s.Till.Name} → {s.Accountant.Box.Name}", row[2]);
        Assert.Equal("300000.00", row[5]);                 // a number cell, not text
        Assert.Equal("Ko'chirish", row[6]);
        Assert.Equal("Naqd", row[8]);
        Assert.Equal("Tasdiq kutilmoqda", row[9]);
        Assert.Equal(s.CashierName, row[10]);              // the cashier, not the payer
    }

    [Fact]
    public async Task Eksport_teacherga_yopiq()
    {
        var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher);
        var response = await teacher.PostAsJsonAsync($"{Url}/transactions/export", new { });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    private sealed record Person(HttpClient Client, CashBoxDto Box);

    /// <summary>A cashier's till with 1 000 000 cash, and an accountant responsible for their own box.</summary>
    private sealed record Setup(HttpClient Owner, HttpClient Cashier, string CashierName, CashBoxDto Till, Person Accountant);

    private async Task<Setup> SetupAsync()
    {
        var owner = await fixture.Api.ClientAsAsync(Roles.SuperAdmin);

        var (cashierUser, _) = await fixture.Api.SeedUserAsync(Roles.Cashier);
        var cashier = fixture.Api.ClientWithToken(
            fixture.Api.TokenFor(Roles.Cashier, cashierUser.Id, cashierUser.FullName, cashierUser.Email));

        var (accountantUser, _) = await fixture.Api.SeedUserAsync(Roles.Admin);
        var accountant = fixture.Api.ClientWithToken(
            fixture.Api.TokenFor(Roles.Admin, accountantUser.Id, accountantUser.FullName, accountantUser.Email));

        var till = await CreateBoxAsync(owner, cashierUser.Id);
        var accountantBox = await CreateBoxAsync(owner, accountantUser.Id);

        var payIn = await cashier.PostAsJsonAsync($"{Url}/{till.Id}/in", new { amount = 1_000_000m, method = PaymentMethod.Cash });
        Assert.Equal(HttpStatusCode.OK, payIn.StatusCode);

        return new Setup(owner, cashier, cashierUser.FullName, till, new Person(accountant, accountantBox));
    }

    private static async Task<CashBoxDto> CreateBoxAsync(HttpClient client, string? responsibleUserId)
    {
        var response = await client.PostAsJsonAsync(Url, new
        {
            name = "Kassa " + Guid.NewGuid().ToString("N")[..8],
            responsibleUserId,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CashBoxDto>())!;
    }

    private static async Task<CashBoxTransactionRowDto> TransferAsync(HttpClient client, Guid from, Guid to, decimal amount)
    {
        var response = await client.PostAsJsonAsync($"{Url}/{from}/transfer",
            new { toBoxId = to, amount, method = PaymentMethod.Cash, note = "kunlik pul buxgalterga" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CashBoxTransactionRowDto>())!;
    }

    private static async Task<CashBoxDto> GetBoxAsync(HttpClient client, Guid id)
    {
        var list = (await (await client.GetAsync(Url)).Content.ReadFromJsonAsync<List<CashBoxDto>>())!;
        return list.Single(b => b.Id == id);
    }

    private static async Task<List<IncomingCashBoxTransferDto>> IncomingAsync(HttpClient client)
    {
        var response = await client.GetAsync($"{Url}/transfers/incoming");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<IncomingCashBoxTransferDto>>())!;
    }

    private static async Task<CashBoxTransactionsPageDto> JournalAsync(HttpClient client, Guid boxId) =>
        (await (await client.GetAsync($"{Url}/transactions?boxId={boxId}"))
            .Content.ReadFromJsonAsync<CashBoxTransactionsPageDto>())!;

    private static async Task<TransactionJournalPageDto> FinanceJournalAsync(HttpClient client, string status)
    {
        var response = await client.GetAsync($"/api/admin/finance/transactions?kind=cash_transfer&status={status}&pageSize=200");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TransactionJournalPageDto>())!;
    }

    private static async Task<ErrorBody> Error(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ErrorBody>())!;

    /// <summary>Every row of the first sheet as text — inline strings and numbers alike.</summary>
    private static List<List<string>> ReadSheet(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var doc = SpreadsheetDocument.Open(stream, false);
        var sheet = doc.WorkbookPart!.WorksheetParts.First().Worksheet;
        return [.. sheet.Descendants<Row>().Select(r => r.Elements<Cell>()
            .Select(c => c.DataType?.Value == CellValues.InlineString ? c.InlineString!.InnerText : c.CellValue?.Text ?? "")
            .ToList())];
    }
}

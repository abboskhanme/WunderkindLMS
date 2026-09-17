using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// <b>Tranzaksiyalar jurnali</b> — bitta ro'yxatda hamma pul harakati:
/// to'lovlar, to'lov stornolari va chiqimlar
/// (docs/modules/finance-parity.md §2.9, gap F9.01; eksport — F9.03).
///
/// <para>
/// <b>RUXSAT — admin va direktor, kassirga YO'Q (SPEC §4.3).</b> Ikki qavat:
/// klass darajasida <c>[Authorize(Roles = Roles.FinanceStaff)]</c>, amal
/// darajasida <c>[FinanceRole(FinanceAction.ViewBillingReports)]</c> — aynan
/// <see cref="CashDayController"/> va <see cref="FinanceStatementsController"/>
/// dagi darvoza.
/// </para>
/// <para>
/// <b>Nega kassirga umuman ochilmadi</b> (savol §4.3 dan hal qilindi): bu
/// ro'yxat kassirlar KESIMIDAGI ko'rinish (boshqa kassirning cheki, boshqa
/// smenasi) va ustiga chiqimlarni ham qo'shadi — §4.3 jadvalida kassirga
/// "See variance report across cashiers" ⛔, "Record an expense" ⛔. Kassirga
/// o'z smenasi KERAK bo'lsa, u allaqachon ikki joyda bor va ikkalasi ham
/// o'zi bilan cheklangan: <c>GET /api/billing/payments</c>
/// (<c>PaymentsController.OnlyOwnPayments</c> majburan o'z id'sini qo'yadi)
/// va Z-hisobot. Ya'ni bu yerda kassirga "faqat o'z smenasi" ko'rinishini
/// qurish uchinchi nusxa bo'lardi.
/// </para>
/// <para>
/// <b>Bu controllerda YOZISH amali yo'q.</b> Jurnaldagi "Storno qilish"
/// tugmasi mavjud endpoint'ga boradi —
/// <c>POST /api/admin/billing/payments/{id}/reverse</c>
/// (<see cref="PaymentsController"/>, <c>FinanceAction.ReversePayment</c>),
/// chiqim stornosi esa <c>POST /api/admin/expenses/{id}/reverse</c> ga.
/// Ikkinchi yo'l ochilmaydi: bitta amal — bitta endpoint.
/// </para>
/// <para>
/// <b>DI:</b> <c>Program.cs</c> ga tegilmaydi — so'rov klassi holatsiz va
/// faqat <c>AppDbContext</c> ga bog'liq, shuning uchun so'rov doirasida shu
/// yerda quriladi (<see cref="FinanceStatementsController"/> bilan bir xil).
/// </para>
/// </summary>
[ApiController]
[Authorize(Roles = Roles.FinanceStaff)]
[FinanceRole(FinanceAction.ViewBillingReports)]
[Route("api/admin/finance/transactions")]
public sealed class TransactionJournalController(AppDbContext db) : ControllerBase
{
    private const string XlsxMime =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly TransactionJournalQuery _journal = new(db);

    /// <summary>
    /// <c>GET /api/admin/finance/transactions</c> — jurnalning bitta sahifasi
    /// va BUTUN FILTR bo'yicha yakun.
    /// </summary>
    /// <param name="from">Boshlanish sanasi (sukut: joriy oyning 1-kuni).</param>
    /// <param name="to">Tugash sanasi (sukut: bugun).</param>
    /// <param name="direction">kirim (<c>in</c>) yoki chiqim (<c>out</c>).</param>
    /// <param name="kind"><c>payment</c> | <c>reversal</c> | <c>expense</c>.</param>
    /// <param name="method">To'lov usuli (<c>cash</c>, <c>card</c>, …).</param>
    /// <param name="actorId">Kassir yoki chiqimni yozgan/tasdiqlagan foydalanuvchi.</param>
    /// <param name="studentId">Bitta o'quvchi.</param>
    /// <param name="className">Sinf.</param>
    /// <param name="category">Toifa kodi.</param>
    /// <param name="status"><c>active</c> | <c>reversed</c> | <c>pending</c>.</param>
    /// <param name="receiptNo">Chek raqami bo'yicha qidiruv.</param>
    /// <param name="firstPaymentOnly">Faqat o'quvchining birinchi to'lovi (F9.05).</param>
    /// <param name="page">Sahifa raqami (1 dan).</param>
    /// <param name="pageSize">Sahifadagi qatorlar soni.</param>
    /// <param name="sort"><c>date</c> yoki <c>amount</c>.</param>
    /// <param name="desc">Kamayish tartibida (sukut: ha).</param>
    /// <param name="ct">Bekor qilish belgisi.</param>
    [HttpGet]
    public async Task<ActionResult<TransactionJournalPageDto>> List(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? direction,
        [FromQuery] string? kind,
        [FromQuery] string? method,
        [FromQuery] string? actorId,
        [FromQuery] string? studentId,
        [FromQuery] string? className,
        [FromQuery] string? category,
        [FromQuery] string? status,
        [FromQuery] long? receiptNo,
        [FromQuery] bool firstPaymentOnly = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = TransactionJournalQuery.DefaultPageSize,
        [FromQuery] string? sort = null,
        [FromQuery] bool desc = true,
        CancellationToken ct = default)
    {
        var filter = Build(
            from, to, direction, kind, method, actorId, studentId, className,
            category, status, receiptNo, firstPaymentOnly, page, pageSize, sort, desc,
            out var error);

        return error is not null ? error : Ok(await _journal.PageAsync(filter, ct));
    }

    /// <summary>
    /// <c>GET /api/admin/finance/transactions/export</c> — o'sha filtr bo'yicha
    /// .xlsx (F9.03). Summalar HAQIQIY son bo'lib tushadi va oxirida yakun
    /// qatori turadi, ya'ni buxgalter faylni ochib darrov qo'shа oladi.
    /// </summary>
    [HttpGet("export")]
    public async Task<ActionResult> Export(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? direction,
        [FromQuery] string? kind,
        [FromQuery] string? method,
        [FromQuery] string? actorId,
        [FromQuery] string? studentId,
        [FromQuery] string? className,
        [FromQuery] string? category,
        [FromQuery] string? status,
        [FromQuery] long? receiptNo,
        [FromQuery] bool firstPaymentOnly = false,
        [FromQuery] string? sort = null,
        [FromQuery] bool desc = true,
        CancellationToken ct = default)
    {
        var filter = Build(
            from, to, direction, kind, method, actorId, studentId, className,
            category, status, receiptNo, firstPaymentOnly,
            page: 1, pageSize: TransactionJournalQuery.MaxPageSize, sort, desc,
            out var error);
        if (error is not null) return error;

        var rows = await _journal.ExportRowsAsync(filter, ct);
        var totals = (await _journal.PageAsync(filter with { PageSize = 1 }, ct)).Totals;

        string[] headers =
        [
            "Sana", "Turi", "Kim", "Sinf", "Chek №", "Summa",
            "Toifa", "Usul", "Kassir / yozgan", "Izoh", "Holat",
        ];

        var cells = rows.Select(r => (IReadOnlyList<ExcelExport.XlsxCell>)
        [
            ExcelExport.XlsxCell.Of(r.OccurredOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ExcelExport.XlsxCell.Of(KindLabel(r.Kind)),
            ExcelExport.XlsxCell.Of(r.PersonName),
            ExcelExport.XlsxCell.Of(r.ClassName),
            r.ReceiptNo is { } no
                ? ExcelExport.XlsxCell.Num(no)
                : ExcelExport.XlsxCell.Of(null),
            ExcelExport.XlsxCell.Num(r.Amount),
            ExcelExport.XlsxCell.Of(r.CategoryLabel ?? r.Category),
            ExcelExport.XlsxCell.Of(r.Method),
            ExcelExport.XlsxCell.Of(r.ActorName),
            ExcelExport.XlsxCell.Of(r.Note),
            ExcelExport.XlsxCell.Of(StatusLabel(r.Status)),
        ]);

        IReadOnlyList<ExcelExport.XlsxCell> totalsRow =
        [
            ExcelExport.XlsxCell.Of("Jami"),
            ExcelExport.XlsxCell.Of(null),
            ExcelExport.XlsxCell.Of(null),
            ExcelExport.XlsxCell.Of(null),
            ExcelExport.XlsxCell.Of(null),
            ExcelExport.XlsxCell.Num(totals.Net),
            ExcelExport.XlsxCell.Of($"Kirim {totals.TotalIn:0.00} · Chiqim {totals.TotalOut:0.00}"),
        ];

        var bytes = ExcelExport.BuildTable("Tranzaksiyalar", headers, cells, totalsRow);
        return File(bytes, XlsxMime, $"tranzaksiyalar_{AppClock.Now:yyyy-MM-dd}.xlsx");
    }

    // -----------------------------------------------------------------
    //  Yordamchilar
    // -----------------------------------------------------------------

    /// <summary>
    /// So'rov parametrlarini filtrga o'giradi va TEKSHIRADI. Noma'lum qiymat
    /// jimgina e'tiborsiz qoldirilmaydi: "status=faol" deb yozgan odam bo'sh
    /// emas, TO'LIQ ro'yxatni olardi va buni sezmasdi.
    /// </summary>
    /// <param name="error">Tekshiruvdan o'tmasa — tayyor 400 javobi, aks holda null.</param>
    private TransactionJournalFilter Build(
        DateOnly? from, DateOnly? to, string? direction, string? kind, string? method,
        string? actorId, string? studentId, string? className, string? category,
        string? status, long? receiptNo, bool firstPaymentOnly,
        int page, int pageSize, string? sort, bool desc,
        out ActionResult? error)
    {
        var today = AppClock.Today;
        var start = from ?? new DateOnly(today.Year, today.Month, 1);
        var end = to ?? today;

        var filter = new TransactionJournalFilter(
            start, end,
            Clean(direction), Clean(kind), Clean(method), Clean(actorId), Clean(studentId),
            Clean(className), Clean(category), Clean(status), receiptNo, firstPaymentOnly,
            page, pageSize, Clean(sort) ?? TransactionSort.Date, desc);

        error =
            end < start
                ? Invalid($"Davr teskari: {start:yyyy-MM-dd} dan {end:yyyy-MM-dd} gacha.")
            : filter.Direction is { } d && !TransactionDirection.All.Contains(d, StringComparer.Ordinal)
                ? Invalid($"Noma'lum yo'nalish: '{d}'. Ruxsat etilganlar: {Join(TransactionDirection.All)}.")
            : filter.Kind is { } k && !TransactionKind.All.Contains(k, StringComparer.Ordinal)
                ? Invalid($"Noma'lum tur: '{k}'. Ruxsat etilganlar: {Join(TransactionKind.All)}.")
            : filter.Status is { } s && !TransactionStatus.All.Contains(s, StringComparer.Ordinal)
                ? Invalid($"Noma'lum holat: '{s}'. Ruxsat etilganlar: {Join(TransactionStatus.All)}.")
            : filter.Method is { } m && !PaymentMethod.All.Contains(m, StringComparer.Ordinal)
                ? Invalid($"Noma'lum to'lov usuli: '{m}'. Ruxsat etilganlar: {Join(PaymentMethod.All)}.")
            : !TransactionSort.All.Contains(filter.Sort, StringComparer.Ordinal)
                ? Invalid($"Noma'lum saralash: '{filter.Sort}'. Ruxsat etilganlar: {Join(TransactionSort.All)}.")
            : null;

        return filter;
    }

    private BadRequestObjectResult Invalid(string message) =>
        BadRequest(new BillingErrorDto("invalid_filter", message));

    private static string Join(IReadOnlyList<string> values) => string.Join(", ", values);

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string KindLabel(string kind) => kind switch
    {
        TransactionKind.Payment => "To'lov",
        TransactionKind.Reversal => "Storno",
        TransactionKind.Expense => "Chiqim",
        _ => kind,
    };

    private static string StatusLabel(string status) => status switch
    {
        TransactionStatus.Active => "Faol",
        TransactionStatus.Reversed => "Storno qilingan",
        TransactionStatus.Pending => "Tasdiq kutmoqda",
        _ => status,
    };
}

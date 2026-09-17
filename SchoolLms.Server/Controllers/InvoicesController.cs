using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// <b>Hisob-fakturalar registri</b> — har bir hisoblangan oy: kim, qaysi
/// toifa, qancha, qancha to'langan, qancha qoldi
/// (docs/modules/finance-parity.md §2.10, gaplar F10.01 va F10.02).
///
/// <para>
/// <b>Nega alohida controller.</b> <see cref="BillingCatalogController"/>
/// P1-08 ning shartnomasi (toifalar, obunalar, chegirmalar) va uning marshruti
/// ham <c>api/admin/billing</c> — lekin bu yerdagi yo'llar boshqa
/// (<c>invoices</c>, <c>invoices/{id}/void</c>), ya'ni to'qnashuv yo'q.
/// Ikkinchi ta'rif ham paydo bo'lmaydi: ikkalasi ham AYNAN bitta
/// <see cref="IInvoiceService"/> ni chaqiradi.
/// </para>
///
/// <para>
/// <b>RUXSAT ikki qavatli (SPEC §4.3).</b> Klass darajasi
/// <c>[Authorize(Roles = Roles.FinanceStaff)]</c> — admin va direktor; kassir
/// bu yerga umuman kirmaydi (§4.3 da unga "Change monthly fee / subscription"
/// ⛔, hisobot ustuni ham yo'q). Metod darajasi:
/// <see cref="FinanceAction.ViewBillingReports"/> o'qishga,
/// <see cref="FinanceAction.ManageSubscriptions"/> bekor qilishga.
/// </para>
///
/// <para>
/// <b>BU CONTROLLERDA O'CHIRISH YO'Q — VA BO'LMAYDI.</b> Xato hisoblangan oy
/// <c>void</c> qilinadi: qator joyida qoladi, jurnal partiyasi esa ko'zgu
/// satrlar bilan qaytariladi (SPEC §4.1). EduSchool'dagi
/// <c>/student/subscription/delete-all</c> ning muqobili shu (§2.0).
/// </para>
/// </summary>
[ApiController]
[Authorize(Roles = Roles.FinanceStaff)]
[Route("api/admin/billing/invoices")]
[BillingFault]
public sealed class InvoicesController(IInvoiceService invoices) : ControllerBase
{
    /// <summary>
    /// <c>GET /api/admin/billing/invoices</c> — registrning bitta sahifasi va
    /// BUTUN FILTR bo'yicha yakun (F10.01).
    /// </summary>
    /// <param name="studentId">Bitta o'quvchi.</param>
    /// <param name="categoryId">To'lov toifasi.</param>
    /// <param name="fromMonth">Boshlanish oyi (kuni ahamiyatsiz; sukut: joriy oy).</param>
    /// <param name="toMonth">Tugash oyi (sukut: joriy oy).</param>
    /// <param name="status"><c>open</c> | <c>partial</c> | <c>paid</c> | <c>void</c>.</param>
    /// <param name="onlyOverdue">Faqat muddati o'tganlar.</param>
    /// <param name="onlyDebtors">Faqat qoldig'i borlar.</param>
    /// <param name="className">Sinf.</param>
    /// <param name="page">Sahifa raqami (1 dan).</param>
    /// <param name="pageSize">Sahifadagi qatorlar soni.</param>
    /// <param name="ct">Bekor qilish belgisi.</param>
    [HttpGet]
    [FinanceRole(FinanceAction.ViewBillingReports)]
    public async Task<ActionResult<InvoicePageDto>> List(
        [FromQuery] string? studentId,
        [FromQuery] Guid? categoryId,
        [FromQuery] DateOnly? fromMonth,
        [FromQuery] DateOnly? toMonth,
        [FromQuery] string? status,
        [FromQuery] bool onlyOverdue = false,
        [FromQuery] bool onlyDebtors = false,
        [FromQuery] string? className = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = InvoiceService.DefaultPageSize,
        CancellationToken ct = default)
    {
        var clean = Clean(status);
        if (clean is not null && !InvoiceStatus.All.Contains(clean, StringComparer.Ordinal))
            throw BillingRuleException.Invalid("invalid_status",
                $"Noma'lum holat: '{status}'. Ruxsat etilganlar: {string.Join(", ", InvoiceStatus.All)}.");

        var today = AppClock.Today;
        var thisMonth = new DateOnly(today.Year, today.Month, 1);

        var query = new InvoicePageQuery(
            Clean(studentId), categoryId,
            fromMonth ?? thisMonth, toMonth ?? thisMonth,
            clean, onlyOverdue, Clean(className), onlyDebtors,
            page, pageSize);

        if (query.ToMonth < query.FromMonth)
            throw BillingRuleException.Invalid("invalid_period",
                $"Davr teskari: {query.FromMonth:yyyy-MM} dan {query.ToMonth:yyyy-MM} gacha.");

        return Ok(await invoices.ListPageAsync(query, ct));
    }

    /// <summary>
    /// <c>GET /api/admin/billing/invoices/export</c> — o'sha filtr bo'yicha
    /// .xlsx (F10.05). Filtrlar <see cref="List"/> bilan AYNAN bir xil.
    /// </summary>
    [HttpGet("export")]
    [FinanceRole(FinanceAction.ViewBillingReports)]
    public async Task<ActionResult> Export(
        [FromQuery] string? studentId,
        [FromQuery] Guid? categoryId,
        [FromQuery] DateOnly? fromMonth,
        [FromQuery] DateOnly? toMonth,
        [FromQuery] string? status,
        [FromQuery] bool onlyOverdue = false,
        [FromQuery] bool onlyDebtors = false,
        [FromQuery] string? className = null,
        CancellationToken ct = default)
    {
        var clean = Clean(status);
        if (clean is not null && !InvoiceStatus.All.Contains(clean, StringComparer.Ordinal))
            throw BillingRuleException.Invalid("invalid_status",
                $"Noma'lum holat: '{status}'. Ruxsat etilganlar: {string.Join(", ", InvoiceStatus.All)}.");

        var today = AppClock.Today;
        var thisMonth = new DateOnly(today.Year, today.Month, 1);

        var query = new InvoicePageQuery(
            Clean(studentId), categoryId,
            fromMonth ?? thisMonth, toMonth ?? thisMonth,
            clean, onlyOverdue, Clean(className), onlyDebtors,
            Page: 1, PageSize: InvoiceService.MaxPageSize);

        if (query.ToMonth < query.FromMonth)
            throw BillingRuleException.Invalid("invalid_period",
                $"Davr teskari: {query.FromMonth:yyyy-MM} dan {query.ToMonth:yyyy-MM} gacha.");

        var rows = await invoices.ExportRowsAsync(query, ct);
        var totals = (await invoices.ListPageAsync(query with { PageSize = 1 }, ct)).Totals;

        // Sinf bu yerda YO'Q: `InvoiceDto` muzlatilgan (P1-06) va uni
        // ko'tarmaydi — jadvalda u FAQAT yonma-yon `classNames` xaritasidan
        // olinadi (§2.10 izohi, `InvoicePageDto.ClassNames`), eksport esa
        // BUTUN FILTR bo'yicha (sahifasiz) ketadi. EduSchool'ning o'zida ham
        // bu ustun yo'q (§2.10.1: STUDENT · TRANSACTION_TYPE · TO_BE_PAID ·
        // PAID · AMOUNT · STATE · PERIOD · DATE).
        string[] headers =
        [
            "O'quvchi", "Toifa", "Oy", "Summa", "Chegirma",
            "To'lanadi", "To'langan", "Qoldiq", "Muddat", "Holat",
        ];

        var cells = rows.Select(r => (IReadOnlyList<ExcelExport.XlsxCell>)
        [
            ExcelExport.XlsxCell.Of(r.StudentName),
            ExcelExport.XlsxCell.Of(r.CategoryName),
            ExcelExport.XlsxCell.Of(r.PeriodMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture)),
            ExcelExport.XlsxCell.Num(r.Amount),
            ExcelExport.XlsxCell.Num(r.Discount),
            ExcelExport.XlsxCell.Num(r.Payable),
            ExcelExport.XlsxCell.Num(r.Paid),
            ExcelExport.XlsxCell.Num(r.Remaining),
            ExcelExport.XlsxCell.Of(r.DueOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ExcelExport.XlsxCell.Of(InvoiceStatusLabels.GetValueOrDefault(r.Status, r.Status)),
        ]);

        IReadOnlyList<ExcelExport.XlsxCell> totalsRow =
        [
            ExcelExport.XlsxCell.Of("Jami"),
            ExcelExport.XlsxCell.Of(null),
            ExcelExport.XlsxCell.Of(null),
            ExcelExport.XlsxCell.Num(totals.Amount),
            ExcelExport.XlsxCell.Num(totals.Discount),
            ExcelExport.XlsxCell.Num(totals.Payable),
            ExcelExport.XlsxCell.Num(totals.Paid),
            ExcelExport.XlsxCell.Num(totals.Remaining),
            ExcelExport.XlsxCell.Of(null),
            ExcelExport.XlsxCell.Of(null),
        ];

        var bytes = ExcelExport.BuildTable("Hisob-fakturalar", headers, cells, totalsRow);
        return File(bytes, XlsxMime,
            $"hisob-fakturalar_{AppClock.Today:yyyy-MM-dd}.xlsx");
    }

    private const string XlsxMime =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Holat kodi → o'zbekcha nom — FE dagi <c>InvoiceStatusLabels</c> bilan bir xil.</summary>
    private static readonly IReadOnlyDictionary<string, string> InvoiceStatusLabels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [InvoiceStatus.Open] = "Ochiq",
            [InvoiceStatus.Partial] = "Qisman to'langan",
            [InvoiceStatus.Paid] = "To'langan",
            [InvoiceStatus.Void] = "Bekor qilingan",
        };

    /// <summary>
    /// <c>POST /api/admin/billing/invoices/{id}/void</c> — xato hisoblangan
    /// oyni bekor qiladi (F10.02). Sabab MAJBURIY.
    ///
    /// <para>
    /// Javob kodlari (hammasi o'zbekcha <c>message</c> bilan, 500 emas):
    /// <b>400</b> sabab bo'sh · <b>404</b> topilmadi · <b>409</b>
    /// <c>already_void</c> · <b>409</b> <c>has_effective_allocation</c> —
    /// avval to'lovni storno qilish kerak · <b>403</b> <c>self_reversal</c> —
    /// jurnalga o'zi qo'ygan odam o'zi bekor qila olmaydi (SPEC §4.5).
    /// </para>
    /// </summary>
    [HttpPost("{id:guid}/void")]
    [FinanceRole(FinanceAction.ManageSubscriptions)]
    public async Task<ActionResult<InvoiceDto>> Void(
        Guid id, [FromBody] VoidInvoiceRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // SPEC §4.4 — bekor qiluvchi HAR DOIM JWT'dan, so'rov tanasidan emas.
        var actorId = FinanceActor.RequireUserId(User);
        return Ok(await invoices.VoidAsync(id, request.Reason ?? string.Empty, actorId, ct));
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

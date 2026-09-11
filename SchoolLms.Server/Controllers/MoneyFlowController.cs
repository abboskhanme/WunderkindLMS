using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Billing;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Maktabning pul aylanmasi — 3D halqa vizualizatsiyasi uchun graf. Vazifa: P1-26.
///
/// <para>
/// <b>Ruxsat.</b> Ikki qavat: tashqi qo'pol darvoza
/// <c>[Authorize(Roles = Roles.FinanceStaff)]</c> va amal darajasidagi qoida
/// <c>[FinanceRole(FinanceAction.ViewBillingReports)]</c>. Ikkalasi ham bir xil
/// javob beradi (kassir — 403), lekin ikkinchisi YAGONA MANBA:
/// <c>FinanceMatrix.Rules</c> o'zgarsa, bu endpoint ham avtomatik ergashadi.
/// Bu yerda hech qanday <c>if (User.IsInRole(...))</c> yozilmaydi.
/// </para>
///
/// <para>
/// <b>Nega alohida controller.</b> Eski <c>FinanceController</c> ga qo'shib
/// bo'lmaydi: u <c>[AdminPerm("finance")]</c> ostida, ya'ni HAR QANDAY xodim
/// (staff) uni O'QIY oladi (<c>AdminPermAttribute</c> 42-qator). Pul aylanmasi
/// esa SPEC §4.3 bo'yicha faqat admin va direktorniki.
/// </para>
///
/// <para>
/// <b>DI:</b> <c>IAppDbContext</c> allaqachon ro'yxatdan o'tgan
/// (<c>Program.cs:56</c>), <see cref="MoneyFlowQueries"/> esa statik — shuning
/// uchun bu endpoint <c>Program.cs</c> ga TEGMASDAN ishlaydi (P1-15 hali
/// oldinda). Batafsil: docs/PENDING_WIRING.md.
/// </para>
/// </summary>
[ApiController]
[Authorize(Roles = Roles.FinanceStaff)]
[FinanceRole(FinanceAction.ViewBillingReports)]
[Route("api/admin/finance/money-flow")]
public sealed class MoneyFlowController(IAppDbContext db) : ControllerBase
{
    /// <summary>
    /// <c>GET /api/admin/finance/money-flow?from=YYYY-MM-DD&amp;to=YYYY-MM-DD</c>
    ///
    /// <para>
    /// Sanalar berilmasa — joriy kalendar yilining 1-yanvaridan bugungacha
    /// (eski <c>FinancePage</c> dagi standart davr bilan bir xil, foydalanuvchi
    /// ikki ekranda har xil raqam ko'rmasin).
    /// </para>
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<MoneyFlowDto>> Get(
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct)
    {
        var today = AppClock.Today;

        if (!TryParseDate(from, new DateOnly(today.Year, 1, 1), out var fromDate))
            return BadRequest(new { message = "'from' sanasi noto'g'ri. Format: YYYY-MM-DD." });

        if (!TryParseDate(to, today, out var toDate))
            return BadRequest(new { message = "'to' sanasi noto'g'ri. Format: YYYY-MM-DD." });

        if (fromDate > toDate)
            return BadRequest(new { message = "Boshlanish sanasi tugash sanasidan keyin bo'lishi mumkin emas." });

        return await MoneyFlowQueries.BuildAsync(db, fromDate, toDate, ct);
    }

    /// <summary>
    /// Bo'sh/berilmagan qiymat uchun standart sana, aks holda QAT'IY
    /// <c>YYYY-MM-DD</c>. Erkin format qabul qilinmaydi: "01.02.2026" ni
    /// mintaqaga qarab ikki xil o'qish mumkin, moliyada esa bu jimgina bir
    /// oylik siljish degani.
    /// </summary>
    private static bool TryParseDate(string? raw, DateOnly fallback, out DateOnly value)
    {
        if (string.IsNullOrWhiteSpace(raw)) { value = fallback; return true; }

        return DateOnly.TryParseExact(
            raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }
}

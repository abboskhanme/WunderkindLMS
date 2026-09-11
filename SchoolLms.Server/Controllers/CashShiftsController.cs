using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Kassa smenasi: ochish, joriy smena, yopish, Z-hisobot, ro'yxat (SPEC §4.2, §4.6).
/// Vazifa: P1-10.
///
/// <para>
/// <b>Bu controller'da PUT ham, DELETE ham YO'Q va bo'lmaydi.</b> Smena
/// tarixdir: u yopiladi, tahrirlanmaydi va o'chirilmaydi. Xato sanalgan naqd
/// keyingi smena hisobotida izoh bilan tuzatiladi, yopilgan qatorni qayta
/// yozish bilan emas.
/// </para>
///
/// <para>
/// <b>SPEC §4.4 — shaxs serverda aniqlanadi.</b> Hech bir endpoint so'rov
/// tanasidan <c>cashierId</c> yoki <c>closedBy</c> olmaydi: ikkalasi ham
/// <see cref="FinanceActor.RequireUserId"/> orqali JWT claim'idan keladi.
/// Muzlatilgan so'rov tiplarida (<see cref="OpenShiftRequest"/>,
/// <see cref="CloseShiftRequest"/>) bunday maydon umuman yo'q.
/// </para>
///
/// <para>
/// <b>Ruxsat ikki qavatda.</b> (1) <see cref="FinanceRoleAttribute"/> — SPEC §4.3
/// jadvali bo'yicha "bu rol kassaga umuman kira oladimi"; (2) egalik tekshiruvi —
/// "AYNAN shu smena seniki mi". Ikkinchisini yopishda XIZMAT bajaradi
/// (<c>CashShiftService.RequireMayCloseAsync</c>), ya'ni endpoint chetlab
/// o'tilsa ham qoida kuchda qoladi.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[Route("api/cash/shifts")]
[Produces("application/json")]
public sealed class CashShiftsController(ICashShiftService shifts) : ControllerBase
{
    // =====================================================================
    //  Kassirning o'z smenasi
    // =====================================================================

    /// <summary>
    /// Joriy OCHIQ smena. Ochiq smena bo'lmasa — <b>204 No Content</b>
    /// (bu kutilgan holat, xato emas: kassa ekrani "smenani oching" deb
    /// ko'rsatadi, 404 esa xato jurnalini bezovta qilardi).
    /// </summary>
    [HttpGet("current")]
    [FinanceRole(FinanceAction.ManageOwnShift)]
    public async Task<ActionResult<CashShiftDto>> Current(CancellationToken ct)
    {
        var shift = await shifts.CurrentAsync(FinanceActor.RequireUserId(User), ct);
        return shift is null ? NoContent() : Ok(shift);
    }

    /// <summary>
    /// Smena ochish. Kassirda allaqachon ochiq smena bo'lsa — <b>409</b>
    /// (SPEC §4.2). Buni bazadagi <c>ux_cash_shifts_one_open_per_cashier</c>
    /// indeksi ham kafolatlaydi, ya'ni parallel ikki so'rov ham o'tib keta olmaydi.
    /// </summary>
    /// <param name="request">Ochilish qoldig'i (sukut 0 — docs/ASSUMPTIONS.md, Q11).</param>
    [HttpPost("open")]
    [FinanceRole(FinanceAction.ManageOwnShift)]
    public async Task<ActionResult<CashShiftDto>> Open(OpenShiftRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await shifts.OpenAsync(
                FinanceActor.RequireUserId(User), request.OpeningFloat, ct));
        }
        catch (CashShiftException ex)
        {
            return Failure(ex);
        }
    }

    /// <summary>
    /// Smenani yopish. <c>countedCash</c> — kassir QO'LDA sanagan naqd,
    /// MAJBURIY (SPEC §4.2); <c>expectedCash</c> ni server ledger'dan hisoblaydi
    /// va uni so'rovdan QABUL QILMAYDI.
    ///
    /// <para>
    /// O'zganing smenasini yopish — <b>403</b>; admin va direktor yopa oladi,
    /// va <c>closed_by</c> da aynan kim yopgani qoladi.
    /// </para>
    /// </summary>
    [HttpPost("{id:guid}/close")]
    [FinanceRole(FinanceAction.ManageOwnShift)]
    [Consumes("application/json")]
    public async Task<ActionResult<CashShiftDto>> Close(
        Guid id, [FromBody] JsonElement body, CancellationToken ct)
    {
        if (!TryReadCloseRequest(body, out var request, out var error))
            return BadRequest(new { code = CashShiftError.InvalidCountedCash, message = error });

        try
        {
            return Ok(await shifts.CloseAsync(
                id, FinanceActor.RequireUserId(User), request.CountedCash, request.Note, ct));
        }
        catch (CashShiftException ex)
        {
            return Failure(ex);
        }
    }

    // =====================================================================
    //  Hisobotlar
    // =====================================================================

    /// <summary>
    /// Smena yakuni (SPEC §4.6): ochilish qoldig'i, to'lov usullari kesimi,
    /// toifalar kesimi, chek raqamlari oralig'i, kutilgan / sanalgan / nomuvofiqlik.
    ///
    /// <para>
    /// Kassir FAQAT o'z smenasining hisobotini ko'radi — SPEC §4.3 unga
    /// "kassirlar kesimidagi nomuvofiqlik" qatorini bermaydi.
    /// </para>
    /// </summary>
    [HttpGet("{id:guid}/z-report")]
    [FinanceRole(FinanceAction.ManageOwnShift)]
    public async Task<ActionResult<ZReportDto>> ZReport(Guid id, CancellationToken ct)
    {
        ZReportDto report;
        try
        {
            report = await shifts.ZReportAsync(id, ct);
        }
        catch (CashShiftException ex)
        {
            return Failure(ex);
        }

        // Egalik tekshiruvi hisobot yig'ilgandan KEYIN: u faqat o'qiydi, hech
        // narsani o'zgartirmaydi, va smenaning kassirini bilish uchun baribir
        // o'sha qator kerak edi. Javob berilmaydi — 403 shu yerda to'xtatadi.
        if (!IsSupervisor && !string.Equals(
                report.Shift.CashierId, FinanceActor.RequireUserId(User), StringComparison.Ordinal))
            return Failure(new CashShiftException(CashShiftError.NotYourShift,
                "Bu smena boshqa kassirniki — uning Z-hisobotini admin yoki direktor ko'radi (SPEC §4.3)."));

        return Ok(report);
    }

    /// <summary>
    /// Smenalar ro'yxati. Admin va direktor — hamma kassirlar kesimida
    /// (SPEC §4.3, "See variance report across cashiers"); kassir esa faqat
    /// O'ZINIKINI, so'rovda boshqa <c>cashierId</c> yozgan bo'lsa ham.
    /// </summary>
    [HttpGet]
    [FinanceRole(FinanceAction.ManageOwnShift)]
    public async Task<ActionResult<IEnumerable<CashShiftDto>>> List(
        [FromQuery] string? cashierId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? status,
        [FromQuery] bool onlyWithVariance,
        CancellationToken ct)
    {
        // Filtrni JIMGINA tor qilamiz, 403 bermaymiz: kassir uchun "smenalar
        // ro'yxati" O'Z ro'yxati degani, va u boshqasini so'rayotganini bilmasligi
        // ham mumkin (masalan sahifa umumiy komponentdan filtr yuborsa).
        var scopedCashierId = IsSupervisor ? cashierId : FinanceActor.RequireUserId(User);

        var query = new CashShiftQuery(scopedCashierId, from, to, status, onlyWithVariance);
        return Ok(await shifts.ListAsync(query, ct));
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    /// <summary>
    /// Nazoratchi (admin/direktor) mi? Rollar ro'yxati YAGONA joyda —
    /// <see cref="CashShiftService.SupervisorRoles"/> da, chunki AYNAN o'sha
    /// ro'yxat xizmat ichida ham ("o'zganing smenasini yopish") ishlatiladi.
    /// Ikki nusxa bo'lsa, ular albatta bir-biridan uzoqlashadi.
    /// </summary>
    private bool IsSupervisor => CashShiftService.SupervisorRoles.Any(User.IsInRole);

    /// <summary>
    /// Xizmat xatosini HTTP holatiga o'giradi. Kod (<see cref="CashShiftError"/>)
    /// javob tanasida ham qoladi — frontend matnni tahlil qilmasin.
    /// </summary>
    private ObjectResult Failure(CashShiftException ex)
    {
        var status = ex.Code switch
        {
            CashShiftError.NotFound => StatusCodes.Status404NotFound,
            CashShiftError.NotYourShift => StatusCodes.Status403Forbidden,
            CashShiftError.AlreadyOpen => StatusCodes.Status409Conflict,
            CashShiftError.AlreadyClosed => StatusCodes.Status409Conflict,
            CashShiftError.NotOpen => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };
        return StatusCode(status, new { code = ex.Code, message = ex.Message });
    }

    /// <summary>
    /// <c>countedCash</c> ning SO'ROVDA BOR-YO'QLIGINI tekshiradi.
    ///
    /// <para>
    /// Nega qo'lda: <see cref="CloseShiftRequest"/> da <c>CountedCash</c> —
    /// <c>decimal</c> (nullable emas, muzlatilgan shartnoma). Maydon so'rovda
    /// umuman bo'lmasa System.Text.Json unga JIMGINA 0 qo'yadi va model
    /// validatsiyasi buni to'g'ri deb hisoblaydi — ya'ni "sanamadim" bilan
    /// "sanadim, nol chiqdi" farqsiz bo'lib qolardi. Nol esa haqiqiy va
    /// qonuniy qiymat (ochilish qoldig'i 0 va naqd to'lov bo'lmagan smena).
    /// SPEC §4.2 esa smenani SANALGAN naqdsiz yopishni taqiqlaydi, shuning
    /// uchun maydonning mavjudligi aniq tekshiriladi.
    /// </para>
    /// </summary>
    private static bool TryReadCloseRequest(
        JsonElement body, out CloseShiftRequest request, out string? error)
    {
        request = default!;
        error = null;

        if (body.ValueKind != JsonValueKind.Object)
        {
            error = "So'rov tanasi JSON obyekt bo'lishi kerak: { \"countedCash\": 0, \"note\": null }.";
            return false;
        }

        if (!TryGetProperty(body, "countedCash", out var counted)
            || counted.ValueKind is not (JsonValueKind.Number or JsonValueKind.String)
            || !TryReadDecimal(counted, out var countedCash))
        {
            error = "Sanalgan naqd (`countedCash`) majburiy: smenani sanamasdan yopib bo'lmaydi (SPEC §4.2).";
            return false;
        }

        string? note = null;
        if (TryGetProperty(body, "note", out var noteElement) && noteElement.ValueKind == JsonValueKind.String)
            note = noteElement.GetString();

        request = new CloseShiftRequest(countedCash, note);
        return true;
    }

    /// <summary>Maydonni registrga qaramay topadi (klient camelCase yuboradi, qo'lda so'rov — har xil).</summary>
    private static bool TryGetProperty(JsonElement body, string name, out JsonElement value)
    {
        foreach (var property in body.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }

        value = default;
        return false;
    }

    /// <summary>Sonni ham, satr ichidagi sonni ham qabul qiladi ("150000" — JS'ning odatiy chiqishi).</summary>
    private static bool TryReadDecimal(JsonElement element, out decimal value)
    {
        if (element.ValueKind == JsonValueKind.Number) return element.TryGetDecimal(out value);

        return decimal.TryParse(
            element.GetString(),
            System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture,
            out value);
    }
}

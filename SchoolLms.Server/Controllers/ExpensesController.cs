using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Billing;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

// ===========================================================================
//  Chiqim endpoint'lari — SPEC §3.7, §4.3 ("Record an expense"), §4.5.
// ===========================================================================
//
//  BU CONTROLLERDA O'ZGARTIRISH VA O'CHIRISH AMALLARI YO'Q — VA BO'LMAYDI.
//  ---------------------------------------------------------------------
//  Chiqim jurnalga tushgan zahoti u pul yozuvi bo'ladi, pul yozuvi esa
//  o'zgarmas (SPEC §4.1). Xato chiqim FAQAT storno bilan tuzatiladi: original
//  qator ham, jurnal satrlari ham joyida qoladi, ustiga ko'zgu satrlar
//  qo'shiladi. Qabul mezoni: bu faylda tahrirlash/o'chirish HTTP fe'llari NOL
//  marta uchraydi — shuning uchun ular bu izohda ham YOZILMAGAN (grep ularni
//  izohdan topib, mezonni buzilgan deb ko'rsatardi). Tekshiruvning o'zi
//  `ExpensesTests` da refleksiya bilan: u qurilgan assembly'ni ko'radi va
//  so'zga emas, atributga qaraydi.
//
//  RUXSAT IKKI QAVATLI (BillingCatalogController bilan bir xil uslub)
//  -----------------------------------------------------------------
//  1) Klass darajasi: `[Authorize(Roles = Roles.FinanceStaff)]` — admin va
//     direktor. KASSIR BU YERGA UMUMAN KIRMAYDI: SPEC §4.3 jadvalining
//     "Record an expense" qatorida kassir ustuni ⛔. `teacher`/`staff` ham
//     shu darvozada to'xtaydi.
//  2) Metod darajasi: har amalda `[FinanceRole(...)]` — §4.3 ning AYNAN o'sha
//     qatori. Tasdiqlash va storno faqat direktorga ochiq, chunki ikkalasi ham
//     §4.5 dagi "ikkinchi shaxs" amali.
//
//  NEGA `ReverseExpense` degan alohida amal yo'q: `FinanceAction` P1-06 da
//  muzlatilgan va unda `ApproveExpense` bor — "chiqimni tasdiqlash,
//  yaratuvchidan boshqa shaxs (SPEC §4.5)". Storno ham aynan shu ma'noni
//  bildiradi (pul chiqishini ikkinchi shaxs bekor qiladi), shuning uchun
//  yangi enum a'zosi qo'shish o'rniga mavjudi ishlatiladi — ruxsatning ikkita
//  manbasi paydo bo'lmasligi uchun.
// ===========================================================================

/// <summary>
/// Chiqimlar: kiritish, ro'yxat, tasdiqlash va storno. Batafsil: fayl boshidagi izoh.
///
/// <para>
/// Xato javoblari <c>{ "code": ..., "message": ... }</c> shaklida —
/// <see cref="BillingFaultAttribute"/> orqali, moliya modulining qolgan
/// qismidagi bilan bir xil (docs/PENDING_WIRING.md §11).
/// </para>
/// </summary>
[ApiController]
[Authorize(Roles = Roles.FinanceStaff)]
[Route("api/admin/expenses")]
[BillingFault]
[Produces("application/json")]
public class ExpensesController(IExpenseService expenses) : ControllerBase
{
    /// <summary>
    /// SPEC §4.4 — SERVER ANIQLAYDIGAN SHAXS. Bu nomlar so'rov tanasida
    /// uchrasa, so'rov <b>rad etiladi</b>; jimgina e'tiborsiz qoldirilmaydi.
    ///
    /// <para>
    /// Nega jim o'tkazib yubormaymiz: <c>System.Text.Json</c> notanish maydonni
    /// sukut bo'yicha TASHLAB ketadi. Ya'ni admin <c>{"createdBy": "direktor"}</c>
    /// yuborsa, so'rov muvaffaqiyatli o'tardi va u chiqimni boshqa odam nomiga
    /// yozdim deb o'ylab qolardi — aslida yozuv o'z nomiga tushgan bo'lardi.
    /// </para>
    /// </summary>
    private static readonly string[] ServerDerivedFields =
    [
        "createdBy", "createdByName", "createdAt",
        "approvedBy", "approvedByName", "approverId",
        "reversedBy", "reversedByName", "reversedOn",
        "status", "postedOn", "settlementAccount",
    ];

    /// <summary>MVC ning o'z sozlamalari bilan bir xil: camelCase, registrga befarq.</summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // -----------------------------------------------------------------
    //  O'qish
    // -----------------------------------------------------------------

    /// <summary>
    /// Chiqimlar ro'yxati (yangisidan eskisiga, eng ko'pi 2000 qator).
    /// Filtrlar: davr (<paramref name="from"/> … <paramref name="to"/>,
    /// <c>on_date</c> bo'yicha), toifa va holat.
    ///
    /// <para>
    /// <b>Yig'indi bu yerda qaytmaydi — ataylab.</b> Davr bo'yicha chiqim
    /// summasini <c>GET /api/admin/finance/pnl</c> beradi: u jurnaldan
    /// hisoblaydi, ya'ni storno qilingan qatorlar o'z-o'zidan chiqib ketadi.
    /// Bu yerda ikkinchi marta summalash ikkita "haqiqiy chiqim" raqamini
    /// keltirib chiqarardi, va ular albatta bir kun bir-biridan farq qilardi.
    /// </para>
    /// </summary>
    /// <param name="status"><c>pending</c> | <c>posted</c> | <c>reversed</c>.
    /// Direktorning "tasdiq kutmoqda" ro'yxati — <c>?status=pending</c>.</param>
    [HttpGet]
    [FinanceRole(FinanceAction.ViewBillingReports)]
    public async Task<ActionResult<IEnumerable<ExpenseDto>>> List(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] string? category, [FromQuery] string? status,
        CancellationToken ct)
    {
        var list = await expenses.ListAsync(new ExpenseQuery(from, to, category, status), ct);
        return Ok(list);
    }

    /// <summary>Bitta chiqim.</summary>
    [HttpGet("{id:guid}")]
    [FinanceRole(FinanceAction.ViewBillingReports)]
    public async Task<ActionResult<ExpenseDto>> Get(Guid id, CancellationToken ct)
    {
        var expense = await expenses.GetAsync(id, ct);
        return expense is null
            ? NotFound(new BillingErrorDto("expense_not_found", "Chiqim topilmadi."))
            : Ok(expense);
    }

    // -----------------------------------------------------------------
    //  Kiritish
    // -----------------------------------------------------------------

    /// <summary>
    /// Chiqimni qayd etadi. Chegaradan (<c>billing_settings.expense_approval_threshold</c>,
    /// sukut 5 000 000 so'm) katta bo'lmasa — <c>expenses</c> qatori va
    /// <c>debit expense:&lt;toifa&gt; / credit cash|bank</c> juftligi BITTA
    /// tranzaksiyada yoziladi, javobdagi <c>status</c> = <c>posted</c>.
    ///
    /// <para>
    /// Katta bo'lsa — jurnalga TUSHMAYDI, <c>status</c> = <c>pending</c> bo'ladi
    /// va pul ikkinchi shaxs <c>POST /{id}/approve</c> qilmaguncha hisobotga
    /// kirmaydi (SPEC §4.5).
    /// </para>
    /// <para>
    /// <c>created_by</c> JWT'dan olinadi; tanada kelsa — <b>400</b> (SPEC §4.4).
    /// </para>
    /// </summary>
    [HttpPost]
    [FinanceRole(FinanceAction.RecordExpense)]
    public async Task<ActionResult<ExpenseDto>> Create(
        [FromBody] JsonElement body, CancellationToken ct)
    {
        if (RejectServerDerivedFields(body) is { } rejected) return rejected;

        var request = Read<CreateExpenseRequest>(body);
        if (request is null)
            return BadRequest(new BillingErrorDto("invalid_body", "So'rov tanasi o'qilmadi."));

        var expense = await expenses.CreateAsync(request, FinanceActor.RequireUserId(User), ct);
        return Ok(expense);
    }

    // -----------------------------------------------------------------
    //  Ikki qavatli nazorat (SPEC §4.5)
    // -----------------------------------------------------------------

    /// <summary>
    /// Chegaradan yuqori chiqimni tasdiqlaydi va AYNAN shu lahzada jurnalga
    /// qo'yadi. Faqat direktor (<c>superadmin</c>); admin — <b>403</b>.
    ///
    /// <para>
    /// O'zi kiritgan chiqimni tasdiqlashga urinish — <b>403</b>
    /// (<c>self_approval</c>): ilovada ham, bazada ham
    /// (<c>ck_expenses_approver_differs</c>) taqiqlangan. Allaqachon
    /// tasdiqlangan yoki tasdiq talab qilmaydigan chiqim — <b>409</b>.
    /// </para>
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    [FinanceRole(FinanceAction.ApproveExpense)]
    public async Task<ActionResult<ExpenseDto>> Approve(
        Guid id, [FromBody] JsonElement body, CancellationToken ct)
    {
        if (RejectServerDerivedFields(body) is { } rejected) return rejected;

        var request = Read<ApproveExpenseRequest>(body);
        if (request is null)
            return BadRequest(new BillingErrorDto("invalid_body", "So'rov tanasi o'qilmadi."));

        var expense = await expenses.ApproveAsync(
            id, request.Method ?? string.Empty, FinanceActor.RequireUserId(User), ct);
        return Ok(expense);
    }

    // -----------------------------------------------------------------
    //  Storno — xato chiqimni tuzatishning YAGONA yo'li
    // -----------------------------------------------------------------

    /// <summary>
    /// Chiqimni storno qiladi: jurnalga ko'zgu satrlar qo'yiladi
    /// (<c>ref_type = 'reversal'</c>), original satrlar TEGILMAYDI va
    /// <c>expenses</c> jadvaliga yangi qator yozilmaydi.
    ///
    /// <para>
    /// Sabab majburiy — bo'sh bo'lsa <b>400</b>; u jurnal satrining
    /// <c>memo</c> siga tushadi va hisobotda ko'rinadi. Allaqachon storno
    /// qilingan yoki hali jurnalga tushmagan (tasdiq kutayotgan) chiqim —
    /// <b>409</b>. Jurnalga o'zi qo'ygan chiqimni storno qilishga urinish —
    /// <b>403</b> (SPEC §4.5).
    /// </para>
    /// </summary>
    [HttpPost("{id:guid}/reverse")]
    [FinanceRole(FinanceAction.ApproveExpense)]
    public async Task<ActionResult<ExpenseDto>> Reverse(
        Guid id, [FromBody] JsonElement body, CancellationToken ct)
    {
        if (RejectServerDerivedFields(body) is { } rejected) return rejected;

        var request = Read<ReverseExpenseRequest>(body);
        if (request is null)
            return BadRequest(new BillingErrorDto("invalid_body", "So'rov tanasi o'qilmadi."));

        var expense = await expenses.ReverseAsync(
            id, request.Reason ?? string.Empty, FinanceActor.RequireUserId(User), ct);
        return Ok(expense);
    }

    // -----------------------------------------------------------------
    //  Yordamchilar
    // -----------------------------------------------------------------

    /// <summary>
    /// SPEC §4.4 tekshiruvi. Faqat YUQORI qavat nomlari ko'riladi — chiqim
    /// so'rovlarida ichki obyekt yo'q.
    /// </summary>
    private ActionResult? RejectServerDerivedFields(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object) return null;

        foreach (var property in body.EnumerateObject())
        {
            if (!ServerDerivedFields.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                continue;

            return BadRequest(new BillingErrorDto("identity_in_body",
                $"'{property.Name}' so'rov tanasida yuborilmaydi — uni server JWT'dan "
                + "yoki jurnaldan aniqlaydi (SPEC §4.4). Maydonni olib tashlang."));
        }

        return null;
    }

    private static T? Read<T>(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object) return default;
        try { return body.Deserialize<T>(Json); }
        catch (JsonException) { return default; }
    }
}

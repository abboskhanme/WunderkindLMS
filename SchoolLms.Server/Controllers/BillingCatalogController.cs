using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Moliya ma'lumotnomasi: to'lov toifalari, o'quvchi obunalari, chegirmalar
/// (P1-08, SPEC §3.7 va §4.3).
///
/// <para>
/// <b>RUXSAT IKKI QAVATLI — ataylab.</b>
/// </para>
/// <list type="number">
///   <item><b>Klass darajasi:</b> <c>[Authorize(Roles = Roles.FinanceStaff)]</c> —
///   admin va direktor. Kassir bu yerga UMUMAN kirmaydi: SPEC §4.3 jadvalida
///   unga "Change monthly fee" ham, "Grant a discount" ham berilmagan, ya'ni
///   o'qish uchun ham asos yo'q. Bu "qo'pol filtr".</item>
///   <item><b>Metod darajasi:</b> har bir YOZISH amalida
///   <c>[FinanceRole(...)]</c> — SPEC §4.3 ning AYNAN o'sha qatori. Chegirmani
///   tasdiqlash faqat direktorga ochiq (<c>ApproveDiscount</c>), admin esa uni
///   faqat SO'RAY oladi (<c>GrantDiscount</c>) — mijoz javobi, SPEC §8.1 Q5.</item>
/// </list>
/// <para>
/// Ikkinchi qavat birinchisidan kuchliroq bo'lgani uchun kerak: klass atributi
/// "admin yoki direktor" deydi, chegirmani tasdiqlash esa faqat direktorniki.
/// Qoida <c>if</c> bilan emas, atribut bilan yozilgan — P1-22 butun matritsani
/// bitta tsiklda sinay olishi uchun (qarang <c>FinanceMatrix</c>).
/// </para>
///
/// <para>
/// <b>DIQQAT — <c>IBillingSettingsService</c> hali ulanmagan.</b> (Boshqa to'rttasi —
/// <c>ISubscriptionService</c>, <c>IDiscountService</c>, <c>IInvoiceService</c> — P1-15
/// tomonidan allaqachon <c>Program.cs</c> da ro'yxatdan o'tgan.) F14.01 qo'shgan
/// <c>settings</c> bog'liqligi hozircha ulanmagan; shu holda <c>GET/PUT
/// api/admin/billing/settings</c> so'rov vaqtida <c>InvalidOperationException</c> beradi
/// (RBAC 401/403 baribir ishlayveradi — filtr controller quriladigandan OLDIN ishlaydi).
/// Qo'shilishi kerak bo'lgan qator <c>docs/PENDING_WIRING.md</c> da yozilgan.
/// </para>
/// </summary>
[ApiController]
[Authorize(Roles = Roles.FinanceStaff)]
[Route("api/admin/billing")]
[BillingFault]
public class BillingCatalogController(
    AppDbContext db,
    AuditService audit,
    ISubscriptionService subscriptions,
    IDiscountService discounts,
    IInvoiceService invoices,
    IBillingSettingsService settings) : ControllerBase
{
    // ==================================================================
    //  To'lov toifalari (ma'lumotnoma)
    // ==================================================================
    //
    //  Toifalar — ma'lumotnoma, pul emas: bu yerda ledger yozuvi ham,
    //  ikki qavatli nazorat ham yo'q. Shuning uchun ular alohida xizmatsiz,
    //  to'g'ridan-to'g'ri kontekst orqali boshqariladi (qo'shni
    //  `BranchesController` bilan bir xil uslub). SPEC §2.2 dagi "moliyaviy
    //  yozuv umumiy repozitoriy orqali o'tmaydi" qoidasi PUL harakatiga
    //  tegishli — u `LedgerService` va P1-11 zimmasida.

    /// <summary>To'lov toifalari. <paramref name="activeOnly"/> = true bo'lsa faqat faollari.</summary>
    [HttpGet("categories")]
    public async Task<ActionResult<IEnumerable<FeeCategoryDto>>> GetCategories([FromQuery] bool activeOnly = false)
    {
        var q = db.FeeCategories.AsNoTracking().AsQueryable();
        if (activeOnly) q = q.Where(c => c.IsActive);

        return await q
            .OrderBy(c => c.Name)
            .Select(c => new FeeCategoryDto(c.Id, c.Code, c.Name, c.IsActive))
            .ToListAsync();
    }

    /// <summary>Yangi toifa qo'shadi (beshtasi migratsiyada seed qilingan).</summary>
    [HttpPost("categories")]
    [FinanceRole(FinanceAction.ManageSubscriptions)]
    public async Task<ActionResult<FeeCategoryDto>> CreateCategory(FeeCategoryRequest request)
    {
        var code = (request.Code ?? string.Empty).Trim().ToLowerInvariant();
        var name = (request.Name ?? string.Empty).Trim();

        if (code.Length == 0)
            throw BillingRuleException.Invalid("code_required", "Toifa kodi majburiy (masalan: club).");
        if (name.Length == 0)
            throw BillingRuleException.Invalid("name_required", "Toifa nomi majburiy.");
        if (await db.FeeCategories.AnyAsync(c => c.Code == code))
            throw BillingRuleException.Conflict("category_code_taken", $"'{code}' kodli toifa allaqachon bor.");

        var category = new FeeCategory { Code = code, Name = name, IsActive = request.IsActive };
        db.FeeCategories.Add(category);

        audit.Record("FeeCategory", category.Id.ToString(), "create",
            $"To'lov toifasi qo'shildi: {name} ({code})",
            after: new { category.Code, category.Name, category.IsActive });

        await db.SaveChangesAsync();
        return new FeeCategoryDto(category.Id, category.Code, category.Name, category.IsActive);
    }

    /// <summary>
    /// Toifa nomini yoki faolligini o'zgartiradi. <b>Kod o'zgarmaydi</b>: unga
    /// <c>Accounts.RevenueFor</c> (daromad hisobi) va butun hisobot tarixi
    /// bog'langan — kodni o'zgartirish eski daromadni jimgina boshqa hisobga
    /// ko'chirardi.
    /// </summary>
    [HttpPut("categories/{id:guid}")]
    [FinanceRole(FinanceAction.ManageSubscriptions)]
    public async Task<ActionResult<FeeCategoryDto>> UpdateCategory(Guid id, FeeCategoryRequest request)
    {
        var category = await db.FeeCategories.FirstOrDefaultAsync(c => c.Id == id)
            ?? throw BillingRuleException.NotFound("category_not_found", "To'lov toifasi topilmadi.");

        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0)
            throw BillingRuleException.Invalid("name_required", "Toifa nomi majburiy.");

        var code = (request.Code ?? string.Empty).Trim().ToLowerInvariant();
        if (code.Length > 0 && code != category.Code)
            throw BillingRuleException.Conflict(
                "category_code_immutable",
                $"Toifa kodini o'zgartirib bo'lmaydi ('{category.Code}'): unga daromad hisobi va "
                + "butun hisobot tarixi bog'langan. Kerak bo'lsa eskisini o'chirib, yangisini qo'shing.");

        var before = new { category.Code, category.Name, category.IsActive };
        category.Name = name;
        category.IsActive = request.IsActive;

        audit.Record("FeeCategory", category.Id.ToString(), "update",
            $"To'lov toifasi o'zgardi: {category.Code} — nomi '{before.Name}' → '{name}', "
            + $"faol: {before.IsActive} → {request.IsActive}",
            before: before, after: new { category.Code, category.Name, category.IsActive });

        await db.SaveChangesAsync();
        return new FeeCategoryDto(category.Id, category.Code, category.Name, category.IsActive);
    }

    // ==================================================================
    //  Obunalar
    // ==================================================================

    /// <summary>Obunalar ro'yxati (o'quvchi / toifa / faqat faollar bo'yicha filtr).</summary>
    [HttpGet("subscriptions")]
    public async Task<ActionResult<IEnumerable<StudentSubscriptionDto>>> GetSubscriptions(
        [FromQuery] string? studentId,
        [FromQuery] Guid? categoryId,
        [FromQuery] bool activeOnly = false) =>
        (await subscriptions.ListAsync(new SubscriptionQuery(studentId, categoryId, activeOnly), HttpContext.RequestAborted))
        .ToList();

    /// <summary>
    /// Formaga oldindan qo'yiladigan oylik summa: o'quvchining BIRINCHI
    /// <c>tuition</c> obunasi sinf oylik to'lovidan taklif qilinadi (P1-08 qabul
    /// mezoni — bugungi xulq bilan uzviylik). Hech narsa yozilmaydi.
    /// </summary>
    [HttpGet("subscriptions/default")]
    public async Task<ActionResult<SubscriptionDefaultDto>> GetSubscriptionDefault(
        [FromQuery] string studentId, [FromQuery] Guid categoryId) =>
        await subscriptions.DefaultAmountAsync(studentId, categoryId, HttpContext.RequestAborted);

    /// <summary>
    /// Obuna ochadi. Bir xil (o'quvchi, toifa) uchun davri kesishadigan ikkinchi
    /// obuna — <b>409</b> (<c>subscription_overlap</c>).
    /// </summary>
    [HttpPost("subscriptions")]
    [FinanceRole(FinanceAction.ManageSubscriptions)]
    public async Task<ActionResult<StudentSubscriptionDto>> CreateSubscription(CreateSubscriptionRequest request) =>
        await subscriptions.CreateAsync(request, Actor(), HttpContext.RequestAborted);

    /// <summary>Narx / tafsilot / tugash sanasini o'zgartiradi.</summary>
    [HttpPut("subscriptions/{id:guid}")]
    [FinanceRole(FinanceAction.ManageSubscriptions)]
    public async Task<ActionResult<StudentSubscriptionDto>> UpdateSubscription(
        Guid id, UpdateSubscriptionRequest request) =>
        await subscriptions.UpdateAsync(id, request, Actor(), HttpContext.RequestAborted);

    /// <summary>Obunani yopadi — ko'rsatilgan sanadan keyin hisoblanmaydi.</summary>
    [HttpPost("subscriptions/{id:guid}/end")]
    [FinanceRole(FinanceAction.ManageSubscriptions)]
    public async Task<ActionResult<StudentSubscriptionDto>> EndSubscription(
        Guid id, EndSubscriptionRequest request) =>
        await subscriptions.EndAsync(id, request, Actor(), HttpContext.RequestAborted);

    // ==================================================================
    //  Chegirmalar (mijoz javobi: SPEC §8.1 Q5 — chegara YO'Q)
    // ==================================================================

    /// <summary>Chegirmalar ro'yxati (o'quvchi / toifa / holat bo'yicha filtr).</summary>
    [HttpGet("discounts")]
    public async Task<ActionResult<IEnumerable<DiscountDto>>> GetDiscounts(
        [FromQuery] string? studentId,
        [FromQuery] Guid? categoryId,
        [FromQuery] string? status) =>
        (await discounts.ListAsync(new DiscountQuery(studentId, categoryId, status), HttpContext.RequestAborted))
        .ToList();

    /// <summary>Direktor paneli uchun "tasdiq kutmoqda" navbati (eng eski so'rov birinchi).</summary>
    [HttpGet("discounts/pending")]
    public async Task<ActionResult<IEnumerable<DiscountDto>>> GetPendingDiscounts() =>
        (await discounts.PendingAsync(HttpContext.RequestAborted)).ToList();

    /// <summary>
    /// Chegirma SO'RAYDI. <b>Har doim <c>pending</c> holatda yaratiladi va javob
    /// 200 bo'ladi — 409 emas</b>: mijoz javobiga ko'ra (SPEC §8.1 Q5) tasdiq
    /// kutish istisno emas, NORMAL oqim. Chaqiruvchi javobdagi
    /// <c>status = "pending"</c> ni ko'rib "tasdiq kutilmoqda" deb ko'rsatadi.
    ///
    /// <para>
    /// docs/TASKS.md dagi "chegaradan yuqori bo'lsa <c>409 approval_required</c>"
    /// jumlasi shu javob bilan BEKOR QILINGAN — qarang <c>DiscountService</c> izohi.
    /// </para>
    /// </summary>
    [HttpPost("discounts")]
    [FinanceRole(FinanceAction.GrantDiscount)]
    public async Task<ActionResult<DiscountDto>> CreateDiscount(CreateDiscountRequest request) =>
        await discounts.CreateAsync(request, Actor(), HttpContext.RequestAborted);

    /// <summary>
    /// Chegirmani tasdiqlaydi — faqat direktor va faqat BOSHQA shaxs
    /// (yaratuvchining o'zi urinsa <b>403</b>, SPEC §4.5). Shu paytdan boshlab
    /// chegirma hisob-kitobga kiradi.
    /// </summary>
    [HttpPost("discounts/{id:guid}/approve")]
    [FinanceRole(FinanceAction.ApproveDiscount)]
    public async Task<ActionResult<DiscountDto>> ApproveDiscount(Guid id) =>
        await discounts.ApproveAsync(id, Actor(), HttpContext.RequestAborted);

    /// <summary>Chegirmani rad etadi (sabab majburiy). Qator tarix uchun qoladi.</summary>
    [HttpPost("discounts/{id:guid}/reject")]
    [FinanceRole(FinanceAction.ApproveDiscount)]
    public async Task<ActionResult<DiscountDto>> RejectDiscount(Guid id, RejectDiscountRequest request) =>
        await discounts.RejectAsync(id, Actor(), request.Reason, HttpContext.RequestAborted);

    /// <summary>
    /// SPEC §4.4 — <c>created_by</c> / <c>approved_by</c> HAR DOIM JWT'dan.
    /// So'rov tanasida bunday maydon yo'q va bo'lmaydi.
    /// </summary>
    // ==================================================================
    //  Oylik hisoblash — QO'LDA ishga tushirish (P1-21)
    // ==================================================================

    /// <summary>
    /// Hisoblanmagan oylarni to'ldiradi: har faol obuna uchun bitta
    /// hisob-faktura. <b>IDEMPOTENT</b> — ikki marta bosilsa ikkinchi
    /// yurishda hech narsa yozilmaydi (<c>invoices</c> dagi unikal indeks
    /// kafolatlaydi), shuning uchun tugmani takror bosish xavfsiz.
    ///
    /// <para>
    /// Odatda buni <c>BillingAccrualService</c> fon xizmati bajaradi
    /// (startupda va har 12 soatda). Bu endpoint kutishni chetlab o'tish
    /// uchun: yangi obuna ochilgach hisob-faktura DARHOL kerak bo'ladi —
    /// demo ma'lumotni tiklashda ham, o'quv yili boshida ham.
    /// </para>
    /// <para>
    /// <b>Actor fon xizmatinikidan farq qiladi va bu ataylab:</b> tugmani
    /// bosgan odam jurnalga o'z nomi bilan tushadi (SPEC §4.4), fon xizmati
    /// esa direktor nomidan yozadi. Jurnalga qarab "bu hisobni kim
    /// boshlagan" degan savolga javob topish mumkin bo'lsin.
    /// </para>
    /// </summary>
    /// <param name="month">
    /// <c>"yyyy-MM"</c> — faqat shu oy. Berilmasa: hisoblanmagan BARCHA oylar.
    /// </param>
    [HttpPost("accrual/run")]
    [FinanceRole(FinanceAction.ManageSubscriptions)]
    public async Task<ActionResult<IEnumerable<AccrualResultDto>>> RunAccrual(
        [FromQuery] string? month, CancellationToken ct)
    {
        var actor = Actor();

        if (string.IsNullOrWhiteSpace(month))
            return Ok(await invoices.AccrueDueAsync(actor, ct));

        if (!DateOnly.TryParseExact(month.Trim() + "-01", "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var periodMonth))
            return BadRequest(new BillingErrorDto(
                "invalid_month", $"Oy formati noto'g'ri: '{month}'. Kutilgani: yyyy-MM."));

        return Ok(new[] { await invoices.AccrueMonthAsync(periodMonth, actor, ct) });
    }

    // ==================================================================
    //  Moliya sozlamalari (F14.01, finance-parity.md §2.14)
    // ==================================================================
    //
    //  O'QISH klass darajasidagi darvozadan o'tadi (admin/direktor) — alohida
    //  `[FinanceRole]` shart emas, negaki bu yerda "kim ko'ra oladi" bitta
    //  javobga ega (SubscriptionsPage/DiscountsPage GET'lari kabi).
    //
    //  YOZISH ikki qavatli: (1) `[FinanceRole(ManageBillingSettings)]` — admin
    //  va direktor, (2) `BillingSettingsService.UpdateAsync` ICHIDA, faqat
    //  chegara HAQIQATAN o'zgarsa — faqat direktor (SPEC §4.5, F14.01 gap
    //  yozuvi). Ikkinchisi FinanceMatrix'da ifodalanmaydi, chunki u maydon
    //  darajasidagi qoida, amal darajasidagi emas.

    /// <summary>Joriy moliya sozlamalari.</summary>
    [HttpGet("settings")]
    public async Task<ActionResult<BillingSettingsDto>> GetSettings(CancellationToken ct) =>
        await settings.GetAsync(ct);

    /// <summary>
    /// Sozlamalarni saqlaydi. <c>expenseApprovalThreshold</c> HAQIQATAN
    /// o'zgargan bo'lsa va so'rovchi direktor bo'lmasa — <b>403</b>
    /// (<c>threshold_requires_director</c>).
    /// </summary>
    [HttpPut("settings")]
    [FinanceRole(FinanceAction.ManageBillingSettings)]
    public async Task<ActionResult<BillingSettingsDto>> UpdateSettings(
        UpdateBillingSettingsRequest request, CancellationToken ct) =>
        await settings.UpdateAsync(request, Actor(), User.IsInRole(Roles.SuperAdmin), ct);

    private string Actor() => FinanceActor.RequireUserId(User);
}

/// <summary>
/// Moliya qoidasi buzilishini HTTP javobiga o'giradi — AYNAN bitta joyda.
///
/// <para>
/// Faqat <see cref="BillingRuleException"/> tutiladi. Boshqa har qanday istisno
/// (DI'da ro'yxatdan o'tmagan xizmat, Npgsql xatosi, <c>NullReference</c>)
/// TEGILMAYDI va 500 bo'lib chiqadi — chunki ular buzilgan tizim belgisi, va
/// ularni 409 qilib ko'rsatish nosozlikni "normal ish oqimi" ko'rinishida
/// yashirardi.
/// </para>
/// <para>
/// Javob tanasida <c>message</c> maydoni bor: butun frontend xatoni
/// <c>err.response.data.message</c> dan o'qiydi (LoginPage, ClassesPage va h.k.),
/// shuning uchun <c>ProblemDetails</c> emas, <see cref="BillingErrorDto"/>.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class BillingFaultAttribute : Attribute, IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not BillingRuleException fault) return;

        var status = fault.Fault switch
        {
            BillingFault.Invalid => StatusCodes.Status400BadRequest,
            BillingFault.NotFound => StatusCodes.Status404NotFound,
            BillingFault.Forbidden => StatusCodes.Status403Forbidden,
            BillingFault.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };

        context.Result = new ObjectResult(new BillingErrorDto(fault.Code, fault.Message)) { StatusCode = status };
        context.ExceptionHandled = true;
    }
}

/// <summary>
/// Moliya endpoint'larining xato javobi.
/// </summary>
/// <param name="Code">Mashina o'qiydigan kod: <c>subscription_overlap</c>,
/// <c>self_approval</c>, … Frontend shartli mantiqda SHUNGA qaraydi.</param>
/// <param name="Message">O'zbekcha, foydalanuvchiga ko'rsatiladigan xabar.</param>
public record BillingErrorDto(string Code, string Message);

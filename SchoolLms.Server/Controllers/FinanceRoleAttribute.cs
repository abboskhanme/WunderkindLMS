using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// SPEC §4.3 dagi rol chegaralari — <b>MA'LUMOT sifatida</b>, tarqoq <c>if</c>
/// zanjiri sifatida emas. Vazifa: P1-06.
///
/// <para>
/// Nega shunday: P1-22 butun matritsani BITTA tsiklda tekshirishi kerak
/// ("har amal × har rol"). Agar qoidalar o'nta controller bo'ylab sochilgan
/// bo'lsa, bunday testni yozib bo'lmaydi — har bir endpoint alohida esga
/// olinishi kerak bo'ladi, va unutilgani jimgina ochiq qoladi.
/// </para>
/// <para>
/// Qo'llanishi: <c>[FinanceRole(FinanceAction.AcceptPayment)]</c> — klass yoki
/// metod ustida. Qaysi rollar ruxsat etilgani FAQAT
/// <see cref="FinanceMatrix.Rules"/> da yoziladi.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class FinanceRoleAttribute(FinanceAction action) : Attribute, IAuthorizationFilter
{
    public FinanceAction Action { get; } = action;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true) { context.Result = new UnauthorizedResult(); return; }
        if (!FinanceMatrix.IsAllowed(Action, user)) context.Result = new ForbidResult();
    }
}

/// <summary>
/// SPEC §4.3 jadvalidagi amallar. Har biriga <see cref="FinanceMatrix.Rules"/>
/// da AYNAN bitta qoida to'g'ri keladi.
/// </summary>
public enum FinanceAction
{
    /// <summary>To'lov qabul qilish va chek berish.</summary>
    AcceptPayment,

    /// <summary>To'lovni storno qilish (sabab majburiy).</summary>
    ReversePayment,

    /// <summary>
    /// To'lovni TAHRIRLASH yoki O'CHIRISH. Hech kimga ruxsat etilmagan —
    /// qoidaning rollar ro'yxati ATAYLAB BO'SH. Endpoint yozilmasligi kerak;
    /// kimdir yozib qo'ysa, bu atribut uni hammaga yopadi.
    /// </summary>
    EditOrDeletePayment,

    /// <summary>Oylik narx / obunani o'zgartirish.</summary>
    ManageSubscriptions,

    /// <summary>Chegirma SO'RASH (har doim `pending` holatda yaratiladi).</summary>
    GrantDiscount,

    /// <summary>
    /// Chegirmani TASDIQLASH. Mijoz javobi (SPEC §8.1 Q5): faqat direktor —
    /// chegara yo'q, admin ham tasdiqlay olmaydi.
    /// </summary>
    ApproveDiscount,

    /// <summary>O'z smenasini ochish va yopish.</summary>
    ManageOwnShift,

    /// <summary>Kassirlar kesimidagi nomuvofiqlik (variance) hisobotini ko'rish.</summary>
    ViewVarianceReport,

    /// <summary>Chiqim yozish.</summary>
    RecordExpense,

    /// <summary>Chiqimni tasdiqlash (yaratuvchidan boshqa shaxs — SPEC §4.5).</summary>
    ApproveExpense,

    /// <summary>Moliya hisobotlari: qarzdorlar, daromad dinamikasi, P&amp;L.</summary>
    ViewBillingReports,

    /// <summary>Moliya sozlamalari (`payment_due_day`, `overdue_after_day`).</summary>
    ManageBillingSettings,

    /// <summary>
    /// O'quvchiga pul qaytarishni SO'RASH (F1.05, finance-parity §2.1). Har
    /// doim <c>pending</c> holatda tug'iladi — <see cref="ApproveRefund"/>
    /// alohida, ikkinchi shaxsning amali.
    /// </summary>
    RequestRefund,

    /// <summary>
    /// Qaytarimni TASDIQLASH (yoki rad etish). Mijoz javobi F1.05 bilan bir
    /// xil: chegirma tasdig'i kabi — chegara yo'q, faqat direktor.
    /// </summary>
    ApproveRefund,

    /// <summary>
    /// Xodimga bonus/jarima yozish, bekor qilish va sabab katalogini
    /// boshqarish (F11.01, F11.02; hr.md §5.4 — "Manage penalty/bonus rules"
    /// bilan bir xil ruxsat darajasi, lekin ATAYLAB alohida nom: bu yerdagi
    /// qo'lda kiritiladigan bir martalik yozuv, `hr_rules` qoidalar
    /// dvigateli emas — hr.md §2.7). Xodimning pulini o'zgartiradigan amal,
    /// shuning uchun kassir va oddiy xodim YO'Q — F3.05 xatosi bu yerda
    /// TAKRORLANMASLIGI kerak (SPEC §4.3).
    /// </summary>
    ManagePayrollAdjustments,

    // ----- Kassalar (cash boxes) — "smena" o'rnini bosadi, 2026-09 -----

    /// <summary>Kassalar ro'yxati va harakatlar jurnalini ko'rish.</summary>
    ViewCashBoxes,

    /// <summary>Kassa ochish, nomini/mas'ulini/sukut belgisini/faolligini o'zgartirish.</summary>
    ManageCashBoxes,

    /// <summary>Kunlik amal: kirim, chiqim, ko'chirish, ayirboshlash.</summary>
    OperateCashBox,

    /// <summary>Kassa amalini bekor qilish (storno).</summary>
    CancelCashBoxTransaction,
}

/// <summary>
/// Bitta qator = SPEC §4.3 jadvalining bitta qatori.
/// </summary>
/// <param name="Action">Amal.</param>
/// <param name="Roles">Ruxsat etilgan rollar. <b>Bo'sh ro'yxat = hech kimga
/// mumkin emas</b> (masalan to'lovni tahrirlash).</param>
/// <param name="Summary">O'zbekcha ta'rif — 403 jurnalida va hujjatda ishlatiladi.</param>
public sealed record FinanceRule(FinanceAction Action, string[] Roles, string Summary);

/// <summary>
/// SPEC §4.3 jadvali, kod ko'rinishida. Bu YAGONA manba — boshqa hech qayerda
/// "kassir buni qila oladimi" degan <c>if</c> yozilmaydi.
/// </summary>
public static class FinanceMatrix
{
    private static readonly string[] Director = [Roles.SuperAdmin];
    private static readonly string[] AdminAndDirector = [Roles.Admin, Roles.SuperAdmin];
    private static readonly string[] CashDesk = [Roles.Cashier, Roles.Admin, Roles.SuperAdmin];
    private static readonly string[] NoOne = [];

    /// <summary>
    /// SPEC §4.3 — to'liq matritsa. P1-22 shu ro'yxat bo'ylab aylanib, har
    /// amalni har rol bilan sinaydi.
    /// </summary>
    public static readonly IReadOnlyList<FinanceRule> Rules =
    [
        new(FinanceAction.AcceptPayment, CashDesk,
            "To'lov qabul qilish va chek berish"),

        // Kassir YO'Q: "pulni oldim, keyin storno qildim" — tavsifdagi
        // firibgarlikning aynan o'zi. Storno ikkinchi shaxsni talab qiladi.
        new(FinanceAction.ReversePayment, AdminAndDirector,
            "To'lovni storno qilish (sabab majburiy)"),

        // SPEC §4.3: "Edit or delete a payment — impossible for anyone".
        // Bo'sh ro'yxat shuni AYTIB turadi; baza darajasida ham (REVOKE)
        // shunday. Jadvalda qator bor, chunki qoidaning YO'QLIGI emas,
        // qoidaning O'ZI hujjatlashtirilishi kerak.
        new(FinanceAction.EditOrDeletePayment, NoOne,
            "To'lovni tahrirlash yoki o'chirish — HECH KIMGA mumkin emas"),

        new(FinanceAction.ManageSubscriptions, AdminAndDirector,
            "Oylik narx va obunalarni boshqarish"),

        new(FinanceAction.GrantDiscount, AdminAndDirector,
            "Chegirma so'rash (pending holatda yaratiladi)"),

        // Mijoz javobi (SPEC §8.1 Q5): chegara yo'q, TASDIQ direktorniki.
        // Shuning uchun admin chegirma SO'RAY oladi, lekin TASDIQLAY olmaydi.
        new(FinanceAction.ApproveDiscount, Director,
            "Chegirmani tasdiqlash — faqat direktor"),

        new(FinanceAction.ManageOwnShift, CashDesk,
            "O'z smenasini ochish va yopish"),

        new(FinanceAction.ViewVarianceReport, AdminAndDirector,
            "Kassirlar kesimidagi nomuvofiqlik hisoboti"),

        new(FinanceAction.RecordExpense, AdminAndDirector,
            "Chiqim yozish"),

        new(FinanceAction.ApproveExpense, Director,
            "Chiqimni tasdiqlash — yaratuvchidan boshqa shaxs (SPEC §4.5)"),

        new(FinanceAction.ViewBillingReports, AdminAndDirector,
            "Moliya hisobotlari: qarzdorlar, daromad dinamikasi, P&L"),

        new(FinanceAction.ManageBillingSettings, AdminAndDirector,
            "To'lov muddati sozlamalari (payment_due_day, overdue_after_day)"),

        // F1.05 — admin so'raydi, direktor tasdiqlaydi. Kassir bu yerda YO'Q:
        // qaytarim so'rovi kassa amali emas, ma'muriy qaror.
        new(FinanceAction.RequestRefund, AdminAndDirector,
            "O'quvchiga pul qaytarishni so'rash (F1.05)"),

        // Mijoz javobi (finance-parity §2.1, F1.05): faqat direktor —
        // chegara yo'q, admin ham tasdiqlay olmaydi (chegirma tasdig'i bilan
        // bir xil naqsh, SPEC §8.1 Q5).
        new(FinanceAction.ApproveRefund, Director,
            "Qaytarimni tasdiqlash yoki rad etish — faqat direktor"),

        // F11.01/F11.02 — xodimga bonus/jarima. Kassir ham, oddiy xodim ham
        // YO'Q (hr.md §5.4 ning "Manage penalty/bonus rules" qatori bilan
        // bir xil daraja) — bu pulga tegishli amal, AdminPerm("teachers")
        // emas (F3.05 ning aynan o'zi bu yerda takrorlanmasligi kerak).
        new(FinanceAction.ManagePayrollAdjustments, AdminAndDirector,
            "Xodimga bonus/jarima yozish, bekor qilish va sabab katalogini boshqarish"),

        // Kassalar (cash boxes) — "smena" o'rnini bosadi. Kunlik amal
        // (kirim/chiqim/ko'chirish/ayirboshlash) kassaning O'Z ishi, xuddi
        // ilgari smena ochish/yopish kabi — CashDesk. Kataloqni boshqarish
        // (yangi kassa ochish, mas'ul/sukut belgisini o'zgartirish) va
        // bekor qilish esa AdminAndDirector — kassir yangi kassa ocholmaydi
        // va o'z-o'zini tekshirmasdan tarixni bekor qilolmaydi.
        new(FinanceAction.ViewCashBoxes, CashDesk,
            "Kassalar ro'yxati va harakatlar jurnalini ko'rish"),

        new(FinanceAction.ManageCashBoxes, AdminAndDirector,
            "Kassa ochish, nomini/mas'ulini/sukut belgisini o'zgartirish"),

        new(FinanceAction.OperateCashBox, CashDesk,
            "Kunlik amal: kirim, chiqim, ko'chirish, ayirboshlash"),

        new(FinanceAction.CancelCashBoxTransaction, AdminAndDirector,
            "Kassa amalini bekor qilish (storno)"),
    ];

    /// <summary>
    /// Amal uchun ruxsat etilgan rollar. Qoida topilmasa — bo'sh ro'yxat
    /// (ya'ni RAD ETISH). Yangi <see cref="FinanceAction"/> qo'shib, qoidasini
    /// yozishni unutgan odam ochiq teshik emas, yopiq eshik oladi.
    /// </summary>
    public static IReadOnlyList<string> RolesFor(FinanceAction action) =>
        Rules.FirstOrDefault(r => r.Action == action)?.Roles ?? [];

    /// <summary>Shu foydalanuvchi shu amalni bajara oladimi?</summary>
    public static bool IsAllowed(FinanceAction action, ClaimsPrincipal user) =>
        RolesFor(action).Any(user.IsInRole);

    /// <summary>Rol nomi bo'yicha tekshiruv — HTTP so'rovsiz (P1-22 tsikli uchun).</summary>
    public static bool IsAllowed(FinanceAction action, string role) =>
        RolesFor(action).Contains(role, StringComparer.Ordinal);
}

/// <summary>
/// SPEC §4.4 — SERVER ANIQLAYDIGAN SHAXS.
///
/// <para>
/// <c>cashier_id</c>, <c>created_by</c>, <c>approved_by</c> HAR DOIM JWT
/// claim'idan olinadi. Moliya controller'lari shu yagona yordamchini
/// ishlatadi — har biri o'zicha <c>User.FindFirst(...)</c> yozsa, kimdir
/// ertami-kechmi so'rov tanasidagi id'ga ishonib qo'yadi.
/// </para>
/// </summary>
public static class FinanceActor
{
    /// <summary>
    /// Joriy foydalanuvchi id'si. Topilmasa — <see cref="InvalidOperationException"/>:
    /// moliyaviy yozuv "noma'lum shaxs" nomidan yozilgandan ko'ra, so'rov
    /// yiqilgani yaxshiroq.
    /// </summary>
    public static string RequireUserId(ClaimsPrincipal user) =>
        user.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? user.FindFirst("sub")?.Value
        ?? throw new InvalidOperationException(
            "JWT'da foydalanuvchi id'si yo'q — moliyaviy yozuvni kim yozayotgani aniqlanmadi.");

    /// <summary>Ko'rsatish uchun ism (audit xabarida ishlatiladi).</summary>
    public static string DisplayName(ClaimsPrincipal user) =>
        user.FindFirst(ClaimTypes.Name)?.Value ?? "Noma'lum";
}

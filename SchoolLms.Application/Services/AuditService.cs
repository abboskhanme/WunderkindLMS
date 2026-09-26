using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;

namespace SchoolLms.Application.Services;

/// <summary>
/// Moliyaga oid o'zgarishlarni tarix (audit) sifatida yozib boradi.
/// Yozuv joriy tranzaksiyaga qo'shiladi — controller'dagi SaveChanges uni ham saqlaydi.
/// </summary>
public class AuditService(IAppDbContext db, IHttpContextAccessor http)
{
    /// <summary>Pulni "850 000" ko'rinishida formatlash.</summary>
    public static string Money(decimal v) =>
        v.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ");

    /// <summary>
    /// Eski yassi moliya yozuvi. Jadval P1-21 da o'chdi, lekin konstanta QOLADI:
    /// undan oldin yozilgan <c>audit_logs</c> qatorlari shu satr bilan
    /// saqlangan va audit ekranidagi filtr ularni topa olishi kerak.
    /// Yangi kod bu konstantani ISHLATMAYDI.
    /// </summary>
    public const string EntityFinanceTransaction = "FinanceTransaction";
    public const string EntityTeacherSalary = "TeacherSalary";
    /// <summary>O'qituvchi bo'lmagan xodim maoshi — oylik belgilash va maosh berish (employees-unified.md).</summary>
    public const string EntityStaffSalary = "StaffSalary";
    public const string EntityClassFee = "ClassFee";
    public const string EntityStudentDiscount = "StudentDiscount";

    /// <summary>
    /// O'quvchining SINFI o'zgardi (P1-21). Ilgari bu
    /// <see cref="EntityStudentDiscount"/> ostida yozilardi, chunki sinf va
    /// chegirma bitta formada birga o'zgarardi. Chegirma o'sha formadan olib
    /// tashlangach, "StudentDiscount" yorlig'i chalg'ituvchi bo'lib qoldi:
    /// audit jurnali — yolg'on gapirmasligi kerak bo'lgan yagona joy.
    /// </summary>
    public const string EntityStudentClass = "StudentClass";

    // ----- Moliya yadrosi (SPEC §4.6, P1-14) -----
    // Bu to'rttasi FAQAT `Entry(...)` orqali yoziladi: aktyor JWT'dan emas,
    // chaqiruvchidan keladi (SPEC §4.4 — kassir/tasdiqlovchi id'si allaqachon
    // xizmatning parametri, va fon xizmatida HttpContext umuman yo'q).

    /// <summary>Ikki yoqlama jurnal yozuvi (<c>LedgerService</c>).</summary>
    public const string EntityLedgerEntry = "LedgerEntry";
    /// <summary>Kassaga tushgan to'lov va storno (<c>PaymentService</c>).</summary>
    public const string EntityPayment = "Payment";
    /// <summary>Chiqim (<c>ExpenseService</c>).</summary>
    public const string EntityExpense = "Expense";
    /// <summary>Tungi tekshiruv bayrog'ini yopish (<c>AnomalyService</c>).</summary>
    public const string EntityAnomalyFlag = "AnomalyFlag";

    /// <summary>Sertifikat (§2.3) — yozuv, tahrir, o'chirish.</summary>
    public const string EntityCertificate = "Certificate";

    /// <summary>Qarzdor bilan ishlash amali (§3.5) — izoh, status, va'da qilingan sana.</summary>
    public const string EntityDebtorAction = "DebtorAction";

    /// <summary>Qarzdor holatlari katalogi (§3.5) — nom, rang, tartib, faollik.</summary>
    public const string EntityDebtorStatus = "DebtorStatus";

    /// <summary>O'quv guruhi (§2.1) — yaratish, tahrir, arxiv, ro'yxat o'zgarishi.</summary>
    public const string EntityStudyGroup = "StudyGroup";

    /// <summary>Hisob-faktura (§2.10) — hozircha faqat bekor qilish yoziladi.</summary>
    public const string EntityInvoice = "Invoice";

    /// <summary>O'quvchi holati katalogi va o'quvchiga holat qo'yilishi (§2.3).</summary>
    public const string EntityStudentStatus = "StudentStatus";

    /// <summary>Excel'dan o'quvchi importi (§2.3) — nechta yaratildi, nechta yangilandi.</summary>
    public const string EntityStudentImport = "StudentImport";

    /// <summary>O'quvchini butunlay o'chirish (§2.3) — arxivlash EMAS.</summary>
    public const string EntityStudentDelete = "StudentDelete";

    /// <summary>O'quvchi shartnomasi (§2.10) — raqam, sana, fayl, holat.</summary>
    public const string EntityStudentContract = "StudentContract";

    /// <summary>Kassadan pul topshirish (bank yoki seyf) — §2.1.</summary>
    public const string EntityCashHandover = "CashHandover";
    /// <summary>O'quvchiga pul qaytarish (F1.05) — so'rov, tasdiq, rad, storno.</summary>
    public const string EntityStudentRefund = "StudentRefund";

    /// <summary>Chiqimga biriktirilgan hujjat — §2.1.</summary>
    public const string EntityExpenseAttachment = "ExpenseAttachment";

    /// <summary>Xulq-atvor bali (§6) — qo'lda qo'yilgan ball va uning sababi.</summary>
    public const string EntityDisciplinePoint = "DisciplinePoint";

    /// <summary>Lid — hozircha faqat o'quvchiga aylantirish va o'chirish (<c>enrol</c>) yoziladi.</summary>
    public const string EntityLead = "Lead";

    /// <summary>Ommaviy ariza formasi (docs/modules/sales-marketing.md §5.2).</summary>
    public const string EntitySurvey = "Survey";

    /// <summary>Maktab yangiligi (docs/modules/sales-marketing.md §5.4).</summary>
    public const string EntityNews = "News";

    /// <summary>Audit yozuvini joriy DbContext'ga qo'shadi (hali SaveChanges qilinmaydi).</summary>
    public void Record(
        string entityType, string entityId, string action, string summary,
        object? before = null, object? after = null,
        string? studentId = null, string? teacherId = null)
    {
        var user = http.HttpContext?.User;
        db.AuditLogs.Add(Entry(
            entityType, entityId, action, summary,
            actorId: user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? user?.FindFirst("sub")?.Value,
            actorName: user?.FindFirst(ClaimTypes.Name)?.Value ?? "Tizim",
            before: before, after: after,
            studentId: studentId, teacherId: teacherId));
    }

    /// <summary>
    /// Audit qatorini QURADI (bazaga qo'shmaydi) — aktyor PARAMETR sifatida.
    ///
    /// <para>
    /// <b>Nega <see cref="Record"/> yetmaydi.</b> U aktyorni <c>HttpContext</c> dan
    /// oladi, moliya xizmatlari esa uni chaqiruvchidan alohida parametr sifatida
    /// oladi (SPEC §4.4) va fon xizmatidan ham chaqiriladi — u yerda so'rov
    /// konteksti UMUMAN yo'q. Bunday holatda <c>Record</c> "Tizim" deb yozib
    /// qo'yardi, ya'ni jurnal kimning nomidan pul harakat qilganini KO'RSATMASDI.
    /// </para>
    /// <para>
    /// Qatorni chaqiruvchi o'zi <c>db.AuditLogs.Add(...)</c> qiladi — ataylab:
    /// shunda yozuv chaqiruvchining O'Z tranzaksiyasiga tushadi va orqaga
    /// qaytarilgan to'lov audit izini qoldirmaydi (docs/PENDING_WIRING.md §11).
    /// Bir xil naqsh <c>CashShiftService.WriteAuditTrail</c> da ham ishlatilgan.
    /// </para>
    /// </summary>
    public static AuditLog Entry(
        string entityType, string entityId, string action, string summary,
        string? actorId, string? actorName = null,
        object? before = null, object? after = null,
        string? studentId = null, string? teacherId = null) => new()
        {
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            Timestamp = AppClock.Iso(),
            ActorId = actorId,
            ActorName = actorName ?? "Tizim",
            Summary = summary,
            // SPEC §4.6 — `before`/`after` JSON. Ustun turi `jsonb` (P1-14
            // migratsiyasi), shuning uchun bu yerdan HAR DOIM haqiqiy JSON
            // chiqishi shart: yaroqsiz matn INSERT paytida 22P02 bilan yiqiladi.
            Before = Json(before),
            After = Json(after),
            StudentId = studentId,
            TeacherId = teacherId,
        };

    /// <summary>Snapshot'ni <c>jsonb</c> ustuni uchun matnga o'giradi. null — null bo'lib qoladi.</summary>
    public static string? Json(object? value) =>
        value is null ? null : JsonSerializer.Serialize(value);

    // P1-21: `Snapshot(FinanceTransaction)` olib tashlandi — `finance_transactions`
    // jadvali va uning entity'si o'chdi. Moliyaviy yozuvlar endi o'zgarmas
    // (`payments`, `ledger_entries`), ya'ni "oldingi holat" degan tushunchaning
    // o'zi yo'q: tuzatish storno bilan, YANGI qator qo'shib bo'ladi (SPEC §4.1).
    // Chiqim va to'lov uchun `before`/`after` kerak bo'lsa — ular o'z DTO'lari
    // bilan uzatiladi (`ExpenseDto`, `PaymentDto`).
}

/// <summary>
/// <c>users.id</c> → to'liq ism, KESHLANGAN. Audit qatoridagi
/// <c>actor_name</c> uchun (P1-14).
///
/// <para>
/// <b>Nega kesh kerak.</b> Oylik hisoblash (P1-09) bitta scope ichida har
/// hisob-faktura uchun <c>LedgerService.PostAsync</c> ni chaqiradi — 500
/// o'quvchida 500 marta. Keshsiz bu 500 ta bir xil <c>SELECT full_name</c>
/// bo'lardi: klassik N+1, faqat audit yozuvining ismi uchun.
/// </para>
/// <para>
/// Kesh EGASINING umriga bog'liq (xizmat scoped, ya'ni bitta so'rov yoki
/// bitta fon tsikli). Ism o'sha oraliqda o'zgarishi amalda mumkin emas, va
/// o'zgarsa ham audit qatoriga o'sha lahzadagi ism yozilgani TO'G'RI —
/// keyinchalik nomi o'zgargan foydalanuvchi tarixni qayta yozmaydi.
/// </para>
/// </summary>
public sealed class ActorNames(IAppDbContext db)
{
    private const string Unknown = "Noma'lum";

    private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);

    /// <summary>Foydalanuvchi ismi; topilmasa — "Noma'lum" (xato TASHLAMAYDI).</summary>
    public async Task<string> OfAsync(string? userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) return Unknown;
        if (_cache.TryGetValue(userId, out var cached)) return cached;

        var name = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync(ct)
            ?? Unknown;

        _cache[userId] = name;
        return name;
    }
}

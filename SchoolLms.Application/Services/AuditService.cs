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

    /// <summary>Audit yozuvini joriy DbContext'ga qo'shadi (hali SaveChanges qilinmaydi).</summary>
    public void Record(
        string entityType, string entityId, string action, string summary,
        object? before = null, object? after = null,
        string? studentId = null, string? teacherId = null)
    {
        var user = http.HttpContext?.User;
        db.AuditLogs.Add(new AuditLog
        {
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            Timestamp = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            ActorId = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? user?.FindFirst("sub")?.Value,
            ActorName = user?.FindFirst(ClaimTypes.Name)?.Value ?? "Tizim",
            Summary = summary,
            Before = before is null ? null : JsonSerializer.Serialize(before),
            After = after is null ? null : JsonSerializer.Serialize(after),
            StudentId = studentId,
            TeacherId = teacherId,
        });
    }

    // P1-21: `Snapshot(FinanceTransaction)` olib tashlandi — `finance_transactions`
    // jadvali va uning entity'si o'chdi. Moliyaviy yozuvlar endi o'zgarmas
    // (`payments`, `ledger_entries`), ya'ni "oldingi holat" degan tushunchaning
    // o'zi yo'q: tuzatish storno bilan, YANGI qator qo'shib bo'ladi (SPEC §4.1).
    // Chiqim va to'lov uchun `before`/`after` kerak bo'lsa — ular o'z DTO'lari
    // bilan uzatiladi (`ExpenseDto`, `PaymentDto`).
}

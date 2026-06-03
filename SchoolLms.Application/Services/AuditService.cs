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

    public const string EntityFinanceTransaction = "FinanceTransaction";
    public const string EntityTeacherSalary = "TeacherSalary";
    public const string EntityClassFee = "ClassFee";
    public const string EntityStudentDiscount = "StudentDiscount";

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

    /// <summary>Moliyaviy amal snapshot'i (Before/After uchun).</summary>
    public static object Snapshot(FinanceTransaction t) => new
    {
        t.Date,
        t.Direction,
        t.Category,
        t.Amount,
        t.Note,
        t.Month,
        t.StudentId,
        t.TeacherId,
    };
}

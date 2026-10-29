using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore.Query;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  The audit log seen as one screen (docs/modules/audit-log.md, 2026-10-15).
//
//  `audit_logs` rows carry a free-form entity type and action. The screen groups them into
//  sections ("O'quvchilar", "Moliya", ...) and three kinds of action (created / changed /
//  deleted). Both mappings live here and only here: the list, its filters, the Excel file and
//  the money rule all read them, so they cannot drift apart.
//
//  OLD MISFILED ROWS. Before 2026-10-15 some events were written under the wrong type —
//  archiving a pupil as "StudentDiscount", a teacher as "TeacherSalary", a class as
//  "ClassFee". New code writes Student / Teacher / SchoolClass. The old rows are NOT
//  rewritten (the log is history); they are recognised by their summary prefix and shown in
//  the right section — which also means non-finance staff now see them.
// ===========================================================================

/// <summary>One audit section: key and the label the screen shows.</summary>
public sealed record AuditSection(string Key, string Label);

/// <summary>Sections, action kinds and the money rule of the audit log.</summary>
public static class AuditCatalog
{
    // ----- Sections -----------------------------------------------------------

    public const string Students = "students";
    public const string Finance = "finance";
    public const string Salary = "salary";
    public const string Schedule = "schedule";
    public const string Contracts = "contracts";
    public const string Assessment = "assessment";
    public const string Staff = "staff";
    public const string Settings = "settings";
    public const string Other = "other";

    public static readonly IReadOnlyList<AuditSection> Sections =
    [
        new(Students, "O'quvchilar"),
        new(Finance, "Moliya"),
        new(Salary, "Maosh"),
        new(Schedule, "Jadval"),
        new(Contracts, "Shartnoma"),
        new(Assessment, "Baholash"),
        new(Staff, "Xodimlar"),
        new(Settings, "Sozlamalar"),
        new(Other, "Boshqa"),
    ];

    public static string SectionLabel(string key) =>
        Sections.FirstOrDefault(s => s.Key == key)?.Label ?? "Boshqa";

    /// <summary>Entity types per section. A type not listed anywhere is "Boshqa".</summary>
    private static readonly Dictionary<string, string[]> TypesBySection = new()
    {
        [Students] =
        [
            AuditService.EntityStudent, AuditService.EntityStudentClass, AuditService.EntitySchoolClass,
            AuditService.EntityStudentStatus, AuditService.EntityStudentImport, AuditService.EntityStudentDelete,
            AuditService.EntityGuardian, AuditService.EntityStudentEvent, AuditService.EntityCertificate,
            AuditService.EntityStudyGroup, AuditService.EntityLead, "StudentLocation",
            AuditService.EntityAttendanceDay,
        ],
        [Finance] =
        [
            AuditService.EntityFinanceTransaction, AuditService.EntityClassFee, AuditService.EntityStudentDiscount,
            AuditService.EntityLedgerEntry, AuditService.EntityPayment, AuditService.EntityExpense,
            AuditService.EntityAnomalyFlag, AuditService.EntityDebtorAction, AuditService.EntityDebtorStatus,
            AuditService.EntityInvoice, AuditService.EntityCashHandover, AuditService.EntityStudentRefund,
            AuditService.EntityExpenseAttachment, "CashBox", "CashBoxTransaction", "CashShift",
            "StudentSubscription", "StudentWithdrawal", "SubscriptionPlan", "FeeCategory", "DiscountType",
            "TransactionType", "ExpenseTemplate", "BillingSettings",
        ],
        [Salary] =
        [
            AuditService.EntityTeacherSalary, AuditService.EntityStaffSalary, "PayrollAdjustment", "AdjustmentReason",
        ],
        [Schedule] =
        [
            AuditService.EntityScheduleTemplate, AuditService.EntityWeekAssignment, AuditService.EntitySubstitution,
            AuditService.EntityHoliday,
        ],
        [Contracts] = [AuditService.EntityStudentContract, AuditService.EntityContract],
        [Assessment] =
        [
            "SeasonalMark", "Exam", "ExamType", "ExamParticipant", "ExamResult", "QuestionBank", "Question",
            AuditService.EntityDisciplinePoint,
        ],
        [Staff] = [AuditService.EntityTeacher, AuditService.EntityStaff, AuditService.EntityAccessRole, AuditService.EntityRoleAssignment],
        [Settings] =
        [
            "AcademicYear", "TelegramBot", "AutoMessageRule", "CertificateType", "LeadStage",
            AuditService.EntitySurvey,
        ],
    };

    private static readonly Dictionary<string, string> SectionByType =
        TypesBySection.SelectMany(kv => kv.Value.Select(t => (Type: t, Section: kv.Key)))
            .ToDictionary(x => x.Type, x => x.Section, StringComparer.Ordinal);

    private static readonly string[] AllKnownTypes = [.. SectionByType.Keys];

    /// <summary>Finance and salary rows: hidden from users without the money right.</summary>
    private static readonly string[] MoneyTypes = [.. TypesBySection[Finance], .. TypesBySection[Salary]];

    // Old misfiled rows (see the file header). Prefixes are the summaries those paths wrote.
    private const string OldStudentArchiveType = AuditService.EntityStudentDiscount;
    private const string OldStudentArchivePrefix = "O'quvchi arxiv";
    private const string OldStudentBulkArchivePrefix = "O'quvchi ommaviy arxiv";
    private const string OldClassArchiveType = AuditService.EntityClassFee;
    private const string OldClassArchivePrefix = "Sinf arxiv";
    private const string OldTeacherArchiveType = AuditService.EntityTeacherSalary;
    private const string OldTeacherArchivePrefix = "O'qituvchi arxiv";

    private static readonly Expression<Func<AuditLog, bool>> MisfiledStudents = a =>
        (a.EntityType == OldStudentArchiveType
         && (a.Summary.StartsWith(OldStudentArchivePrefix) || a.Summary.StartsWith(OldStudentBulkArchivePrefix)))
        || (a.EntityType == OldClassArchiveType && a.Summary.StartsWith(OldClassArchivePrefix));

    private static readonly Expression<Func<AuditLog, bool>> MisfiledStaff = a =>
        a.EntityType == OldTeacherArchiveType && a.Summary.StartsWith(OldTeacherArchivePrefix);

    /// <summary>Section of one row — in memory (list rows, Excel).</summary>
    public static string SectionOf(string entityType, string summary)
    {
        if (entityType == OldStudentArchiveType
            && (summary.StartsWith(OldStudentArchivePrefix, StringComparison.Ordinal)
                || summary.StartsWith(OldStudentBulkArchivePrefix, StringComparison.Ordinal)))
            return Students;
        if (entityType == OldClassArchiveType && summary.StartsWith(OldClassArchivePrefix, StringComparison.Ordinal))
            return Students;
        if (entityType == OldTeacherArchiveType && summary.StartsWith(OldTeacherArchivePrefix, StringComparison.Ordinal))
            return Staff;
        return SectionByType.GetValueOrDefault(entityType, Other);
    }

    /// <summary>A finance or salary row — hidden from users without the money right.</summary>
    public static bool IsMoneyRow(string entityType, string summary) =>
        SectionOf(entityType, summary) is Finance or Salary;

    /// <summary>SQL predicate of one section (old misfiled rows included where they belong).</summary>
    public static Expression<Func<AuditLog, bool>> InSection(string section)
    {
        if (section == Other) return a => !AllKnownTypes.Contains(a.EntityType);
        var types = TypesBySection.GetValueOrDefault(section) ?? [];
        Expression<Func<AuditLog, bool>> listed = a => types.Contains(a.EntityType);
        return section switch
        {
            Students => Or(listed, MisfiledStudents),
            Staff => Or(listed, MisfiledStaff),
            Finance => And(listed, Not(MisfiledStudents)),
            Salary => And(listed, Not(MisfiledStaff)),
            _ => listed,
        };
    }

    /// <summary>Rows of any of the given sections. Unknown keys are ignored; none known — no rows.</summary>
    public static IQueryable<AuditLog> WhereSections(IQueryable<AuditLog> q, IReadOnlyCollection<string> sections)
    {
        var known = sections.Where(s => Sections.Any(x => x.Key == s)).Distinct().ToList();
        if (known.Count == 0) return q.Where(_ => false);
        var predicate = known.Select(InSection).Aggregate(Or);
        return q.Where(predicate);
    }

    /// <summary>Drops finance and salary rows (the misfiled archive rows stay — they are not money).</summary>
    public static IQueryable<AuditLog> WithoutMoneyRows(IQueryable<AuditLog> q) =>
        q.Where(Not(Or(InSection(Finance), InSection(Salary))));

    // ----- Kinds of action ----------------------------------------------------

    public const string KindCreate = "create";
    public const string KindUpdate = "update";
    public const string KindDelete = "delete";

    /// <summary>Actions that bring a record into being.</summary>
    private static readonly string[] CreateActions = ["create", "enrol", "import", "request", "add", "request-reversal"];

    /// <summary>Actions that take a record away. Everything else is a change.</summary>
    private static readonly string[] DeleteActions = ["delete", "remove"];

    /// <summary>Sub-labels shown next to the kind ("Tahrirladi · Storno").</summary>
    private static readonly Dictionary<string, string> ActionLabels = new(StringComparer.Ordinal)
    {
        ["reverse"] = "Storno",
        ["cancel"] = "Bekor qildi",
        ["void"] = "Bekor qildi",
        ["adjust"] = "Tuzatdi",
        ["adjust_cancel"] = "Tuzatishni bekor qildi",
        ["approve"] = "Tasdiqladi",
        ["approve-reversal"] = "Stornoni tasdiqladi",
        ["reject"] = "Rad etdi",
        ["request"] = "So'radi",
        ["request-reversal"] = "Storno so'radi",
        ["publish"] = "E'lon qildi",
        ["unpublish"] = "E'londan oldi",
        ["retire"] = "Faolsizlantirdi",
        ["resolve"] = "Hal qildi",
        ["end"] = "Yakunladi",
        ["enrol"] = "O'quvchiga aylantirdi",
        ["import"] = "Import",
        ["remove"] = "Olib tashladi",
        ["rollover"] = "Yangi o'quv yili",
        ["archive"] = "Arxivladi",
        ["restore"] = "Arxivdan qaytardi",
        ["password"] = "Parol yangilandi",
        ["accept"] = "Qabul qildi",
        ["return"] = "Qaytardi",
        ["activate"] = "Yoqdi",
        ["deactivate"] = "O'chirib qo'ydi",
        ["add"] = "Qo'shdi",
        ["score"] = "Ball qo'ydi",
        ["absent"] = "Kelmadi deb belgiladi",
        ["send"] = "Yubordi",
    };

    public static string KindOf(string action) =>
        CreateActions.Contains(action) ? KindCreate
        : DeleteActions.Contains(action) ? KindDelete
        : KindUpdate;

    /// <summary>Sub-label for an action beyond plain create/update/delete; null for those three.</summary>
    public static string? ActionLabel(string action) => ActionLabels.GetValueOrDefault(action);

    /// <summary>"Yaratdi" / "Tahrirladi" / "O'chirdi".</summary>
    public static string KindLabel(string kind) => kind switch
    {
        KindCreate => "Yaratdi",
        KindDelete => "O'chirdi",
        _ => "Tahrirladi",
    };

    public static IQueryable<AuditLog> WhereKind(IQueryable<AuditLog> q, string kind) => kind switch
    {
        KindCreate => q.Where(a => CreateActions.Contains(a.Action)),
        KindDelete => q.Where(a => DeleteActions.Contains(a.Action)),
        KindUpdate => q.Where(a => !CreateActions.Contains(a.Action) && !DeleteActions.Contains(a.Action)),
        _ => q.Where(_ => false),
    };

    // ----- Money in before/after ------------------------------------------------

    /// <summary>
    /// Field names that carry money. A non-finance row may still hold an amount (an import
    /// total, a class fee next to a rename), so the type filter alone is not enough.
    /// </summary>
    private static readonly HashSet<string> MoneyKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "amount", "balance", "salary", "monthlyFee", "fee", "price", "sum", "total",
        "paid", "debt", "credit", "discount", "discountAmount", "discountPct",
        "remaining", "charged", "cash",
    };

    /// <summary>
    /// Removes money fields from a before/after JSON. Unreadable JSON is dropped whole —
    /// better nothing than a leak.
    /// </summary>
    public static string? RedactMoney(string? json)
    {
        if (string.IsNullOrEmpty(json)) return json;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject obj) return null;
            foreach (var key in obj.Select(p => p.Key).Where(MoneyKeys.Contains).ToList())
                obj.Remove(key);
            return obj.ToJsonString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ----- Expression helpers -------------------------------------------------

    private static Expression<Func<AuditLog, bool>> Or(Expression<Func<AuditLog, bool>> a, Expression<Func<AuditLog, bool>> b) =>
        Combine(a, b, Expression.OrElse);

    private static Expression<Func<AuditLog, bool>> And(Expression<Func<AuditLog, bool>> a, Expression<Func<AuditLog, bool>> b) =>
        Combine(a, b, Expression.AndAlso);

    private static Expression<Func<AuditLog, bool>> Not(Expression<Func<AuditLog, bool>> a) =>
        Expression.Lambda<Func<AuditLog, bool>>(Expression.Not(a.Body), a.Parameters);

    private static Expression<Func<AuditLog, bool>> Combine(
        Expression<Func<AuditLog, bool>> a, Expression<Func<AuditLog, bool>> b,
        Func<Expression, Expression, BinaryExpression> join)
    {
        var right = ReplacingExpressionVisitor.Replace(b.Parameters[0], a.Parameters[0], b.Body);
        return Expression.Lambda<Func<AuditLog, bool>>(join(a.Body, right), a.Parameters);
    }
}

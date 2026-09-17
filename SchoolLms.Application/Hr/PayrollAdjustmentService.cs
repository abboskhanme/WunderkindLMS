using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.Hr;

// ===========================================================================
//  BONUS / JARIMA — F11.01, F11.02 (docs/modules/finance-parity.md §2.11,
//  §3.2 Batch B). Naqsh: `SchoolLms.Application/Billing/CashHandoverService.cs`
//  (hr.md o'zining "reference implementation" ko'rsatmasi — ExpenseService
//  emas, bu yerda YAQINROQ analog: bitta pul jadvali + storno, ikkinchi
//  pul harakati yo'q).
// ===========================================================================
//
//  BU JURNALGA (`ledger_entries`) YOZMAYDI — ATAYLAB
//  ---------------------------------------------------
//  Bonus/jarima yozilgan lahzada pul HECH QAYERGA ko'chmaydi: bu faqat
//  keyingi oylik hisob-kitobga (HR-09, `PayrollService`) kiritiladigan
//  raqam (hr.md §2.7 — "employee balance"; §5.2 Batch 1: bonus/jarima
//  allaqachon `gross` ichida). Pulning o'zi FAQAT payroll hujjati POST
//  qilinganda jurnalga tushadi. Shuning uchun bu yerda `ILedgerService`
//  ISHLATILMAYDI — lekin xuddi jurnal yozuvlari kabi APPEND-ONLY va
//  STORNO bilan tuzatiladi (SPEC §4.1).
//
//  "OYLIK ORQAGA SURILMAYDI" (SPEC §4, "no back-dating")
//  --------------------------------------------------------
//  `PeriodYear`/`PeriodMonth` joriy oydan OLDINGI bo'lishi mumkin emas —
//  bu bazada CHECK emas (joriy oy VAQT bilan o'zgaradi, bazadagi CHECK esa
//  doimiy bo'lishi kerak), shuning uchun shu yerda tekshiriladi.
//
//  "POSTED PAYROLL" TEKSHIRUVI — HALI YO'Q
//  -----------------------------------------
//  F11.01: "reversing a row already inside a posted payroll → 409". Bu
//  tekshiruv `payroll_documents` ga bog'liq, u esa HR-09 ning ishi (hali
//  qurilmagan — bu migratsiyaning izohi, `PayrollAdjustments.cs` boshida).
//  Hozircha bu yerda YO'Q; HR-09 qurilganda `ReverseAsync` shu tekshiruvni
//  oladi. Bu yerda faqat "allaqachon storno qilingan" (baza darajasidagi
//  unikal indeks bilan qo'shimcha himoyalangan) tekshiriladi.
// ===========================================================================

/// <summary>Sabab katalogi qatori — o'qish uchun.</summary>
public record AdjustmentReasonDto(Guid Id, string Kind, string Name, bool IsActive, int Position);

/// <summary>Yangi sabab. <c>Kind</c> yaratilgandan keyin O'ZGARMAYDI (pastdagi izoh).</summary>
public record CreateAdjustmentReasonRequest(string Kind, string Name, int Position);

/// <summary>
/// Sababni tahrirlash. <c>Kind</c> YO'Q — u yaratilgandan keyin muzlaydi:
/// kompozit FK (<c>payroll_adjustments.(reason_id, kind)</c>) allaqachon
/// ishlatilgan sababning kind'i o'zgarsa, eski yozuvlar bilan MOS
/// KELMAY QOLARDI. Kind'ni o'zgartirish kerak bo'lsa — yangi sabab yozing,
/// eskisini faolsizlantiring.
/// </summary>
public record UpdateAdjustmentReasonRequest(string Name, bool IsActive, int Position);

/// <summary>Bonus/jarima yozuvi — o'qish uchun.</summary>
public record PayrollAdjustmentDto(
    Guid Id,
    string EmployeeKind,
    string EmployeeId,
    string EmployeeName,
    string Kind,
    Guid ReasonId,
    string ReasonName,
    decimal Amount,
    short PeriodYear,
    short PeriodMonth,
    string? Comment,
    string? ImageUrl,
    string CreatedBy,
    string CreatedByName,
    DateTimeOffset CreatedAt,
    Guid? ReversalOf,
    bool Reversed);

/// <summary>
/// Yangi bonus/jarima. <c>EmployeeKind</c>: <see cref="EmployeeKind.Teacher"/>
/// yoki <see cref="EmployeeKind.Staff"/>. <c>createdBy</c> so'rovda YO'Q —
/// JWT'dan (SPEC §4.4).
/// </summary>
public record CreatePayrollAdjustmentRequest(
    string EmployeeKind,
    string EmployeeId,
    string Kind,
    Guid ReasonId,
    decimal Amount,
    short PeriodYear,
    short PeriodMonth,
    string? Comment,
    string? ImageUrl);

/// <summary>Registr filtri.</summary>
public record PayrollAdjustmentQuery(
    string? Kind = null,
    string? EmployeeKind = null,
    string? EmployeeId = null,
    short? PeriodYear = null,
    short? PeriodMonth = null);

/// <summary>Xodim turi — <see cref="PayrollAdjustment.TeacherId"/> / <see cref="PayrollAdjustment.UserId"/> ning qaysi biri to'lganini bildiradi.</summary>
public static class EmployeeKind
{
    public const string Teacher = "teacher";
    public const string Staff = "staff";

    public static readonly IReadOnlyList<string> All = [Teacher, Staff];
}

public interface IPayrollAdjustmentService
{
    // ---- Sabab katalogi (F11.02) ----
    Task<IReadOnlyList<AdjustmentReasonDto>> ListReasonsAsync(string? kind, CancellationToken ct = default);
    Task<AdjustmentReasonDto> CreateReasonAsync(CreateAdjustmentReasonRequest request, CancellationToken ct = default);
    Task<AdjustmentReasonDto> UpdateReasonAsync(Guid id, UpdateAdjustmentReasonRequest request, CancellationToken ct = default);

    // ---- Bonus/jarima registri (F11.01) ----
    Task<PayrollAdjustmentDto> CreateAsync(
        CreatePayrollAdjustmentRequest request, string actorId, CancellationToken ct = default);
    Task<PayrollAdjustmentDto> ReverseAsync(
        Guid id, string reason, string actorId, CancellationToken ct = default);
    Task<IReadOnlyList<PayrollAdjustmentDto>> ListAsync(
        PayrollAdjustmentQuery query, CancellationToken ct = default);
    Task<PayrollAdjustmentDto?> GetAsync(Guid id, CancellationToken ct = default);
}

/// <inheritdoc cref="IPayrollAdjustmentService"/>
public sealed class PayrollAdjustmentService(IAppDbContext db) : IPayrollAdjustmentService
{
    /// <summary>Baza ustuni <c>numeric(14,2)</c> — arifmetika ham shu aniqlikda.</summary>
    private const int MoneyScale = 2;

    /// <summary>Registrning yuqori chegarasi — bitta oylik varaq uchun yetarlicha katta.</summary>
    private const int MaxRows = 5000;

    /// <summary><c>audit_log.entity_type</c> — sabab katalogi.</summary>
    public const string AuditEntityAdjustmentReason = "AdjustmentReason";

    /// <summary><c>audit_log.entity_type</c> — bonus/jarima registri.</summary>
    public const string AuditEntityPayrollAdjustment = "PayrollAdjustment";

    private readonly ActorNames actors = new(db);

    // -----------------------------------------------------------------
    //  Sabab katalogi (F11.02) — moliyaviy emas, to'liq CRUD
    // -----------------------------------------------------------------

    public async Task<IReadOnlyList<AdjustmentReasonDto>> ListReasonsAsync(
        string? kind, CancellationToken ct = default)
    {
        var q = db.AdjustmentReasons.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(kind))
            q = q.Where(r => r.Kind == RequireKind(kind));

        var rows = await q
            .OrderBy(r => r.Kind).ThenBy(r => r.Position).ThenBy(r => r.Name)
            .ToListAsync(ct);

        return [.. rows.Select(ToDto)];
    }

    public async Task<AdjustmentReasonDto> CreateReasonAsync(
        CreateAdjustmentReasonRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var kind = RequireKind(request.Kind);
        var name = Trim(request.Name)
            ?? throw BillingRuleException.Invalid("reason_name_required", "Sabab nomi majburiy.");

        if (await db.AdjustmentReasons.AsNoTracking()
                .AnyAsync(r => r.Kind == kind && r.Name == name, ct))
            throw BillingRuleException.Conflict("reason_name_taken",
                $"'{name}' nomli sabab shu turda allaqachon bor.");

        var reason = new AdjustmentReason
        {
            Kind = kind,
            Name = name,
            IsActive = true,
            Position = request.Position,
        };
        db.AdjustmentReasons.Add(reason);
        await db.SaveChangesAsync(ct);

        return ToDto(reason);
    }

    public async Task<AdjustmentReasonDto> UpdateReasonAsync(
        Guid id, UpdateAdjustmentReasonRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var reason = await db.AdjustmentReasons.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw BillingRuleException.NotFound("reason_not_found", "Sabab topilmadi.");

        var name = Trim(request.Name)
            ?? throw BillingRuleException.Invalid("reason_name_required", "Sabab nomi majburiy.");

        if (await db.AdjustmentReasons.AsNoTracking()
                .AnyAsync(r => r.Id != id && r.Kind == reason.Kind && r.Name == name, ct))
            throw BillingRuleException.Conflict("reason_name_taken",
                $"'{name}' nomli sabab shu turda allaqachon bor.");

        reason.Name = name;
        reason.IsActive = request.IsActive;
        reason.Position = request.Position;
        await db.SaveChangesAsync(ct);

        return ToDto(reason);
    }

    // -----------------------------------------------------------------
    //  Bonus/jarima registri (F11.01) — moliyaviy, FAQAT QO'SHILADI
    // -----------------------------------------------------------------

    public async Task<PayrollAdjustmentDto> CreateAsync(
        CreatePayrollAdjustmentRequest request, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireActor(actorId);

        var kind = RequireKind(request.Kind);
        var amount = Money(request.Amount);
        var (employeeKind, teacherId, userId) = await RequireEmployeeAsync(
            request.EmployeeKind, request.EmployeeId, ct);
        RequirePeriodNotPast(request.PeriodYear, request.PeriodMonth);

        var reason = await db.AdjustmentReasons.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.ReasonId, ct)
            ?? throw BillingRuleException.NotFound("reason_not_found", "Sabab topilmadi.");
        if (reason.Kind != kind)
            throw BillingRuleException.Invalid("reason_kind_mismatch",
                $"'{reason.Name}' — {Label(reason.Kind)} sababi, {Label(kind)} yozuviga bog'lab bo'lmaydi.");
        if (!reason.IsActive)
            throw BillingRuleException.Invalid("reason_inactive",
                $"'{reason.Name}' sababi faolsizlantirilgan — yangi yozuvda tanlanmaydi.");

        var adjustment = new PayrollAdjustment
        {
            TeacherId = teacherId,
            UserId = userId,
            Kind = kind,
            ReasonId = reason.Id,
            Amount = amount,
            PeriodYear = request.PeriodYear,
            PeriodMonth = request.PeriodMonth,
            Comment = Trim(request.Comment),
            ImageUrl = Trim(request.ImageUrl),
            CreatedBy = actorId,
            CreatedAt = AppClock.NowInstant,
            ReversalOf = null,
        };

        db.PayrollAdjustments.Add(adjustment);
        db.AuditLogs.Add(AuditService.Entry(
            AuditEntityPayrollAdjustment, adjustment.Id.ToString("D"), "create",
            $"{Label(kind)} yozildi ({employeeKind}): {AuditService.Money(amount)} so'm, "
            + $"sabab: {reason.Name}, davr: {request.PeriodYear}-{request.PeriodMonth:D2}",
            actorId: actorId,
            actorName: await actors.OfAsync(actorId, ct),
            after: Snapshot(adjustment)));

        await db.SaveChangesAsync(ct);

        return (await ToDtosAsync([adjustment], ct))[0];
    }

    public async Task<PayrollAdjustmentDto> ReverseAsync(
        Guid id, string reason, string actorId, CancellationToken ct = default)
    {
        RequireActor(actorId);

        var cleanReason = Trim(reason)
            ?? throw BillingRuleException.Invalid("reason_required",
                "Bekor qilish sababi majburiy — u yozuvda qoladi.");

        var original = await db.PayrollAdjustments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw BillingRuleException.NotFound("adjustment_not_found", "Yozuv topilmadi.");

        if (original.ReversalOf is not null)
            throw BillingRuleException.Conflict("already_reversal",
                "Bu yozuvning O'ZI storno. Storno'ni storno qilib bo'lmaydi — kerak bo'lsa yangi yozuv yozing.");

        // Ikki marta storno = summani ikki marta "bekor qilish". Bazada ham
        // shunday (`reversal_of` unikal), bu yerdagi tekshiruv sababni aniq
        // aytish uchun (`CashHandoverService.ReverseAsync` bilan bir xil naqsh).
        if (await db.PayrollAdjustments.AsNoTracking().AnyAsync(a => a.ReversalOf == id, ct))
            throw BillingRuleException.Conflict("already_reversed", "Bu yozuv allaqachon bekor qilingan.");

        // TODO(HR-09): "reversing a row already inside a posted payroll → 409"
        // (F11.01) — payroll_documents hali qurilmagan (PayrollAdjustments.cs
        // boshidagi izoh). HR-09 shu yerga tekshiruv qo'shadi.

        var mirror = new PayrollAdjustment
        {
            TeacherId = original.TeacherId,
            UserId = original.UserId,
            Kind = original.Kind,
            ReasonId = original.ReasonId,
            // Summa MUSBAT bo'lib qoladi (`ck_payroll_adjustments_amount`);
            // "bu qarshi qator" degan ma'noni `ReversalOf` beradi — xuddi
            // `CashHandover`/`payments` dagidek (SPEC §4.1).
            Amount = original.Amount,
            PeriodYear = original.PeriodYear,
            PeriodMonth = original.PeriodMonth,
            Comment = original.Comment,
            ImageUrl = original.ImageUrl,
            CreatedBy = actorId,
            CreatedAt = AppClock.NowInstant,
            ReversalOf = original.Id,
            ReversalReason = cleanReason,
        };

        db.PayrollAdjustments.Add(mirror);
        db.AuditLogs.Add(AuditService.Entry(
            AuditEntityPayrollAdjustment, original.Id.ToString("D"), "reverse",
            $"{Label(original.Kind)} STORNO qilindi: {AuditService.Money(original.Amount)} so'm. "
            + $"Sabab: {cleanReason}",
            actorId: actorId,
            actorName: await actors.OfAsync(actorId, ct),
            before: Snapshot(original),
            after: Snapshot(mirror)));

        await db.SaveChangesAsync(ct);

        return (await ToDtosAsync([mirror], ct))[0];
    }

    public async Task<IReadOnlyList<PayrollAdjustmentDto>> ListAsync(
        PayrollAdjustmentQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var q = db.PayrollAdjustments.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Kind))
            q = q.Where(a => a.Kind == RequireKind(query.Kind));

        if (!string.IsNullOrWhiteSpace(query.EmployeeId))
        {
            q = query.EmployeeKind switch
            {
                EmployeeKind.Teacher => q.Where(a => a.TeacherId == query.EmployeeId),
                EmployeeKind.Staff => q.Where(a => a.UserId == query.EmployeeId),
                _ => q.Where(a => a.TeacherId == query.EmployeeId || a.UserId == query.EmployeeId),
            };
        }

        if (query.PeriodYear is { } year) q = q.Where(a => a.PeriodYear == year);
        if (query.PeriodMonth is { } month) q = q.Where(a => a.PeriodMonth == month);

        var rows = await q
            .OrderByDescending(a => a.CreatedAt)
            .Take(MaxRows)
            .ToListAsync(ct);

        return await ToDtosAsync(rows, ct);
    }

    public async Task<PayrollAdjustmentDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var row = await db.PayrollAdjustments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        return row is null ? null : (await ToDtosAsync([row], ct))[0];
    }

    // -----------------------------------------------------------------
    //  Ichki yordamchilar
    // -----------------------------------------------------------------

    private async Task<(string EmployeeKind, string? TeacherId, string? UserId)> RequireEmployeeAsync(
        string? employeeKind, string? employeeId, CancellationToken ct)
    {
        var id = Trim(employeeId)
            ?? throw BillingRuleException.Invalid("employee_required", "Xodim tanlanishi shart.");

        var kind = employeeKind?.Trim().ToLowerInvariant();
        if (kind is null || !EmployeeKind.All.Contains(kind, StringComparer.Ordinal))
            throw BillingRuleException.Invalid("invalid_employee_kind",
                $"Noma'lum xodim turi: '{employeeKind}'. Ruxsat etilganlar: "
                + string.Join(", ", EmployeeKind.All));

        if (kind == EmployeeKind.Teacher)
        {
            if (!await db.Teachers.AsNoTracking().AnyAsync(t => t.Id == id, ct))
                throw BillingRuleException.NotFound("employee_not_found", "O'qituvchi topilmadi.");
            return (EmployeeKind.Teacher, id, null);
        }

        if (!await db.Users.AsNoTracking().AnyAsync(u => u.Id == id, ct))
            throw BillingRuleException.NotFound("employee_not_found", "Xodim topilmadi.");
        return (EmployeeKind.Staff, null, id);
    }

    /// <summary>
    /// SPEC §4 — "no back-dating". Davr joriy oydan OLDINGI bo'lishi mumkin
    /// emas; joriy yoki KEYINGI oy ruxsat etiladi (hali yopilmagan oyga
    /// yozish — oddiy holat, EduSchool'ning o'zi ham oldindan yozishga
    /// yo'l qo'yadi).
    /// </summary>
    private static void RequirePeriodNotPast(short year, short month)
    {
        if (month is < 1 or > 12)
            throw BillingRuleException.Invalid("invalid_period",
                $"Noto'g'ri oy: {month}. 1 dan 12 gacha bo'lishi kerak.");

        var today = AppClock.Today;
        var requested = new DateOnly(year, month, 1);
        var current = new DateOnly(today.Year, today.Month, 1);

        if (requested < current)
            throw BillingRuleException.Invalid("period_in_past",
                $"{year}-{month:D2} allaqachon o'tgan oy — orqaga sana qo'yib bo'lmaydi (SPEC §4). "
                + "Joriy yoki keyingi oyni tanlang.");
    }

    private async Task<List<PayrollAdjustmentDto>> ToDtosAsync(
        IReadOnlyList<PayrollAdjustment> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];

        var ids = rows.Select(a => a.Id).ToList();
        var teacherIds = rows.Where(a => a.TeacherId is not null).Select(a => a.TeacherId!).Distinct().ToList();
        var userIds = rows.Select(a => a.UserId).Where(x => x is not null).Select(x => x!)
            .Concat(rows.Select(a => a.CreatedBy))
            .Distinct(StringComparer.Ordinal).ToList();
        var reasonIds = rows.Select(a => a.ReasonId).Distinct().ToList();

        var teacherNames = await db.Teachers.AsNoTracking()
            .Where(t => teacherIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.FullName, ct);

        var userNames = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        var reasonNames = await db.AdjustmentReasons.AsNoTracking()
            .Where(r => reasonIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Name, ct);

        // "Storno qilinganmi" — bitta so'rov, sikl ichida emas.
        var reversed = await db.PayrollAdjustments.AsNoTracking()
            .Where(a => a.ReversalOf != null && ids.Contains(a.ReversalOf.Value))
            .Select(a => a.ReversalOf!.Value)
            .ToListAsync(ct);

        return [.. rows.Select(a =>
        {
            var isTeacher = a.TeacherId is not null;
            var employeeId = isTeacher ? a.TeacherId! : a.UserId!;
            var employeeName = isTeacher
                ? teacherNames.GetValueOrDefault(employeeId, "Noma'lum")
                : userNames.GetValueOrDefault(employeeId, "Noma'lum");

            return new PayrollAdjustmentDto(
                a.Id,
                isTeacher ? EmployeeKind.Teacher : EmployeeKind.Staff,
                employeeId,
                employeeName,
                a.Kind,
                a.ReasonId,
                reasonNames.GetValueOrDefault(a.ReasonId, "Noma'lum"),
                a.Amount,
                a.PeriodYear,
                a.PeriodMonth,
                a.Comment,
                a.ImageUrl,
                a.CreatedBy,
                userNames.GetValueOrDefault(a.CreatedBy, "Noma'lum"),
                a.CreatedAt,
                a.ReversalOf,
                reversed.Contains(a.Id));
        })];
    }

    private static AdjustmentReasonDto ToDto(AdjustmentReason r) =>
        new(r.Id, r.Kind, r.Name, r.IsActive, r.Position);

    /// <summary>Audit uchun snapshot (SPEC §4.6).</summary>
    private static object Snapshot(PayrollAdjustment a) => new
    {
        a.Id,
        a.TeacherId,
        a.UserId,
        a.Kind,
        a.ReasonId,
        a.Amount,
        a.PeriodYear,
        a.PeriodMonth,
        a.CreatedBy,
        a.ReversalOf,
        a.ReversalReason,
    };

    private static string Label(string kind) =>
        string.Equals(kind, AdjustmentKind.Bonus, StringComparison.Ordinal) ? "Bonus" : "Jarima";

    private static string RequireKind(string? kind)
    {
        var clean = kind?.Trim().ToLowerInvariant();
        return clean is not null && AdjustmentKind.All.Contains(clean, StringComparer.Ordinal)
            ? clean
            : throw BillingRuleException.Invalid("invalid_kind",
                $"Noma'lum tur: '{kind}'. Ruxsat etilganlar: {string.Join(", ", AdjustmentKind.All)}.");
    }

    /// <summary>
    /// Summa musbat va baza aniqligiga (2 kasr) mos bo'lishi shart. Yaxlitlab
    /// yubormaymiz: 1000.005 "qabul qilindi" bo'lib ko'rinib, bazada boshqa
    /// raqam bo'lib qolardi (`CashHandoverService.Money` bilan bir xil naqsh).
    /// </summary>
    private static decimal Money(decimal value)
    {
        if (decimal.Round(value, MoneyScale) != value)
            throw BillingRuleException.Invalid("invalid_amount",
                $"Summa tiyin aniqligida (2 kasr) bo'lishi shart: {value}.");
        if (value <= 0m)
            throw BillingRuleException.Invalid("invalid_amount", "Summa musbat bo'lishi shart.");
        return value;
    }

    private static void RequireActor(string? actorId)
    {
        if (string.IsNullOrWhiteSpace(actorId))
            throw new ArgumentException(
                "Shaxs noma'lum. U JWT claim'idan olinadi — SPEC §4.4.", nameof(actorId));
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

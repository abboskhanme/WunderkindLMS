using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  QARZDORLAR BILAN ISHLASH — yig'ish CRM'ining yupqa varianti (§3.5).
// ===========================================================================
//
//  NIMA QILADI: rangli HOLAT ma'lumotnomasini boshqaradi va qarzdor bo'yicha
//  AMALLAR tarixini yozadi (izoh, holat, kelishilgan yangi to'lov sanasi).
//
//  NIMA QILMAYDI — VA HECH QACHON QILMAYDI (SPEC §4.1)
//  --------------------------------------------------
//  `payments`, `payment_allocations`, `ledger_entries` ga YOZMAYDI. Qarzning
//  o'zini ham o'zgartirmaydi: bu modul qarz haqida nima QILINGANINI yozadi,
//  qarzning o'zini emas. Shu faylda `Add`/`Update` faqat ikkita jadvalga
//  tegadi: `debtor_statuses` va `debtor_actions` (+ `audit_logs`).
//
//  UCHTA QAT'IY QOIDA
//  ------------------
//  1. JORIY HOLAT HISOBLANADI — saqlanmaydi. U eng oxirgi tirik amalning
//     holati (<see cref="RowsAsync"/>). Alohida ustun bo'lganida u tarix
//     bilan bir kunda ziddiyatga tushardi (`students.balance`, P1-21).
//  2. AMAL O'CHIRILMAYDI — <c>deleted_at</c> qo'yiladi
//     (<see cref="DeleteActionAsync"/>). Uch yildan keyin kimdir aynan
//     "nima va'da qilingan edi" deb qaraydi.
//  3. HOLAT QATORI O'CHIRILMAYDI — <c>is_active = false</c> qilinadi
//     (<see cref="RetireStatusAsync"/>). Bazadagi FK ham RESTRICT: ishlatilgan
//     holatni o'chirishga urinish 23503 beradi.
//
//  AUDIT
//  -----
//  Amallar `AuditService.EntityDebtorAction` ostida yoziladi (umumiy fayl,
//  docs/modules/existing-module-gaps.md §8). Holat ma'lumotnomasi uchun u
//  yerda konstanta YO'Q, shuning uchun <see cref="EntityDebtorStatus"/> shu
//  yerda turibdi — `AuditService` ga ko'chirilishi kerak (hisobotda).
//  Qiymat o'sha faylning uslubiga mos: entity klass nomining o'zi.
//
//  NEGA DI'DA YO'Q
//  ---------------
//  `Program.cs` ga tegilmaydi (parallel vazifalar konflikt maydoni), shuning
//  uchun controller uni so'rov doirasidagi kontekst ustidan o'zi yaratadi —
//  `FinanceReportsController` → `FinanceReportQueries` bilan bir xil naqsh.

/// <summary>
/// Qarzdor holatlari ma'lumotnomasi va amallar tarixi (§3.5).
/// </summary>
public sealed partial class DebtorWorkflowService(IAppDbContext db)
{
    /// <summary>
    /// Audit jurnalidagi entity turi — holat ma'lumotnomasi. <c>AuditService</c>
    /// da hali yo'q; u yerga ko'chirilguncha shu yerda.
    /// </summary>
    public const string EntityDebtorStatus = "DebtorStatus";

    /// <summary>Izohning yuqori chegarasi — matn maydonini hujjat saqlashga aylantirmaslik uchun.</summary>
    public const int MaxCommentLength = 2000;

    /// <summary>
    /// Va'da sanasining ishonchli oynasi: bir yil orqaga, bir yil oldinga.
    /// Chegara TERISHDAGI XATO uchun: "2226" deb yozilgan va'da hech qachon
    /// o'tib ketmaydi, ya'ni qarzdor jimgina ro'yxatdan chiqib ketardi.
    /// </summary>
    public const int PromiseWindowDays = 366;

    private readonly ActorNames actors = new(db);

    // =====================================================================
    //  1. Holat ma'lumotnomasi
    // =====================================================================

    /// <summary>
    /// Katalog, <c>position</c> bo'yicha. Sukut bo'yicha faqat FAOL qatorlar:
    /// "Amal qo'shish" oynasi tanlovi shu ro'yxatdan.
    /// </summary>
    /// <param name="includeInactive">true = sozlamalar ekrani uchun, chiqarilganlar bilan.</param>
    public async Task<IReadOnlyList<DebtorStatusDto>> StatusesAsync(
        bool includeInactive = false, CancellationToken ct = default)
    {
        var q = db.DebtorStatuses.AsNoTracking();
        if (!includeInactive) q = q.Where(s => s.IsActive);

        return await q
            .OrderBy(s => s.Position).ThenBy(s => s.Name)
            .Select(s => new DebtorStatusDto(s.Id, s.Name, s.Color, s.Hint, s.Position, s.IsActive))
            .ToListAsync(ct);
    }

    /// <summary>Yangi holat qo'shadi.</summary>
    public async Task<DebtorStatusDto> CreateStatusAsync(
        SaveDebtorStatusRequest request, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (name, color, hint, position) = Validate(request);

        var status = new DebtorStatus
        {
            Name = name,
            Color = color,
            Hint = hint,
            Position = position,
            IsActive = request.IsActive,
        };

        db.DebtorStatuses.Add(status);
        db.AuditLogs.Add(AuditService.Entry(
            EntityDebtorStatus, status.Id.ToString("D"), "create",
            $"Qarzdor holati qo'shildi: \"{status.Name}\".",
            actorId: actorId,
            actorName: await actors.OfAsync(actorId, ct),
            after: Snapshot(status)));

        await SaveWithNameGuardAsync(name, ct);
        return ToDto(status);
    }

    /// <summary>
    /// Holatni tahrirlaydi. Nomi, rangi, izohi, tartibi va faolligi —
    /// hammasi o'zgaradi; qator O'CHIRILMAYDI.
    /// </summary>
    public async Task<DebtorStatusDto> UpdateStatusAsync(
        Guid id, SaveDebtorStatusRequest request, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (name, color, hint, position) = Validate(request);

        var status = await db.DebtorStatuses.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw BillingRuleException.NotFound("status_not_found", "Bunday holat topilmadi.");

        var before = Snapshot(status);
        status.Name = name;
        status.Color = color;
        status.Hint = hint;
        status.Position = position;
        status.IsActive = request.IsActive;

        db.AuditLogs.Add(AuditService.Entry(
            EntityDebtorStatus, status.Id.ToString("D"), "update",
            $"Qarzdor holati tahrirlandi: \"{status.Name}\".",
            actorId: actorId,
            actorName: await actors.OfAsync(actorId, ct),
            before: before, after: Snapshot(status)));

        await SaveWithNameGuardAsync(name, ct);
        return ToDto(status);
    }

    /// <summary>
    /// Holatni katalogdan CHIQARADI (<c>is_active = false</c>) — o'chirmaydi.
    ///
    /// <para>
    /// Sabab <c>Debtors.cs</c> da: chiqarilgan holat eski amallarda ko'rinib
    /// turaveradi. O'chirilsa, o'sha amallar "qaysi holat edi" degan savolga
    /// javobsiz qolardi (bazada ham FK RESTRICT — o'chirishga umuman yo'l yo'q).
    /// Qaytarish uchun <see cref="UpdateStatusAsync"/> da
    /// <c>IsActive = true</c>.
    /// </para>
    /// </summary>
    public async Task<DebtorStatusDto> RetireStatusAsync(
        Guid id, string actorId, CancellationToken ct = default)
    {
        var status = await db.DebtorStatuses.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw BillingRuleException.NotFound("status_not_found", "Bunday holat topilmadi.");

        if (!status.IsActive) return ToDto(status);

        var before = Snapshot(status);
        status.IsActive = false;

        db.AuditLogs.Add(AuditService.Entry(
            EntityDebtorStatus, status.Id.ToString("D"), "retire",
            $"Qarzdor holati katalogdan chiqarildi: \"{status.Name}\" (qator o'chirilmadi).",
            actorId: actorId,
            actorName: await actors.OfAsync(actorId, ct),
            before: before, after: Snapshot(status)));

        await db.SaveChangesAsync(ct);
        return ToDto(status);
    }

    // =====================================================================
    //  2. Amallar tarixi
    // =====================================================================

    /// <summary>
    /// Bitta o'quvchining amallari, eng yangisidan. O'chirilganlar
    /// KO'RINMAYDI — lekin bazada qoladi.
    /// </summary>
    public async Task<IReadOnlyList<DebtorActionDto>> ActionsAsync(
        string studentId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(studentId);

        var rows = await db.DebtorActions.AsNoTracking()
            .Where(a => a.StudentId == studentId && a.DeletedAt == null)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

        return await ToDtosAsync(rows, ct);
    }

    /// <summary>
    /// Yangi amal yozadi. Shu amaldan keyin o'quvchining JORIY holati —
    /// aynan shu qatorniki (u eng oxirgisi bo'lib qoladi).
    /// </summary>
    /// <param name="studentId">Qaysi o'quvchi (bo'lmasa 404).</param>
    /// <param name="request">Izoh (majburiy), holat va va'da sanasi (ixtiyoriy).</param>
    /// <param name="actorId">Kim yozayotgani — JWT'dan (SPEC §4.4).</param>
    public async Task<DebtorActionDto> AddActionAsync(
        string studentId, CreateDebtorActionRequest request, string actorId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(studentId);
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(actorId))
            throw new ArgumentException(
                "Amalni kim yozayotgani noma'lum (JWT claim'i bo'sh) — SPEC §4.4.", nameof(actorId));

        var comment = (request.Comment ?? string.Empty).Trim();
        if (comment.Length == 0)
            throw BillingRuleException.Invalid(
                "comment_required",
                "Izoh majburiy: izohsiz amal \"kimdir nimadir qildi\" degani, ya'ni foydasiz.");
        if (comment.Length > MaxCommentLength)
            throw BillingRuleException.Invalid(
                "comment_too_long",
                $"Izoh juda uzun ({comment.Length} belgi). Ruxsat etilgani — {MaxCommentLength}.");

        var studentExists = await db.Students.AsNoTracking().AnyAsync(s => s.Id == studentId, ct);
        if (!studentExists)
            throw BillingRuleException.NotFound("student_not_found", "O'quvchi topilmadi.");

        DebtorStatus? status = null;
        if (request.StatusId is { } statusId)
        {
            status = await db.DebtorStatuses.AsNoTracking().FirstOrDefaultAsync(s => s.Id == statusId, ct)
                ?? throw BillingRuleException.NotFound("status_not_found", "Bunday holat topilmadi.");

            // Chiqarilgan holat YANGI amalda tanlanmaydi — eskilarida esa
            // ko'rinib turaveradi (`Debtors.cs`).
            if (!status.IsActive)
                throw BillingRuleException.Invalid(
                    "status_inactive",
                    $"\"{status.Name}\" holati katalogdan chiqarilgan — yangi amalda tanlab bo'lmaydi.");
        }

        if (request.PromisedOn is { } promised) ValidatePromise(promised);

        var action = new DebtorAction
        {
            StudentId = studentId,
            StatusId = request.StatusId,
            Comment = comment,
            PromisedOn = request.PromisedOn,
            CreatedBy = actorId,
            CreatedAt = AppClock.NowInstant,
        };

        db.DebtorActions.Add(action);

        var actorName = await actors.OfAsync(actorId, ct);
        db.AuditLogs.Add(AuditService.Entry(
            AuditService.EntityDebtorAction, action.Id.ToString("D"), "create",
            SummaryOf(action, status),
            actorId: actorId, actorName: actorName,
            after: Snapshot(action),
            studentId: studentId));

        await db.SaveChangesAsync(ct);

        return ToDto(action, status?.Name, status?.Color, actorName);
    }

    /// <summary>
    /// Amalni YUMSHOQ o'chiradi: <c>deleted_at</c> qo'yiladi, qator bazada
    /// qoladi.
    ///
    /// <para>
    /// §3.5: "What must not happen is deleting the history of what was
    /// promised". Shuning uchun bu yerda ham, controller'da ham haqiqiy
    /// <c>DELETE</c> yo'q. Baza darajasida taqiq YO'Q va ATAYLAB yo'q —
    /// jadval moliyaviy emas, `deploy/init-roles.sql` §5 ro'yxatiga
    /// qo'shilmaydi (migratsiyaning o'z izohi).
    /// </para>
    /// </summary>
    public async Task<DebtorActionDto> DeleteActionAsync(
        Guid id, string actorId, CancellationToken ct = default)
    {
        var action = await db.DebtorActions.FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw BillingRuleException.NotFound("action_not_found", "Bunday amal topilmadi.");

        if (action.DeletedAt is not null)
            throw BillingRuleException.Conflict(
                "action_already_deleted", "Bu amal allaqachon o'chirilgan.");

        var before = Snapshot(action);
        action.DeletedAt = AppClock.NowInstant;

        var actorName = await actors.OfAsync(actorId, ct);
        db.AuditLogs.Add(AuditService.Entry(
            AuditService.EntityDebtorAction, action.Id.ToString("D"), "delete",
            $"Qarzdor amali o'chirildi (yumshoq): \"{Shorten(action.Comment)}\".",
            actorId: actorId, actorName: actorName,
            before: before, after: Snapshot(action),
            studentId: action.StudentId));

        await db.SaveChangesAsync(ct);

        var statusName = action.StatusId is { } sid
            ? await db.DebtorStatuses.AsNoTracking()
                .Where(s => s.Id == sid).Select(s => s.Name).FirstOrDefaultAsync(ct)
            : null;

        return ToDto(action, statusName, null, actorName);
    }

    // =====================================================================
    //  3. Qarzdorlar ro'yxatining yangi ustunlari
    // =====================================================================

    /// <summary>
    /// Har o'quvchi uchun: JORIY holat, oxirgi amal, va'da sanasi va
    /// "va'da buzildimi" (§3.5).
    ///
    /// <para>
    /// Faqat kamida bitta TIRIK amali bor o'quvchilar qaytadi — qolganlari
    /// uchun ko'rsatiladigan narsa yo'q va ro'yxatni shishirishning ma'nosi
    /// ham yo'q.
    /// </para>
    /// <para>
    /// <b>Uchta so'rov + qoldiq</b>, o'quvchilar soniga bog'liq emas. Sikl
    /// ichida <c>await</c> yo'q.
    /// </para>
    /// </summary>
    /// <param name="className">Sinf bo'yicha filtr (aniq moslik).</param>
    public async Task<IReadOnlyList<DebtorWorkflowRowDto>> RowsAsync(
        string? className = null, CancellationToken ct = default)
    {
        var live = db.DebtorActions.AsNoTracking().Where(a => a.DeletedAt == null);

        // ---- 1. Eng oxirgi amal (joriy holat shundan) ----
        var latestRows = await live
            .Where(a => !live.Any(b => b.StudentId == a.StudentId && b.CreatedAt > a.CreatedAt))
            .ToListAsync(ct);

        var latest = Reduce(latestRows);
        if (latest.Count == 0) return [];

        // ---- 2. Eng oxirgi VA'DA (oxirgi amalda va'da bo'lmasligi mumkin) ----
        var promises = live.Where(a => a.PromisedOn != null);
        var promiseRows = await promises
            .Where(a => !promises.Any(b => b.StudentId == a.StudentId && b.CreatedAt > a.CreatedAt))
            .ToListAsync(ct);

        var promise = Reduce(promiseRows);

        // ---- 3. Amallar soni ----
        var counts = await live
            .GroupBy(a => a.StudentId)
            .Select(g => new { StudentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.StudentId, x => x.Count, ct);

        // ---- 4. O'quvchi (sinf filtri shu yerda) ----
        var ids = latest.Keys.ToList();
        var studentsQuery = db.Students.AsNoTracking().Where(s => ids.Contains(s.Id));
        if (!string.IsNullOrWhiteSpace(className))
        {
            var name = className.Trim();
            studentsQuery = studentsQuery.Where(s => s.ClassName == name);
        }

        var students = await studentsQuery
            .Select(s => new { s.Id, s.FullName, s.ClassName })
            .ToListAsync(ct);

        if (students.Count == 0) return [];

        // ---- 5. Holat nomlari va mualliflar ----
        var statusIds = latest.Values.Where(a => a.StatusId != null).Select(a => a.StatusId!.Value)
            .Distinct().ToList();
        var statuses = await db.DebtorStatuses.AsNoTracking()
            .Where(s => statusIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name, s.Color })
            .ToDictionaryAsync(s => s.Id, ct);

        var authorIds = latest.Values.Select(a => a.CreatedBy).Distinct(StringComparer.Ordinal).ToList();
        var authors = await db.Users.AsNoTracking()
            .Where(u => authorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        // ---- 6. "Va'da buzildi" — SANA + QARZ, ikkovi ham serverda ----
        // Frontend buni o'zi hisoblamaydi: bitta ekranda sana bilan, boshqasida
        // qarz bilan tekshirilsa, ikki ekran bir savolga har xil javob berardi.
        var today = AppClock.Today;
        var overdueIds = students
            .Select(s => s.Id)
            .Where(id => promise.TryGetValue(id, out var p) && p.PromisedOn < today)
            .ToList();

        IReadOnlyDictionary<string, decimal> balances = overdueIds.Count == 0
            ? new Dictionary<string, decimal>(StringComparer.Ordinal)
            : await new StudentBalanceQuery(db).ForManyAsync(overdueIds, ct);

        return [.. students
            .Select(s =>
            {
                var last = latest[s.Id];
                var status = last.StatusId is { } id ? statuses.GetValueOrDefault(id) : null;
                var promisedOn = promise.TryGetValue(s.Id, out var p) ? p.PromisedOn : null;

                return new DebtorWorkflowRowDto(
                    StudentId: s.Id,
                    FullName: s.FullName,
                    ClassName: s.ClassName,
                    StatusId: last.StatusId,
                    StatusName: status?.Name,
                    StatusColor: status?.Color,
                    LastActionAt: last.CreatedAt,
                    LastComment: last.Comment,
                    LastActionByName: authors.GetValueOrDefault(last.CreatedBy, "Noma'lum"),
                    PromisedOn: promisedOn,
                    // Manfiy qoldiq = qarz hali ochiq (StudentBalanceQuery).
                    PromiseBroken: promisedOn < today && balances.GetValueOrDefault(s.Id) < 0m,
                    ActionCount: counts.GetValueOrDefault(s.Id));
            })
            .OrderByDescending(r => r.PromiseBroken)
            .ThenByDescending(r => r.LastActionAt)];
    }

    // =====================================================================
    //  Ichki yordamchilar
    // =====================================================================

    /// <summary>
    /// Bir xil lahzada yozilgan qatorlarni bittaga siqadi. SQL'dagi
    /// <c>NOT EXISTS</c> qat'iy "&gt;" bilan ishlaydi, ya'ni teng
    /// <c>created_at</c> li ikki qator ikkovi ham qaytadi — <c>uuid</c>
    /// taqqoslashini SQL'ga tarjima qilishga tayanmaslik uchun ataylab.
    /// </summary>
    private static Dictionary<string, DebtorAction> Reduce(List<DebtorAction> rows) =>
        rows.GroupBy(a => a.StudentId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).First(),
                StringComparer.Ordinal);

    private async Task<IReadOnlyList<DebtorActionDto>> ToDtosAsync(
        IReadOnlyList<DebtorAction> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];

        var statusIds = rows.Where(a => a.StatusId != null).Select(a => a.StatusId!.Value).Distinct().ToList();
        var statuses = await db.DebtorStatuses.AsNoTracking()
            .Where(s => statusIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name, s.Color })
            .ToDictionaryAsync(s => s.Id, ct);

        var authorIds = rows.Select(a => a.CreatedBy).Distinct(StringComparer.Ordinal).ToList();
        var authors = await db.Users.AsNoTracking()
            .Where(u => authorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        return [.. rows.Select(a =>
        {
            var status = a.StatusId is { } id ? statuses.GetValueOrDefault(id) : null;
            return ToDto(a, status?.Name, status?.Color, authors.GetValueOrDefault(a.CreatedBy, "Noma'lum"));
        })];
    }

    private static DebtorActionDto ToDto(
        DebtorAction a, string? statusName, string? statusColor, string createdByName) =>
        new(
            a.Id, a.StudentId, a.StatusId, statusName, statusColor,
            a.Comment, a.PromisedOn,
            PromiseOverdue: a.PromisedOn is { } p && p < AppClock.Today,
            a.CreatedBy, createdByName, a.CreatedAt);

    private static DebtorStatusDto ToDto(DebtorStatus s) =>
        new(s.Id, s.Name, s.Color, s.Hint, s.Position, s.IsActive);

    /// <summary>
    /// So'rovni tozalaydi va tekshiradi. Baza ham tekshiradi
    /// (<c>ck_debtor_statuses_name</c>, <c>ck_debtor_statuses_position</c>) —
    /// bu yerdagi qavat 500 o'rniga tushunarli javob berish uchun.
    /// </summary>
    private static (string Name, string Color, string? Hint, int Position) Validate(
        SaveDebtorStatusRequest request)
    {
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0)
            throw BillingRuleException.Invalid("name_required", "Holat nomi bo'sh bo'lishi mumkin emas.");
        if (name.Length > 100)
            throw BillingRuleException.Invalid("name_too_long", "Holat nomi 100 belgidan uzun bo'lmasin.");

        var color = (request.Color ?? string.Empty).Trim();
        if (color.Length > 0 && !HexColor().IsMatch(color))
            throw BillingRuleException.Invalid(
                "invalid_color",
                $"Rang \"#RRGGBB\" ko'rinishida bo'lishi kerak (masalan #FF9500), berilgani — \"{color}\".");

        if (request.Position < 0)
            throw BillingRuleException.Invalid("invalid_position", "Tartib raqami manfiy bo'lishi mumkin emas.");

        var hint = string.IsNullOrWhiteSpace(request.Hint) ? null : request.Hint.Trim();

        return (name, color, hint, request.Position);
    }

    /// <summary>
    /// Va'da sanasining ishonchli oynasi — <see cref="PromiseWindowDays"/>
    /// izohiga qarang. O'tgan sana TAQIQLANMAYDI: suhbat o'tgan hafta bo'lib,
    /// kelishilgan sana allaqachon o'tib ketgan bo'lishi mumkin — bunday
    /// yozuv darhol "buzilgan va'da" bo'lib chiqadi va bu TO'G'RI.
    /// </summary>
    private static void ValidatePromise(DateOnly promised)
    {
        var today = AppClock.Today;
        var distance = Math.Abs(promised.DayNumber - today.DayNumber);
        if (distance > PromiseWindowDays)
            throw BillingRuleException.Invalid(
                "promise_out_of_range",
                $"Va'da sanasi haqiqatga o'xshamaydi: {promised:dd.MM.yyyy}. "
                + $"Ruxsat etilgan oraliq — bugundan ±{PromiseWindowDays} kun.");
    }

    /// <summary>
    /// <c>SaveChanges</c> + nom takrorlanishini o'zbekcha 409 ga aylantirish.
    /// Unikallikni BAZA kafolatlaydi (<c>ix_debtor_statuses_name</c>); oldindan
    /// <c>SELECT</c> qilib tekshirish poyga (race) oynasini qoldirardi.
    /// </summary>
    private async Task SaveWithNameGuardAsync(string name, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is DbException { SqlState: "23505" })
        {
            throw BillingRuleException.Conflict(
                "status_name_taken",
                $"\"{name}\" nomli holat allaqachon bor. Ikkita bir xil holat ro'yxatni chalkashtiradi.");
        }
    }

    private static string SummaryOf(DebtorAction action, DebtorStatus? status)
    {
        var parts = new List<string> { $"Qarzdor bo'yicha amal: \"{Shorten(action.Comment)}\"" };
        if (status is not null) parts.Add($"holat — {status.Name}");
        if (action.PromisedOn is { } promised) parts.Add($"va'da — {promised:dd.MM.yyyy}");
        return string.Join("; ", parts) + ".";
    }

    /// <summary>Audit xabari uchun izohning qisqa ko'rinishi.</summary>
    private static string Shorten(string text) =>
        text.Length <= 80 ? text : text[..77] + "...";

    private static object Snapshot(DebtorStatus s) => new
    {
        s.Id, s.Name, s.Color, s.Hint, s.Position, s.IsActive,
    };

    private static object Snapshot(DebtorAction a) => new
    {
        a.Id, a.StudentId, a.StatusId, a.Comment,
        PromisedOn = a.PromisedOn?.ToString("yyyy-MM-dd"),
        a.CreatedBy, a.CreatedAt, a.DeletedAt,
    };

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();
}

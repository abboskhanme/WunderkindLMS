using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

/// <summary>
/// Chegirmalar (P1-08). <b>Mijoz javobi (SPEC §8.1, Q5): CHEGARA YO'Q — har qanday
/// chegirma direktor tasdig'ini talab qiladi.</b>
///
/// <para>
/// docs/TASKS.md hali "chegaradan yuqori bo'lsa tasdiq kerak" deb turadi va
/// <c>409 approval_required</c> ni kutadi — bu MATN ESKIRGAN, mijoz javobi uni
/// bekor qildi. Farqi kichik emas: chegara bo'lsa, chegaradan past chegirma
/// DARHOL amal qilardi; chegara bo'lmasa, chegirma tug'ilishidanoq harakatsiz.
/// Shuning uchun:
/// </para>
/// <list type="number">
///   <item>Chegirma HAR DOIM <c>pending</c> holatda yaratiladi;</item>
///   <item>Yaratish javobi 409 EMAS — bu istisno emas, normal oqim. Chaqiruvchi
///   javobdagi <c>status = "pending"</c> ni ko'rib "tasdiq kutilmoqda" deb ko'rsatadi;</item>
///   <item><c>approved_by</c> qo'yilmaguncha hisob-kitobga UMUMAN ta'sir qilmaydi —
///   accrual (P1-09) faqat <c>approved</c> larni oladi;</item>
///   <item>Tasdiqlash faqat <c>superadmin</c>, va yaratuvchining O'ZI emas
///   (SPEC §4.5, baza: <c>ck_discounts_approver_differs</c>).</item>
/// </list>
///
/// <para>
/// <b>Ikki qavatli tekshiruv ataylab.</b> Rolni controller
/// (<c>[FinanceRole(FinanceAction.ApproveDiscount)]</c>) ham, bu xizmat ham
/// tekshiradi. Takror emas: controller atributini kelajakda kimdir olib tashlasa
/// yoki yangi endpoint yozib unutsa, chegirma baribir tasdiqlanmaydi. Pulga oid
/// qoida bitta qatlamning esidan chiqishiga bog'liq bo'lmasligi kerak.
/// </para>
/// </summary>
public sealed class DiscountService(IAppDbContext db, AuditService audit) : IDiscountService
{
    /// <summary>Audit yozuvidagi ob'ekt turi — eski chegirma yo'li bilan bir xil nom.</summary>
    private const string AuditEntity = AuditService.EntityStudentDiscount;

    /// <summary>Baza ustuni <c>numeric(14,2)</c> — hisob-kitob ham shu aniqlikda.</summary>
    private const int MoneyScale = 2;

    // ==================================================================
    //  Arifmetika — KO'CHIRILGAN, qayta yozilmagan
    // ==================================================================

    /// <summary>
    /// Chegirmadan keyingi summa. <b>Bu — <c>TuitionService.ChargeFor</c> ning AYNAN
    /// o'sha arifmetikasi</b> (P1-08 qabul mezoni: "moved not rewritten"):
    /// avval foiz, keyin aniq summa, quyi chegara 0, oxirida 2 kasrga yaxlitlash.
    ///
    /// <para>
    /// Yagona farq — <paramref name="percent"/> tipi: eski kodda <c>int</c>
    /// (o'quvchi qatoridagi chegirma ustuni, P1-21 da o'chdi), yangi ustun esa
    /// <c>numeric(5,2)</c>, ya'ni
    /// 12.5% ham yozilishi mumkin. Butun foizlarda natija bir xil bo'lib qoladi —
    /// buni <c>DiscountServiceTests</c> ikkala funksiyani bir xil kirishlarda
    /// solishtirib tekshiradi (P1-23 shu solishtirishni davom ettiradi).
    /// </para>
    /// <para>
    /// <b>Tartib muhim.</b> "Avval foiz, keyin summa" va "keyin foiz, avval summa"
    /// har xil natija beradi. Eski kod birinchisini qiladi va bugungi hisob-kitob
    /// shunga qurilgan — shuning uchun tartib O'ZGARTIRILMAYDI.
    /// </para>
    /// </summary>
    /// <param name="grossAmount">To'liq summa (obunadagi oylik narx).</param>
    /// <param name="percent">Foiz 0..100 (chegaradan tashqarisi qisiladi).</param>
    /// <param name="amount">Foizdan KEYIN ayriladigan aniq summa.</param>
    public decimal ChargeFor(decimal grossAmount, decimal percent, decimal amount)
    {
        if (grossAmount <= 0) return 0m;
        var pct = Math.Clamp(percent, 0m, 100m);
        var flat = Math.Max(0m, amount);
        var afterPct = grossAmount * (100m - pct) / 100m;
        var charge = afterPct - flat;
        if (charge < 0m) charge = 0m;
        return decimal.Round(charge, MoneyScale);
    }

    /// <inheritdoc />
    public decimal DiscountFor(decimal grossAmount, decimal percent, decimal amount)
    {
        if (grossAmount <= 0) return 0m;
        return decimal.Round(grossAmount - ChargeFor(grossAmount, percent, amount), MoneyScale);
    }

    // ==================================================================
    //  O'qish
    // ==================================================================

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiscountDto>> ListAsync(
        DiscountQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Filtr ENTITY ustida — ya'ni SQL'da (`QueryAsync` izohiga qarang).
        var source = db.Discounts.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.StudentId))
            source = source.Where(d => d.StudentId == query.StudentId);

        // DIQQAT: `category_id = null` chegirma BARCHA toifalarga tegishli (SPEC §3.7),
        // shuning uchun toifa bo'yicha filtr uni ham qaytaradi. Ya'ni savol
        // "shu toifaga TEGISHLI chegirmalar", "shu toifa YOZILGAN qatorlar" emas —
        // aks holda accrual umumiy chegirmani ko'rmay qolardi.
        if (query.CategoryId is { } categoryId)
            source = source.Where(d => d.CategoryId == categoryId || d.CategoryId == null);

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = RequireStatus(query.Status);
            source = source.Where(d => d.Status == status);
        }

        return await QueryAsync(source, oldestFirst: false, ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiscountDto>> PendingAsync(CancellationToken ct = default) =>
        // Eng eski so'rov birinchi: direktor navbati "kim ko'proq kutdi" tartibida.
        await QueryAsync(
            db.Discounts.AsNoTracking().Where(d => d.Status == DiscountStatus.Pending),
            oldestFirst: true, ct);

    /// <summary>Bitta chegirma; topilmasa null.</summary>
    public async Task<DiscountDto?> GetAsync(Guid id, CancellationToken ct = default) =>
        (await QueryAsync(db.Discounts.AsNoTracking().Where(d => d.Id == id), oldestFirst: false, ct))
        .FirstOrDefault();

    // ==================================================================
    //  Yozish
    // ==================================================================

    /// <inheritdoc />
    public async Task<DiscountDto> CreateAsync(
        CreateDiscountRequest request, string createdByUserId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(createdByUserId))
            throw new ArgumentException(
                "Chegirmani kim so'rayotgani noma'lum (createdByUserId bo'sh).", nameof(createdByUserId));

        var percent = decimal.Round(request.Percent, MoneyScale);
        var amount = decimal.Round(request.Amount, MoneyScale);

        if (percent is < 0m or > 100m)
            throw BillingRuleException.Invalid("invalid_percent", "Chegirma foizi 0 va 100 orasida bo'lishi kerak.");
        if (amount < 0m)
            throw BillingRuleException.Invalid("invalid_amount", "Chegirma summasi manfiy bo'la olmaydi.");
        if (percent == 0m && amount == 0m)
            throw BillingRuleException.Invalid(
                "empty_discount", "Chegirma bo'sh: foiz ham, summa ham 0. Hech bo'lmaganda bittasini kiriting.");

        var reason = (request.Reason ?? string.Empty).Trim();
        if (reason.Length == 0)
            throw BillingRuleException.Invalid("reason_required", "Chegirma sababi majburiy (SPEC §4.3).");

        if (request.EndsOn is { } endsOn && endsOn < request.StartsOn)
            throw BillingRuleException.Invalid(
                "invalid_period",
                $"Tugash sanasi ({endsOn:yyyy-MM-dd}) boshlanish sanasidan ({request.StartsOn:yyyy-MM-dd}) oldin bo'la olmaydi.");

        var student = await db.Students.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.StudentId, ct)
            ?? throw BillingRuleException.NotFound("student_not_found", "O'quvchi topilmadi.");
        if (student.IsArchived)
            throw BillingRuleException.Conflict(
                "student_archived", $"{student.FullName} arxivlangan — unga chegirma berib bo'lmaydi.");

        FeeCategory? category = null;
        if (request.CategoryId is { } categoryId)
        {
            category = await db.FeeCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == categoryId, ct)
                ?? throw BillingRuleException.NotFound("category_not_found", "To'lov toifasi topilmadi.");
        }

        var discount = new Discount
        {
            StudentId = student.Id,
            CategoryId = category?.Id,
            Percent = percent,
            Amount = amount,
            Reason = reason,
            StartsOn = request.StartsOn,
            EndsOn = request.EndsOn,
            // Mijoz javobi (SPEC §8.1 Q5): boshqa boshlang'ich holat YO'Q.
            Status = DiscountStatus.Pending,
            CreatedBy = createdByUserId,
            ApprovedBy = null,
            DecidedAt = null,
            CreatedAt = AppClock.NowInstant,
        };
        db.Discounts.Add(discount);

        audit.Record(
            AuditEntity, discount.Id.ToString(), "create",
            $"Chegirma so'raldi ({CategoryLabel(category)}): {Describe(percent, amount)} — "
            + $"sabab: {reason}. Direktor tasdig'i kutilmoqda.",
            after: Snapshot(discount), studentId: student.Id);

        await db.SaveChangesAsync(ct);
        return await RequireDtoAsync(discount.Id, ct);
    }

    /// <inheritdoc />
    public async Task<DiscountDto> ApproveAsync(
        Guid discountId, string approverId, CancellationToken ct = default)
    {
        var (discount, approver) = await RequireDecidableAsync(discountId, approverId, ct);

        var before = Snapshot(discount);
        discount.Status = DiscountStatus.Approved;
        discount.ApprovedBy = approver.Id;
        discount.DecidedAt = AppClock.NowInstant;

        audit.Record(
            AuditEntity, discount.Id.ToString(), "update",
            $"Chegirma TASDIQLANDI ({Describe(discount.Percent, discount.Amount)}) — "
            + $"tasdiqlovchi: {approver.FullName}. Shu paytdan hisob-kitobga kiradi.",
            before: before, after: Snapshot(discount), studentId: discount.StudentId);

        await db.SaveChangesAsync(ct);
        return await RequireDtoAsync(discount.Id, ct);
    }

    /// <inheritdoc />
    public async Task<DiscountDto> RejectAsync(
        Guid discountId, string approverId, string reason, CancellationToken ct = default)
    {
        var note = (reason ?? string.Empty).Trim();
        if (note.Length == 0)
            throw BillingRuleException.Invalid("reason_required", "Rad etish sababi majburiy.");

        var (discount, approver) = await RequireDecidableAsync(discountId, approverId, ct);

        var before = Snapshot(discount);
        discount.Status = DiscountStatus.Rejected;
        discount.ApprovedBy = approver.Id;
        discount.DecidedAt = AppClock.NowInstant;

        // Rad etish sababini saqlaydigan USTUN yo'q (sxema P1-05 da muzlagan) va
        // `DiscountDto` da ham unga joy yo'q, shuning uchun u audit jurnalida
        // yashaydi. `discounts.reason` — SO'ROVCHINING asosi, uning ustiga yozish
        // nima uchun chegirma so'ralganini butunlay yo'qotardi.
        audit.Record(
            AuditEntity, discount.Id.ToString(), "update",
            $"Chegirma RAD ETILDI ({Describe(discount.Percent, discount.Amount)}) — "
            + $"rad etdi: {approver.FullName}, sabab: {note}",
            before: before, after: Snapshot(discount), studentId: discount.StudentId);

        await db.SaveChangesAsync(ct);
        return await RequireDtoAsync(discount.Id, ct);
    }

    // ==================================================================
    //  Ichki yordamchilar
    // ==================================================================

    /// <summary>
    /// Tasdiqlash/rad etish uchun umumiy darvoza: chegirma bormi, hali
    /// qaror qabul qilinmaganmi, tasdiqlovchi direktormi va yaratuvchining
    /// O'ZI emasmi.
    /// </summary>
    private async Task<(Discount Discount, AppUser Approver)> RequireDecidableAsync(
        Guid discountId, string approverId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(approverId))
            throw new ArgumentException("Tasdiqlovchi noma'lum (approverId bo'sh).", nameof(approverId));

        var discount = await db.Discounts.FirstOrDefaultAsync(d => d.Id == discountId, ct)
            ?? throw BillingRuleException.NotFound("discount_not_found", "Chegirma topilmadi.");

        if (discount.Status != DiscountStatus.Pending)
            throw BillingRuleException.Conflict(
                "discount_already_decided",
                $"Bu chegirma bo'yicha qaror allaqachon qabul qilingan (holat: {discount.Status}).");

        // SPEC §4.5 — ikki qavatli nazorat. Bu tekshiruv bazadagi
        // `ck_discounts_approver_differs` dan OLDIN ishlaydi: aks holda
        // foydalanuvchi Postgres'ning 23514 xatosini ko'rardi, sababini emas.
        if (string.Equals(discount.CreatedBy, approverId, StringComparison.Ordinal))
            throw BillingRuleException.Forbidden(
                "self_approval",
                "O'zingiz so'ragan chegirmani o'zingiz tasdiqlay/rad eta olmaysiz — "
                + "ikkinchi shaxs (direktor) qarori kerak (SPEC §4.5).");

        var approver = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == approverId, ct)
            ?? throw BillingRuleException.NotFound("approver_not_found", "Tasdiqlovchi foydalanuvchi topilmadi.");

        // Mijoz javobi (SPEC §8.1 Q5): chegara yo'q, tasdiq FAQAT direktorniki.
        if (!string.Equals(approver.Role, Roles.SuperAdmin, StringComparison.Ordinal))
            throw BillingRuleException.Forbidden(
                "approver_not_director",
                "Chegirmani faqat direktor (superadmin) tasdiqlay oladi.");

        return (discount, approver);
    }

    /// <summary>
    /// Bitta SQL so'rov: chegirma + o'quvchi + toifa + so'rovchi + tasdiqlovchi
    /// nomlari, saralash ham BAZADA. Toifa va tasdiqlovchi ixtiyoriy, shuning
    /// uchun ular <c>LEFT JOIN</c> (<c>DefaultIfEmpty</c>) — o'quvchi va so'rovchi
    /// esa NOT NULL tashqi kalitlar. Ro'yxat ekrani necha qator bo'lsa ham BITTA
    /// murojaat qiladi (N+1 yo'q).
    ///
    /// <para>
    /// <b>Nega DTO oxirida, xotirada quriladi.</b> EF <c>new DiscountDto(…)</c>
    /// konstruktorli proyeksiyaning ichini ko'ra olmaydi: undan keyin qo'yilgan
    /// <c>Where</c>/<c>OrderBy</c> tarjima qilinmaydi va so'rov ishga tushganda
    /// yiqiladi. Shuning uchun filtr va saralash ENTITY ustunlarida bajariladi.
    /// </para>
    /// </summary>
    /// <param name="oldestFirst">
    /// true — direktor navbati (eng ko'p kutgan so'rov tepada);
    /// false — odatiy ro'yxat (eng yangisi tepada).
    /// </param>
    private async Task<List<DiscountDto>> QueryAsync(
        IQueryable<Discount> source, bool oldestFirst, CancellationToken ct)
    {
        var joined =
            from d in source
            join st in db.Students.AsNoTracking() on d.StudentId equals st.Id
            join cRaw in db.FeeCategories.AsNoTracking() on d.CategoryId equals (Guid?)cRaw.Id into cats
            from c in cats.DefaultIfEmpty()
            join u in db.Users.AsNoTracking() on d.CreatedBy equals u.Id
            join aRaw in db.Users.AsNoTracking() on d.ApprovedBy equals aRaw.Id into approvers
            from a in approvers.DefaultIfEmpty()
            select new
            {
                d.Id,
                d.StudentId,
                StudentName = st.FullName,
                d.CategoryId,
                CategoryCode = c == null ? null : c.Code,
                CategoryName = c == null ? null : c.Name,
                d.Percent,
                d.Amount,
                d.Reason,
                d.StartsOn,
                d.EndsOn,
                d.Status,
                CreatedByName = u.FullName,
                ApprovedByName = a == null ? null : a.FullName,
                d.DecidedAt,
                d.CreatedAt,
            };

        joined = oldestFirst
            ? joined.OrderBy(r => r.CreatedAt).ThenBy(r => r.StudentName)
            : joined.OrderByDescending(r => r.CreatedAt).ThenBy(r => r.StudentName);

        var rows = await joined.ToListAsync(ct);

        return [.. rows.Select(r => new DiscountDto(
            r.Id, r.StudentId, r.StudentName,
            r.CategoryId, r.CategoryCode, r.CategoryName,
            r.Percent, r.Amount, r.Reason,
            r.StartsOn, r.EndsOn,
            r.Status,
            r.CreatedByName, r.ApprovedByName,
            r.DecidedAt, r.CreatedAt))];
    }

    private async Task<DiscountDto> RequireDtoAsync(Guid id, CancellationToken ct) =>
        await GetAsync(id, ct)
        ?? throw BillingRuleException.NotFound("discount_not_found", "Chegirma topilmadi.");

    private static string RequireStatus(string status) =>
        DiscountStatus.All.Contains(status, StringComparer.Ordinal)
            ? status
            : throw BillingRuleException.Invalid(
                "invalid_status",
                $"Noma'lum holat '{status}'. Ruxsat etilganlar: {string.Join(", ", DiscountStatus.All)}.");

    private static string CategoryLabel(FeeCategory? category) => category?.Name ?? "barcha toifalar";

    private static string Describe(decimal percent, decimal amount) => (percent, amount) switch
    {
        (0m, var a) => $"{AuditService.Money(a)} so'm",
        (var p, 0m) => $"{p:0.##}%",
        var (p, a) => $"{p:0.##}% + {AuditService.Money(a)} so'm",
    };

    /// <summary>Audit uchun snapshot (<c>before</c>/<c>after</c>).</summary>
    private static object Snapshot(Discount d) => new
    {
        d.StudentId,
        d.CategoryId,
        d.Percent,
        d.Amount,
        d.Reason,
        d.StartsOn,
        d.EndsOn,
        d.Status,
        d.CreatedBy,
        d.ApprovedBy,
    };
}

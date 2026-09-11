using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

/// <summary>
/// O'quvchi obunalari: kim nimaga yozilgan va qancha to'laydi (P1-08, SPEC §3.7).
///
/// <para>
/// <b>Nega interfeys shu yerda, <c>IBillingServices.cs</c> da emas.</b> P1-06 beshta
/// moliya interfeysini muzlatgan, lekin obuna xizmati ular orasida yo'q edi.
/// <c>IBillingServices.cs</c> — Faza 1.C dagi beshta parallel agent tayanadigan
/// MUZLATILGAN fayl; unga yangi tip qo'shish merge konfliktini anglatadi. Shuning
/// uchun imzo o'z faylida yashaydi, qolgan hamma qoida (DTO'lar, <c>actorId</c>
/// parametri, "generic CRUD yo'q") esa AYNAN o'sha faylnikidek.
/// </para>
///
/// <para>
/// <b>Obuna — kelajak, hisob-faktura — tarix.</b> Bu xizmat faqat "keyingi oylarda
/// nima hisoblanadi" ni o'zgartiradi. Allaqachon yozilgan <c>invoices</c> qatorlariga
/// TEGMAYDI: narxni orqaga qarab o'zgartirish o'tgan oyning qarzini jimgina qayta
/// yozib yuborardi. Xato hisoblangan oy <c>IInvoiceService.VoidAsync</c> (P1-09) bilan
/// bekor qilinadi.
/// </para>
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Filtr bo'yicha obunalar (bitta SQL so'rov — o'quvchi/toifa/muallif nomlari join bilan).</summary>
    Task<IReadOnlyList<StudentSubscriptionDto>> ListAsync(SubscriptionQuery query, CancellationToken ct = default);

    /// <summary>Bitta obuna; topilmasa null.</summary>
    Task<StudentSubscriptionDto?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Forma uchun TAKLIF qilinadigan oylik summa (P1-08 qabul mezoni: o'quvchining
    /// birinchi <c>tuition</c> obunasi <c>SchoolClass.MonthlyFee</c> dan to'ldiriladi).
    /// Bu faqat taklif — yozilmaydi.
    /// </summary>
    Task<SubscriptionDefaultDto> DefaultAmountAsync(
        string studentId, Guid categoryId, CancellationToken ct = default);

    /// <summary>
    /// Obuna ochadi. Bir xil (o'quvchi, toifa) uchun davri KESISHADIGAN ikkinchi
    /// obuna rad etiladi (<c>subscription_overlap</c> → 409): aks holda accrual
    /// bir oyga ikkita narx ko'rib, qaysi birini olishni bilmay qolardi.
    /// <c>created_by</c> JWT'dan (SPEC §4.4).
    /// </summary>
    Task<StudentSubscriptionDto> CreateAsync(
        CreateSubscriptionRequest request, string createdByUserId, CancellationToken ct = default);

    /// <summary>Narx/tafsilot/tugash sanasini o'zgartiradi. O'quvchi va toifa o'zgarmaydi.</summary>
    Task<StudentSubscriptionDto> UpdateAsync(
        Guid id, UpdateSubscriptionRequest request, string actorId, CancellationToken ct = default);

    /// <summary>Obunani yopadi (o'quvchi avtobusdan chiqdi va h.k.).</summary>
    Task<StudentSubscriptionDto> EndAsync(
        Guid id, EndSubscriptionRequest request, string actorId, CancellationToken ct = default);
}

/// <summary>
/// Obunalar ro'yxati uchun filtr. <see cref="ActiveOnly"/> — BUGUNGI kunda
/// amal qiladiganlar (<c>starts_on ≤ bugun ≤ ends_on</c>).
/// </summary>
public record SubscriptionQuery(
    string? StudentId = null,
    Guid? CategoryId = null,
    bool ActiveOnly = false);

/// <summary>
/// Formaga oldindan qo'yiladigan oylik summa va u QAYERDAN kelgani.
/// </summary>
/// <param name="MonthlyAmount">Taklif qilinadigan summa (0 = taklif yo'q).</param>
/// <param name="Source">
/// <c>class_fee</c> — o'quvchi sinfining <c>MonthlyFee</c> qiymati;
/// <c>none</c> — taklif yo'q (toifa <c>tuition</c> emas, sinf narxi 0, yoki
/// o'quvchining shu toifada obunasi allaqachon bor).
/// </param>
public record SubscriptionDefaultDto(
    string StudentId, Guid CategoryId, string CategoryCode,
    decimal MonthlyAmount, string Source);

/// <summary>Obuna narxi taklifining manbai (<see cref="SubscriptionDefaultDto.Source"/>).</summary>
public static class SubscriptionDefaultSource
{
    /// <summary>Sinf oylik to'lovidan (eski xulq bilan uzviylik).</summary>
    public const string ClassFee = "class_fee";

    /// <summary>Taklif yo'q — summani admin o'zi kiritadi.</summary>
    public const string None = "none";
}

/// <inheritdoc cref="ISubscriptionService"/>
public sealed class SubscriptionService(IAppDbContext db, AuditService audit) : ISubscriptionService
{
    /// <summary>Audit yozuvidagi ob'ekt turi (<c>AuditService.Entity*</c> qatoriga mos).</summary>
    private const string AuditEntity = "StudentSubscription";

    /// <summary>Baza ustuni <c>numeric(14,2)</c> — hisob-kitob ham shu aniqlikda.</summary>
    private const int MoneyScale = 2;

    // ==================================================================
    //  O'qish
    // ==================================================================

    /// <inheritdoc />
    public async Task<IReadOnlyList<StudentSubscriptionDto>> ListAsync(
        SubscriptionQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var today = AppClock.Today;

        // Filtr ENTITY ustida — ya'ni SQL'da. DTO ustida filtrlab bo'lmaydi:
        // EF konstruktorli proyeksiyaning ichini ko'rmaydi (izohga qarang).
        var source = db.StudentSubscriptions.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.StudentId))
            source = source.Where(s => s.StudentId == query.StudentId);
        if (query.CategoryId is { } categoryId)
            source = source.Where(s => s.CategoryId == categoryId);
        if (query.ActiveOnly)
            source = source.Where(s => s.StartsOn <= today && (s.EndsOn == null || s.EndsOn >= today));

        return await QueryAsync(source, today, ct);
    }

    /// <inheritdoc />
    public async Task<StudentSubscriptionDto?> GetAsync(Guid id, CancellationToken ct = default) =>
        (await QueryAsync(db.StudentSubscriptions.AsNoTracking().Where(s => s.Id == id), AppClock.Today, ct))
        .FirstOrDefault();

    /// <inheritdoc />
    public async Task<SubscriptionDefaultDto> DefaultAmountAsync(
        string studentId, Guid categoryId, CancellationToken ct = default)
    {
        var student = await RequireStudentAsync(studentId, ct);
        var category = await RequireCategoryAsync(categoryId, ct);

        var (amount, source) = await SuggestAmountAsync(student, category, ct);
        return new SubscriptionDefaultDto(student.Id, category.Id, category.Code, amount, source);
    }

    // ==================================================================
    //  Yozish
    // ==================================================================

    /// <inheritdoc />
    public async Task<StudentSubscriptionDto> CreateAsync(
        CreateSubscriptionRequest request, string createdByUserId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireActor(createdByUserId);
        RequirePeriod(request.StartsOn, request.EndsOn);

        if (request.MonthlyAmount < 0m)
            throw BillingRuleException.Invalid("invalid_amount", "Oylik summa manfiy bo'la olmaydi.");

        var student = await RequireStudentAsync(request.StudentId, ct);
        if (student.IsArchived)
            throw BillingRuleException.Conflict(
                "student_archived",
                $"{student.FullName} arxivlangan — unga yangi obuna ochib bo'lmaydi.");

        var category = await RequireCategoryAsync(request.CategoryId, ct);
        if (!category.IsActive)
            throw BillingRuleException.Conflict(
                "category_inactive",
                $"'{category.Name}' toifasi o'chirilgan — unga yangi obuna ochib bo'lmaydi.");

        await RequireNoOverlapAsync(student, category, request.StartsOn, request.EndsOn, exclude: null, ct);

        // P1-08 qabul mezoni: o'quvchining BIRINCHI `tuition` obunasi sinf narxidan
        // to'ldiriladi. Tafsilot uchun `SuggestAmountAsync` izohiga qarang.
        var amount = decimal.Round(request.MonthlyAmount, MoneyScale);
        var amountSource = "so'rovda ko'rsatilgan";
        if (amount == 0m)
        {
            var (suggested, _) = await SuggestAmountAsync(student, category, ct);
            if (suggested > 0m)
            {
                amount = suggested;
                amountSource = $"{student.ClassName} sinfining oylik to'lovi";
            }
        }

        var subscription = new StudentSubscription
        {
            StudentId = student.Id,
            CategoryId = category.Id,
            MonthlyAmount = amount,
            Detail = Trimmed(request.Detail),
            StartsOn = request.StartsOn,
            EndsOn = request.EndsOn,
            CreatedBy = createdByUserId,
            CreatedAt = AppClock.NowInstant,
        };
        db.StudentSubscriptions.Add(subscription);

        audit.Record(
            AuditEntity, subscription.Id.ToString(), "create",
            $"Obuna ochildi: {category.Name} — {AuditService.Money(amount)} so'm/oy "
            + $"({request.StartsOn:yyyy-MM-dd} dan; summa manbai: {amountSource})",
            after: Snapshot(subscription), studentId: student.Id);

        await db.SaveChangesAsync(ct);
        return await RequireDtoAsync(subscription.Id, ct);
    }

    /// <inheritdoc />
    public async Task<StudentSubscriptionDto> UpdateAsync(
        Guid id, UpdateSubscriptionRequest request, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireActor(actorId);

        var subscription = await RequireSubscriptionAsync(id, ct);
        RequirePeriod(subscription.StartsOn, request.EndsOn);

        if (request.MonthlyAmount < 0m)
            throw BillingRuleException.Invalid("invalid_amount", "Oylik summa manfiy bo'la olmaydi.");

        var student = await RequireStudentAsync(subscription.StudentId, ct);
        var category = await RequireCategoryAsync(subscription.CategoryId, ct);

        // Tugash sanasini UZAYTIRISH keyingi obuna bilan kesishib qolishi mumkin
        // (qisqartirish esa hech qachon) — shuning uchun tekshiruv har safar.
        await RequireNoOverlapAsync(student, category, subscription.StartsOn, request.EndsOn, exclude: id, ct);

        var before = Snapshot(subscription);
        var changes = new List<string>();

        var amount = decimal.Round(request.MonthlyAmount, MoneyScale);
        if (subscription.MonthlyAmount != amount)
            changes.Add($"summa {AuditService.Money(subscription.MonthlyAmount)} → {AuditService.Money(amount)} so'm");
        var detail = Trimmed(request.Detail);
        if (subscription.Detail != detail)
            changes.Add($"tafsilot '{subscription.Detail}' → '{detail}'");
        if (subscription.EndsOn != request.EndsOn)
            changes.Add($"tugash sanasi {Show(subscription.EndsOn)} → {Show(request.EndsOn)}");

        subscription.MonthlyAmount = amount;
        subscription.Detail = detail;
        subscription.EndsOn = request.EndsOn;

        audit.Record(
            AuditEntity, subscription.Id.ToString(), "update",
            $"Obuna o'zgardi ({category.Name}): "
            + (changes.Count > 0 ? string.Join(", ", changes) : "o'zgarishsiz saqlandi"),
            before: before, after: Snapshot(subscription), studentId: subscription.StudentId);

        await db.SaveChangesAsync(ct);
        return await RequireDtoAsync(subscription.Id, ct);
    }

    /// <inheritdoc />
    public async Task<StudentSubscriptionDto> EndAsync(
        Guid id, EndSubscriptionRequest request, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireActor(actorId);

        var subscription = await RequireSubscriptionAsync(id, ct);
        RequirePeriod(subscription.StartsOn, request.EndsOn);

        var student = await RequireStudentAsync(subscription.StudentId, ct);
        var category = await RequireCategoryAsync(subscription.CategoryId, ct);
        await RequireNoOverlapAsync(student, category, subscription.StartsOn, request.EndsOn, exclude: id, ct);

        var before = Snapshot(subscription);
        subscription.EndsOn = request.EndsOn;

        audit.Record(
            AuditEntity, subscription.Id.ToString(), "update",
            $"Obuna yopildi ({category.Name}): {request.EndsOn:yyyy-MM-dd} dan keyin hisoblanmaydi",
            before: before, after: Snapshot(subscription), studentId: subscription.StudentId);

        await db.SaveChangesAsync(ct);
        return await RequireDtoAsync(subscription.Id, ct);
    }

    // ==================================================================
    //  Ichki yordamchilar
    // ==================================================================

    /// <summary>
    /// Bitta SQL so'rov: obuna + o'quvchi + toifa + muallif nomlari, saralash
    /// ham BAZADA. Har qator uchun alohida so'rov (N+1) YO'Q — ro'yxat ekrani
    /// 500 ta obunada ham bitta murojaat qiladi.
    ///
    /// <para>
    /// <b>Nega DTO oxirida, xotirada quriladi.</b> EF <c>new StudentSubscriptionDto(…)</c>
    /// konstruktorli proyeksiyaning ICHINI ko'ra olmaydi: undan keyin qo'yilgan
    /// <c>Where</c>/<c>OrderBy</c> tarjima qilinmaydi va so'rov ishga tushganda
    /// yiqiladi. Shuning uchun filtr va saralash ENTITY ustunlarida bajariladi,
    /// DTO esa ma'lumot kelgandan keyin yig'iladi. Bu hech narsa qimmatlashtirmaydi:
    /// SQL baribir bitta va faqat kerakli ustunlar tanlanadi.
    /// </para>
    /// <para>
    /// Uchala <c>join</c> ham ichki (inner): <c>student_id</c>, <c>category_id</c>
    /// va <c>created_by</c> — NOT NULL tashqi kalitlar (P1-05), ya'ni juftini
    /// topa olmaydigan qator bo'lishi mumkin emas.
    /// </para>
    /// </summary>
    private async Task<List<StudentSubscriptionDto>> QueryAsync(
        IQueryable<StudentSubscription> source, DateOnly asOf, CancellationToken ct)
    {
        var rows = await (
            from s in source
            join st in db.Students.AsNoTracking() on s.StudentId equals st.Id
            join c in db.FeeCategories.AsNoTracking() on s.CategoryId equals c.Id
            join u in db.Users.AsNoTracking() on s.CreatedBy equals u.Id
            orderby st.FullName, c.Code, s.StartsOn descending
            select new
            {
                s.Id,
                s.StudentId,
                StudentName = st.FullName,
                CategoryId = c.Id,
                CategoryCode = c.Code,
                CategoryName = c.Name,
                s.MonthlyAmount,
                s.Detail,
                s.StartsOn,
                s.EndsOn,
                CreatedByName = u.FullName,
                s.CreatedAt,
            }).ToListAsync(ct);

        return [.. rows.Select(r => new StudentSubscriptionDto(
            r.Id, r.StudentId, r.StudentName,
            r.CategoryId, r.CategoryCode, r.CategoryName,
            r.MonthlyAmount, r.Detail,
            r.StartsOn, r.EndsOn,
            r.StartsOn <= asOf && (r.EndsOn == null || r.EndsOn >= asOf),
            r.CreatedByName, r.CreatedAt))];
    }

    /// <summary>
    /// P1-08 qabul mezoni: "o'quvchining birinchi obunasi <c>tuition</c> bo'lsa,
    /// oylik summa <c>SchoolClass.MonthlyFee</c> dan oldindan to'ldiriladi
    /// (bugungi xulq bilan uzviylik)".
    ///
    /// <para>
    /// Bugungacha o'quvchining oyligi HECH QAYERDA saqlanmagan — u har hisoblashda
    /// sinf narxidan olingan (<c>TuitionService.AccrueMonth</c>). Ya'ni sinf narxi
    /// "birinchi obunaning standart qiymati", undan keyin esa narx obunaning O'ZIDA
    /// yashaydi va sinf narxi o'zgarsa u bilan birga o'zgarmaydi — bu ataylab:
    /// sinf narxini ko'tarish 300 ta o'quvchining shartnomasini jimgina qimmatlashtirmaydi.
    /// </para>
    /// <para>
    /// Shu sabab ikkinchi va keyingi <c>tuition</c> obunalarida taklif YO'Q: u yerda
    /// "avvalgi narx" allaqachon mavjud va uni admin ko'rib turadi.
    /// </para>
    /// </summary>
    private async Task<(decimal Amount, string Source)> SuggestAmountAsync(
        Student student, FeeCategory category, CancellationToken ct)
    {
        if (category.Code != FeeCategoryCodes.Tuition)
            return (0m, SubscriptionDefaultSource.None);

        var hasTuition = await db.StudentSubscriptions.AsNoTracking()
            .AnyAsync(s => s.StudentId == student.Id && s.CategoryId == category.Id, ct);
        if (hasTuition)
            return (0m, SubscriptionDefaultSource.None);

        // Sinf NOMI bo'yicha (o'quvchida sinf id'si emas, nomi saqlanadi — eski sxema).
        var fee = await db.Classes.AsNoTracking()
            .Where(c => c.Name == student.ClassName)
            .Select(c => (decimal?)c.MonthlyFee)
            .FirstOrDefaultAsync(ct);

        return fee is > 0m
            ? (decimal.Round(fee.Value, MoneyScale), SubscriptionDefaultSource.ClassFee)
            : (0m, SubscriptionDefaultSource.None);
    }

    /// <summary>
    /// Bir xil (o'quvchi, toifa) uchun davri kesishadigan obuna bormi.
    ///
    /// <para>
    /// Kesishish qoidasi: <c>yangi.starts ≤ eski.ends</c> VA <c>eski.starts ≤ yangi.ends</c>,
    /// bunda <c>ends_on = null</c> "cheksiz" degani. Bazada bunga mos cheklov YO'Q
    /// (buning uchun <c>btree_gist</c> kengaytmasi va <c>EXCLUDE</c> constraint kerak —
    /// migratsiya esa P1-05 niki, ya'ni bu vazifaning fayli emas), shuning uchun
    /// himoya hozircha faqat shu yerda. Qarang: docs/PENDING_WIRING.md.
    /// </para>
    /// </summary>
    private async Task RequireNoOverlapAsync(
        Student student, FeeCategory category,
        DateOnly startsOn, DateOnly? endsOn, Guid? exclude, CancellationToken ct)
    {
        var clash = await db.StudentSubscriptions.AsNoTracking()
            .Where(s => s.StudentId == student.Id && s.CategoryId == category.Id)
            .Where(s => exclude == null || s.Id != exclude)
            .Where(s => (endsOn == null || s.StartsOn <= endsOn)
                        && (s.EndsOn == null || s.EndsOn >= startsOn))
            .OrderBy(s => s.StartsOn)
            .Select(s => new { s.StartsOn, s.EndsOn })
            .FirstOrDefaultAsync(ct);

        if (clash is null) return;

        throw BillingRuleException.Conflict(
            "subscription_overlap",
            $"{student.FullName} uchun '{category.Name}' toifasida davri kesishadigan obuna allaqachon bor "
            + $"({clash.StartsOn:yyyy-MM-dd} … {Show(clash.EndsOn)}). "
            + "Avval eskisini yoping, keyin yangisini oching.");
    }

    private async Task<Student> RequireStudentAsync(string? studentId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentId))
            throw BillingRuleException.Invalid("student_required", "O'quvchi tanlanmagan.");

        return await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == studentId, ct)
            ?? throw BillingRuleException.NotFound("student_not_found", "O'quvchi topilmadi.");
    }

    private async Task<FeeCategory> RequireCategoryAsync(Guid categoryId, CancellationToken ct) =>
        await db.FeeCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == categoryId, ct)
        ?? throw BillingRuleException.NotFound("category_not_found", "To'lov toifasi topilmadi.");

    private async Task<StudentSubscription> RequireSubscriptionAsync(Guid id, CancellationToken ct) =>
        await db.StudentSubscriptions.FirstOrDefaultAsync(s => s.Id == id, ct)
        ?? throw BillingRuleException.NotFound("subscription_not_found", "Obuna topilmadi.");

    private async Task<StudentSubscriptionDto> RequireDtoAsync(Guid id, CancellationToken ct) =>
        await GetAsync(id, ct)
        ?? throw BillingRuleException.NotFound("subscription_not_found", "Obuna topilmadi.");

    private static void RequireActor(string actorId)
    {
        if (string.IsNullOrWhiteSpace(actorId))
            throw new ArgumentException(
                "Obunani kim o'zgartirayotgani noma'lum (actorId bo'sh).", nameof(actorId));
    }

    private static void RequirePeriod(DateOnly startsOn, DateOnly? endsOn)
    {
        if (endsOn is { } e && e < startsOn)
            throw BillingRuleException.Invalid(
                "invalid_period",
                $"Tugash sanasi ({e:yyyy-MM-dd}) boshlanish sanasidan ({startsOn:yyyy-MM-dd}) oldin bo'la olmaydi.");
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Show(DateOnly? date) => date?.ToString("yyyy-MM-dd") ?? "cheksiz";

    /// <summary>Audit uchun snapshot (<c>before</c>/<c>after</c>).</summary>
    private static object Snapshot(StudentSubscription s) => new
    {
        s.StudentId,
        s.CategoryId,
        s.MonthlyAmount,
        s.Detail,
        s.StartsOn,
        s.EndsOn,
    };
}

/// <summary>
/// Migratsiyada seed qilingan to'lov toifalari kodlari (<c>fee_categories.code</c>).
///
/// <para>
/// Kodlar <c>Accounts.RevenueFor</c> da ham ishlatiladi, lekin u yerda ular
/// <c>Dictionary</c> kalitlari — ya'ni satr xatosi jimgina "boshqa daromad" ga
/// tushardi. Bu yerdagi konstanta esa kompilyator tekshiradigan yagona nom.
/// </para>
/// </summary>
public static class FeeCategoryCodes
{
    public const string Tuition = "tuition";
    public const string Bus = "bus";
    public const string Dormitory = "dormitory";
    public const string Meals = "meals";
    public const string Other = "other";

    /// <summary>Migratsiya seed qilgan beshtasi (admin yangisini qo'sha oladi).</summary>
    public static readonly IReadOnlyList<string> Seeded = [Tuition, Bus, Dormitory, Meals, Other];
}

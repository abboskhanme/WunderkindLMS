using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  BUZILGAN VA'DA — beshinchi anomaliya (§3.5).
// ===========================================================================
//
//  SHART BITTA JUMLA
//  -----------------
//  O'quvchining eng oxirgi TIRIK va'dasi (`promised_on`) o'tib ketgan, qarzi
//  esa hali ochiq. §3.5 buni to'rtta mavjud bayroq yoniga beshinchi
//  `AnomalyKind` sifatida chiqarishni so'raydi — direktor paneli allaqachon
//  bor, yangi ekran kerak emas.
//
//  NEGA QATOR YOZILMAYDI — ENG MUHIM QAROR
//  ---------------------------------------
//  Mavjud to'rtta bayroq — HODISA: smena nomuvofiqlik bilan yopildi, storno
//  24 soat ichida qilindi. Ular sodir bo'ldi va abadiy o'zgarmaydi, shuning
//  uchun `finance_anomaly_flags` ga yoziladi va sabab bilan yopiladi.
//
//  Buzilgan va'da — HOLAT. Ota-ona ertaga to'lasa, va'da buzilgan bo'lib
//  qolmaydi: shart o'z-o'zidan yo'qoladi. Yozilgan qator esa qolardi va
//  kimdir uni qo'lda "yopishi" kerak bo'lardi — ya'ni jadval bilan haqiqat
//  ajralib ketardi. Aynan shu sabab bilan `students.balance` P1-21 da
//  o'chirilgan (SPEC §3.7). Shuning uchun bu yerda qator YO'Q: shart HAR
//  SO'ROVDA manba jadvallardan hisoblanadi.
//
//  Ikkinchi, amaliy sabab: `ck_finance_anomaly_flags_kind` check constraint'i
//  faqat to'rtta qiymatga ruxsat beradi va uni kengaytirish MIGRATSIYA
//  degani. Bu vazifaning qat'iy sharti — sxemaga tegmaslik. Ikkala sabab
//  bitta yechimga olib keladi, va hisoblanadigan yechim baribir to'g'rirog'i.
//  (Kelajakda qator yozish kerak bo'lsa — hisobotda: constraint + `ref_type`
//  ro'yxatini kengaytiruvchi kichik migratsiya kerak bo'ladi.)
//
//  QARZ QAYERDAN
//  -------------
//  <see cref="StudentBalanceQuery"/> dan — SPEC §3.7 ning yagona manbasi
//  (manfiy qoldiq = qarzdor). Qarzdorlar hisoboti (`DebtorsAsync`) toifalar
//  kesimida ishlaydi va AVANSNI hisobga olmaydi; bu yerda esa savol boshqa:
//  "ota-ona va'dasini bajardimi?" — oldindan to'lab qo'ygan ota-ona
//  bajargan, ya'ni sof qoldiq to'g'ri o'lchov.
//
//  UNUMDORLIK
//  ----------
//  Uchta so'rov, o'quvchilar soniga BOG'LIQ EMAS: (1) o'tib ketgan va'dalar
//  — `ix_debtor_actions_open_promises` qisman indeksiga TUSHADI; (2) ismlar;
//  (3) qoldiqlar (ikkita so'rov, faqat topilgan o'quvchilar uchun). Sikl
//  ichida `await` yo'q.

/// <summary>
/// Buzilgan va'dalarni HISOBLAB topadi (saqlamaydi). Batafsil — fayl
/// boshidagi izoh.
/// </summary>
public sealed class BrokenPromiseScan(IAppDbContext db)
{
    /// <summary>Bir so'rovda qaytadigan eng ko'p qator.</summary>
    public const int MaxRows = 500;

    /// <summary>
    /// O'tib ketgan va'dalar, eng eskisidan (ya'ni eng uzoq kutilganidan)
    /// boshlab. Qarzi yopilganlar ro'yxatga TUSHMAYDI.
    /// </summary>
    /// <param name="limit">Qatorlar chegarasi (1..<see cref="MaxRows"/>).</param>
    public async Task<IReadOnlyList<BrokenPromiseDto>> FindAsync(
        int limit = 200, CancellationToken ct = default)
    {
        var today = AppClock.Today;

        // ---- 1. Har o'quvchining ENG OXIRGI tirik va'dasi ----
        // Keyinroq berilgan va'da oldingisini bekor qiladi: ota-ona bilan
        // qayta kelishilgan sana — yangi kelishuv, eskisi tarixda qoladi.
        // Shuning uchun avval OXIRGI va'da topiladi, KEYIN u o'tib ketganmi
        // deb qaraladi. Teskari tartibda qilsak, "kecha buzilgan, bugun qayta
        // kelishilgan" holat ham buzilgan bo'lib chiqaverardi.
        var latest = await LatestPromisesAsync(ct);

        var overdue = latest
            .Where(a => a.PromisedOn < today)
            .OrderBy(a => a.PromisedOn)
            .ThenBy(a => a.StudentId, StringComparer.Ordinal)
            .ToList();

        if (overdue.Count == 0) return [];

        // ---- 2. Qarz hali ochiqmi? ----
        var studentIds = overdue.Select(a => a.StudentId).Distinct(StringComparer.Ordinal).ToList();
        var balances = await new StudentBalanceQuery(db).ForManyAsync(studentIds, ct);

        // Manfiy qoldiq = qarzdor (StudentBalanceQuery izohi). Nol yoki musbat
        // — ota-ona to'lagan, va'da bajarilgan.
        var stillOwing = overdue.Where(a => balances.GetValueOrDefault(a.StudentId) < 0m).ToList();
        if (stillOwing.Count == 0) return [];

        // ---- 3. Ism, sinf, telefon va holat nomlari ----
        var owingIds = stillOwing.Select(a => a.StudentId).Distinct(StringComparer.Ordinal).ToList();
        var students = await db.Students.AsNoTracking()
            .Where(s => owingIds.Contains(s.Id))
            .Select(s => new { s.Id, s.FullName, s.ClassName, s.ParentPhone })
            .ToDictionaryAsync(s => s.Id, ct);

        var statusIds = stillOwing.Where(a => a.StatusId != null).Select(a => a.StatusId!.Value).Distinct().ToList();
        var statuses = statusIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.DebtorStatuses.AsNoTracking()
                .Where(s => statusIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var authorIds = stillOwing.Select(a => a.CreatedBy).Distinct(StringComparer.Ordinal).ToList();
        var authors = await db.Users.AsNoTracking()
            .Where(u => authorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        return [.. stillOwing
            .Take(Math.Clamp(limit, 1, MaxRows))
            .Select(a =>
            {
                var student = students.GetValueOrDefault(a.StudentId);
                var promised = a.PromisedOn!.Value;
                return new BrokenPromiseDto(
                    ActionId: a.Id,
                    StudentId: a.StudentId,
                    FullName: student?.FullName ?? "Noma'lum",
                    ClassName: student?.ClassName ?? string.Empty,
                    ParentPhone: student?.ParentPhone ?? string.Empty,
                    PromisedOn: promised,
                    DaysLate: today.DayNumber - promised.DayNumber,
                    // Qarz musbat son sifatida ko'rsatiladi (qoldiq manfiy edi).
                    Debt: -balances.GetValueOrDefault(a.StudentId),
                    StatusName: a.StatusId is { } id ? statuses.GetValueOrDefault(id) : null,
                    Comment: a.Comment,
                    CreatedByName: authors.GetValueOrDefault(a.CreatedBy, "Noma'lum"),
                    CreatedAt: a.CreatedAt);
            })];
    }

    /// <summary>
    /// Xuddi shu natija, lekin direktor paneli tushunadigan shaklda —
    /// <see cref="FinanceAnomalyFlag"/>. <b>Qatorlar bazaga YOZILMAYDI</b>
    /// (fayl boshidagi izoh); ular faqat
    /// <see cref="AnomalyService.ListAsync"/> javobiga qo'shiladi.
    ///
    /// <para>
    /// <c>Id</c> = <c>RefId</c> = amal id'si. Bu ataylab: bayroqning id'si
    /// har so'rovda O'ZGARMASLIGI kerak (UI ro'yxat kaliti), va uni yopishga
    /// urinish 404 berishi kerak — buzilgan va'da sabab yozib emas, YANGI
    /// AMAL yozib "yopiladi".
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<FinanceAnomalyFlag>> AsFlagsAsync(
        int limit = 200, CancellationToken ct = default)
    {
        var rows = await FindAsync(limit, ct);
        if (rows.Count == 0) return [];

        var now = AppClock.NowInstant;
        return [.. rows.Select(p => ToFlag(p, now))];
    }

    /// <summary>Bitta buzilgan va'da → bitta (saqlanmaydigan) bayroq.</summary>
    public static FinanceAnomalyFlag ToFlag(BrokenPromiseDto p, DateTimeOffset now) => new()
    {
        Id = p.ActionId,
        Kind = AnomalyKind.BrokenPromise,
        RefType = AnomalyRefType.DebtorAction,
        RefId = p.ActionId,
        // Hodisa sanasi — VA'DA kuni (ro'yxat shu bo'yicha tartiblanadi):
        // eng uzoq kutilgan va'da yuqorida turadi.
        OccurredAt = new DateTimeOffset(p.PromisedOn.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
        DetectedAt = now,
        Amount = p.Debt,
        Summary =
            $"Va'da buzildi: {p.FullName} ({p.ClassName}) uchun {p.PromisedOn:dd.MM.yyyy} "
            + $"sanasiga to'lov va'da qilingan edi — {p.DaysLate} kun o'tdi, qarz hali ochiq: "
            + $"{AuditService.Money(p.Debt)} so'm.",
        Details = AuditService.Json(new
        {
            p.ActionId,
            p.StudentId,
            p.FullName,
            p.ClassName,
            p.ParentPhone,
            PromisedOn = p.PromisedOn.ToString("yyyy-MM-dd"),
            p.DaysLate,
            p.Debt,
            p.StatusName,
            p.Comment,
            p.CreatedByName,
        }),
    };

    /// <summary>
    /// Har o'quvchining eng oxirgi TIRIK va'dasi.
    ///
    /// <para>
    /// SQL'da "keyinroq yozilgan va'da yo'q" shartini NOT EXISTS bajaradi —
    /// <c>ix_debtor_actions_open_promises</c> qisman indeksi aynan shu
    /// filtrga mos (<c>promised_on is not null and deleted_at is null</c>).
    /// Bir xil lahzada yozilgan ikki qator (amalda deyarli imkonsiz, testda
    /// mumkin) ikkovi ham qaytadi va XOTIRADA bittaga siqiladi: <c>uuid</c>
    /// taqqoslashini SQL'ga tarjima qilishga tayanmaslik uchun.
    /// </para>
    /// </summary>
    private async Task<List<DebtorAction>> LatestPromisesAsync(CancellationToken ct)
    {
        var promises = db.DebtorActions.AsNoTracking()
            .Where(a => a.DeletedAt == null && a.PromisedOn != null);

        var rows = await promises
            .Where(a => !promises.Any(b => b.StudentId == a.StudentId && b.CreatedAt > a.CreatedAt))
            .ToListAsync(ct);

        return [.. rows
            .GroupBy(a => a.StudentId, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).First())];
    }
}

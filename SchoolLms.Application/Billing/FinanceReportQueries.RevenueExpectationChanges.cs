using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  F6.03 — O'ZGARISHLAR JURNALI ("nega reja shu oy siljidi")
//  (docs/modules/finance-parity.md §2.6.3 — "changes" tabi, EduSchool'da
//  `GET reports/pnl/expectation/changes`. FAQAT O'QISH.)
// ===========================================================================
//
//  TO'RT MANBA, YANGI JADVAL YO'Q
//  -------------------------------
//  Vazifa qat'iy: "no new table". Har voqea to'rtta MAVJUD manbadan o'qiladi:
//    · `student_subscriptions` — kim yozildi / kim chiqdi (starts_on/ends_on),
//      AYNAN <see cref="FinanceReportQueries.RevenueExpectationAsync"/> dagi
//      `StudentsAdmitted`/`StudentsDeparted` bilan bir xil shart (pastdagi
//      reconciliation testi buni tekshiradi — "qaysi oy" ikkovida ham bir xil
//      bo'lishi shart, aks holda bu jurnal boshqa hisobot bilan kelishmay
//      qolardi);
//    · `discounts` — chegirma so'raldi / qaror qilindi / muddati tugadi;
//    · o'quvchi arxiv sanasi (`students.archived_at`) — obunasi YOPILMAGAN
//      holda arxivlangan o'quvchini alohida ko'rsatadi (haqiqiy billing
//      teshigi: arxivlangan, lekin hisoblash to'xtamagan bo'lishi mumkin);
//    · `audit_log` — ikki joyda, boshqa hech qanday manba bera olmaydigan
//      narsa uchun: (1) obunaning OYLIK SUMMASI o'zgarganini bilish (joriy
//      qator faqat OXIRGI summani saqlaydi — tarix faqat Before/After'da
//      yotibdi, <see cref="SubscriptionService.UpdateAsync"/> ga qarang),
//      (2) obunani KIM yopganini bilish (`StudentSubscription.CreatedBy`
//      faqat OCHGAN kishini saqlaydi, `SubscriptionService.EndAsync` ga
//      qarang).
//
//  PUL — FAQAT ANIQ BO'LGANDA
//  ---------------------------
//  <see cref="ChangeJournalRowDto.NetEffect"/> obuna voqealarida
//  (studentJoined / studentLeft / tariffChanged) HAR DOIM to'ldiriladi —
//  bitta obunaning o'zidan, ikkinchi manba shart emas. Chegirma voqealarida
//  esa ATAYLAB quyidagicha:
//    · so'ralgan / rad etilgan — ANIQ 0: tasdiqlanmagan chegirma hech qachon
//      hisob-kitobga kirmagan (`DiscountService` qoidasi, mijoz javobi §8.1 Q5);
//    · tasdiqlangan / muddati tugagan — ATAYLAB `null`. Haqiqiy summaviy
//      ta'sir <c>InvoiceService.AccrueMonthAsync</c> dagi "ENG ANIQ chegirma
//      yutadi" qoidasiga bog'liq (bitta o'quvchida bir nechta chegirma
//      bo'lishi va ular RAQOBATLASHISHI mumkin). G'olibni aniqlash algoritmi
//      shu yerda TAKRORLANMAYDI — aks holda ikkinchi ta'rif paydo bo'lardi,
//      fayl darajasidagi umumiy ogohlantirish AYNAN shu haqda. Ekranda bu
//      "—" bo'lib ko'rinadi, soxta raqam emas.
//
//  SAHIFALASH — XOTIRADA, SQL OFFSET EMAS
//  ----------------------------------------
//  Bitta oyning voqealari soni kichik (bitta maktabda oyiga o'nlab yozuv,
//  minglab emas), shuning uchun butun oy BIR MARTA o'qiladi, birlashtiriladi
//  va saralanadi; sahifalash RO'YXAT ustida (`Skip`/`Take`). Haqiqiy ko'lamda
//  bu alohida jadval talab qilardi — aynan shu vazifaning chegarasi.
//
//  MA'LUM CHEKLOV: ARXIVDAN QAYTARILGAN O'QUVCHI
//  ------------------------------------------------
//  `studentArchived` voqeasi o'quvchining JORIY holatidan o'qiladi
//  (`students.is_archived`/`archived_at`) — tarix jadvali yo'q. Agar
//  o'quvchi arxivlangandan keyin (masalan, keyingi oyda) qaytarilgan bo'lsa,
//  "arxivlangan edi" voqeasi bu jurnalda ENDI ko'rinmaydi. Bu — halol
//  chegara, "keyinroq" deb yozilmagan.

/// <summary>O'zgarish turi — <see cref="ChangeJournalRowDto.Kind"/>.</summary>
public static class ChangeJournalKind
{
    /// <summary>Yangi obuna ochildi (<c>student_subscriptions.starts_on</c> shu oyda).</summary>
    public const string StudentJoined = "studentJoined";

    /// <summary>Obuna yopildi (<c>student_subscriptions.ends_on</c> shu oyda).</summary>
    public const string StudentLeft = "studentLeft";

    /// <summary>
    /// O'quvchi arxivlandi, LEKIN uning faol obunasi shu oy YOPILMAGAN —
    /// ehtimoliy billing teshigi (arxivlangan o'quvchiga hisoblash davom
    /// etishi mumkin). EduSchool'ning besh turida yo'q — bu tizimning o'ziga
    /// xos, "student archive dates" manbasidan chiqadigan aniqlash imkoniyati.
    /// </summary>
    public const string StudentArchived = "studentArchived";

    /// <summary>Obunaning oylik summasi o'zgardi (<c>audit_log</c> Before/After farqi).</summary>
    public const string TariffChanged = "tariffChanged";

    /// <summary>Chegirma so'raldi — hali tasdiqlanmagan, hisob-kitobga HALI ta'sir qilmagan.</summary>
    public const string DiscountRequested = "discountRequested";

    /// <summary>Chegirma direktor tomonidan TASDIQLANDI — shu paytdan hisob-kitobga kiradi.</summary>
    public const string DiscountApproved = "discountApproved";

    /// <summary>Chegirma RAD ETILDI — hech qachon hisob-kitobga kirmagan.</summary>
    public const string DiscountRejected = "discountRejected";

    /// <summary>Tasdiqlangan chegirmaning muddati shu oy tugadi.</summary>
    public const string DiscountExpired = "discountExpired";

    /// <summary>Filtr dropdown va tekshiruv uchun to'liq ro'yxat.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        StudentJoined, StudentLeft, StudentArchived, TariffChanged,
        DiscountRequested, DiscountApproved, DiscountRejected, DiscountExpired,
    ];
}

/// <summary>Jurnalning bitta qatori.</summary>
/// <param name="Date">Voqea sanasi — obuna/chegirma voqealarida tegishli maydonning sanasi
/// (starts_on/ends_on/decided_at/created_at), tarif o'zgarishida — tahrir sanasi.</param>
/// <param name="Kind"><see cref="ChangeJournalKind"/>.</param>
/// <param name="NetEffect">Kutilgan sof daromadga ta'sir. Chegirma voqealarida
/// qачон aniq (0), qachon ataylab <c>null</c> — fayl boshidagi izohga qarang.</param>
/// <param name="Note">O'qiladigan o'zbekcha izoh — nima sodir bo'lgani.</param>
/// <param name="Author">Kim qilgani. Topilmasa yoki egasi yo'q voqea (masalan, muddat
/// tugashi) — "—".</param>
public record ChangeJournalRowDto(
    DateOnly Date, string Kind,
    string StudentId, string StudentName, string ClassName,
    string? CategoryCode, string? CategoryName,
    decimal? NetEffect, string Note, string Author);

/// <summary>Jurnal so'rovi filtri.</summary>
/// <param name="Month">Qaysi oy (kuni ahamiyatsiz).</param>
/// <param name="Kind">Bitta turga toraytirish. null = hammasi.</param>
/// <param name="Page">Sahifa raqami (1 dan).</param>
/// <param name="PageSize"></param>
public record ChangeJournalQuery(
    DateOnly Month, string? Kind = null,
    int Page = 1, int PageSize = FinanceReportQueries.ChangeJournalDefaultPageSize);

/// <summary>Jurnalning bitta sahifasi.</summary>
public record ChangeJournalPageDto(
    IReadOnlyList<ChangeJournalRowDto> Rows, int Page, int PageSize, int Total, DateOnly Month);

public sealed partial class FinanceReportQueries
{
    public const int ChangeJournalDefaultPageSize = 50;
    public const int ChangeJournalMaxPageSize = 200;

    /// <summary>
    /// O'zgarishlar jurnali (§2.6, F6.03) — bitta oy uchun, sahifalangan.
    /// To'liq qoida fayl boshidagi izohda.
    /// </summary>
    public async Task<ChangeJournalPageDto> RevenueExpectationChangesAsync(
        ChangeJournalQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var periodMonth = FirstDayOfMonth(query.Month);
        var monthEnd = periodMonth.AddMonths(1).AddDays(-1);
        var (isoStart, isoEndExclusive) = MonthIsoBounds(periodMonth);
        var (instantStart, instantEndExclusive) = MonthInstantBounds(periodMonth);

        var rows = new List<ChangeJournalRowDto>();

        // =================================================================
        //  1) studentJoined / studentLeft — student_subscriptions
        // =================================================================
        var touchedSubs = await (
            from s in db.StudentSubscriptions.AsNoTracking()
            where (s.StartsOn >= periodMonth && s.StartsOn <= monthEnd)
                  || (s.EndsOn != null && s.EndsOn >= periodMonth && s.EndsOn <= monthEnd)
            join st in db.Students.AsNoTracking() on s.StudentId equals st.Id
            join c in db.FeeCategories.AsNoTracking() on s.CategoryId equals c.Id
            join u in db.Users.AsNoTracking() on s.CreatedBy equals u.Id
            select new
            {
                s.Id,
                s.StudentId,
                StudentName = st.FullName,
                st.ClassName,
                CategoryCode = c.Code,
                CategoryName = c.Name,
                s.MonthlyAmount,
                s.StartsOn,
                s.EndsOn,
                CreatedByName = u.FullName,
            }).ToListAsync(ct);

        var leftCandidates = new List<(Guid SubscriptionId, DateOnly EndsOn, string StudentId, string StudentName,
            string ClassName, string CategoryCode, string CategoryName, decimal MonthlyAmount)>();
        var currentEndsOnBySubscription = new Dictionary<Guid, DateOnly>();

        foreach (var s in touchedSubs)
        {
            if (s.StartsOn >= periodMonth && s.StartsOn <= monthEnd)
            {
                rows.Add(new ChangeJournalRowDto(
                    s.StartsOn, ChangeJournalKind.StudentJoined,
                    s.StudentId, s.StudentName, s.ClassName, s.CategoryCode, s.CategoryName,
                    NetEffect: s.MonthlyAmount,
                    Note: $"'{s.CategoryName}' obunasi ochildi: {AuditService.Money(s.MonthlyAmount)} so'm/oy",
                    Author: s.CreatedByName));
            }

            if (s.EndsOn is { } endsOn && endsOn >= periodMonth && endsOn <= monthEnd)
            {
                currentEndsOnBySubscription[s.Id] = endsOn;
                leftCandidates.Add((s.Id, endsOn, s.StudentId, s.StudentName, s.ClassName,
                    s.CategoryCode, s.CategoryName, s.MonthlyAmount));
            }
        }

        // Kim yopganini `audit_log` dan aniqlaymiz — qator o'zi faqat OCHGANNI
        // (CreatedBy) biladi. BITTA so'rov, sikl ichida await YO'Q.
        var authorBySubscription = new Dictionary<Guid, string>();
        if (leftCandidates.Count > 0)
        {
            var leftIds = leftCandidates.Select(c => c.SubscriptionId.ToString()).ToList();
            var edits = await db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityType == "StudentSubscription" && a.Action == "update"
                            && leftIds.Contains(a.EntityId))
                .Select(a => new { a.EntityId, a.Timestamp, a.ActorName, a.After })
                .ToListAsync(ct);

            foreach (var group in edits.GroupBy(e => e.EntityId, StringComparer.Ordinal))
            {
                if (!Guid.TryParse(group.Key, out var subId)) continue;
                if (!currentEndsOnBySubscription.TryGetValue(subId, out var currentEndsOn)) continue;

                // Joriy `ends_on` ni yozgan ENG OXIRGI tahrir — shu tahrir
                // haqiqatan ham hozirgi yopilish sanasini o'rnatgan
                // (keyinroq boshqa maydon o'zgarmagan bo'lsa ham).
                var match = group
                    .Select(e => new { e.Timestamp, e.ActorName, After = ParseSubscriptionSnapshot(e.After) })
                    .Where(e => e.After?.EndsOn == currentEndsOn)
                    .OrderByDescending(e => e.Timestamp, StringComparer.Ordinal)
                    .FirstOrDefault();

                if (match?.ActorName is { } actorName)
                    authorBySubscription[subId] = actorName;
            }
        }

        foreach (var c in leftCandidates)
        {
            rows.Add(new ChangeJournalRowDto(
                c.EndsOn, ChangeJournalKind.StudentLeft,
                c.StudentId, c.StudentName, c.ClassName, c.CategoryCode, c.CategoryName,
                NetEffect: -c.MonthlyAmount,
                Note: $"'{c.CategoryName}' obunasi yopildi: {AuditService.Money(c.MonthlyAmount)} so'm/oy endi hisoblanmaydi",
                Author: authorBySubscription.GetValueOrDefault(c.SubscriptionId, "Noma'lum")));
        }

        var leftStudentIds = new HashSet<string>(leftCandidates.Select(c => c.StudentId), StringComparer.Ordinal);

        // =================================================================
        //  2) tariffChanged — audit_log Before/After, oylik summa farqi
        // =================================================================
        var subscriptionEdits = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == "StudentSubscription" && a.Action == "update"
                        && string.Compare(a.Timestamp, isoStart) >= 0
                        && string.Compare(a.Timestamp, isoEndExclusive) < 0)
            .Select(a => new { a.EntityId, a.Timestamp, a.ActorName, a.Before, a.After })
            .ToListAsync(ct);

        var tariffCandidates = new List<(Guid SubscriptionId, DateOnly Date, decimal Delta, string Author)>();
        foreach (var edit in subscriptionEdits)
        {
            if (!Guid.TryParse(edit.EntityId, out var subId)) continue;

            var before = ParseSubscriptionSnapshot(edit.Before);
            var after = ParseSubscriptionSnapshot(edit.After);
            if (before?.MonthlyAmount is not { } beforeAmount || after?.MonthlyAmount is not { } afterAmount) continue;
            if (beforeAmount == afterAmount) continue;

            tariffCandidates.Add((subId, ParseAuditDate(edit.Timestamp), afterAmount - beforeAmount,
                edit.ActorName ?? "Noma'lum"));
        }

        if (tariffCandidates.Count > 0)
        {
            var subIds = tariffCandidates.Select(t => t.SubscriptionId).Distinct().ToList();
            var subInfo = await (
                from s in db.StudentSubscriptions.AsNoTracking()
                where subIds.Contains(s.Id)
                join st in db.Students.AsNoTracking() on s.StudentId equals st.Id
                join c in db.FeeCategories.AsNoTracking() on s.CategoryId equals c.Id
                select new
                {
                    s.Id, s.StudentId, StudentName = st.FullName, st.ClassName,
                    CategoryCode = c.Code, CategoryName = c.Name,
                }).ToListAsync(ct);
            var infoById = subInfo.ToDictionary(x => x.Id);

            foreach (var t in tariffCandidates)
            {
                // Himoya: obuna hech qachon o'chirilmaydi (faqat yopiladi), shuning
                // uchun bu deyarli har doim topiladi — topilmasa jimgina o'tkazamiz.
                if (!infoById.TryGetValue(t.SubscriptionId, out var info)) continue;

                rows.Add(new ChangeJournalRowDto(
                    t.Date, ChangeJournalKind.TariffChanged,
                    info.StudentId, info.StudentName, info.ClassName, info.CategoryCode, info.CategoryName,
                    NetEffect: t.Delta,
                    Note: $"'{info.CategoryName}' oylik summasi {(t.Delta > 0 ? "oshdi" : "kamaydi")}: "
                          + $"{AuditService.Money(Math.Abs(t.Delta))} so'mga",
                    Author: t.Author));
            }
        }

        // =================================================================
        //  3) studentArchived — students.archived_at, obuna YOPILMAGAN holat
        // =================================================================
        var archivedInMonth = await db.Students.AsNoTracking()
            .Where(s => s.IsArchived && s.ArchivedAt != null
                        && string.Compare(s.ArchivedAt, isoStart) >= 0
                        && string.Compare(s.ArchivedAt, isoEndExclusive) < 0)
            .Select(s => new { s.Id, s.FullName, s.ClassName, s.ArchivedAt, s.ArchiveReason })
            .ToListAsync(ct);

        foreach (var s in archivedInMonth)
        {
            // Agar shu o'quvchining obunasi ham shu oy yopilgan bo'lsa —
            // `studentLeft` qatori allaqachon bor, ikkinchi qator shovqin bo'lardi.
            if (leftStudentIds.Contains(s.Id)) continue;

            var date = DateOnly.TryParseExact(
                s.ArchivedAt, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : periodMonth;

            rows.Add(new ChangeJournalRowDto(
                date, ChangeJournalKind.StudentArchived,
                s.Id, s.FullName, s.ClassName, null, null,
                NetEffect: null,
                Note: $"O'quvchi arxivga ko'chirildi (\"{s.ArchiveReason}\"), lekin faol obunasi shu oy "
                      + "yopilmagan — hisoblash davom etishi mumkin",
                // O'quvchini arxivlagan kishi ustuni yo'q (`students.archived_by`
                // yo'q); audit izi bor, lekin boshqa amal bilan bir yorliq ostida
                // saqlangan (`AuditService.EntityStudentDiscount`,
                // `StudentsController.Archive` / `StudentBulkArchiveController`),
                // shuning uchun bu yerda aniqlashga urinilmaydi.
                Author: "—"));
        }

        // =================================================================
        //  4) chegirmalar — so'ralgan / qaror qilingan / muddati tugagan
        // =================================================================
        var requested = await (
            from d in db.Discounts.AsNoTracking()
            where d.CreatedAt >= instantStart && d.CreatedAt < instantEndExclusive
            join st in db.Students.AsNoTracking() on d.StudentId equals st.Id
            join cRaw in db.FeeCategories.AsNoTracking() on d.CategoryId equals (Guid?)cRaw.Id into cats
            from c in cats.DefaultIfEmpty()
            join u in db.Users.AsNoTracking() on d.CreatedBy equals u.Id
            select new
            {
                d.StudentId, StudentName = st.FullName, st.ClassName,
                CategoryCode = c == null ? null : c.Code, CategoryName = c == null ? null : c.Name,
                d.Percent, d.Amount, d.Reason, d.CreatedAt, CreatedByName = u.FullName,
            }).ToListAsync(ct);

        foreach (var d in requested)
        {
            var label = d.CategoryName ?? "barcha toifalar";
            rows.Add(new ChangeJournalRowDto(
                AppClock.LocalDateOf(d.CreatedAt), ChangeJournalKind.DiscountRequested,
                d.StudentId, d.StudentName, d.ClassName, d.CategoryCode, d.CategoryName,
                NetEffect: 0m,
                Note: $"Chegirma so'raldi ({label}): {DescribeDiscount(d.Percent, d.Amount)} — sabab: {d.Reason}",
                Author: d.CreatedByName));
        }

        var decided = await (
            from d in db.Discounts.AsNoTracking()
            where d.DecidedAt != null && d.DecidedAt >= instantStart && d.DecidedAt < instantEndExclusive
                  && (d.Status == DiscountStatus.Approved || d.Status == DiscountStatus.Rejected)
            join st in db.Students.AsNoTracking() on d.StudentId equals st.Id
            join cRaw in db.FeeCategories.AsNoTracking() on d.CategoryId equals (Guid?)cRaw.Id into cats
            from c in cats.DefaultIfEmpty()
            join aRaw in db.Users.AsNoTracking() on d.ApprovedBy equals aRaw.Id into approvers
            from a in approvers.DefaultIfEmpty()
            select new
            {
                d.StudentId, StudentName = st.FullName, st.ClassName,
                CategoryCode = c == null ? null : c.Code, CategoryName = c == null ? null : c.Name,
                d.Percent, d.Amount, d.Status, DecidedAt = d.DecidedAt!.Value,
                DecidedByName = a == null ? "Noma'lum" : a.FullName,
            }).ToListAsync(ct);

        foreach (var d in decided)
        {
            var approved = d.Status == DiscountStatus.Approved;
            var label = d.CategoryName ?? "barcha toifalar";
            rows.Add(new ChangeJournalRowDto(
                AppClock.LocalDateOf(d.DecidedAt), approved ? ChangeJournalKind.DiscountApproved : ChangeJournalKind.DiscountRejected,
                d.StudentId, d.StudentName, d.ClassName, d.CategoryCode, d.CategoryName,
                NetEffect: approved ? null : 0m,
                Note: approved
                    ? $"Chegirma TASDIQLANDI ({label}): {DescribeDiscount(d.Percent, d.Amount)} — shu paytdan hisob-kitobga kiradi"
                    : $"Chegirma RAD ETILDI ({label}): {DescribeDiscount(d.Percent, d.Amount)}",
                Author: d.DecidedByName));
        }

        var expired = await (
            from d in db.Discounts.AsNoTracking()
            where d.Status == DiscountStatus.Approved && d.EndsOn != null
                  && d.EndsOn >= periodMonth && d.EndsOn <= monthEnd
            join st in db.Students.AsNoTracking() on d.StudentId equals st.Id
            join cRaw in db.FeeCategories.AsNoTracking() on d.CategoryId equals (Guid?)cRaw.Id into cats
            from c in cats.DefaultIfEmpty()
            select new
            {
                d.StudentId, StudentName = st.FullName, st.ClassName,
                CategoryCode = c == null ? null : c.Code, CategoryName = c == null ? null : c.Name,
                d.Percent, d.Amount, EndsOn = d.EndsOn!.Value,
            }).ToListAsync(ct);

        foreach (var d in expired)
        {
            var label = d.CategoryName ?? "barcha toifalar";
            rows.Add(new ChangeJournalRowDto(
                d.EndsOn, ChangeJournalKind.DiscountExpired,
                d.StudentId, d.StudentName, d.ClassName, d.CategoryCode, d.CategoryName,
                NetEffect: null,
                Note: $"Chegirma muddati tugadi ({label}): {DescribeDiscount(d.Percent, d.Amount)} endi qo'llanmaydi",
                Author: "—"));
        }

        // =================================================================
        //  Filtr, saralash, sahifalash (XOTIRADA — fayl boshidagi izohga qarang)
        // =================================================================
        IEnumerable<ChangeJournalRowDto> filtered = query.Kind is null
            ? rows
            : rows.Where(r => r.Kind == query.Kind);

        var ordered = filtered
            .OrderByDescending(r => r.Date)
            .ThenBy(r => r.StudentName, StringComparer.Ordinal)
            .ThenBy(r => r.Kind, StringComparer.Ordinal)
            .ToList();

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, ChangeJournalMaxPageSize);
        var pageRows = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new ChangeJournalPageDto(pageRows, page, pageSize, ordered.Count, periodMonth);
    }

    /// <summary>Chegirmani qisqa matnga o'giradi — <c>DiscountService.Describe</c> bilan
    /// AYNAN bir xil shakl (formatlash, pul arifmetikasi emas — ikkinchi ta'rif xavfi yo'q).</summary>
    private static string DescribeDiscount(decimal percent, decimal amount) => (percent, amount) switch
    {
        (0m, var a) => $"{AuditService.Money(a)} so'm",
        (var p, 0m) => $"{p:0.##}%",
        var (p, a) => $"{p:0.##}% + {AuditService.Money(a)} so'm",
    };

    /// <summary>Obuna audit qatoridagi Before/After JSON'ini o'qiydi. Yaroqsiz/yo'q — <c>null</c>.</summary>
    private static SubscriptionSnapshot? ParseSubscriptionSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<SubscriptionSnapshot>(json); }
        catch (JsonException) { return null; }
    }

    /// <summary><c>audit_log.Timestamp</c> ("yyyy-MM-ddTHH:mm:ss") dan sanani oladi.</summary>
    private static DateOnly ParseAuditDate(string timestamp) =>
        timestamp.Length >= 10
        && DateOnly.TryParseExact(
            timestamp[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : AppClock.Today;

    /// <summary>Oy chegaralari — matn (<c>audit_log.Timestamp</c>, <c>students.archived_at</c>
    /// kabi "yyyy-MM-dd[THH:mm:ss]" ustunlar bilan LEKSIK solishtirish uchun; ISO shaklda
    /// bu chronologik solishtirishga teng).</summary>
    private static (string Start, string EndExclusive) MonthIsoBounds(DateOnly periodMonth) => (
        periodMonth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        periodMonth.AddMonths(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    /// <summary>Oy chegaralari — lahza (<c>timestamptz</c> ustunlar: <c>discounts.created_at</c>,
    /// <c>discounts.decided_at</c>). Maktab mintaqasi doim UTC+5 (yozgi vaqt yo'q, <see cref="AppClock"/>
    /// izohi), shuning uchun mahalliy yarim tunni UTC'ga aylantirish uchun sobit ofset yetarli.
    /// <b>Ofset 0 bilan qaytariladi</b> — Npgsql <c>timestamptz</c> parametrini FAQAT shunday
    /// qabul qiladi (<see cref="AppClock.NowInstant"/> dagi izohdagi qoidaning o'zi).</summary>
    private static (DateTimeOffset Start, DateTimeOffset EndExclusive) MonthInstantBounds(DateOnly periodMonth)
    {
        var offset = TimeSpan.FromHours(5);
        var startLocal = periodMonth.ToDateTime(TimeOnly.MinValue);
        var endLocal = periodMonth.AddMonths(1).ToDateTime(TimeOnly.MinValue);
        var start = new DateTimeOffset(startLocal - offset, TimeSpan.Zero);
        var endExclusive = new DateTimeOffset(endLocal - offset, TimeSpan.Zero);
        return (start, endExclusive);
    }

    /// <summary>Audit Before/After JSON'idagi obuna suratining tayanchi (<c>SubscriptionService.Snapshot</c>
    /// bilan bir xil maydonlar — property nomlari AYNAN mos, aks holda deserializatsiya jim qoladi).</summary>
    private sealed record SubscriptionSnapshot(
        string? StudentId, Guid? CategoryId, decimal? MonthlyAmount,
        string? Detail, DateOnly? StartsOn, DateOnly? EndsOn);
}

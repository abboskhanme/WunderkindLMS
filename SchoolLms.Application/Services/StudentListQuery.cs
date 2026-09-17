using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  O'quvchilar ro'yxati — SERVER tarafdagi filtr, tartib va sahifalash.
//  docs/modules/students-parity.md §2.3 (S-1, S-4, K-4) va §2.4 (A-1).
// ===========================================================================
//
//  NEGA YANGI YO'L, ESKISINI O'ZGARTIRISH EMAS
//  -------------------------------------------
//  `GET /api/admin/students` javobi (StudentDto) o'n joydan o'qiladi:
//  ota-ona portali, hisobotlar, boshqa ekranlar. Uni "biroz" kengaytirish —
//  o'sha o'nta joyni bir vaqtda sinashni talab qiladi. Shuning uchun ro'yxat
//  ekrani UCHUN alohida so'rov yozildi; eskisi tegilmagan va o'z joyida
//  ishlayveradi.
//
//  BITTA FILTRSIZ SO'ROV = BUGUNGI XATTI-HARAKAT
//  ---------------------------------------------
//  Hech qanday parametr berilmasa natija bugungi ekran ko'rsatadigan narsaning
//  AYNAN o'zi: faqat arxivlanmaganlar, F.I.SH bo'yicha o'sish tartibida.
//  Arxiv tab'ida esa — arxiv sanasi bo'yicha kamayish, keyin F.I.SH
//  (`StudentsController.GetArchived` bilan bir xil).
//
//  IKKI BOSQICH: NEGA HAMMASI SQL'DA EMAS
//  --------------------------------------
//  Qoldiq USTUN EMAS — u `invoices` va `payment_allocations` dan har safar
//  hisoblanadi (P1-21, `StudentBalanceQuery`). Ya'ni "qarzdorlar",
//  "eng kam qarz" va "qoldiq oralig'i" filtrlarini SQL'dagi `where` ga
//  qo'yib bo'lmaydi. Shuning uchun:
//
//      1) SQL: ustunlarda ifodalanadigan hamma filtr (sinf, jins, til, holat,
//         sana oraliqlari, guruh, shartnoma, obuna, sertifikat...);
//      2) xotira: qoldiqlar BITTA partiyada olinadi (ikki so'rov, o'quvchilar
//         soniga bog'liq emas), keyin pulga oid filtrlar, tartib va sahifa.
//
//  Bu bugungi ekrandan QIMMAT EMAS: u ham butun ro'yxatni va butun qoldiq
//  lug'atini brauzerga tortardi. Farqi — endi brauzerga faqat bitta sahifa
//  ketadi.
// ===========================================================================

/// <summary>
/// O'quvchilar ro'yxati uchun filtr/tartib/sahifa so'rovi (S-1). Hech narsa
/// YOZMAYDI — barcha so'rovlar <c>AsNoTracking</c>.
/// </summary>
public sealed class StudentListQuery(IAppDbContext db)
{
    /// <summary>Sahifadagi eng ko'p qator — tasodifiy "hammasini ber" dan himoya.</summary>
    public const int MaxPageSize = 1000;

    /// <summary>Sahifa hajmi ko'rsatilmasa.</summary>
    public const int DefaultPageSize = 200;

    public const string StateActive = "active";
    public const string StateArchived = "archived";
    public const string StateAll = "all";

    /// <summary>
    /// Filtrga mos o'quvchilarning BITTA sahifasi va yakunlari.
    /// </summary>
    public async Task<StudentListPageDto> RunAsync(
        StudentListFilter f, CancellationToken ct = default)
    {
        var rows = await MatchingAsync(f, ct);

        var page = Math.Max(1, f.Page ?? 1);
        var pageSize = Math.Clamp(f.PageSize ?? DefaultPageSize, 1, MaxPageSize);

        // Yakunlar — BUTUN filtrlangan to'plam bo'yicha, sahifa bo'yicha emas
        // (S-4: ro'yxat ostidagi "jami qarz / jami avans").
        var totalDebt = rows.Where(r => r.Balance < 0).Sum(r => -r.Balance);
        var totalCredit = rows.Where(r => r.Balance > 0).Sum(r => r.Balance);

        var items = rows.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new StudentListPageDto(items, rows.Count, page, pageSize, totalDebt, totalCredit);
    }

    /// <summary>
    /// Filtrga mos BARCHA qatorlar, tartiblangan holda (sahifalashsiz) —
    /// eksport shu yerdan oziqlanadi, ya'ni eksport va ekran AYNAN bir xil
    /// to'plamni ko'radi.
    /// </summary>
    public async Task<List<StudentListRowDto>> AllAsync(
        StudentListFilter f, CancellationToken ct = default) => await MatchingAsync(f, ct);

    // =====================================================================
    //  1-bosqich — SQL'da ifodalanadigan filtrlar
    // =====================================================================

    private async Task<List<StudentListRowDto>> MatchingAsync(
        StudentListFilter f, CancellationToken ct)
    {
        var state = Normalize(f.State) switch
        {
            StateArchived => StateArchived,
            StateAll => StateAll,
            _ => StateActive,
        };

        // Sinflar lug'ati — daraja filtri ham, javobdagi `grade` ustuni ham
        // shundan. Sinflar jadvali kichik (o'nlab qator), bitta so'rov.
        var classes = await db.Classes.AsNoTracking()
            .Select(c => new { c.Name, c.Grade }).ToListAsync(ct);
        var gradeByClass = classes
            .GroupBy(c => c.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Grade, StringComparer.Ordinal);

        var q = db.Students.AsNoTracking();

        if (state == StateActive) q = q.Where(s => !s.IsArchived);
        else if (state == StateArchived) q = q.Where(s => s.IsArchived);

        var search = (f.Search ?? "").Trim().ToLowerInvariant();
        if (search.Length > 0)
        {
            // Bugungi ekran AYNAN shu ikki maydon bo'yicha qidiradi
            // (F.I.SH va ota-ona F.I.SH) — kengaytirilmadi.
            q = q.Where(s => s.FullName.ToLower().Contains(search)
                             || s.ParentFullName.ToLower().Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(f.ClassName))
        {
            var className = f.ClassName.Trim();
            q = q.Where(s => s.ClassName == className);
        }

        var grades = ParseInts(f.Grades);
        if (grades.Count > 0)
        {
            // Daraja `classes` da, o'quvchida esa sinf NOMI turadi. Nomlar
            // ro'yxatini C# da yig'ib, SQL'ga bitta `IN` sifatida beramiz.
            var names = classes.Where(c => grades.Contains(c.Grade)).Select(c => c.Name).ToList();
            q = names.Count == 0
                ? q.Where(_ => false)
                : q.Where(s => names.Contains(s.ClassName));
        }

        var gender = Normalize(f.Gender);
        if (gender is "male" or "female") q = q.Where(s => s.Gender == gender);

        var language = Normalize(f.Language);
        if (language.Length > 0) q = q.Where(s => s.Language == language);

        if (f.StatusId is { } statusId) q = q.Where(s => s.StatusId == statusId);
        else if (f.HasStatus is { } hasStatus)
            q = hasStatus ? q.Where(s => s.StatusId != null) : q.Where(s => s.StatusId == null);

        if (f.GroupId is { } groupId)
        {
            q = q.Where(s => db.StudyGroupMembers
                .Any(m => m.GroupId == groupId && m.StudentId == s.Id && m.LeftOn == null));
        }

        if (f.HasContract is { } hasContract)
        {
            q = hasContract
                ? q.Where(s => db.StudentContracts.Any(c => c.StudentId == s.Id))
                : q.Where(s => !db.StudentContracts.Any(c => c.StudentId == s.Id));
        }

        if (f.HasSubscription is { } hasSubscription)
        {
            q = hasSubscription
                ? q.Where(s => db.StudentSubscriptions.Any(x => x.StudentId == s.Id && x.EndsOn == null))
                : q.Where(s => !db.StudentSubscriptions.Any(x => x.StudentId == s.Id && x.EndsOn == null));
        }

        if (f.CategoryId is { } categoryId)
        {
            q = q.Where(s => db.StudentSubscriptions
                .Any(x => x.StudentId == s.Id && x.EndsOn == null && x.CategoryId == categoryId));
        }

        if (f.HasDiscount is { } hasDiscount)
        {
            var today = AppClock.Today;
            // Tasdiqlangan va BUGUN kuchda bo'lgan chegirma (so'ralgan, lekin
            // hali tasdiqlanmagani chegirma emas — SPEC §8.1 Q5).
            var live = db.Discounts.AsNoTracking()
                .Where(d => d.Status == DiscountStatus.Approved
                            && d.StartsOn <= today
                            && (d.EndsOn == null || d.EndsOn >= today));
            q = hasDiscount
                ? q.Where(s => live.Any(d => d.StudentId == s.Id))
                : q.Where(s => !live.Any(d => d.StudentId == s.Id));
        }

        var holders = CertificateService.HolderStudentIds(
            db, CertificateService.ParseIds(f.CertificateTypeIds), f.CertificateTeacherId);
        if (holders is not null) q = q.Where(s => holders.Contains(s.Id));

        // Sana oraliqlari — ISO matn ustidagi solishtiruv (uy uslubi, masalan
        // `AttendanceAnalytics`): ISO sana leksikografik tartibda ham to'g'ri.
        if (Iso(f.EnrolledFrom) is { } enrolledFrom)
            q = q.Where(s => string.Compare(s.EnrollmentDate, enrolledFrom) >= 0);
        if (Iso(f.EnrolledTo) is { } enrolledTo)
            q = q.Where(s => string.Compare(s.EnrollmentDate, enrolledTo) <= 0);

        if (Iso(f.ArchivedFrom) is { } archivedFrom)
            q = q.Where(s => s.ArchivedAt != null && string.Compare(s.ArchivedAt, archivedFrom) >= 0);
        if (Iso(f.ArchivedTo) is { } archivedTo)
            q = q.Where(s => s.ArchivedAt != null && string.Compare(s.ArchivedAt, archivedTo) <= 0);

        if (f.ArchiveReasonId is { } reasonId) q = q.Where(s => s.ArchiveReasonId == reasonId);

        // Yosh -> tug'ilgan sana oralig'i. Solishtiruv MATN ustida bo'ladi
        // (ISO sanalar leksikografik tartibda ham to'g'ri saralanadi), ya'ni
        // bazada sana arifmetikasi umuman kerak emas.
        var today2 = AppClock.Today;
        if (f.AgeFrom is { } ageFrom && ageFrom >= 0)
        {
            // yosh >= ageFrom  <=>  tug'ilgan sana <= bugun - ageFrom yil
            var latest = today2.AddYears(-ageFrom).ToString("yyyy-MM-dd");
            q = q.Where(s => s.BirthDate != "" && string.Compare(s.BirthDate, latest) <= 0);
        }
        if (f.AgeTo is { } ageTo && ageTo >= 0)
        {
            // yosh <= ageTo  <=>  tug'ilgan sana > bugun - (ageTo + 1) yil
            var earliest = today2.AddYears(-(ageTo + 1)).ToString("yyyy-MM-dd");
            q = q.Where(s => s.BirthDate != "" && string.Compare(s.BirthDate, earliest) > 0);
        }

        var students = await q.ToListAsync(ct);
        return await DecorateAsync(students, f, gradeByClass, ct);
    }

    // =====================================================================
    //  2-bosqich — qoldiq, holat/shartnoma ustunlari, pul filtrlari, tartib
    // =====================================================================

    private async Task<List<StudentListRowDto>> DecorateAsync(
        List<Student> students,
        StudentListFilter f,
        Dictionary<string, int> gradeByClass,
        CancellationToken ct)
    {
        if (students.Count == 0) return [];

        var ids = students.Select(s => s.Id).ToList();

        // Qoldiq — bitta partiyada (ikki so'rov, siklda so'rov YO'Q).
        var balances = await new StudentBalanceQuery(db).ForManyAsync(ids, ct);

        // Holat katalogi kichik — butunligicha olinadi.
        var statuses = await db.StudentStatuses.AsNoTracking().ToListAsync(ct);
        var statusById = statuses.ToDictionary(x => x.Id);

        // Shartnoma raqami (K-4): har o'quvchi uchun ENG YANGI imzolangani.
        var contracts = await db.StudentContracts.AsNoTracking()
            .Where(c => ids.Contains(c.StudentId))
            .Select(c => new { c.StudentId, c.Number, c.SignedOn })
            .ToListAsync(ct);
        var contractByStudent = contracts
            .GroupBy(c => c.StudentId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.SignedOn ?? DateOnly.MinValue).First().Number,
                StringComparer.Ordinal);
        var hasContract = contracts.Select(c => c.StudentId).ToHashSet(StringComparer.Ordinal);

        var today = AppClock.Today;

        var rows = students.Select(s =>
        {
            var status = s.StatusId is { } sid ? statusById.GetValueOrDefault(sid) : null;
            return new StudentListRowDto(
                s.Id,
                s.FullName,
                s.ClassName,
                gradeByClass.GetValueOrDefault(s.ClassName),
                s.Gender,
                s.BirthDate,
                AgeOf(s.BirthDate, today),
                s.Address,
                s.Phone,
                s.ParentFullName,
                s.ParentPhone,
                s.Language,
                s.EnrollmentDate,
                balances.GetValueOrDefault(s.Id),
                s.StatusId,
                status?.Name,
                status?.Color,
                hasContract.Contains(s.Id),
                contractByStudent.GetValueOrDefault(s.Id),
                s.IsArchived,
                s.ArchivedAt,
                s.ArchiveReason,
                s.ArchiveReasonId,
                s.BirthCertificateUrl,
                s.TargetGrade);
        });

        // Pulga oid filtrlar — qoldiq hisoblangandan KEYIN.
        var balanceState = Normalize(f.BalanceState);
        if (balanceState == "debt") rows = rows.Where(r => r.Balance < 0);
        else if (balanceState == "paid") rows = rows.Where(r => r.Balance >= 0);

        if (f.MinDebt is { } minDebt && minDebt > 0)
            rows = rows.Where(r => -r.Balance >= minDebt);

        if (f.BalanceFrom is { } balanceFrom) rows = rows.Where(r => r.Balance >= balanceFrom);
        if (f.BalanceTo is { } balanceTo) rows = rows.Where(r => r.Balance <= balanceTo);

        return Sort(rows, f).ToList();
    }

    /// <summary>
    /// Tartib. Sukut qiymatlari bugungi ekranning tartibi bilan AYNAN bir xil:
    /// faol ro'yxat — F.I.SH o'sish bo'yicha; arxiv — arxiv sanasi kamayish,
    /// keyin F.I.SH.
    /// </summary>
    private static IEnumerable<StudentListRowDto> Sort(
        IEnumerable<StudentListRowDto> rows, StudentListFilter f)
    {
        var name = StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: true);
        var desc = Normalize(f.SortOrder) == "desc";
        var sortBy = Normalize(f.SortBy);

        if (sortBy.Length == 0)
        {
            return Normalize(f.State) == StateArchived
                ? rows.OrderByDescending(r => r.ArchivedAt ?? "").ThenBy(r => r.FullName, name)
                : rows.OrderBy(r => r.FullName, name);
        }

        IOrderedEnumerable<StudentListRowDto> ordered = sortBy switch
        {
            "classname" => desc
                ? rows.OrderByDescending(r => r.Grade).ThenByDescending(r => r.ClassName, name)
                : rows.OrderBy(r => r.Grade).ThenBy(r => r.ClassName, name),
            "balance" => desc
                ? rows.OrderByDescending(r => r.Balance)
                : rows.OrderBy(r => r.Balance),
            "birthdate" => desc
                ? rows.OrderByDescending(r => r.BirthDate)
                : rows.OrderBy(r => r.BirthDate),
            "enrollmentdate" => desc
                ? rows.OrderByDescending(r => r.EnrollmentDate)
                : rows.OrderBy(r => r.EnrollmentDate),
            "status" => desc
                ? rows.OrderByDescending(r => r.StatusName ?? "", name)
                : rows.OrderBy(r => r.StatusName ?? "", name),
            "archivedat" => desc
                ? rows.OrderByDescending(r => r.ArchivedAt ?? "")
                : rows.OrderBy(r => r.ArchivedAt ?? ""),
            "parentfullname" => desc
                ? rows.OrderByDescending(r => r.ParentFullName, name)
                : rows.OrderBy(r => r.ParentFullName, name),
            _ => desc
                ? rows.OrderByDescending(r => r.FullName, name)
                : rows.OrderBy(r => r.FullName, name),
        };

        // Ikkinchi kalit har doim F.I.SH — bir xil qiymatli qatorlar sahifadan
        // sahifaga sakramasligi uchun (barqaror tartib).
        return ordered.ThenBy(r => r.FullName, name).ThenBy(r => r.Id, StringComparer.Ordinal);
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    /// <summary>
    /// Sana filtri — faqat HAQIQIY ISO sana bo'lsa qaytadi. Shakli buzilgan
    /// matn (masalan "2026-13-45") filtr sifatida ISHLATILMAYDI: aks holda
    /// matn solishtiruvi jimgina tasodifiy to'plam qaytarardi.
    /// </summary>
    private static string? Iso(string? value)
    {
        var v = (value ?? "").Trim();
        if (v.Length == 0) return null;
        return DateOnly.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out _) ? v : null;
    }

    /// <summary>To'liq yil. Sana bo'sh yoki noto'g'ri bo'lsa — null.</summary>
    public static int? AgeOf(string? birthDate, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(birthDate)) return null;
        if (!DateOnly.TryParseExact(birthDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var d)) return null;
        var age = today.Year - d.Year;
        if (d.AddYears(age) > today) age--;
        return age < 0 || age > 150 ? null : age;
    }

    private static string Normalize(string? value) => (value ?? "").Trim().ToLowerInvariant();

    /// <summary>"1,2,9" -> [1,2,9]. Yaroqsiz bo'laklar e'tiborsiz qoldiriladi.</summary>
    public static List<int> ParseInts(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? []
            : [.. csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Select(p => int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                         ? n : (int?)null)
                     .Where(n => n is not null)
                     .Select(n => n!.Value)
                     .Distinct()];
}

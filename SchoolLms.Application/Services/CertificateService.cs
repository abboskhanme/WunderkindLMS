using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// Sertifikatlar registrining QOIDALARI (docs/modules/existing-module-gaps.md §2.3).
///
/// <para>
/// <b>Nega STATIK va nega DI'da yo'q.</b> <c>Program.cs</c> ga tegilmaydi (bu to'lqinda u
/// boshqa agent qo'lida), ya'ni yangi xizmatni ro'yxatdan o'tkazib bo'lmaydi. Loyihada
/// bunday hollar uchun tayyor naqsh bor — <c>AttendanceDisciplineReport</c>,
/// <c>StudentProfileBuilder</c>: statik klass, <c>IAppDbContext</c> parametr sifatida.
/// Controller <c>AppDbContext</c> ni DI'dan oladi va shu yerga uzatadi.
/// </para>
///
/// <para>
/// <b>ENG MUHIM QOIDA — BALL.</b> <c>Certificate.Score</c> faqat turi
/// <see cref="CertificateType.IsScored"/> bo'lganda qabul qilinadi. Baza buni TEKSHIRA
/// OLMAYDI: shart ikkita jadvalga tegishli, CHECK esa bitta qatordan narini ko'rmaydi
/// (bazada faqat <c>score is null or score &gt;= 0</c> bor). Demak tekshiruv shu yerda —
/// va xato SHU YERDA o'qiladigan qilib yoziladi, chunki 23514 raqamli Postgres xatosini
/// o'quv bo'limi xodimi tushunmaydi.
/// </para>
/// </summary>
public static class CertificateService
{
    /// <summary>
    /// Audit jurnalidagi yorliq — <c>AuditService.EntityCertificate</c> ning o'zi.
    /// Qiymat O'ZGARMASLIGI shart: audit qatorlari shu matn bo'yicha topiladi.
    /// </summary>
    public const string AuditEntity = AuditService.EntityCertificate;

    /// <summary>Tur katalogidagi o'zgarishlar uchun yorliq.</summary>
    public const string AuditEntityType = "CertificateType";

    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>Sana → sim formati ("yyyy-MM-dd").</summary>
    public static string Fmt(DateOnly d) => d.ToString(DateFormat, CultureInfo.InvariantCulture);

    /// <summary>Sim formatidagi sanani o'qiydi. Noto'g'ri bo'lsa — null.</summary>
    public static DateOnly? Parse(string? value) =>
        DateOnly.TryParseExact(value ?? "", DateFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d)
            ? d
            : null;

    // =====================================================================
    //  Tur katalogi
    // =====================================================================

    /// <summary>
    /// Turni o'chirish MUMKIN EMASMI. Mumkin bo'lmasa — o'qiladigan sabab qaytadi.
    ///
    /// <para>
    /// FK <c>on delete restrict</c> (ParityModel.cs) — ya'ni bazaning o'zi ham to'xtatadi,
    /// lekin u 23503 raqami bilan to'xtatadi. Bu yerdagi tekshiruv xodimga NIMA QILISH
    /// kerakligini aytadi: turni o'chirish emas, "faol emas" qilish.
    /// </para>
    /// </summary>
    public static async Task<string?> DeleteTypeBlockedAsync(
        IAppDbContext db, CertificateType type, CancellationToken ct = default)
    {
        var used = await db.Certificates.CountAsync(c => c.TypeId == type.Id, ct);
        if (used == 0) return null;

        return $"«{type.Name}» turida {used} ta sertifikat bor — turni o'chirib bo'lmaydi. "
             + "Uni «Faol emas» qilib qo'ying: eski hujjatlar joyida qoladi, "
             + "yangi sertifikatda esa bu tur tanlanmaydi.";
    }

    /// <summary>Tur nomini tekshiradi (bo'shmi, takrormi). Xato bo'lsa — matn.</summary>
    public static async Task<string?> ValidateTypeAsync(
        IAppDbContext db, string? name, Guid? exceptId, CancellationToken ct = default)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0) return "Tur nomini kiriting";

        var lowered = trimmed.ToLowerInvariant();
        var taken = await db.CertificateTypes
            .AnyAsync(t => t.Name.ToLower() == lowered && (exceptId == null || t.Id != exceptId), ct);

        return taken ? $"«{trimmed}» nomli tur allaqachon bor" : null;
    }

    // =====================================================================
    //  Sertifikat — tekshirish va yozish
    // =====================================================================

    /// <summary>
    /// Formadagi qiymatlarni tekshirib, <paramref name="row"/> ga yozadi.
    /// Xato bo'lsa — o'qiladigan matn qaytadi va <paramref name="row"/> TEGILMAYDI
    /// (yarim yozilgan qator SaveChanges'ga tushib ketmasin).
    ///
    /// <para><c>SaveChanges</c> QILMAYDI — chaqiruvchi (controller) o'zi saqlaydi.</para>
    /// </summary>
    public static async Task<string?> ApplyAsync(
        IAppDbContext db, Certificate row, CertificatePayload p, CancellationToken ct = default)
    {
        // ---- O'quvchi ----
        var studentId = (p.StudentId ?? "").Trim();
        if (studentId.Length == 0) return "O'quvchini tanlang";
        if (!await db.Students.AnyAsync(s => s.Id == studentId, ct)) return "O'quvchi topilmadi";

        // ---- Tur ----
        var type = await db.CertificateTypes.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == p.TypeId, ct);
        if (type is null) return "Sertifikat turi topilmadi";
        // Arxivlangan tur FAQAT tur ALMASHTIRILAYOTGANDA to'sadi. Eski hujjatning izohini
        // yoki faylini tahrirlash, turi keyinchalik arxivlangani uchun bloklanmaydi.
        if (!type.IsActive && row.TypeId != type.Id)
            return $"«{type.Name}» turi faol emas — yangi sertifikatga uni tanlab bo'lmaydi";

        // ---- BALL: §2.3 ning yagona mazmunli qoidasi ----
        var score = p.Score;
        if (score is not null && !type.IsScored)
            return $"«{type.Name}» turi standart test emas — unga ball qo'yilmaydi. "
                 + "Ball kerak bo'lsa, turni Sozlamalarda «Ball qo'yiladi» deb belgilang.";
        if (score < 0) return "Ball manfiy bo'la olmaydi";

        // ---- Sanalar ----
        var issued = Parse(p.IssuedOn);
        if (issued is null) return "Berilgan sanani kiriting";

        DateOnly? expires = null;
        if (!string.IsNullOrWhiteSpace(p.ExpiresOn))
        {
            expires = Parse(p.ExpiresOn);
            if (expires is null) return "Amal qilish muddati noto'g'ri";
            if (expires < issued) return "Amal qilish muddati berilgan sanadan oldin bo'la olmaydi";
        }

        // ---- Fan(lar) — Z-3: bitta hujjat bir nechta fanni qamrab olishi mumkin ----
        // `SubjectIds` yo'q/bo'sh bo'lsa — eski bitta-fanli `SubjectId` bitta elementli
        // ro'yxat sifatida olinadi (CertificateDtos.cs dagi izoh, orqaga moslik).
        var subjectIds = (p.SubjectIds ?? [])
            .Select(Clean)
            .Where(s => s is not null)
            .Select(s => s!)
            .Distinct()
            .ToList();
        if (subjectIds.Count == 0 && Clean(p.SubjectId) is { } legacySubjectId)
            subjectIds.Add(legacySubjectId);

        if (subjectIds.Count > 0)
        {
            var foundSubjects = await db.Subjects.CountAsync(s => subjectIds.Contains(s.Id), ct);
            if (foundSubjects != subjectIds.Count) return "Fan topilmadi";
        }

        var teacherId = Clean(p.TeacherId);
        if (teacherId is not null && !await db.Teachers.AnyAsync(t => t.Id == teacherId, ct))
            return "O'qituvchi topilmadi";

        // ---- Fayl ----
        // Faqat O'ZIMIZNING yuklash yo'limiz (UploadsController `/uploads/...` qaytaradi).
        // Tashqi manzilga ruxsat berilsa, forma `javascript:` yoki begona sayt havolasini
        // saqlab qo'yardi va u keyin ro'yxatdagi "Faylni ochish" tugmasiga tushardi.
        var fileUrl = Clean(p.FileUrl);
        if (fileUrl is not null && !fileUrl.StartsWith("/uploads/", StringComparison.Ordinal))
            return "Faylni «Fayl yuklash» tugmasi orqali qo'shing";

        // ---- Hammasi joyida: yozamiz ----
        row.StudentId = studentId;
        row.TypeId = type.Id;
        // `subject_id` — BIRINCHI tanlangan fan. Ustun O'RNIGA emas YONIGA qo'shilgan
        // `certificate_subjects`ning yagona sababi shu: eski ustunni o'qiydigan kod
        // (ro'yxatdagi "Fan" ustuni, `SubjectsController.Delete`) ishlashda davom etadi.
        row.SubjectId = subjectIds.Count > 0 ? subjectIds[0] : null;
        row.TeacherId = teacherId;
        row.Number = Clean(p.Number);
        row.Score = score;
        row.IssuedOn = issued.Value;
        row.ExpiresOn = expires;
        row.FileUrl = fileUrl;
        row.Comment = Clean(p.Comment);

        await SyncSubjectLinksAsync(db, row.Id, subjectIds, ct);
        return null;
    }

    /// <summary>
    /// <c>certificate_subjects</c> ni <paramref name="subjectIds"/> bilan moslaydi (Z-3):
    /// yo'qolganlarni o'chiradi, yangilarni qo'shadi, turgan qatorga tegmaydi.
    ///
    /// <para>
    /// <b>Nega "hammasini o'chirib, hammasini qayta qo'shish" emas.</b> EF Core bitta
    /// kalit (<c>certificate_id, subject_id</c>) uchun ikkita instansiyani (biri
    /// o'chirilayotgan, biri qo'shilayotgan) bir vaqtda kuzata olmaydi — xato beradi.
    /// Shuning uchun farq (diff) olinadi: faqat chindan yo'qolgan/yangi qatorlarga tegiladi.
    /// </para>
    /// </summary>
    private static async Task SyncSubjectLinksAsync(
        IAppDbContext db, Guid certificateId, List<string> subjectIds, CancellationToken ct)
    {
        var existing = await db.CertificateSubjects
            .Where(cs => cs.CertificateId == certificateId).ToListAsync(ct);
        var existingIds = existing.Select(e => e.SubjectId).ToHashSet();
        var wantedIds = subjectIds.ToHashSet();

        foreach (var stale in existing.Where(e => !wantedIds.Contains(e.SubjectId)))
            db.CertificateSubjects.Remove(stale);

        foreach (var sid in subjectIds.Where(id => !existingIds.Contains(id)))
            db.CertificateSubjects.Add(new CertificateSubject { CertificateId = certificateId, SubjectId = sid });
    }

    /// <summary>Bo'sh/probel matnni <c>null</c> ga aylantiradi (bazada "" saqlanmasin).</summary>
    private static string? Clean(string? value)
    {
        var trimmed = (value ?? "").Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    // =====================================================================
    //  O'qish
    // =====================================================================

    /// <summary>
    /// Ro'yxat — barcha filtrlar bilan, bitta so'rovda (nomlar JOIN orqali keladi,
    /// sikl ichida so'rov YO'Q).
    /// </summary>
    /// <param name="studentId">Bitta o'quvchi (o'quvchi kartochkasidagi blok).</param>
    /// <param name="typeId">Bitta tur.</param>
    /// <param name="teacherId">Bergan o'qituvchi.</param>
    /// <param name="subjectId">Fan.</param>
    /// <param name="className">Sinf (o'quvchining JORIY sinfi).</param>
    /// <param name="from">Berilgan sana shu kundan boshlab.</param>
    /// <param name="to">Berilgan sana shu kungacha.</param>
    /// <param name="expiringInDays">Muddati shu necha kun ichida tugaydiganlar. Muddati
    /// ALLAQACHON o'tganlar ham kiradi — ular ham diqqat talab qiladi.</param>
    /// <param name="search">O'quvchi FISH yoki hujjat raqami bo'yicha qidiruv.</param>
    public static async Task<List<CertificateDto>> ListAsync(
        IAppDbContext db,
        string? studentId = null, Guid? typeId = null, string? teacherId = null,
        string? subjectId = null, string? className = null,
        DateOnly? from = null, DateOnly? to = null, int? expiringInDays = null,
        string? search = null,
        CancellationToken ct = default)
    {
        var today = AppClock.Today;

        var q = db.Certificates.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(studentId)) q = q.Where(c => c.StudentId == studentId);
        if (typeId is { } tid) q = q.Where(c => c.TypeId == tid);
        if (!string.IsNullOrWhiteSpace(teacherId)) q = q.Where(c => c.TeacherId == teacherId);
        if (!string.IsNullOrWhiteSpace(subjectId))
            // Z-3: filtr hujjatning ISTALGAN fani bo'yicha ishlaydi, faqat asosiy
            // (birinchi) fan emas — shuning uchun `certificate_subjects` orqali.
            q = q.Where(c => db.CertificateSubjects.Any(cs => cs.CertificateId == c.Id && cs.SubjectId == subjectId));
        if (from is { } f) q = q.Where(c => c.IssuedOn >= f);
        if (to is { } u) q = q.Where(c => c.IssuedOn <= u);
        if (expiringInDays is { } days)
        {
            var limit = today.AddDays(days);
            q = q.Where(c => c.ExpiresOn != null && c.ExpiresOn <= limit);
        }

        var cls = string.IsNullOrWhiteSpace(className) ? null : className;

        var rows = await (
            from c in q
            join s in db.Students.AsNoTracking() on c.StudentId equals s.Id
            join tp in db.CertificateTypes.AsNoTracking() on c.TypeId equals tp.Id
            from sub in db.Subjects.AsNoTracking().Where(x => x.Id == c.SubjectId).DefaultIfEmpty()
            from te in db.Teachers.AsNoTracking().Where(x => x.Id == c.TeacherId).DefaultIfEmpty()
            where cls == null || s.ClassName == cls
            orderby c.IssuedOn descending, s.FullName
            select new { Cert = c, Student = s, Type = tp, Subject = sub, Teacher = te })
            .ToListAsync(ct);

        // Qidiruv — xotirada. Registr bir necha yuz qatorlik jadval, Postgres'ning `ILIKE` i
        // esa o'zbekcha apostroflar bilan har doim ham kutilganday ishlamaydi.
        var needle = (search ?? "").Trim();
        if (needle.Length > 0)
            rows = [.. rows.Where(r =>
                r.Student.FullName.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || (r.Cert.Number ?? "").Contains(needle, StringComparison.OrdinalIgnoreCase))];

        // Z-3: har hujjatning HAMMA fani — bitta qo'shimcha so'rovda (sikl ichida so'rov yo'q).
        var certIds = rows.Select(r => r.Cert.Id).ToList();
        var subjectLinks = await (
            from cs in db.CertificateSubjects.AsNoTracking()
            join sub in db.Subjects.AsNoTracking() on cs.SubjectId equals sub.Id
            where certIds.Contains(cs.CertificateId)
            orderby sub.Name
            select new { cs.CertificateId, sub.Id, sub.Name })
            .ToListAsync(ct);
        var subjectsByCert = subjectLinks
            .GroupBy(x => x.CertificateId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return [.. rows.Select(r =>
        {
            subjectsByCert.TryGetValue(r.Cert.Id, out var subs);
            return new CertificateDto(
                r.Cert.Id,
                r.Cert.StudentId, r.Student.FullName, r.Student.ClassName,
                r.Cert.TypeId, r.Type.Name, r.Type.IsScored,
                r.Cert.SubjectId, r.Subject == null ? null : r.Subject.Name,
                r.Cert.TeacherId, r.Teacher == null ? null : r.Teacher.FullName,
                r.Cert.Number, r.Cert.Score,
                Fmt(r.Cert.IssuedOn),
                r.Cert.ExpiresOn is { } e ? Fmt(e) : null,
                r.Cert.ExpiresOn is { } x && x < today,
                r.Cert.FileUrl, r.Cert.Comment,
                subs is null ? [] : [.. subs.Select(s => s.Id)],
                subs is null ? [] : [.. subs.Select(s => s.Name)]);
        })];
    }

    // =====================================================================
    //  "Natijalar" tab'i (§2.3)
    // =====================================================================

    /// <summary>
    /// Bitta tur bo'yicha o'quvchilar jadvali va yig'ma raqamlar.
    /// Tur topilmasa — null (controller 404 qaytaradi).
    ///
    /// <para>
    /// Yig'ma raqamlar har o'quvchining ENG YAXSHI balli ustidan olinadi, hamma
    /// hujjatlar ustidan emas: IELTS'ni uch marta topshirgan bola o'rtachani uch
    /// marta pasaytirib yubormasligi kerak.
    /// </para>
    /// </summary>
    public static async Task<CertificateResultsDto?> ResultsAsync(
        IAppDbContext db, Guid typeId, string? className = null, CancellationToken ct = default)
    {
        var type = await db.CertificateTypes.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == typeId, ct);
        if (type is null) return null;

        var cls = string.IsNullOrWhiteSpace(className) ? null : className;

        var rows = await (
            from c in db.Certificates.AsNoTracking().Where(c => c.TypeId == typeId)
            join s in db.Students.AsNoTracking() on c.StudentId equals s.Id
            where cls == null || s.ClassName == cls
            select new { c.StudentId, s.FullName, s.ClassName, c.Score, c.IssuedOn })
            .ToListAsync(ct);

        var perStudent = rows
            .GroupBy(r => r.StudentId)
            .Select(g =>
            {
                var latest = g.OrderByDescending(x => x.IssuedOn).First();
                return new CertificateResultRowDto(
                    g.Key, latest.FullName, latest.ClassName,
                    g.Max(x => x.Score), latest.Score, Fmt(latest.IssuedOn), g.Count());
            })
            // Ballik jadval — eng yuqori ball tepada. Bali yo'qlar oxirida, ism bo'yicha.
            .OrderByDescending(r => r.BestScore ?? decimal.MinValue)
            .ThenBy(r => r.StudentName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var scores = perStudent.Where(r => r.BestScore is not null)
            .Select(r => r.BestScore!.Value).ToList();

        return new CertificateResultsDto(
            type.Id, type.Name,
            StudentCount: perStudent.Count,
            CertificateCount: rows.Count,
            AverageScore: scores.Count == 0 ? null : Math.Round(scores.Average(), 2),
            MaxScore: scores.Count == 0 ? null : scores.Max(),
            MinScore: scores.Count == 0 ? null : scores.Min(),
            Rows: perStudent);
    }

    // =====================================================================
    //  O'quvchilar ro'yxatidagi ikkita filtr (§2.3)
    // =====================================================================

    /// <summary>
    /// "IELTS sertifikati bor har bir bolani ko'rsat" — o'quvchilar ro'yxati filtri.
    /// Filtr berilmagan bo'lsa <c>null</c> qaytadi va chaqiruvchi so'rovga TEGMAYDI.
    ///
    /// <para>
    /// <paramref name="typeIds"/> ko'p tanlanadi va ular orasidagi bog'lovchi —
    /// <b>YOKI</b>: "IELTS YOKI SAT bor bolalar". Ikkala tur ham bo'lishini talab
    /// qiladigan "VA" varianti EduSchool'da ham yo'q va ko'p tanlovli ro'yxat bilan
    /// ifodalanmaydi.
    /// </para>
    /// <para>
    /// Natija — <c>IQueryable</c>: o'quvchilar so'roviga QO'SHIMCHA so'rov bo'lib kiradi,
    /// ya'ni filtrlash serverda, bitta SQL bilan bo'ladi (id'lar ro'yxati ilovaga
    /// tortilmaydi).
    /// </para>
    /// </summary>
    public static IQueryable<string>? HolderStudentIds(
        IAppDbContext db, IReadOnlyCollection<Guid> typeIds, string? teacherId)
    {
        var hasTypes = typeIds.Count > 0;
        var hasTeacher = !string.IsNullOrWhiteSpace(teacherId);
        if (!hasTypes && !hasTeacher) return null;

        var q = db.Certificates.AsNoTracking();
        if (hasTypes)
        {
            var ids = typeIds.ToList();
            q = q.Where(c => ids.Contains(c.TypeId));
        }
        if (hasTeacher) q = q.Where(c => c.TeacherId == teacherId);
        return q.Select(c => c.StudentId);
    }

    /// <summary>
    /// <c>?certificateTypeIds=guid,guid</c> ni o'qiydi.
    ///
    /// <para>
    /// Vergul bilan — massiv parametri emas: axios massivni <c>name[]=</c> ko'rinishida
    /// yuboradi, ASP.NET esa <c>name=</c> kutadi va ikkalasini moslash uchun har ikki
    /// tomonda alohida sozlama kerak bo'lardi. Bitta satr — bitta shartnoma.
    /// </para>
    /// <para>Noto'g'ri bo'lak JIM tashlab yuboriladi: filtr — xato emas, qulaylik.</para>
    /// </summary>
    public static List<Guid> ParseIds(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? []
            : [.. csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Select(p => Guid.TryParse(p, out var g) ? g : (Guid?)null)
                     .Where(g => g is not null)
                     .Select(g => g!.Value)
                     .Distinct()];
}

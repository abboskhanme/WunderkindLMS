using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Vasiy ↔ o'quvchi: O'QISH (kim kimni ko'radi) va SINXRON (kim yoziladi).
//  SPEC §3.2. Faza 3.
// ===========================================================================
//
//  "Bu foydalanuvchi qaysi o'quvchini ko'ra oladi?" degan savolning YAGONA
//  javobi shu fayl bo'lishi kerak. Bugungacha u ikki joyda takrorlangan
//  (`StudentPortalController.TargetAsync` va `PortalFinanceController.ResolveAsync`,
//  docs/PENDING_WIRING.md §15) va ikkalasi ham telefon raqamiga tayanadi —
//  ya'ni ikki farzandli ota-onaga BITTA farzand ko'rsatadi. Yangi
//  `/api/tg/*` yuzasi faqat shu yerdan so'raydi; eski ikkitasi joyida
//  qoladi (ular bir farzandli holatda bir xil javob beradi) va ularni
//  ko'chirish alohida vazifa sifatida yozilgan.
// ===========================================================================

/// <summary>
/// Vasiyning farzandlari va egalik tekshiruvi. Holatsiz (stateless), yagona
/// bog'liqligi so'rov doirasidagi <see cref="IAppDbContext"/> — shuning uchun
/// chaqiruv joyida yasalishi mumkin (<c>FinanceReportQueries</c> namunasi).
/// </summary>
public sealed class GuardianAccess(IAppDbContext db)
{
    /// <summary>
    /// Foydalanuvchi (rol = <c>parent</c>) akkauntiga bog'langan vasiyning
    /// ARXIVLANMAGAN farzandlari — sinf, keyin ism bo'yicha.
    /// Vasiy topilmasa yoki farzandi bo'lmasa — bo'sh ro'yxat.
    ///
    /// <para>
    /// BITTA so'rov (join), sikl ichida so'rov yo'q: ota-onaning 2 ta emas,
    /// 5 ta farzandi bo'lsa ham xarajat o'zgarmaydi.
    /// </para>
    /// </summary>
    public async Task<List<Student>> ChildrenOfAsync(string userId, CancellationToken ct = default)
    {
        var guardianId = await db.Guardians.AsNoTracking()
            .Where(g => g.UserId == userId)
            .Select(g => g.Id)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrEmpty(guardianId)) return [];

        return await ChildrenOfGuardianAsync(guardianId, ct);
    }

    /// <summary>Vasiy id'si bo'yicha farzandlar (admin ekrani uchun).</summary>
    public async Task<List<Student>> ChildrenOfGuardianAsync(string guardianId, CancellationToken ct = default) =>
        await (from link in db.StudentGuardians.AsNoTracking()
               join s in db.Students.AsNoTracking() on link.StudentId equals s.Id
               where link.GuardianId == guardianId && !s.IsArchived
               orderby s.ClassName, s.FullName
               select s)
            .ToListAsync(ct);

    /// <summary>
    /// EGALIK TEKSHIRUVI: shu foydalanuvchi shu o'quvchini ko'ra oladimi.
    /// Ko'ra olsa — o'quvchi, aks holda null. Chaqiruvchi null'ni 404 ga
    /// aylantiradi (403 emas: begona bolaning MAVJUDLIGI ham ma'lumot).
    /// </summary>
    public async Task<Student?> ChildAsync(string userId, string? studentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentId)) return null;

        return await (from g in db.Guardians.AsNoTracking()
                      join link in db.StudentGuardians.AsNoTracking() on g.Id equals link.GuardianId
                      join s in db.Students.AsNoTracking() on link.StudentId equals s.Id
                      where g.UserId == userId && s.Id == studentId && !s.IsArchived
                      select s)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Mini App'ning farzand almashtirgichi uchun tayyor kartalar: ism, sinf,
    /// vasiylik turi va QARZ.
    ///
    /// <para>
    /// Qarzlar <see cref="StudentBalanceQuery.ForManyAsync"/> orqali BITTA
    /// to'plamda olinadi. Sikl ichida <c>ForAsync</c> chaqirish besh farzandli
    /// oilada beshta qo'shimcha so'rov bo'lardi — va aynan shunday narsa
    /// keyinchalik ro'yxat ekranlarida sezilarli sekinlashuvga aylanadi.
    /// </para>
    /// </summary>
    public async Task<List<TgChildDto>> ChildCardsAsync(string userId, CancellationToken ct = default)
    {
        var children = await ChildrenOfAsync(userId, ct);
        if (children.Count == 0) return [];

        var ids = children.Select(s => s.Id).ToList();

        var links = await (from g in db.Guardians.AsNoTracking()
                           join l in db.StudentGuardians.AsNoTracking() on g.Id equals l.GuardianId
                           where g.UserId == userId && ids.Contains(l.StudentId)
                           select l)
            .ToDictionaryAsync(l => l.StudentId, ct);

        var balances = await new StudentBalanceQuery(db).ForManyAsync(ids, ct);

        return [.. children.Select(s =>
        {
            links.TryGetValue(s.Id, out var link);
            // Qoldiq manfiy = qarz. Ekranda qarz MUSBAT son bo'lib ko'rinadi,
            // avans esa bu kartada umuman ko'rsatilmaydi (moliya ekranida bor).
            var balance = balances.GetValueOrDefault(s.Id);
            return new TgChildDto(
                s.Id, s.FullName, s.ClassName, s.BirthCertificateUrl,
                link?.Relation ?? GuardianRelation.Parent, link?.IsPrimary ?? false,
                balance < 0m ? -balance : 0m);
        })];
    }

    /// <summary>Bitta o'quvchining vasiylari (asosiysi birinchi).</summary>
    public async Task<List<(Guardian Guardian, StudentGuardian Link)>> GuardiansOfAsync(
        string studentId, CancellationToken ct = default)
    {
        var rows = await (from link in db.StudentGuardians.AsNoTracking()
                          join g in db.Guardians.AsNoTracking() on link.GuardianId equals g.Id
                          where link.StudentId == studentId
                          select new { g, link })
            .ToListAsync(ct);

        return [.. rows
            .OrderByDescending(x => x.link.IsPrimary)
            .ThenBy(x => x.g.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(x => (x.g, x.link))];
    }
}

/// <summary>
/// O'quvchi yozilganda/tahrirlanganda uning <c>parent_phone</c> / ota-ona ismidan
/// vasiy qatorini va bog'lanishni YARATADI (mavjudi qayta ishlatiladi).
///
/// <para>
/// Busiz jadval migratsiya kunidayoq eskira boshlardi: backfill mavjud
/// o'quvchilarni ko'chiradi, lekin ertaga qo'shilgan o'quvchining vasiysi
/// bo'lmasdi va uning ota-onasi Mini App'da hech narsa ko'rmasdi.
/// </para>
/// <para>
/// <b>`parent_phone` MANBA bo'lib qolaveradi</b> — bu sinxron bir tomonlama:
/// o'quvchi qatoridan vasiyga. Teskarisi (vasiyni tahrirlab `parent_phone` ni
/// yangilash) ATAYLAB yo'q; ustunni butunlay yopish alohida vazifa
/// (docs/PENDING_WIRING.md).
/// </para>
/// </summary>
public static class GuardianSync
{
    /// <summary>Bundan qisqa raqam bilan vasiy yaratilmaydi (chala kiritilgan ma'lumot).</summary>
    private const int MinPhoneDigits = 7;

    /// <summary>Bitta o'quvchi uchun. <see cref="EnsureManyAsync"/> ning qulay ko'rinishi.</summary>
    public static Task EnsureAsync(IAppDbContext db, Student student, CancellationToken ct = default) =>
        EnsureManyAsync(db, [student], ct);

    /// <summary>
    /// Ko'p o'quvchi uchun — Excel importi shu yo'ldan yuradi.
    /// Ikki so'rov (mavjud vasiylar + mavjud bog'lanishlar) va bitta
    /// <c>SaveChanges</c>: 500 qatorli importda ham N+1 yo'q.
    /// </summary>
    public static async Task EnsureManyAsync(
        IAppDbContext db, IReadOnlyCollection<Student> students, CancellationToken ct = default)
    {
        // Telefonsiz o'quvchi vasiysiz qoladi: `guardians.phone` bo'sh bo'la olmaydi
        // (check constraint) va telefonsiz vasiyni keyin topib ham bo'lmaydi.
        var wanted = students
            .Select(s => (Student: s, Key: PhoneUtil.Key(s.ParentPhone)))
            .Where(x => x.Key.Length >= MinPhoneDigits)
            .ToList();
        if (wanted.Count == 0) return;

        var keys = wanted.Select(x => x.Key).Distinct().ToList();
        var studentIds = wanted.Select(x => x.Student.Id).ToList();

        var byKey = await db.Guardians.Where(g => keys.Contains(g.PhoneKey))
            .ToDictionaryAsync(g => g.PhoneKey, ct);
        var existingLinks = (await db.StudentGuardians
                .Where(l => studentIds.Contains(l.StudentId))
                .ToListAsync(ct))
            .GroupBy(l => l.StudentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var touched = false;
        foreach (var (student, key) in wanted)
        {
            if (!byKey.TryGetValue(key, out var guardian))
            {
                guardian = new Guardian
                {
                    FullName = DisplayName(student),
                    Phone = student.ParentPhone.Trim(),
                    PassportUrl = student.ParentPassportUrl,
                    // PhoneKey YOZILMAYDI — u generated stored column (GuardianModel).
                };
                db.Guardians.Add(guardian);
                byKey[key] = guardian;
                touched = true;
            }

            var links = existingLinks.GetValueOrDefault(student.Id) ?? [];
            if (links.Any(l => l.GuardianId == guardian.Id)) continue;

            // Birinchi vasiy — asosiy. Ikkinchisi emas: `ux_student_guardians_one_primary`
            // o'quvchida bittadan ortiq asosiy vasiyga yo'l qo'ymaydi.
            var link = new StudentGuardian
            {
                StudentId = student.Id,
                GuardianId = guardian.Id,
                Relation = GuardianRelation.Parent,
                IsPrimary = links.Count == 0,
            };
            db.StudentGuardians.Add(link);
            links.Add(link);
            existingLinks[student.Id] = links;
            touched = true;
        }

        if (touched) await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Vasiyning ko'rsatiladigan ismi. Ota-ona ismi kiritilmagan bo'lsa
    /// "&lt;o'quvchi&gt; ning ota-onasi" — bo'sh nom check constraint'dan o'tmaydi,
    /// va bu qoida migratsiyadagi backfill bilan AYNAN bir xil.
    /// </summary>
    private static string DisplayName(Student student)
    {
        var name = (student.ParentFullName ?? "").Trim();
        return name.Length > 0 ? name : $"{student.FullName} ning ota-onasi";
    }

    // =======================================================================
    //  students-parity.md §2.3 (S-8) — FORMADAN kelgan vasiylar.
    // =======================================================================
    //
    //  YUQORIDAGI YO'NALISH O'ZGARMADI. `EnsureManyAsync` hamon
    //  `students.parent_phone` dan vasiy chiqaradi va uni hech kim
    //  almashtirmadi: import ham, eski forma ham, bugungi POST/PUT ham
    //  o'sha yo'ldan yuradi.
    //
    //  QUYIDAGILAR QO'SHIMCHA. Forma endi IKKI vasiy yubora oladi
    //  (EduSchool ham ikkitagacha, §2.3.1). Ikkinchi vasiyning telefoni
    //  `students` qatorida saqlanadigan joy YO'Q — u faqat `guardians` da
    //  yashaydi. Shuning uchun:
    //
    //    · ASOSIY vasiy = eski `parent_full_name` / `parent_phone`. Ikkovi
    //      bir qadamda yuradi (`MirrorPrimaryInput`), ya'ni ota-ona portali,
    //      shartnoma matni va eksport bugungiday ishlaydi;
    //    · QOLGAN vasiylar faqat `guardians` + `student_guardians` da.
    //
    //  BU YERDA HECH NARSA O'CHIRILMAYDI. Formadan tushib qolgan vasiy
    //  jimgina uzilmaydi — uzish alohida, ataylab qilinadigan amal
    //  (`StudentGuardiansController.Detach`). Aks holda eski formadan
    //  kelgan har saqlash ikkinchi vasiyni yo'q qilardi.
    // =======================================================================

    /// <summary>
    /// ASOSIY vasiy yozuvini o'quvchi qatoriga ko'chiradi (saqlamaydi —
    /// chaqiruvchi allaqachon <c>SaveChanges</c> qiladi).
    ///
    /// <para>
    /// Ro'yxat bo'sh yoki telefonsiz bo'lsa — HECH NARSA o'zgarmaydi, ya'ni
    /// bugungi payload (vasiylarsiz) bilan saqlash natijasi bir xil qoladi.
    /// </para>
    /// </summary>
    public static void MirrorPrimaryInput(Student student, IReadOnlyList<StudentGuardianInput>? inputs)
    {
        // ATAYLAB faqat ANIQ belgilangan asosiy vasiy: ro'yxatda birinchi
        // turgani "asosiy" degani emas. Aks holda faqat ikkinchi vasiyni
        // yuborgan chaqiruv eski `parent_*` ustunlarini bosib ketardi.
        var primary = Usable(inputs).FirstOrDefault(i => i.IsPrimary);
        if (primary is null) return;

        student.ParentPhone = (primary.Phone ?? "").Trim();

        var name = (primary.FullName ?? "").Trim();
        if (name.Length > 0)
        {
            var (last, first, middle) = StudentImportService.SplitName(name);
            student.ParentFullName = name;
            student.ParentLastName = last;
            student.ParentFirstName = first;
            student.ParentMiddleName = middle;
        }

        if (primary.PassportUrl is not null)
            student.ParentPassportUrl = string.IsNullOrWhiteSpace(primary.PassportUrl)
                ? null
                : primary.PassportUrl.Trim();
    }

    /// <summary>
    /// Formadagi vasiylarni yozadi: yo'qini yaratadi, borini yangilaydi,
    /// bog'lanish turini va asosiysini qo'yadi. O'CHIRMAYDI.
    /// </summary>
    public static async Task ApplyAsync(
        IAppDbContext db, Student student, IReadOnlyList<StudentGuardianInput>? inputs,
        CancellationToken ct = default)
    {
        var wanted = Usable(inputs);
        if (wanted.Count == 0) return;

        var keys = wanted.Select(x => PhoneUtil.Key(x.Phone)).Distinct().ToList();
        var byKey = await db.Guardians.Where(g => keys.Contains(g.PhoneKey))
            .ToDictionaryAsync(g => g.PhoneKey, ct);
        var links = await db.StudentGuardians.Where(l => l.StudentId == student.Id).ToListAsync(ct);

        // Asosiy vasiy FAQAT aniq belgilanganda almashadi. Belgilanmagan
        // bo'lsa va o'quvchida asosiysi umuman yo'q bo'lsa — birinchisi
        // olinadi (o'quvchi asosiy vasiysiz qolmasin).
        var primary = wanted.FirstOrDefault(i => i.IsPrimary)
                      ?? (links.Any(l => l.IsPrimary) ? null : wanted.FirstOrDefault());
        string? primaryGuardianId = null;

        foreach (var input in wanted)
        {
            var key = PhoneUtil.Key(input.Phone);
            var name = (input.FullName ?? "").Trim();

            if (!byKey.TryGetValue(key, out var guardian))
            {
                guardian = new Guardian
                {
                    FullName = name.Length > 0 ? name : DisplayName(student),
                    Phone = (input.Phone ?? "").Trim(),
                    PassportUrl = Clean(input.PassportUrl),
                    // PhoneKey YOZILMAYDI — u generated stored column.
                };
                db.Guardians.Add(guardian);
                byKey[key] = guardian;
            }
            else
            {
                // Mavjud vasiy: nom va hujjat YANGILANADI, telefon esa
                // TEGILMAYDI — u bu yerda IDENTIFIKATOR (oxirgi 9 raqami
                // bo'yicha topildi), uni yozish `phone_key` ni siljitardi.
                if (name.Length > 0) guardian.FullName = name;
                if (input.PassportUrl is not null) guardian.PassportUrl = Clean(input.PassportUrl);
            }

            var link = links.FirstOrDefault(l => l.GuardianId == guardian.Id);
            if (link is null)
            {
                link = new StudentGuardian { StudentId = student.Id, GuardianId = guardian.Id };
                db.StudentGuardians.Add(link);
                links.Add(link);
            }

            link.Relation = NormalizeRelation(input.Relation);
            link.RelationNote = link.Relation == GuardianRelation.Other ? Clean(input.RelationNote) : null;

            if (ReferenceEquals(input, primary)) primaryGuardianId = guardian.Id;
        }

        await db.SaveChangesAsync(ct);

        if (primaryGuardianId is not null)
            await SetPrimaryAsync(db, student.Id, primaryGuardianId, ct);
    }

    /// <summary>
    /// Asosiy vasiyni belgilaydi — o'quvchida ko'pi bilan bittasi bo'ladi
    /// (<c>ux_student_guardians_one_primary</c>).
    ///
    /// <para>
    /// IKKI SAQLASH: avval eskisi bo'shatiladi, keyin yangisi qo'yiladi.
    /// Bitta <c>SaveChanges</c> da EF ikkita UPDATE ni qaysi tartibda
    /// yuborishini kafolatlamaydi, qisman unikal indeks esa har qatorda
    /// darrov tekshiriladi — ya'ni "yangisini qo'yib, keyin eskisini
    /// bo'shatish" tartibida 23505 bilan yiqilish EHTIMOLI bor.
    /// </para>
    /// </summary>
    public static async Task SetPrimaryAsync(
        IAppDbContext db, string studentId, string guardianId, CancellationToken ct = default)
    {
        var links = await db.StudentGuardians.Where(l => l.StudentId == studentId).ToListAsync(ct);
        var target = links.FirstOrDefault(l => l.GuardianId == guardianId);
        if (target is null) return;

        var cleared = false;
        foreach (var other in links.Where(l => l.GuardianId != guardianId && l.IsPrimary))
        {
            other.IsPrimary = false;
            cleared = true;
        }
        if (cleared) await db.SaveChangesAsync(ct);

        if (target.IsPrimary) return;
        target.IsPrimary = true;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Bazadagi ASOSIY vasiydan o'quvchi qatorini tekislaydi (saqlamaydi).
    ///
    /// <para>
    /// Bu <b>yagona</b> teskari yo'nalish va u ataylab tor: vasiy tahrirlanganda
    /// yoki asosiysi almashtirilganda <c>parent_phone</c> eskirib qolsa,
    /// ota-ona portali (telefon bo'yicha topadi) o'sha oilani YO'QOTARDI.
    /// Asosiy vasiy bo'lmasa ustunlar TEGILMAYDI — bo'shatish ma'lumot
    /// yo'qotish bo'lardi.
    /// </para>
    /// </summary>
    public static async Task MirrorPrimaryFromDbAsync(
        IAppDbContext db, Student student, CancellationToken ct = default)
    {
        var primary = await (from l in db.StudentGuardians.AsNoTracking()
                             join g in db.Guardians.AsNoTracking() on l.GuardianId equals g.Id
                             where l.StudentId == student.Id && l.IsPrimary
                             select g)
            .FirstOrDefaultAsync(ct);
        if (primary is null) return;

        var name = primary.FullName.Trim();
        var (last, first, middle) = StudentImportService.SplitName(name);
        student.ParentFullName = name;
        student.ParentLastName = last;
        student.ParentFirstName = first;
        student.ParentMiddleName = middle;
        student.ParentPhone = primary.Phone.Trim();
        student.ParentPassportUrl = primary.PassportUrl;
    }

    /// <summary>Tanish qiymatmi; bo'sh yoki notanish bo'lsa — <c>parent</c>.</summary>
    public static string NormalizeRelation(string? value)
    {
        var v = (value ?? "").Trim().ToLowerInvariant();
        return GuardianRelation.IsStorable(v) ? v : GuardianRelation.Parent;
    }

    /// <summary>Telefoni yaroqli bo'lgan yozuvlar — qolganlari jimgina tashlanadi.</summary>
    private static List<StudentGuardianInput> Usable(IReadOnlyList<StudentGuardianInput>? inputs) =>
        [.. (inputs ?? []).Where(i => PhoneUtil.Key(i.Phone ?? "").Length >= MinPhoneDigits)];

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

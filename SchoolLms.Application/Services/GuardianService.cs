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
}

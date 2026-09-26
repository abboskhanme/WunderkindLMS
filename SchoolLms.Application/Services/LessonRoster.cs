using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  DARS RO'YXATI — G-5 (docs/modules/students-parity.md §2.1.6).
// ===========================================================================
//
//  MUAMMO
//  ------
//  Bugun "shu darsda kim bor?" degan savolga ~14 ta server so'rovi O'ZI
//  javob beradi: `students.class_name == cls.Name`, keyin
//  `SubGroup == 0 || s.SubGroup == lesson.SubGroup`. Nusxa ko'chirilgan
//  mantiq. Guruh darsi qo'shilganda o'sha 14 joyning HAR BIRI alohida
//  tuzatilishi kerak bo'lardi — va bittasi unutilsa, bola jurnalda yo'qoladi.
//
//  YECHIM
//  ------
//  Bitta joy: <see cref="LessonRoster"/>. U darsning EGASINI
//  (<see cref="LessonOwner"/> — sinf yoki guruh) o'quvchilar ro'yxatiga
//  aylantiradi.
//
//  ENG MUHIM XOSSA — BUGUN HECH NARSA O'ZGARMAYDI
//  ----------------------------------------------
//  `school_meta.group_lessons_enabled` O'CHIQ ekan:
//    · <see cref="LiveOwnersAsync"/> faqat sinflarni qaytaradi;
//    · sinf uchun so'rov AYNAN bugungisi — `class_name == <sinf nomi>`;
//    · guruh a'zoligi hech qayerda o'qilmaydi.
//  Ya'ni chiqish qatorlari bugungi kod bilan BIR XIL. Buni `LessonRosterTests`
//  har sinf uchun eski so'rov bilan yonma-yon solishtirib isbotlaydi.
//
//  NEGA `students.class_name`, `class_memberships` EMAS
//  ---------------------------------------------------
//  §2.1.4: `class_name` QOLADI va a'zolik xizmati ikkalasini birga yuritadi.
//  Sinf ro'yxatining haqiqat manbai — hali ham `class_name`. Uni bu yerda
//  almashtirsak, resolver "bugungi bilan bir xil" bo'lmay qolardi.
// ===========================================================================

/// <summary>
/// Darsning egasi — SINF yoki GURUH (§2.1.4).
///
/// <para>
/// <paramref name="Id"/> beshta dars jadvalidagi <c>class_id</c> ustunida
/// turadigan qiymat: sinf uchun sinf id'si, guruh uchun guruh id'si.
/// <paramref name="Name"/> — odam ko'radigan nom; sinf uchun u ro'yxat
/// so'rovining KALITI ham (<c>students.class_name</c>).
/// </para>
/// </summary>
/// <param name="Kind"><see cref="LessonOwnerKind"/> qiymatlaridan biri.</param>
/// <param name="SubjectId">Faqat ODDIY guruhda to'ldiriladi — guruhning fani. Yo'nalish
/// guruhida null (u ko'p fan o'qitadi).</param>
/// <param name="IsTrack">
/// Yo'nalish guruhi (<see cref="StudyGroup.IsTrack"/>) — SINF KABI ishlaydi va darslari
/// cut-over o'chirgichiga bog'liq emas (docs/modules/track-groups-as-classes.md).
/// </param>
public sealed record LessonOwner(
    string Kind, string Id, string Name, string? SubjectId = null, bool IsTrack = false)
{
    public bool IsGroup => Kind == LessonOwnerKind.Group;

    public bool IsClass => Kind == LessonOwnerKind.Class;
}

/// <summary>
/// Qaysi GURUH egalarining darslari "tirik" — cut-over o'chirgichi va yo'nalish
/// guruhlari birga (docs/modules/track-groups-as-classes.md).
///
/// <para>
/// <b>Qoida:</b> sinf darsi — har doim tirik. Yo'nalish guruhining darsi —
/// har doim tirik (o'chirgichga QARAMAYDI). Oddiy guruh darsi — faqat
/// <c>group_lessons_enabled</c> yoqilganda.
/// </para>
/// <para>
/// <see cref="TrackIds"/> — ARXIVLANGANLARI BILAN birga barcha yo'nalish guruhlari:
/// arxivlangan guruhning o'tgan darslari (jurnal, davomat) tarix sifatida ko'rinishda qoladi.
/// </para>
/// </summary>
public sealed record GroupLessonScope(bool AllGroups, IReadOnlySet<string> TrackIds)
{
    /// <summary>SQL filtrlari uchun ro'yxat (<c>Contains</c> → <c>= ANY</c>).</summary>
    public List<string> TrackIdList { get; } = [.. TrackIds];

    /// <summary>Birorta guruh darsi tirikmi (o'chirgich yoki kamida bitta yo'nalish).</summary>
    public bool AnyGroup => AllGroups || TrackIds.Count > 0;

    /// <summary>Shu (tur, ega id'si) dagi dars tirikmi.</summary>
    public bool Allows(string ownerKind, string ownerId) =>
        ownerKind != LessonOwnerKind.Group || AllGroups || TrackIds.Contains(ownerId);

    /// <summary>Guruh id'si (sinf emasligi ma'lum) tirikmi.</summary>
    public bool AllowsGroup(string groupId) => AllGroups || TrackIds.Contains(groupId);
}

/// <summary>
/// Dars jadvali, davomat va jurnal TANLAGICHLARIdagi bitta ega (sinf yoki guruh).
/// Tartib va almashtirish qoidasi — <see cref="LessonRoster.PickerOwnersAsync"/>.
/// </summary>
/// <param name="Grade">Sinf darajasi; guruh uchun 0.</param>
/// <param name="ClassIds">Guruhni boqadigan sinflar (sinf uchun bo'sh).</param>
public sealed record LessonOwnerItem(
    string Id, string Name, string Kind, int Grade, bool IsTrack,
    string? SubjectId, IReadOnlyList<string> ClassIds);

/// <summary>
/// "Shu darsda kim o'qiydi?" — yagona javob beruvchi. Fayl boshidagi izohda
/// nega kerakligi va nima kafolatlanishi yozilgan.
/// </summary>
public static class LessonRoster
{
    /// <summary>
    /// Cut-over o'chirgichi (<c>school_meta.group_lessons_enabled</c>, §4.3).
    /// O'chiq bo'lsa ODDIY guruh darsi UMUMAN yo'q deb qaraladi — jadval, jurnal,
    /// maosh va turniket bugungi raqamni ko'rsatadi.
    ///
    /// <para>
    /// <b>YO'NALISH GURUHLARI BU O'CHIRGICHGA QARAMAYDI</b> (mijoz, 2026-09-26,
    /// docs/modules/track-groups-as-classes.md): ular 9–11-sinflarning sinfi
    /// o'rnida turadi va darslari har doim tirik. Shuning uchun "guruh darsi
    /// tirikmi" degan savolga bu metod emas, <see cref="LessonsLiveAsync"/> yoki
    /// <see cref="GroupScopeAsync"/> javob beradi; bu metodni to'g'ridan-to'g'ri
    /// faqat o'chirgichning O'ZI kerak bo'lgan joy (sozlama ekrani) chaqiradi.
    /// </para>
    /// </summary>
    public static async Task<bool> GroupLessonsEnabledAsync(
        IAppDbContext db, CancellationToken ct = default)
    {
        var enabled = await db.SchoolMeta.AsNoTracking()
            .Select(m => (bool?)m.GroupLessonsEnabled).FirstOrDefaultAsync(ct);
        return enabled ?? false;
    }

    /// <summary>
    /// Guruh darslari qamrovi — o'chirgich + yo'nalish guruhlari
    /// (<see cref="GroupLessonScope"/>). <c>group_lessons_enabled</c> ni
    /// tekshiradigan har bir joy endi SHU qamrovga qaraydi: yo'nalish guruhining
    /// darsi o'chirgich o'chiq bo'lsa ham tirik (mijoz, 2026-09-26).
    /// </summary>
    public static async Task<GroupLessonScope> GroupScopeAsync(
        IAppDbContext db, CancellationToken ct = default)
    {
        var all = await GroupLessonsEnabledAsync(db, ct);
        var tracks = await db.StudyGroups.AsNoTracking()
            .Where(g => g.IsTrack).Select(g => g.Id).ToListAsync(ct);
        return new GroupLessonScope(all,
            tracks.Select(id => id.ToString()).ToHashSet(StringComparer.Ordinal));
    }

    /// <summary>
    /// Shu eganing darslari tirikmi: sinf — ha; yo'nalish guruhi — ha (o'chirgichga
    /// qaramaydi); oddiy guruh — faqat o'chirgich yoqilganda.
    /// </summary>
    public static async Task<bool> LessonsLiveAsync(
        IAppDbContext db, LessonOwner owner, CancellationToken ct = default) =>
        !owner.IsGroup || owner.IsTrack || await GroupLessonsEnabledAsync(db, ct);

    /// <summary>
    /// Faol yo'nalish guruhlarini boqadigan sinflar — dars jadvali, davomat va
    /// jurnal ro'yxatlarida ular YASHIRILADI, o'rnida yo'nalish guruhlari turadi
    /// (9–11-sinflar hamma darsini yo'nalish guruhida o'qiydi). Hujjat, moliya,
    /// shartnoma va hisobotlar uchun sinf joyida qoladi.
    /// </summary>
    public static async Task<HashSet<string>> TrackFedClassIdsAsync(
        IAppDbContext db, CancellationToken ct = default)
    {
        var ids = await db.StudyGroupClasses.AsNoTracking()
            .Where(gc => db.StudyGroups.Any(g => g.Id == gc.GroupId && g.IsTrack && !g.IsArchived))
            .Select(gc => gc.ClassId).Distinct().ToListAsync(ct);
        return ids.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Tanlagich ro'yxati (dars jadvali, davomat, jurnal): yo'nalish guruhini
    /// boqmaydigan arxivlanmagan SINFLAR daraja/nom bo'yicha, keyin faol
    /// YO'NALISH guruhlari nom bo'yicha, keyin (faqat o'chirgich yoqilganda)
    /// faol ODDIY guruhlar nom bo'yicha.
    /// </summary>
    /// <param name="includeOrdinaryGroups">false — oddiy guruhlar o'chirgich yoqilgan bo'lsa ham qo'shilmaydi.</param>
    public static async Task<List<LessonOwnerItem>> PickerOwnersAsync(
        IAppDbContext db, bool includeOrdinaryGroups = true, CancellationToken ct = default)
    {
        var hidden = await TrackFedClassIdsAsync(db, ct);
        var result = (await db.Classes.AsNoTracking()
                .Where(c => !c.IsArchived)
                .Select(c => new { c.Id, c.Name, c.Grade }).ToListAsync(ct))
            .Where(c => !hidden.Contains(c.Id))
            .OrderBy(c => c.Grade).ThenBy(c => c.Name, StringComparer.Ordinal)
            .Select(c => new LessonOwnerItem(c.Id, c.Name, LessonOwnerKind.Class, c.Grade, false, null, []))
            .ToList();

        var groupsOn = includeOrdinaryGroups && await GroupLessonsEnabledAsync(db, ct);
        var groups = await db.StudyGroups.AsNoTracking()
            .Where(g => !g.IsArchived && (g.IsTrack || groupsOn))
            .Select(g => new { g.Id, g.Name, g.IsTrack, g.SubjectId }).ToListAsync(ct);
        if (groups.Count == 0) return result;

        var groupIds = groups.Select(g => g.Id).ToList();
        var feeding = (await db.StudyGroupClasses.AsNoTracking()
                .Where(gc => groupIds.Contains(gc.GroupId))
                .Select(gc => new { gc.GroupId, gc.ClassId }).ToListAsync(ct))
            .GroupBy(x => x.GroupId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)[.. g.Select(x => x.ClassId)]);

        result.AddRange(groups
            .OrderBy(g => g.IsTrack ? 0 : 1).ThenBy(g => g.Name, StringComparer.Ordinal)
            .Select(g => new LessonOwnerItem(
                g.Id.ToString(), g.Name, LessonOwnerKind.Group, 0, g.IsTrack,
                g.IsTrack ? null : g.SubjectId, feeding.GetValueOrDefault(g.Id, []))));
        return result;
    }

    /// <summary>
    /// TIRIK egalar: arxivlanmagan sinflar (har doim) + arxivlanmagan yo'nalish
    /// guruhlari (har doim) + arxivlanmagan oddiy guruhlar (faqat o'chirgich yoqilganda). Kalit — <c>class_id</c> ustunidagi qiymat.
    ///
    /// <para>
    /// Maosh (G-16) va turniket (G-14) shu ro'yxatdan oziqlanadi: guruh bu
    /// yerda BITTA yozuv, uni nechta sinf boqishidan qat'i nazar — shuning
    /// uchun guruh darsi bir marta sanaladi.
    /// </para>
    /// </summary>
    public static Task<Dictionary<string, LessonOwner>> LiveOwnersAsync(
        IAppDbContext db, CancellationToken ct = default) =>
        OwnersAsync(db, includeArchived: false, ct);

    /// <summary>
    /// Arxivlanganlari BILAN birga hamma ega — faqat NOM izlash uchun
    /// (portal jadvali arxivlangan sinf darsini ham nomi bilan ko'rsatadi,
    /// shuning uchun uni <see cref="LiveOwnersAsync"/> bilan almashtirib
    /// bo'lmaydi: bugungi xatti-harakat o'zgarib ketardi).
    /// </summary>
    public static Task<Dictionary<string, LessonOwner>> AllOwnersAsync(
        IAppDbContext db, CancellationToken ct = default) =>
        OwnersAsync(db, includeArchived: true, ct);

    private static async Task<Dictionary<string, LessonOwner>> OwnersAsync(
        IAppDbContext db, bool includeArchived, CancellationToken ct)
    {
        var owners = new Dictionary<string, LessonOwner>(StringComparer.Ordinal);

        var classQuery = db.Classes.AsNoTracking();
        if (!includeArchived) classQuery = classQuery.Where(c => !c.IsArchived);
        foreach (var c in await classQuery.Select(c => new { c.Id, c.Name }).ToListAsync(ct))
            owners[c.Id] = new LessonOwner(LessonOwnerKind.Class, c.Id, c.Name);

        // Yo'nalish guruhlari o'chirgichga qaramaydi; oddiy guruhlar — faqat yoqilganda.
        var groupsOn = await GroupLessonsEnabledAsync(db, ct);

        var groupQuery = db.StudyGroups.AsNoTracking().Where(g => groupsOn || g.IsTrack);
        if (!includeArchived) groupQuery = groupQuery.Where(g => !g.IsArchived);
        foreach (var g in await groupQuery.Select(g => new { g.Id, g.Name, g.SubjectId, g.IsTrack }).ToListAsync(ct))
            owners[g.Id.ToString()] = new LessonOwner(
                LessonOwnerKind.Group, g.Id.ToString(), g.Name, g.SubjectId, g.IsTrack);

        return owners;
    }

    /// <summary>
    /// Bitta id'ni egaga aylantiradi: avval SINF, topilmasa GURUH.
    ///
    /// <para>
    /// O'chirgichga QARAMAYDI — ataylab. Jadval controlleri guruhni topib,
    /// keyin "guruh darslari hali yoqilmagan" degan ANIQ xabar berishi kerak;
    /// 404 esa foydalanuvchini adashtirardi. Arxivlangan egani ham qaytaradi
    /// (o'chirish/nomni o'zgartirish yo'llari uni ko'rishi shart).
    /// </para>
    /// </summary>
    public static async Task<LessonOwner?> OwnerAsync(
        IAppDbContext db, string ownerId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(ownerId)) return null;

        var cls = await db.Classes.AsNoTracking()
            .Where(c => c.Id == ownerId)
            .Select(c => new { c.Id, c.Name }).FirstOrDefaultAsync(ct);
        if (cls is not null) return new LessonOwner(LessonOwnerKind.Class, cls.Id, cls.Name);

        if (!Guid.TryParse(ownerId, out var groupId)) return null;
        var grp = await db.StudyGroups.AsNoTracking()
            .Where(g => g.Id == groupId)
            .Select(g => new { g.Id, g.Name, g.SubjectId, g.IsTrack }).FirstOrDefaultAsync(ct);
        return grp is null
            ? null
            : new LessonOwner(LessonOwnerKind.Group, grp.Id.ToString(), grp.Name, grp.SubjectId, grp.IsTrack);
    }

    /// <summary>
    /// Eganing o'quvchilari — SO'ROV sifatida (chaqiruvchi o'zi filtrlaydi va
    /// tartiblaydi, bugungi kod nimani qilsa shuni).
    ///
    /// <para>
    /// Sinf: <c>class_name == &lt;sinf nomi&gt;</c> — bugungi so'rovning AYNAN o'zi.
    /// Guruh: <c>study_group_members</c> dagi FAOL a'zolik;
    /// <paramref name="asOf"/> berilsa — o'sha sanada ochiq bo'lgan a'zolik
    /// (tarixiy hisobot uchun).
    /// </para>
    /// </summary>
    public static IQueryable<Student> Query(IAppDbContext db, LessonOwner owner, DateOnly? asOf = null)
    {
        if (!owner.IsGroup || !Guid.TryParse(owner.Id, out var groupId))
            return db.Students.Where(s => s.ClassName == owner.Name);

        if (asOf is null)
            return db.Students.Where(s => db.StudyGroupMembers
                .Any(m => m.GroupId == groupId && m.StudentId == s.Id && m.LeftOn == null));

        var on = asOf.Value;
        return db.Students.Where(s => db.StudyGroupMembers
            .Any(m => m.GroupId == groupId && m.StudentId == s.Id
                      && m.JoinedOn <= on && (m.LeftOn == null || m.LeftOn >= on)));
    }

    /// <summary>
    /// BITTA dars katagining ro'yxati: eganing o'quvchilari, keyin bo'linish
    /// filtri.
    ///
    /// <para>
    /// Bo'linish (<c>SubGroup</c> 1/2) — SINF ichidagi narsa. Guruhda u yo'q:
    /// guruh darsida uning hamma faol a'zosi qatnashadi, shuning uchun
    /// <paramref name="subGroup"/> guruh uchun e'tiborga olinmaydi (§2.1.3 —
    /// guruh qatorlarida <c>sub_group</c> 0 bo'lishi kerak).
    /// </para>
    /// </summary>
    public static async Task<List<Student>> ForLessonAsync(
        IAppDbContext db, LessonOwner owner, int subGroup = 0,
        bool includeArchived = false, DateOnly? asOf = null, CancellationToken ct = default)
    {
        var q = Query(db, owner, asOf);
        if (!includeArchived) q = q.Where(s => !s.IsArchived);
        var list = await q.OrderBy(s => s.FullName).ToListAsync(ct);
        return owner.IsGroup || subGroup == 0
            ? list
            : [.. list.Where(s => s.SubGroup == subGroup)];
    }

    /// <summary>
    /// Id bo'yicha qisqa yo'l: egani topadi va ro'yxatini qaytaradi. Ega
    /// topilmasa — bo'sh ro'yxat (bugungi kod ham "yetim" id'da bo'sh beradi).
    /// </summary>
    public static async Task<List<Student>> ForLessonAsync(
        IAppDbContext db, string ownerId, int subGroup = 0,
        bool includeArchived = false, DateOnly? asOf = null, CancellationToken ct = default)
    {
        var owner = await OwnerAsync(db, ownerId, ct);
        return owner is null
            ? []
            : await ForLessonAsync(db, owner, subGroup, includeArchived, asOf, ct);
    }

    /// <summary>
    /// O'quvchi QAYSI egalarning darsiga kiradi: sinf rahbarligidagi sinfi +
    /// har bir faol guruhi (guruhlar faqat o'chirgich yoqilganda).
    ///
    /// <para>
    /// <see cref="PupilTimetable"/> va turniket kutilgan vaqti (G-14) shundan
    /// oziqlanadi. O'chirgich o'chiq bo'lsa — ro'yxatda faqat sinf, ya'ni
    /// bugungi xatti-harakat.
    /// </para>
    /// </summary>
    public static async Task<List<LessonOwner>> OwnersOfAsync(
        IAppDbContext db, Student student, DateOnly? asOf = null, CancellationToken ct = default)
    {
        var owners = new List<LessonOwner>();

        var cls = await db.Classes.AsNoTracking()
            .Where(c => c.Name == student.ClassName)
            .Select(c => new { c.Id, c.Name }).FirstOrDefaultAsync(ct);
        if (cls is not null) owners.Add(new LessonOwner(LessonOwnerKind.Class, cls.Id, cls.Name));

        // Yo'nalish guruhi o'chirgichga qaramaydi; oddiy guruh — faqat yoqilganda.
        var groupsOn = await GroupLessonsEnabledAsync(db, ct);

        var groupIds = asOf is null
            ? await db.StudyGroupMembers.AsNoTracking()
                .Where(m => m.StudentId == student.Id && m.LeftOn == null)
                .Select(m => m.GroupId).ToListAsync(ct)
            : await db.StudyGroupMembers.AsNoTracking()
                .Where(m => m.StudentId == student.Id
                            && m.JoinedOn <= asOf.Value
                            && (m.LeftOn == null || m.LeftOn >= asOf.Value))
                .Select(m => m.GroupId).ToListAsync(ct);
        if (groupIds.Count == 0) return owners;

        var groups = await db.StudyGroups.AsNoTracking()
            .Where(g => groupIds.Contains(g.Id) && !g.IsArchived && (groupsOn || g.IsTrack))
            .Select(g => new { g.Id, g.Name, g.SubjectId, g.IsTrack }).ToListAsync(ct);
        foreach (var g in groups)
            owners.Add(new LessonOwner(LessonOwnerKind.Group, g.Id.ToString(), g.Name, g.SubjectId, g.IsTrack));

        return owners;
    }

    /// <summary>
    /// Ko'p o'quvchi uchun bir martalik variant: studentId → egalar.
    /// Sikl ichida so'rov yo'q (turniket hisoboti minglab o'quvchini ko'radi).
    /// </summary>
    public static async Task<Dictionary<string, List<LessonOwner>>> GroupOwnersByStudentAsync(
        IAppDbContext db, CancellationToken ct = default)
    {
        var result = new Dictionary<string, List<LessonOwner>>(StringComparer.Ordinal);
        // Yo'nalish guruhlari o'chirgichga qaramaydi; oddiy guruhlar — faqat yoqilganda.
        var groupsOn = await GroupLessonsEnabledAsync(db, ct);

        var groups = await db.StudyGroups.AsNoTracking()
            .Where(g => !g.IsArchived && (groupsOn || g.IsTrack))
            .Select(g => new { g.Id, g.Name, g.SubjectId, g.IsTrack }).ToListAsync(ct);
        if (groups.Count == 0) return result;

        var byId = groups.ToDictionary(
            g => g.Id,
            g => new LessonOwner(LessonOwnerKind.Group, g.Id.ToString(), g.Name, g.SubjectId, g.IsTrack));

        var members = await db.StudyGroupMembers.AsNoTracking()
            .Where(m => m.LeftOn == null)
            .Select(m => new { m.GroupId, m.StudentId }).ToListAsync(ct);

        foreach (var m in members)
        {
            if (!byId.TryGetValue(m.GroupId, out var owner)) continue;
            if (!result.TryGetValue(m.StudentId, out var list)) result[m.StudentId] = list = [];
            list.Add(owner);
        }
        return result;
    }
}

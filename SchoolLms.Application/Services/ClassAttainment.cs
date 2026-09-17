using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  "SINF O'ZLASHTIRISHI" — G-15 (docs/modules/students-parity.md §2.1.6).
// ===========================================================================
//
//  SAVOL
//  -----
//  O'quvchining ingliz tili baholarining yarmi 5-A va 5-B dan yig'ilgan
//  GURUHDA qo'yilgan bo'lsa, "5-A sinfining o'zlashtirishi" nima degani?
//  Guruh hisobotda alohida qator bo'ladimi? Baho ikki joyda sanaladimi?
//  Guruh darsi bolaning davomat maxrajiga kiradimi?
//
//  Bu savolga har hisobot ALOHIDA javob bersa, bitta sinf haqida ikki ekran
//  ikki xil raqam ko'rsatardi — bu ikkala javobdan ham yomon. Shuning uchun
//  javob BITTA joyda, shu faylda yozilgan va hamma hisobot shu yerdan o'qiydi.
//
//  QOIDA (yagona, o'zgarmas)
//  -------------------------
//  1. O'quvchining qatori HAR DOIM uning SINF RAHBARLIGIDAGI sinfi ostida
//     sanaladi — darsning egasi sinfmi yoki guruhmi, farqi yo'q. Guruh sinf,
//     parallel yoki maktab hisobotida O'Z QATORI bo'lib CHIQMAYDI (EduSchool
//     ham sinf va guruhni bir xil hisobotda yonma-yon ko'rsatadi — §2.1.1
//     "Group in reports").
//  2. Har qator BIR MARTA sanaladi. Baho — bu bitta `journal_entries` qatori
//     va uning BITTA egasi bor, shuning uchun guruh bahosi sinf bahosining
//     USTIGA qo'shiladi, o'rnini bosmaydi: bolaning ingliz tili o'rtachasi
//     sinfdagi va guruhdagi baholarining birgalikdagi o'rtachasi bo'ladi.
//     Guruh darsi sinf darsini BEKOR QILMAYDI (Q2 faqat vaqt to'qnashuvini
//     taqiqlaydi, §2.1.4) — ya'ni "almashtirish" qoidasi noto'g'ri bo'lardi.
//  3. Guruh qatori FAQAT o'sha guruhning FAOL a'zolariga tegishli. Guruhni
//     bir nechta sinf boqadi, shuning uchun 5-A ning hisobotida guruhning
//     5-B lik bolasi ko'rinmasligi kerak — buni <see cref="CountsFor"/>
//     ushlaydi.
//  4. Davomat maxraji (o'tilgan darslar) ham shu qoida bo'yicha: bolaning
//     sinf darslari + faol guruhlari darslari. Guruh darsida sinf ichidagi
//     bo'linish (SubGroup) yo'q — guruhning o'zi allaqachon tanlangan
//     bolalar to'plami.
//
//  NIMA QILMAYDI (ataylab, va bu bugun ham shunday)
//  ------------------------------------------------
//  A'ZOLIK TARIXI hisobotga kirmaydi: guruhdan chiqqan bolaning o'sha
//  guruhdagi eski baholari sinf hisobotida ko'rinmaydi — xuddi sinf
//  almashtirgan bolaning eski sinfdagi baholari bugun ko'rinmagani kabi
//  (`StudentReportBuilder` `e.ClassId == cls.Id` bilan tashlab yuboradi).
//  Ikkalasi BITTA tuzatish va u C3 dan tashqarida: uni shu yerda qilish
//  o'chirgich O'CHIQ turganda ham raqamlarni siljitardi, ya'ni cut-over'ning
//  asosiy qoidasini buzardi.
//
//  O'CHIRGICH O'CHIQ = BUGUNGI RAQAM
//  ---------------------------------
//  `school_meta.group_lessons_enabled` o'chiq ekan
//  <see cref="LessonRoster.GroupOwnersByStudentAsync"/> bo'sh qaytadi, ya'ni
//  qamrov bo'sh bo'ladi: <see cref="GroupOwnerIds"/> — bo'sh,
//  <see cref="OwnerIds"/> — kiritilgan ro'yxatning o'zi,
//  <see cref="CountsFor"/> — har sinf qatori uchun `true`. Bu fayl bugungi
//  xatti-harakatning AYNAN o'zini beradi; `ClassAttainmentTests` ikkala
//  holatni ham tekshiradi.
// ===========================================================================

/// <summary>
/// "Kimning qatori qaysi sinf ostida sanaladi?" — yagona javob beruvchi.
/// Fayl boshidagi izohda qoida to'liq yozilgan.
/// </summary>
public sealed class ClassAttainment
{
    private static readonly IReadOnlyList<LessonOwner> NoGroups = [];

    private readonly Dictionary<string, List<LessonOwner>> _groupsByStudent;
    private readonly Dictionary<string, HashSet<string>> _groupIdsByStudent;

    private ClassAttainment(Dictionary<string, List<LessonOwner>> groupsByStudent)
    {
        _groupsByStudent = groupsByStudent;
        _groupIdsByStudent = groupsByStudent.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Select(o => o.Id).ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);
        GroupOwnerIds = [.. groupsByStudent.Values.SelectMany(x => x).Select(o => o.Id)
            .Distinct(StringComparer.Ordinal)];
    }

    /// <summary>Qamrovdagi BARCHA guruh id'lari (so'rovni kengaytirish uchun).</summary>
    public IReadOnlyList<string> GroupOwnerIds { get; }

    /// <summary>Guruh darsi umuman bormi — o'chirgich o'chiq bo'lsa har doim <c>false</c>.</summary>
    public bool HasGroups => GroupOwnerIds.Count > 0;

    /// <summary>
    /// Butun maktab uchun qamrov (barcha faol guruh a'zoliklari, BITTA yurishda —
    /// sikl ichida so'rov yo'q). O'chirgich o'chiq bo'lsa qamrov bo'sh.
    /// </summary>
    public static async Task<ClassAttainment> BuildAsync(
        IAppDbContext db, CancellationToken ct = default) =>
        new(await LessonRoster.GroupOwnersByStudentAsync(db, ct));

    /// <summary>
    /// Bitta o'quvchi uchun qamrov — portal va shaxsiy hisobot uchun
    /// (maktab bo'ylab yurish shart emas).
    /// </summary>
    public static async Task<ClassAttainment> ForStudentAsync(
        IAppDbContext db, Student student, CancellationToken ct = default)
    {
        var owners = await LessonRoster.OwnersOfAsync(db, student, ct: ct);
        var groups = owners.Where(o => o.IsGroup).ToList();
        var map = new Dictionary<string, List<LessonOwner>>(StringComparer.Ordinal);
        if (groups.Count > 0) map[student.Id] = groups;
        return new ClassAttainment(map);
    }

    /// <summary>O'quvchining faol guruhlari (ega ko'rinishida).</summary>
    public IReadOnlyList<LessonOwner> GroupsOf(string studentId) =>
        _groupsByStudent.TryGetValue(studentId, out var list) ? list : NoGroups;

    /// <summary>
    /// QOIDANING O'ZI: shu qator (baho, chorak bahosi yoki o'tilgan dars) shu
    /// o'quvchi uchun sanaladimi.
    ///
    /// <para>
    /// Guruh qatori — faqat o'quvchi o'sha guruhning FAOL a'zosi bo'lsa
    /// (guruhni bir nechta sinf boqadi, ya'ni qator begona sinfnikiga ham
    /// tushib qolishi mumkin). Sinf qatori — har doim: chaqiruvchi allaqachon
    /// sinfni va o'quvchini bog'lagan. Fayl boshidagi 2- va 3-bandlar.
    /// </para>
    /// </summary>
    public bool CountsFor(string studentId, string ownerId, string ownerKind) =>
        ownerKind != LessonOwnerKind.Group
        || (_groupIdsByStudent.TryGetValue(studentId, out var ids) && ids.Contains(ownerId));

    /// <summary>
    /// So'rov kengaytmasi: berilgan sinf id'lari + qamrovdagi barcha guruh
    /// id'lari. `class_id` ustuni ikkalasini ham saqlaydi (§2.1.4), shuning
    /// uchun bitta `Contains` yetadi.
    ///
    /// <para>O'chirgich o'chiq bo'lsa natija — kiritilgan ro'yxatning o'zi.</para>
    /// </summary>
    public List<string> OwnerIds(IEnumerable<string> classIds)
    {
        var ids = new List<string>(classIds);
        ids.AddRange(GroupOwnerIds);
        return ids;
    }

    /// <summary>
    /// Bitta sinf uchun so'rov kengaytmasi: sinf id'si + shu sinf
    /// o'quvchilarining faol guruhlari. Maktab bo'yicha hisobotda har sinfga
    /// faqat o'ziga tegishli guruhlar qo'shiladi.
    /// </summary>
    public List<string> OwnerIdsForClass(string classId, IEnumerable<string> studentIds)
    {
        var ids = new List<string> { classId };
        if (!HasGroups) return ids;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var studentId in studentIds)
            foreach (var g in GroupsOf(studentId))
                if (seen.Add(g.Id)) ids.Add(g.Id);
        return ids;
    }

    /// <summary>
    /// Bitta o'quvchining qatorlari shu sinf hisobotida QAYSI egalarda turishi
    /// mumkin: sinfning o'zi + shu bolaning faol guruhlari. Pivot hisobot
    /// katakni shu ro'yxat bo'ylab yig'adi.
    ///
    /// <para>
    /// Sinf id'si ATAYLAB kalitda qoladi: uni tashlab yuborish sinf
    /// ALMASHTIRGAN bolaning eski sinfdagi baholarini yangi sinf katagiga
    /// qo'shib yuborardi — ya'ni o'chirgich o'chiq turganda ham raqam
    /// siljirdi (fayl boshidagi "NIMA QILMAYDI").
    /// </para>
    /// </summary>
    public List<string> OwnerIdsForStudent(string studentId, string classId)
    {
        var ids = new List<string> { classId };
        foreach (var g in GroupsOf(studentId)) ids.Add(g.Id);
        return ids;
    }

    /// <summary>Shu sinfning o'quvchilari (nomi bo'yicha) — <c>class_name</c> hali ham manba.</summary>
    public static IEnumerable<string> StudentIdsOf(SchoolClass cls, IEnumerable<Student> allStudents) =>
        allStudents.Where(s => s.ClassName == cls.Name).Select(s => s.Id);

    /// <summary>
    /// Shu o'quvchilarning guruhlari orqali qo'shiladigan FANLAR — hisobot
    /// ustunlari to'plamini kengaytirish uchun (guruhning jadvali hali
    /// tuzilmagan bo'lsa ham fan ustun bo'lishi kerak).
    /// </summary>
    public List<string> GroupSubjectsOf(IEnumerable<string> studentIds)
    {
        var subjects = new List<string>();
        if (!HasGroups) return subjects;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var studentId in studentIds)
            foreach (var g in GroupsOf(studentId))
                if (!string.IsNullOrEmpty(g.SubjectId) && seen.Add(g.SubjectId!))
                    subjects.Add(g.SubjectId!);
        return subjects;
    }

    /// <summary>
    /// O'quvchining qatorlari qaysi egalarda turadi: sinfi + faol guruhlari.
    /// Shaxsiy hisobot va portal so'rovlari shu ro'yxat bo'yicha kengayadi.
    ///
    /// <para>Bo'sh ro'yxat = "ega topilmadi" (sinfsiz, guruhsiz bola).</para>
    /// </summary>
    public static List<string> OwnerIdsFor(string? classId, IReadOnlyList<LessonOwner> groups)
    {
        var ids = new List<string>();
        if (!string.IsNullOrEmpty(classId)) ids.Add(classId);
        foreach (var g in groups) ids.Add(g.Id);
        return ids;
    }
}

using Microsoft.EntityFrameworkCore;
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
//  1. O'quvchining qatori HAR DOIM uning BUGUNGI sinf rahbarligidagi sinfi
//     ostida sanaladi — darsning egasi sinfmi yoki guruhmi, farqi yo'q. Guruh
//     sinf, parallel yoki maktab hisobotida O'Z QATORI bo'lib CHIQMAYDI
//     (EduSchool ham sinf va guruhni bir xil hisobotda yonma-yon ko'rsatadi —
//     §2.1.1 "Group in reports").
//  2. Har qator BIR MARTA sanaladi — va HECH BO'LMAGANDA bir marta. Baho — bu
//     bitta `journal_entries` qatori va uning BITTA egasi bor, shuning uchun
//     guruh bahosi sinf bahosining USTIGA qo'shiladi, o'rnini bosmaydi:
//     bolaning ingliz tili o'rtachasi sinfdagi va guruhdagi baholarining
//     birgalikdagi o'rtachasi bo'ladi. Guruh darsi sinf darsini BEKOR
//     QILMAYDI (Q2 faqat vaqt to'qnashuvini taqiqlaydi, §2.1.4).
//  3. Qator o'quvchiga tegishli bo'lsa — u o'sha eganing (sinf yoki guruh)
//     A'ZOSI bo'lganda. Guruhni bir nechta sinf boqadi, shuning uchun 5-A ning
//     hisobotida guruhning 5-B lik bolasi ko'rinmasligi kerak; xuddi shunday,
//     boshqa sinfdan O'TGAN bolaning eski sinfi so'rov qamroviga kirsa ham,
//     o'sha sinfning darslari BOSHQA bolalarning maxrajiga kirmasligi kerak.
//     Buni <see cref="CountsFor"/> ushlaydi.
//  4. Davomat maxraji (o'tilgan darslar) ham shu qoida bo'yicha: bolaning
//     sinf darslari + guruhlari darslari. Guruh darsida sinf ichidagi
//     bo'linish (SubGroup) yo'q — guruhning o'zi allaqachon tanlangan
//     bolalar to'plami.
//  5. A'ZOLIK — TARIX, LAHZA EMAS. Qator o'quvchi uchun sanaladi, agar qator
//     YOZILGAN KUN uning o'sha egadagi a'zolik OYNASI ichida bo'lsa:
//         `joined_on <= qator sanasi <= (left_on ?? cheksiz)`.
//     Ya'ni guruhdan chiqqan bola o'sha guruhda olgan baholarini SAQLAB
//     QOLADI, sinf almashtirgan bola esa eski sinfida olgan baholarini YANGI
//     sinfi qatoriga olib o'tadi (1-band: qator bolaning BUGUNGI sinfi ostida
//     turadi). Chiqqan kundan KEYINGI darslar esa uning maxrajiga kirmaydi.
//
//  NEGA 5-BAND KERAK BO'LDI (va nega u o'chirgichdan MUSTAQIL)
//  -----------------------------------------------------------
//  Ilgari bu fayl a'zolikni LAHZA sifatida o'qirdi (`left_on is null`), va
//  `StudentReportBuilder` qatorlarni faqat BUGUNGI sinf id'si bo'yicha
//  filtrlardi. Natijada:
//    · guruhdan chiqqan bolaning o'sha guruhdagi baholari sinf hisobotidan
//      ham, shaxsiy hisobotidan ham YO'QOLARDI;
//    · sinf almashtirgan bolaning eski sinfdagi baholari esa umuman HECH
//      QAYERDA ko'rinmasdi — eski sinf ro'yxatida u yo'q (ro'yxat
//      `class_name` bo'yicha), yangi sinf esa eski qatorlarni ko'rmasdi.
//  Ikkinchisi 2-bandning "har qator bir marta sanaladi" qoidasini "hech
//  qachon sanalmaydi" tomonga buzardi. Tuzatish IKKALASI uchun bitta va u
//  `school_meta.group_lessons_enabled` dan MUSTAQIL: sinf tarixi guruhlardan
//  oldin ham bor edi, shuning uchun o'chirgich O'CHIQ turganda ham raqam
//  siljiydi. Bu ataylab: cut-over qoidasi "guruh darslari ko'rinmasin" degan,
//  "sinf tarixi yo'qolsin" degan emas.
//
//  QUYI CHEGARA (`joined_on`) — FAQAT AJRATISH UCHUN
//  -------------------------------------------------
//  Sinf oynasining quyi chegarasi bitta ishni qiladi: IKKI sinf bitta kunni
//  da'vo qilmasin. 1-oktyabrda 5-B dan 5-A ga o'tgan bola sentyabrda FAQAT
//  5-B da o'qigan — 5-A ning sentyabrdagi darslari uning maxrajiga
//  kirmasligi kerak, aks holda o'sha kunlar IKKI marta sanalardi.
//  Shuning uchun quyi chegara SINF ALMASHTIRGAN bolada qo'llanadi;
//  BIR sinfdan boshqasiga o'tmagan bolada esa oyna butunlay ochiq qoladi —
//  ya'ni uning raqami bu o'zgarishdan MUTLAQO siljimaydi (a'zolik yozuvidagi
//  `joined_on` ko'pchilik uchun migratsiya backfill'idan kelgan
//  `enrollment_date`, unga tayanish esa yangi raqam siljishini keltirardi).
//  Guruhda quyi chegara HAR DOIM ishlaydi: guruh a'zoligi backfill'dan emas,
//  `StudyGroupService` dan keladi va sanasi aniq.
//
//  O'CHIRGICH O'CHIQ = GURUHSIZ RAQAM
//  ----------------------------------
//  `school_meta.group_lessons_enabled` o'chiq ekan guruh qamrovi BO'SH:
//  <see cref="GroupOwnerIds"/> — bo'sh, <see cref="GroupsOf"/> — bo'sh,
//  <see cref="CountsFor"/> har qanday guruh qatorini RAD etadi. Qolgani —
//  sinf va uning tarixi. `ClassAttainmentTests` va `MembershipHistoryTests`
//  ikkala holatni ham tekshiradi.
//
//  QAMROV SO'ROVNI KENGAYTIRADI, FILTR ESA TORAYTIRADI
//  ---------------------------------------------------
//  `OwnerIds*` metodlari `where class_id in (...)` uchun ro'yxat beradi —
//  ular KENG bo'lishi kerak (kerakli qator umuman o'qilmay qolmasin).
//  Qaysi qator kimga tegishli ekanini esa FAQAT <see cref="CountsFor"/>
//  hal qiladi. Shuning uchun keng qamrov xavfsiz: begona sinfning darsi
//  so'rovga tushsa ham, u boshqa bolaning maxrajiga QO'SHILMAYDI.
// ===========================================================================

/// <summary>
/// "Kimning qatori qaysi sinf ostida sanaladi?" — yagona javob beruvchi.
/// Fayl boshidagi izohda qoida to'liq yozilgan.
/// </summary>
public sealed class ClassAttainment
{
    private static readonly IReadOnlyList<LessonOwner> NoGroups = [];

    /// <summary>
    /// A'zolik oynasi. Sanalar ISO <c>yyyy-MM-dd</c> — jurnal qatorlaridagi
    /// bilan bir xil shakl, shuning uchun ordinal solishtirish yetadi.
    /// </summary>
    /// <param name="From">Quyi chegara (<c>joined_on</c>); null = chegarasiz.</param>
    /// <param name="To">Yuqori chegara (<c>left_on</c>); null = a'zolik ochiq.</param>
    private readonly record struct Window(string? From, string? To)
    {
        public bool Covers(string? date) =>
            date is null
            || ((From is null || string.CompareOrdinal(date, From) >= 0)
                && (To is null || string.CompareOrdinal(date, To) <= 0));

        /// <summary>Ikki oynaning BIRLASHMASINI qamrab oluvchi eng tor oyna.</summary>
        public Window Widen(Window other) => new(
            From is null || other.From is null
                ? null
                : (string.CompareOrdinal(From, other.From) <= 0 ? From : other.From),
            To is null || other.To is null
                ? null
                : (string.CompareOrdinal(To, other.To) >= 0 ? To : other.To));

        public Window WithoutLowerBound() => new(null, To);
    }

    private readonly Dictionary<string, Dictionary<string, Window>> _classWindows;
    private readonly Dictionary<string, Dictionary<string, Window>> _groupWindows;
    private readonly Dictionary<string, List<LessonOwner>> _groupsByStudent;

    private ClassAttainment(
        Dictionary<string, Dictionary<string, Window>> classWindows,
        Dictionary<string, List<LessonOwner>> groupsByStudent,
        Dictionary<string, Dictionary<string, Window>> groupWindows)
    {
        _classWindows = classWindows;
        _groupsByStudent = groupsByStudent;
        _groupWindows = groupWindows;
        GroupOwnerIds = [.. groupsByStudent.Values.SelectMany(x => x).Select(o => o.Id)
            .Distinct(StringComparer.Ordinal)];
        PastClassIds = [.. classWindows.Values
            .SelectMany(w => w.Where(kv => kv.Value.To is not null).Select(kv => kv.Key))
            .Distinct(StringComparer.Ordinal)];
    }

    /// <summary>Qamrovdagi BARCHA guruh id'lari (so'rovni kengaytirish uchun).</summary>
    public IReadOnlyList<string> GroupOwnerIds { get; }

    /// <summary>
    /// Qamrovdagi TARK ETILGAN sinf id'lari — 5-band. So'rov shu sinflarni ham
    /// o'qishi kerak, aks holda o'tib ketgan bolaning eski baholari umuman
    /// yuklanmasdi.
    /// </summary>
    public IReadOnlyList<string> PastClassIds { get; }

    /// <summary>Guruh darsi umuman bormi — o'chirgich o'chiq bo'lsa har doim <c>false</c>.</summary>
    public bool HasGroups => GroupOwnerIds.Count > 0;

    /// <summary>Sinf a'zoligi tarixi bormi (kimdir sinf almashtirganmi).</summary>
    public bool HasClassHistory => PastClassIds.Count > 0;

    /// <summary>Qamrov sinfning o'zidan kengmi.</summary>
    public bool HasCoverage => HasGroups || HasClassHistory;

    /// <summary>
    /// Butun maktab uchun qamrov — BITTA yurishda (sikl ichida so'rov yo'q).
    /// Guruh qismi faqat o'chirgich yoqilganda to'ldiriladi.
    /// </summary>
    public static async Task<ClassAttainment> BuildAsync(
        IAppDbContext db, CancellationToken ct = default)
    {
        var classWindows = await ClassWindowsAsync(db, studentId: null, ct);

        var groupsByStudent = new Dictionary<string, List<LessonOwner>>(StringComparer.Ordinal);
        var groupWindows = new Dictionary<string, Dictionary<string, Window>>(StringComparer.Ordinal);

        if (await LessonRoster.GroupLessonsEnabledAsync(db, ct))
        {
            // ARXIVLANGAN GURUH HAM KIRADI — 5-band. Guruhni arxivlash
            // (`StudyGroupService.ArchiveAsync`) faol a'zoliklarni YOPADI, ya'ni
            // tarix a'zolik oynasida turadi. Arxivlangan guruhni qamrovdan
            // tashlab yuborish o'sha oynani ham yo'q qilardi va bola bir yilda
            // olgan baholaridan ayrilardi (o'quv yilini yakunlash HAR guruhni
            // arxivlaydi — `AcademicYearController.Rollover`).
            var byId = (await db.StudyGroups.AsNoTracking()
                    .Select(g => new { g.Id, g.Name, g.SubjectId }).ToListAsync(ct))
                .ToDictionary(
                    g => g.Id,
                    g => new LessonOwner(LessonOwnerKind.Group, g.Id.ToString(), g.Name, g.SubjectId));

            if (byId.Count > 0)
            {
                var members = await db.StudyGroupMembers.AsNoTracking()
                    .Select(m => new { m.GroupId, m.StudentId, m.JoinedOn, m.LeftOn }).ToListAsync(ct);
                foreach (var m in members)
                {
                    if (!byId.TryGetValue(m.GroupId, out var owner)) continue;
                    Remember(groupWindows, m.StudentId, owner.Id, new Window(Iso(m.JoinedOn), Iso(m.LeftOn)));
                    if (!groupsByStudent.TryGetValue(m.StudentId, out var list))
                        groupsByStudent[m.StudentId] = list = [];
                    if (!list.Any(o => o.Id == owner.Id)) list.Add(owner);
                }
            }
        }

        return new ClassAttainment(classWindows, groupsByStudent, groupWindows);
    }

    /// <summary>
    /// Bitta o'quvchi uchun qamrov — portal va shaxsiy hisobot uchun
    /// (maktab bo'ylab yurish shart emas).
    /// </summary>
    public static async Task<ClassAttainment> ForStudentAsync(
        IAppDbContext db, Student student, CancellationToken ct = default)
    {
        var classWindows = await ClassWindowsAsync(db, student.Id, ct);

        var groupsByStudent = new Dictionary<string, List<LessonOwner>>(StringComparer.Ordinal);
        var groupWindows = new Dictionary<string, Dictionary<string, Window>>(StringComparer.Ordinal);

        if (await LessonRoster.GroupLessonsEnabledAsync(db, ct))
        {
            var members = await db.StudyGroupMembers.AsNoTracking()
                .Where(m => m.StudentId == student.Id)
                .Select(m => new { m.GroupId, m.JoinedOn, m.LeftOn }).ToListAsync(ct);
            if (members.Count > 0)
            {
                var ids = members.Select(m => m.GroupId).Distinct().ToList();
                var byId = (await db.StudyGroups.AsNoTracking()
                        .Where(g => ids.Contains(g.Id))
                        .Select(g => new { g.Id, g.Name, g.SubjectId }).ToListAsync(ct))
                    .ToDictionary(
                        g => g.Id,
                        g => new LessonOwner(LessonOwnerKind.Group, g.Id.ToString(), g.Name, g.SubjectId));

                var list = new List<LessonOwner>();
                foreach (var m in members)
                {
                    if (!byId.TryGetValue(m.GroupId, out var owner)) continue;
                    Remember(groupWindows, student.Id, owner.Id, new Window(Iso(m.JoinedOn), Iso(m.LeftOn)));
                    if (!list.Any(o => o.Id == owner.Id)) list.Add(owner);
                }
                if (list.Count > 0) groupsByStudent[student.Id] = list;
            }
        }

        return new ClassAttainment(classWindows, groupsByStudent, groupWindows);
    }

    /// <summary>
    /// O'quvchining guruhlari — FAOL VA TARK ETILGANLARI (5-band, ega ko'rinishida).
    /// O'chirgich o'chiq bo'lsa har doim bo'sh.
    /// </summary>
    public IReadOnlyList<LessonOwner> GroupsOf(string studentId) =>
        _groupsByStudent.TryGetValue(studentId, out var list) ? list : NoGroups;

    /// <summary>
    /// QOIDANING O'ZI: shu qator (baho, chorak bahosi yoki o'tilgan dars) shu
    /// o'quvchi uchun sanaladimi.
    ///
    /// <para>
    /// Javob — a'zolik OYNASI (5-band): o'quvchi shu eganing a'zosi bo'lgan va
    /// <paramref name="date"/> kuni oyna ichida bo'lgan. <paramref name="date"/>
    /// berilmasa (chorak bahosida sana yo'q) faqat a'zolikning O'ZI tekshiriladi.
    /// </para>
    /// <para>
    /// <b>Sinfi noma'lum o'quvchi</b> (<c>class_name</c> hech bir sinfga tushmagan
    /// va a'zolik yozuvi ham yo'q) uchun sinf qatori HAR DOIM sanaladi — bu
    /// bugungi xatti-harakat va uni saqlaymiz: bunday bola baribir birorta
    /// sinf ro'yxatiga tushmaydi, shaxsiy hisoboti esa qamrovsiz quriladi.
    /// </para>
    /// </summary>
    public bool CountsFor(string studentId, string ownerId, string ownerKind, string? date = null)
    {
        if (ownerKind == LessonOwnerKind.Group)
            return _groupWindows.TryGetValue(studentId, out var groups)
                   && groups.TryGetValue(ownerId, out var groupWindow)
                   && groupWindow.Covers(date);

        if (!_classWindows.TryGetValue(studentId, out var classes)) return true;
        return classes.TryGetValue(ownerId, out var window) && window.Covers(date);
    }

    /// <summary>
    /// So'rov kengaytmasi: berilgan sinf id'lari + qamrovdagi barcha guruh va
    /// tark etilgan sinf id'lari. `class_id` ustuni hammasini saqlaydi
    /// (§2.1.4), shuning uchun bitta `Contains` yetadi.
    ///
    /// <para>Qamrov bo'sh bo'lsa natija — kiritilgan ro'yxatning o'zi.</para>
    /// </summary>
    public List<string> OwnerIds(IEnumerable<string> classIds)
    {
        var ids = new List<string>(classIds);
        var seen = ids.ToHashSet(StringComparer.Ordinal);
        foreach (var id in GroupOwnerIds) if (seen.Add(id)) ids.Add(id);
        foreach (var id in PastClassIds) if (seen.Add(id)) ids.Add(id);
        return ids;
    }

    /// <summary>
    /// Bitta sinf uchun so'rov kengaytmasi: sinf id'si + shu sinf
    /// o'quvchilarining guruhlari va ULAR TARK ETGAN sinflari. Maktab bo'yicha
    /// hisobotda har sinfga faqat o'ziga tegishli egalar qo'shiladi.
    /// </summary>
    public List<string> OwnerIdsForClass(string classId, IEnumerable<string> studentIds)
    {
        var ids = new List<string> { classId };
        if (!HasCoverage) return ids;

        var seen = new HashSet<string>(StringComparer.Ordinal) { classId };
        foreach (var studentId in studentIds)
        {
            foreach (var g in GroupsOf(studentId))
                if (seen.Add(g.Id)) ids.Add(g.Id);
            if (_classWindows.TryGetValue(studentId, out var windows))
                foreach (var kv in windows)
                    if (kv.Value.To is not null && seen.Add(kv.Key)) ids.Add(kv.Key);
        }
        return ids;
    }

    /// <summary>
    /// Bitta o'quvchining qatorlari QAYSI egalarda turishi mumkin: berilgan
    /// sinf + shu bolaning barcha sinf va guruh a'zoliklari (tarix bilan).
    /// Pivot hisobot katakni shu ro'yxat bo'ylab yig'adi, shaxsiy hisobot esa
    /// so'rovni shu bo'yicha kengaytiradi.
    ///
    /// <para>
    /// Bo'sh ro'yxat = "ega topilmadi" (sinfsiz, guruhsiz, tarixsiz bola) —
    /// chaqiruvchi bunday holatda qamrovsiz ishlaydi.
    /// </para>
    /// </summary>
    public List<string> OwnerIdsForStudent(string studentId, string? classId)
    {
        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(classId) && seen.Add(classId)) ids.Add(classId);
        if (_classWindows.TryGetValue(studentId, out var windows))
            foreach (var kv in windows)
                if (seen.Add(kv.Key)) ids.Add(kv.Key);
        foreach (var g in GroupsOf(studentId))
            if (seen.Add(g.Id)) ids.Add(g.Id);
        return ids;
    }

    /// <summary>Shu sinfning o'quvchilari (nomi bo'yicha) — <c>class_name</c> hali ham manba.</summary>
    public static IEnumerable<string> StudentIdsOf(SchoolClass cls, IEnumerable<Student> allStudents) =>
        allStudents.Where(s => s.ClassName == cls.Name).Select(s => s.Id);

    /// <summary>
    /// Shu o'quvchilarning guruhlari orqali qo'shiladigan FANLAR — hisobot
    /// ustunlari to'plamini kengaytirish uchun (guruhning jadvali hali
    /// tuzilmagan bo'lsa ham, yoki bola guruhdan chiqib ketgan bo'lsa ham,
    /// fan ustun bo'lishi kerak — aks holda baho tushadigan katak yo'q).
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

    // ------------------------------------------------------------------
    //  Ichki yordamchilar
    // ------------------------------------------------------------------

    /// <summary>
    /// Sinf a'zoligi oynalari. IKKI manbadan yig'iladi:
    /// <list type="number">
    ///   <item><c>class_memberships</c> → TARIX (yopilgan yozuvlar ham), sanalari
    ///     bilan;</item>
    ///   <item><c>students.class_name</c> → sinf id'si — a'zolik yozuvi TOPILMAGAN
    ///     sinf uchun CHEGARASIZ oyna. <c>class_name</c> haqiqat manbai (§2.1.4),
    ///     shuning uchun backfill qamramagan bola (nomi sinfga mos tushmagan yoki
    ///     keyin nomi o'zgargan) o'z sinfining qatorlarini yo'qotmaydi.</item>
    /// </list>
    /// <para>
    /// So'ngra QUYI CHEGARA olib tashlanadi — agar o'quvchining BITTAGINA sinfi
    /// bo'lsa (fayl boshidagi "QUYI CHEGARA" izohi): sinf almashtirmagan bolaning
    /// raqami bu o'zgarishdan siljimasligi kerak.
    /// </para>
    /// </summary>
    private static async Task<Dictionary<string, Dictionary<string, Window>>> ClassWindowsAsync(
        IAppDbContext db, string? studentId, CancellationToken ct)
    {
        var windows = new Dictionary<string, Dictionary<string, Window>>(StringComparer.Ordinal);

        var membershipsQuery = db.ClassMemberships.AsNoTracking();
        if (studentId is not null) membershipsQuery = membershipsQuery.Where(m => m.StudentId == studentId);
        foreach (var m in await membershipsQuery
                     .Select(m => new { m.StudentId, m.ClassId, m.JoinedOn, m.LeftOn }).ToListAsync(ct))
            Remember(windows, m.StudentId, m.ClassId, new Window(Iso(m.JoinedOn), Iso(m.LeftOn)));

        var classIdByName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var c in await db.Classes.AsNoTracking()
                     .Select(c => new { c.Id, c.Name }).ToListAsync(ct))
            classIdByName.TryAdd(c.Name, c.Id);

        var studentsQuery = db.Students.AsNoTracking();
        if (studentId is not null) studentsQuery = studentsQuery.Where(s => s.Id == studentId);
        foreach (var s in await studentsQuery.Select(s => new { s.Id, s.ClassName }).ToListAsync(ct))
        {
            if (string.IsNullOrEmpty(s.ClassName)) continue;
            if (!classIdByName.TryGetValue(s.ClassName, out var id)) continue;
            if (windows.TryGetValue(s.Id, out var byOwner) && byOwner.ContainsKey(id)) continue;
            Remember(windows, s.Id, id, new Window(null, null));
        }

        foreach (var byOwner in windows.Values)
        {
            if (byOwner.Count != 1) continue;
            var only = byOwner.Keys.First();
            byOwner[only] = byOwner[only].WithoutLowerBound();
        }

        return windows;
    }

    /// <summary>Oynani yozadi; bir ega uchun bir nechta yozuv bo'lsa ENG KENG oyna qoladi.</summary>
    private static void Remember(
        Dictionary<string, Dictionary<string, Window>> windows,
        string studentId, string ownerId, Window window)
    {
        if (!windows.TryGetValue(studentId, out var byOwner))
            windows[studentId] = byOwner = new Dictionary<string, Window>(StringComparer.Ordinal);

        byOwner[ownerId] = byOwner.TryGetValue(ownerId, out var existing)
            ? existing.Widen(window)
            : window;
    }

    private static string? Iso(DateOnly? day) => day?.ToString("yyyy-MM-dd");

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd");
}

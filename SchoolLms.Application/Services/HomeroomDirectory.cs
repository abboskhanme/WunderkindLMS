using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// Kim qaysi sinfning rahbari — yagona manba (<see cref="ClassHomeroomTeacher"/>, 2026-10-09).
///
/// <para>
/// Bitta o'qituvchi bir nechta sinfga, bitta sinfga bir nechta o'qituvchi biriktirilishi mumkin.
/// Arxivdagi sinf va arxivdagi o'qituvchi rahbarlik ro'yxatlarida chiqmaydi (bog'lanish esa
/// saqlanadi — arxivdan qaytsa joyida). Yo'nalish guruhining rahbari — guruh o'qituvchisi
/// (<c>study_group_teachers</c>); u faqat matnda (<see cref="LabelsAsync"/>) qo'shiladi.
/// </para>
/// </summary>
public static class HomeroomDirectory
{
    /// <summary>O'qituvchi rahbar bo'lgan faol sinflar — daraja va nom tartibida.</summary>
    public static async Task<List<(string Id, string Name)>> ClassesOfAsync(
        IAppDbContext db, string teacherId, CancellationToken ct = default) =>
        [.. (await (from h in db.ClassHomeroomTeachers.AsNoTracking()
                    join c in db.Classes.AsNoTracking() on h.ClassId equals c.Id
                    where h.TeacherId == teacherId && !c.IsArchived
                    orderby c.Grade, c.Name
                    select new { c.Id, c.Name }).ToListAsync(ct))
            .Select(x => (x.Id, x.Name))];

    /// <summary>O'qituvchi rahbar bo'lgan faol sinflar nomlari (<c>students.class_name</c> bilan solishtirish uchun).</summary>
    public static async Task<HashSet<string>> ClassNamesOfAsync(
        IAppDbContext db, string teacherId, CancellationToken ct = default) =>
        (await ClassesOfAsync(db, teacherId, ct)).Select(c => c.Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// "Sinf rahbarligi" matni har o'qituvchi uchun: rahbarlik sinflari (daraja, nom), keyin u
    /// sinf rahbari bo'lgan yo'nalish guruhlari — "5-A, 6-B, Aniq fanlar". Rahbarligi yo'q
    /// o'qituvchi lug'atda yo'q. <paramref name="teacherIds"/> null — hammasi.
    /// </summary>
    public static async Task<Dictionary<string, string>> LabelsAsync(
        IAppDbContext db, IReadOnlyCollection<string>? teacherIds = null, CancellationToken ct = default)
    {
        var classes = from h in db.ClassHomeroomTeachers.AsNoTracking()
                      join c in db.Classes.AsNoTracking() on h.ClassId equals c.Id
                      where !c.IsArchived
                      select new { h.TeacherId, c.Name, c.Grade };
        var tracks = from gt in db.StudyGroupTeachers.AsNoTracking()
                     join g in db.StudyGroups.AsNoTracking() on gt.GroupId equals g.Id
                     where g.IsTrack && !g.IsArchived
                     select new { gt.TeacherId, g.Name };
        if (teacherIds is not null)
        {
            classes = classes.Where(x => teacherIds.Contains(x.TeacherId));
            tracks = tracks.Where(x => teacherIds.Contains(x.TeacherId));
        }

        var names = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var c in (await classes.ToListAsync(ct)).OrderBy(c => c.Grade).ThenBy(c => c.Name, StringComparer.Ordinal))
            Add(names, c.TeacherId, c.Name);
        foreach (var g in (await tracks.ToListAsync(ct)).OrderBy(g => g.Name, StringComparer.Ordinal))
            Add(names, g.TeacherId, g.Name);
        return names.ToDictionary(kv => kv.Key, kv => string.Join(", ", kv.Value.Distinct(StringComparer.Ordinal)),
            StringComparer.Ordinal);
    }

    /// <summary>Bitta o'qituvchining matni (<see cref="LabelsAsync"/>); rahbarligi yo'q — bo'sh.</summary>
    public static async Task<string> LabelAsync(IAppDbContext db, string teacherId, CancellationToken ct = default) =>
        (await LabelsAsync(db, [teacherId], ct)).GetValueOrDefault(teacherId, "");

    /// <summary>
    /// Ro'yxat qaytaradigan endpoint'lar uchun: har o'qituvchining <see cref="Teacher.HomeroomClass"/>
    /// xususiyatiga matn qo'yiladi. Faqat KUZATILMAYDIGAN (AsNoTracking) yoki saqlab bo'lingan
    /// obyektlarga — bu qiymat bazaga yozilmasin.
    /// </summary>
    public static async Task FillLabelsAsync(IAppDbContext db, IReadOnlyCollection<Teacher> teachers, CancellationToken ct = default)
    {
        if (teachers.Count == 0) return;
        var labels = await LabelsAsync(db, teachers.Count > 200 ? null : [.. teachers.Select(t => t.Id)], ct);
        foreach (var t in teachers) t.HomeroomClass = labels.GetValueOrDefault(t.Id, "");
    }

    /// <summary>Sinfning faol rahbarlari — ism tartibida.</summary>
    public static async Task<List<HomeroomTeacherDto>> TeachersOfClassAsync(
        IAppDbContext db, string classId, CancellationToken ct = default) =>
        await (from h in db.ClassHomeroomTeachers.AsNoTracking()
               join t in db.Teachers.AsNoTracking() on h.TeacherId equals t.Id
               where h.ClassId == classId && !t.IsArchived
               orderby t.FullName
               select new HomeroomTeacherDto(t.Id, t.FullName)).ToListAsync(ct);

    /// <summary>Sinf NOMI bo'yicha faol rahbarlar — o'quvchi (<c>students.class_name</c>) tomonidan qidirish uchun.</summary>
    public static async Task<List<Teacher>> TeachersOfClassNameAsync(
        IAppDbContext db, string className, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(className)) return [];
        return await (from h in db.ClassHomeroomTeachers.AsNoTracking()
                      join c in db.Classes.AsNoTracking() on h.ClassId equals c.Id
                      join t in db.Teachers.AsNoTracking() on h.TeacherId equals t.Id
                      where c.Name == className && !c.IsArchived && !t.IsArchived
                      orderby t.FullName
                      select t).ToListAsync(ct);
    }

    /// <summary>"Rahbarlar matni" sinf nomi bo'yicha: "Aliyeva A., Boboyev B."; rahbarsiz — bo'sh.</summary>
    public static async Task<string> NamesOfClassNameAsync(IAppDbContext db, string className, CancellationToken ct = default) =>
        string.Join(", ", (await TeachersOfClassNameAsync(db, className, ct)).Select(t => t.FullName));

    /// <summary>Faol sinflar: sinf id'si → faol rahbarlar (ism tartibida).</summary>
    public static async Task<Dictionary<string, List<HomeroomTeacherDto>>> TeachersByClassIdAsync(
        IAppDbContext db, CancellationToken ct = default) =>
        (await (from h in db.ClassHomeroomTeachers.AsNoTracking()
                join c in db.Classes.AsNoTracking() on h.ClassId equals c.Id
                join t in db.Teachers.AsNoTracking() on h.TeacherId equals t.Id
                where !c.IsArchived && !t.IsArchived
                orderby t.FullName
                select new { h.ClassId, t.Id, t.FullName }).ToListAsync(ct))
            .GroupBy(x => x.ClassId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(x => new HomeroomTeacherDto(x.Id, x.FullName)).ToList(),
                StringComparer.Ordinal);

    /// <summary>Faol sinflar: sinf NOMI → rahbarlar matni ("A, B").</summary>
    public static async Task<Dictionary<string, string>> NamesByClassNameAsync(
        IAppDbContext db, CancellationToken ct = default)
    {
        var byId = await TeachersByClassIdAsync(db, ct);
        var names = await db.Classes.AsNoTracking().Where(c => !c.IsArchived)
            .Select(c => new { c.Id, c.Name }).ToListAsync(ct);
        return names.Where(c => byId.ContainsKey(c.Id))
            .GroupBy(c => c.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key,
                g => string.Join(", ", g.SelectMany(c => byId[c.Id]).Select(t => t.FullName).Distinct(StringComparer.Ordinal)),
                StringComparer.Ordinal);
    }

    /// <summary>
    /// Sinf rahbarlarini almashtiradi: ro'yxatda yo'qlari bo'shatiladi, yangilari qo'shiladi.
    /// Boshqa sinflardagi rahbarlikka TEGILMAYDI. Saqlashni chaqiruvchi qiladi.
    /// </summary>
    /// <returns>Qo'shilgan va bo'shatilgan o'qituvchilar id'lari.</returns>
    public static async Task<(List<string> Added, List<string> Removed)> SetForClassAsync(
        IAppDbContext db, string classId, IEnumerable<string> teacherIds, CancellationToken ct = default)
    {
        var want = teacherIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.Ordinal);
        var now = await db.ClassHomeroomTeachers.Where(h => h.ClassId == classId).ToListAsync(ct);
        var removed = now.Where(h => !want.Contains(h.TeacherId)).ToList();
        db.ClassHomeroomTeachers.RemoveRange(removed);
        var have = now.Select(h => h.TeacherId).ToHashSet(StringComparer.Ordinal);
        var added = want.Where(id => !have.Contains(id)).OrderBy(id => id, StringComparer.Ordinal).ToList();
        foreach (var id in added)
            db.ClassHomeroomTeachers.Add(new ClassHomeroomTeacher { ClassId = classId, TeacherId = id, CreatedAt = AppClock.NowInstant });
        return (added, [.. removed.Select(h => h.TeacherId)]);
    }

    /// <summary>
    /// O'qituvchining rahbarlik sinflarini almashtiradi (o'qituvchi formasi). Boshqa
    /// o'qituvchilarning shu sinflardagi rahbarligiga TEGILMAYDI. Saqlashni chaqiruvchi qiladi.
    /// </summary>
    public static async Task SetForTeacherAsync(
        IAppDbContext db, string teacherId, IEnumerable<string> classIds, CancellationToken ct = default)
    {
        var want = classIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.Ordinal);
        var now = await db.ClassHomeroomTeachers.Where(h => h.TeacherId == teacherId).ToListAsync(ct);
        db.ClassHomeroomTeachers.RemoveRange(now.Where(h => !want.Contains(h.ClassId)));
        var have = now.Select(h => h.ClassId).ToHashSet(StringComparer.Ordinal);
        foreach (var id in want.Where(id => !have.Contains(id)))
            db.ClassHomeroomTeachers.Add(new ClassHomeroomTeacher { ClassId = id, TeacherId = teacherId, CreatedAt = AppClock.NowInstant });
    }

    private static void Add(Dictionary<string, List<string>> names, string teacherId, string name)
    {
        if (!names.TryGetValue(teacherId, out var list)) names[teacherId] = list = [];
        list.Add(name);
    }
}

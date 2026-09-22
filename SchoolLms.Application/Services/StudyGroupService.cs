using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  O'quv guruhlari — docs/modules/students-parity.md §2.1, G-6.
//
//  NIMA QILADI VA NIMA QILMAYDI
//  ----------------------------
//  QILADI: guruh yaratadi/tahrirlaydi/arxivlaydi/nusxalaydi, ro'yxatni
//  yuritadi (qo'shish, sabab bilan chiqarish, SHU FAN ichida o'tkazish).
//  QILMAYDI: dars jadvaliga, jurnalga, davomatga, maoshga — HECH NARSAGA
//  tegmaydi. `school_meta.group_lessons_enabled` O'CHIQ va bu slice uni
//  yoqmaydi (§4.2): guruh darslari alohida slice'ning (G-11..G-18) ishi.
//
//  BAZA QOIDANI USHLAYDI, XIZMAT ESA TUSHUNTIRADI
//  ----------------------------------------------
//  "Bitta o'quvchi — bitta fandan ko'pi bilan bitta faol guruh" qoidasi
//  `ux_group_members_one_group_per_subject` qisman unikal indeksida yozilgan.
//  Baza uni buzilishga qo'ymaydi, lekin foydalanuvchiga 23505 chiqaradi.
//  Shuning uchun xizmat AVVAL o'zi tekshiradi va o'zbekcha xabar beradi;
//  indeks esa poyga (ikki admin bir vaqtda) holatida oxirgi to'siq bo'lib
//  qoladi — chaqiruvchi <see cref="IsOneGroupPerSubjectViolation"/> bilan uni
//  ham o'sha xabarga o'giradi.
//
//  RO'YXAT MANBAI — `students.class_name`
//  --------------------------------------
//  Nomzodlar ro'yxati o'quvchini AYNAN bugungi haqiqat manbaidan topadi
//  (§2.1.2): `students.class_name == classes.name`. `class_memberships`
//  backfill'i faqat nomi mos tushganlarni qamragan, ya'ni unga tayanish
//  ba'zi bolalarni ro'yxatdan jimgina yo'qotardi.
// ===========================================================================

/// <summary>
/// Guruh va uning ro'yxati bilan bog'liq barcha qoidalar. Metodlar xato matnini
/// (o'zbekcha) qaytaradi; <c>null</c> — muvaffaqiyat.
/// </summary>
public sealed class StudyGroupService(IAppDbContext db)
{
    /// <summary>StudyGroupModel.cs dagi qisman unikal indeks nomlari.</summary>
    public const string OneGroupPerSubjectIndex = "ux_group_members_one_group_per_subject";
    public const string OneActiveInGroupIndex = "ux_group_members_one_active";
    public const string NameActiveIndex = "ux_study_groups_name_active";

    public const string NameRequiredMessage = "Guruh nomini yozing (kamida 3 belgi)";
    public const string SubjectRequiredMessage = "Guruh fanini tanlang";
    public const string SubjectNotFoundMessage = "Fan topilmadi";
    public const string ClassesRequiredMessage = "Guruhni boqadigan kamida bitta sinf tanlang";
    public const string TeachersRequiredMessage = "Guruhga kamida bitta o'qituvchi tanlang";
    public const string ArchivedGroupMessage = "Arxivlangan guruhni o'zgartirib bo'lmaydi";

    public const string DuplicateNameMessage =
        "Shu fan bo'yicha bunday nomli faol guruh allaqachon bor — boshqa nom tanlang.";

    public const string OneGroupPerSubjectMessage =
        "O'quvchi shu fan bo'yicha allaqachon boshqa guruhda. Bitta fandan faqat bitta "
        + "guruhda bo'lish mumkin — avval eski guruhdan chiqaring yoki \"O'tkazish\" "
        + "amalidan foydalaning.";

    public const string NotFedByClassMessage =
        "O'quvchining sinfi bu guruhni boqmaydi — uni guruhga qo'shib bo'lmaydi.";

    public const string GenderMismatchMessage =
        "Guruh jins bo'yicha cheklangan — bu o'quvchi unga to'g'ri kelmaydi.";

    public const string TransferSameSubjectMessage =
        "O'quvchini faqat AYNAN SHU FANDAGI boshqa guruhga o'tkazish mumkin.";

    public const string TransferSameGroupMessage = "O'quvchi allaqachon shu guruhda";

    /// <summary>Guruh nomining eng qisqa uzunligi (§2.1.1 formasi: min 3).</summary>
    private const int MinNameLength = 3;

    /* -------------------------------------------------------------------
     *  1. Guruhning o'zi
     * ---------------------------------------------------------------- */

    /// <summary>
    /// Saqlash so'rovini tekshiradi: fan bor va guruhli, nom bo'sh emas va shu
    /// fan ichida takrorlanmaydi, sinf va o'qituvchi ro'yxati to'g'ri.
    /// </summary>
    public async Task<string?> ValidateAsync(
        SaveStudyGroupRequest req, Guid? excludeGroupId, CancellationToken ct = default)
    {
        var name = (req.Name ?? "").Trim();
        if (name.Length < MinNameLength) return NameRequiredMessage;
        if (string.IsNullOrWhiteSpace(req.SubjectId)) return SubjectRequiredMessage;

        var subject = await db.Subjects.FirstOrDefaultAsync(s => s.Id == req.SubjectId, ct);
        if (subject is null) return SubjectNotFoundMessage;
        // "Guruhlarga bo'linadi" talabi olib tashlandi (mijoz, 2026-09-23: "istalgan fanni
        // guruhga biriktirish mumkin"). `subjects.is_groupable` ustuni qoladi, lekin tekshirilmaydi.

        if (req.Gender is not null and not "male" and not "female")
            return "Jins qiymati noto'g'ri (male yoki female)";

        var classIds = Distinct(req.ClassIds);
        if (classIds.Count == 0) return ClassesRequiredMessage;
        var foundClasses = await db.Classes.CountAsync(c => classIds.Contains(c.Id), ct);
        if (foundClasses != classIds.Count) return "Tanlangan sinflardan biri topilmadi";

        var teacherIds = Distinct(req.TeacherIds);
        if (teacherIds.Count == 0) return TeachersRequiredMessage;
        var foundTeachers = await db.Teachers.CountAsync(t => teacherIds.Contains(t.Id), ct);
        if (foundTeachers != teacherIds.Count) return "Tanlangan o'qituvchilardan biri topilmadi";

        if (await NameTakenAsync(req.SubjectId, name, excludeGroupId, ct)) return DuplicateNameMessage;
        return null;
    }

    /// <summary>
    /// Shu fan ichida ARXIVLANMAGAN guruhlar orasida nom band emasmi
    /// (<c>ux_study_groups_name_active</c> ning ilova tomondagi nusxasi —
    /// indeks <c>lower(name)</c> ustida, shuning uchun taqqoslash ham
    /// katta-kichik harfni farqlamaydi).
    /// </summary>
    private Task<bool> NameTakenAsync(
        string subjectId, string name, Guid? excludeGroupId, CancellationToken ct) =>
        db.StudyGroups.AnyAsync(
            g => g.SubjectId == subjectId
                 && !g.IsArchived
                 && g.Id != (excludeGroupId ?? Guid.Empty)
                 && g.Name.ToLower() == name.ToLower(),
            ct);

    /// <summary>Yangi guruh — sinflari va o'qituvchilari bilan. SaveChanges CHAQIRILMAYDI.</summary>
    public StudyGroup Create(SaveStudyGroupRequest req, string userId)
    {
        var group = new StudyGroup
        {
            Name = req.Name.Trim(),
            SubjectId = req.SubjectId,
            Gender = req.Gender,
            IsTrack = req.IsTrack ?? false,
            CreatedBy = userId,
        };
        db.StudyGroups.Add(group);
        ReplaceClasses(group.Id, Distinct(req.ClassIds));
        ReplaceTeachers(group.Id, Distinct(req.TeacherIds));
        return group;
    }

    /// <summary>
    /// Guruh maydonlarini yangilaydi.
    ///
    /// <para>
    /// <b>Fan (<c>subject_id</c>) — alternativ kalitning qismi</b> va EF uni
    /// kuzatuv orqali o'zgartirishga yo'l qo'ymaydi (StudyGroups.cs izohi).
    /// Shuning uchun fan almashtirish bu yerda RAD ETILADI: guruhning fanini
    /// o'zgartirish — aslida boshqa guruh yaratish. Fan bilan birga a'zolardagi
    /// nusxa ham ko'chishi kerak bo'lardi va "bitta fandan bitta guruh" qoidasi
    /// o'sha lahzada buzilishi mumkin edi.
    /// </para>
    /// </summary>
    public async Task<string?> UpdateAsync(
        StudyGroup group, SaveStudyGroupRequest req, CancellationToken ct = default)
    {
        if (group.IsArchived) return ArchivedGroupMessage;
        if (!string.Equals(group.SubjectId, req.SubjectId, StringComparison.Ordinal))
            return "Guruhning fanini o'zgartirib bo'lmaydi — yangi guruh oching.";

        group.Name = req.Name.Trim();
        group.Gender = req.Gender;
        if (req.IsTrack is { } track) group.IsTrack = track;

        // FARQNI yozamiz, "hammasini o'chirib qaytadan qo'shish" EMAS. O'chirish
        // va qo'shish bitta SaveChanges ichida bir xil birlamchi kalitga tushsa
        // (sinf ro'yxatida qolgan sinf), EF ularning tartibiga kafolat bermaydi
        // va PK to'qnashuvi chiqishi mumkin edi.
        var classIds = Distinct(req.ClassIds);
        var current = await db.StudyGroupClasses.Where(c => c.GroupId == group.Id).ToListAsync(ct);
        db.StudyGroupClasses.RemoveRange(
            current.Where(c => !classIds.Contains(c.ClassId, StringComparer.Ordinal)));
        ReplaceClasses(group.Id,
            [.. classIds.Where(id => !current.Any(c => c.ClassId == id))]);

        var teacherIds = Distinct(req.TeacherIds);
        var currentTeachers = await db.StudyGroupTeachers
            .Where(t => t.GroupId == group.Id).ToListAsync(ct);
        db.StudyGroupTeachers.RemoveRange(
            currentTeachers.Where(t => !teacherIds.Contains(t.TeacherId, StringComparer.Ordinal)));
        ReplaceTeachers(group.Id,
            [.. teacherIds.Where(id => !currentTeachers.Any(t => t.TeacherId == id))]);

        return null;
    }

    /// <summary>
    /// Guruhni arxivlaydi va FAOL a'zoliklarni yopadi (§2.1.4: "arxiv — tarix
    /// saqlanadi, yangi a'zo qo'shilmaydi"). A'zoliklar yopilmasa, bola
    /// arxivdagi guruh tufayli boshqa guruhga qo'shila olmay qolardi.
    /// </summary>
    public async Task ArchiveAsync(StudyGroup group, CancellationToken ct = default)
    {
        group.IsArchived = true;
        group.ArchivedAt = AppClock.NowInstant;

        var members = await db.StudyGroupMembers
            .Where(m => m.GroupId == group.Id && m.LeftOn == null).ToListAsync(ct);
        foreach (var m in members)
        {
            m.LeftOn = AppClock.Today < m.JoinedOn ? m.JoinedOn : AppClock.Today;
            m.LeaveReason = $"Guruh arxivlandi ({group.Name})";
        }
    }

    /// <summary>
    /// Guruhni arxivdan chiqaradi. A'zoliklar QAYTARILMAYDI — yopilgan yozuv
    /// tarix, uni "qayta ochish" bolaning guruhdan chiqqan davrini soxtalashtirardi.
    /// Ro'yxat qaytadan to'ldiriladi.
    /// </summary>
    public async Task<string?> UnarchiveAsync(StudyGroup group, CancellationToken ct = default)
    {
        if (await NameTakenAsync(group.SubjectId, group.Name, group.Id, ct))
            return "Shu nom bilan boshqa faol guruh ochilgan — avval uning nomini o'zgartiring.";

        group.IsArchived = false;
        group.ArchivedAt = null;
        return null;
    }

    private void ReplaceClasses(Guid groupId, IReadOnlyList<string> classIds)
    {
        foreach (var id in classIds)
            db.StudyGroupClasses.Add(new StudyGroupClass { GroupId = groupId, ClassId = id });
    }

    private void ReplaceTeachers(Guid groupId, IReadOnlyList<string> teacherIds)
    {
        foreach (var id in teacherIds)
            db.StudyGroupTeachers.Add(new StudyGroupTeacher { GroupId = groupId, TeacherId = id });
    }

    /* -------------------------------------------------------------------
     *  2. Ro'yxat
     * ---------------------------------------------------------------- */

    /// <summary>
    /// Guruhga qo'shish mumkin bo'lgan o'quvchilar (chap panel, §2.1.1).
    /// Har biriga SHU FAN bo'yicha joriy guruhi qo'shiladi — band bo'lgani
    /// kulrang ko'rinadi.
    /// </summary>
    public async Task<List<GroupCandidateDto>> CandidatesAsync(
        IReadOnlyList<string> classIds, string? gender, string subjectId,
        Guid? excludeGroupId = null, CancellationToken ct = default)
    {
        var ids = Distinct(classIds);
        if (ids.Count == 0 || string.IsNullOrWhiteSpace(subjectId)) return [];

        var classes = await db.Classes.AsNoTracking()
            .Where(c => ids.Contains(c.Id)).ToListAsync(ct);
        if (classes.Count == 0) return [];

        // Bugungi haqiqat manbai — NOM (§2.1.2). Sinf nomlari takrorlanmaydi
        // (ClassesController.Update uni tekshiradi), shuning uchun nom → sinf
        // moslamasi bir qiymatli.
        var names = classes.Select(c => c.Name).ToList();
        var byName = classes.ToDictionary(c => c.Name, StringComparer.Ordinal);

        var studentsQuery = db.Students.AsNoTracking()
            .Where(s => !s.IsArchived && names.Contains(s.ClassName));
        if (gender is "male" or "female") studentsQuery = studentsQuery.Where(s => s.Gender == gender);
        var students = await studentsQuery.OrderBy(s => s.FullName).ToListAsync(ct);
        if (students.Count == 0) return [];

        var studentIds = students.Select(s => s.Id).ToList();
        var busy = await db.StudyGroupMembers.AsNoTracking()
            .Where(m => m.LeftOn == null && m.SubjectId == subjectId && studentIds.Contains(m.StudentId))
            .Join(db.StudyGroups.AsNoTracking(), m => m.GroupId, g => g.Id,
                (m, g) => new { m.StudentId, g.Id, g.Name })
            .ToListAsync(ct);

        // Tahrirlanayotgan guruhning O'Z a'zosi "band" emas — u o'ng panelda turadi.
        var busyByStudent = busy
            .Where(b => excludeGroupId is null || b.Id != excludeGroupId)
            .ToDictionary(b => b.StudentId, b => (b.Id, b.Name), StringComparer.Ordinal);

        return [.. students.Select(s =>
        {
            var cls = byName[s.ClassName];
            var current = busyByStudent.TryGetValue(s.Id, out var g) ? g : default((Guid, string)?);
            return new GroupCandidateDto(
                s.Id, s.FullName, cls.Id, cls.Name, s.Gender,
                current?.Item1, current?.Item2);
        })];
    }

    /// <summary>
    /// Guruhga o'quvchilar qo'shadi. Har biri uchun: guruh boqadigan sinfdan
    /// bo'lishi, jinsga mos kelishi va shu fan bo'yicha boshqa FAOL guruhda
    /// bo'lmasligi tekshiriladi. SaveChanges CHAQIRILMAYDI.
    /// </summary>
    public async Task<string?> AddMembersAsync(
        StudyGroup group, IReadOnlyList<string> studentIds, string userId,
        CancellationToken ct = default)
    {
        if (group.IsArchived) return ArchivedGroupMessage;
        var ids = Distinct(studentIds);
        if (ids.Count == 0) return null;

        var students = await db.Students.Where(s => ids.Contains(s.Id)).ToListAsync(ct);
        if (students.Count != ids.Count) return "Tanlangan o'quvchilardan biri topilmadi";

        var feedingClassIds = await db.StudyGroupClasses
            .Where(c => c.GroupId == group.Id).Select(c => c.ClassId).ToListAsync(ct);
        var feedingNames = await db.Classes
            .Where(c => feedingClassIds.Contains(c.Id)).Select(c => c.Name).ToListAsync(ct);

        var busy = await db.StudyGroupMembers
            .Where(m => m.LeftOn == null && m.SubjectId == group.SubjectId && ids.Contains(m.StudentId))
            .ToListAsync(ct);

        foreach (var student in students)
        {
            // Allaqachon SHU guruhda — jim o'tkazamiz (ro'yxat qayta yuborilgan).
            if (busy.Any(m => m.StudentId == student.Id && m.GroupId == group.Id)) continue;
            if (busy.Any(m => m.StudentId == student.Id))
                return $"{student.FullName}: {OneGroupPerSubjectMessage}";
            if (student.IsArchived)
                return $"{student.FullName}: arxivlangan o'quvchini guruhga qo'shib bo'lmaydi";
            if (!feedingNames.Contains(student.ClassName, StringComparer.Ordinal))
                return $"{student.FullName}: {NotFedByClassMessage}";
            if (group.Gender is not null && !string.Equals(group.Gender, student.Gender, StringComparison.Ordinal))
                return $"{student.FullName}: {GenderMismatchMessage}";

            db.StudyGroupMembers.Add(new StudyGroupMember
            {
                GroupId = group.Id,
                SubjectId = group.SubjectId,
                StudentId = student.Id,
                JoinedOn = AppClock.Today,
                CreatedBy = userId,
            });
        }

        return null;
    }

    /// <summary>
    /// A'zolikni yopadi — o'chirmaydi. Sabab ixtiyoriy, lekin yozilsa saqlanadi
    /// (§2.1.1: sabab MAJBURIY faqat sinfdan chiqarishda).
    /// </summary>
    public static void CloseMember(StudyGroupMember member, string? reason)
    {
        member.LeftOn = AppClock.Today < member.JoinedOn ? member.JoinedOn : AppClock.Today;
        member.LeaveReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    /// <summary>
    /// O'quvchini boshqa guruhga o'tkazadi — FAQAT ayni fan ichida.
    ///
    /// <para>
    /// Ikki qadam va ikki SaveChanges: qisman unikal indeks
    /// (<c>ux_group_members_one_group_per_subject</c>) bir lahzada ikkita faol
    /// qatorni ko'rmasligi kerak. Chaqiruvchi tranzaksiya ochadi.
    /// </para>
    /// </summary>
    public async Task<string?> TransferAsync(
        StudyGroupMember member, StudyGroup from, StudyGroup to, string? reason, string userId,
        CancellationToken ct = default)
    {
        if (to.IsArchived) return ArchivedGroupMessage;
        if (from.Id == to.Id) return TransferSameGroupMessage;
        if (!string.Equals(from.SubjectId, to.SubjectId, StringComparison.Ordinal))
            return TransferSameSubjectMessage;

        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == member.StudentId, ct);
        if (student is null) return "O'quvchi topilmadi";

        var feedingClassIds = await db.StudyGroupClasses
            .Where(c => c.GroupId == to.Id).Select(c => c.ClassId).ToListAsync(ct);
        var feedingNames = await db.Classes
            .Where(c => feedingClassIds.Contains(c.Id)).Select(c => c.Name).ToListAsync(ct);
        if (!feedingNames.Contains(student.ClassName, StringComparer.Ordinal))
            return NotFedByClassMessage;
        if (to.Gender is not null && !string.Equals(to.Gender, student.Gender, StringComparison.Ordinal))
            return GenderMismatchMessage;

        CloseMember(member, string.IsNullOrWhiteSpace(reason)
            ? $"{from.Name} → {to.Name} guruhiga o'tkazildi"
            : reason.Trim());
        await db.SaveChangesAsync(ct);

        db.StudyGroupMembers.Add(new StudyGroupMember
        {
            GroupId = to.Id,
            SubjectId = to.SubjectId,
            StudentId = student.Id,
            JoinedOn = AppClock.Today,
            CreatedBy = userId,
        });
        await db.SaveChangesAsync(ct);
        return null;
    }

    /* -------------------------------------------------------------------
     *  3. Yordamchilar
     * ---------------------------------------------------------------- */

    private static List<string> Distinct(IReadOnlyList<string>? ids) =>
        ids is null
            ? []
            : [.. ids.Where(i => !string.IsNullOrWhiteSpace(i)).Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// "Bitta fandan bitta guruh" indeksining buzilishi. Xizmat buni oldindan
    /// tekshiradi; bu yo'l faqat POYGA (ikki admin bir vaqtda qo'shdi) uchun —
    /// unda ham foydalanuvchi 500 emas, o'sha o'zbekcha xabarni ko'rsin.
    /// </summary>
    public static bool IsOneGroupPerSubjectViolation(DbUpdateException ex) =>
        HasIndexViolation(ex, OneGroupPerSubjectIndex) || HasIndexViolation(ex, OneActiveInGroupIndex);

    /// <summary>Guruh nomining takrorlanishi (<c>ux_study_groups_name_active</c>).</summary>
    public static bool IsDuplicateNameViolation(DbUpdateException ex) =>
        HasIndexViolation(ex, NameActiveIndex);

    private static bool HasIndexViolation(DbUpdateException ex, string indexName)
    {
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is DbException { SqlState: "23505" }
                && inner.Message.Contains(indexName, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}

using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Server.Mcp.Tools;

/// <summary>Pupils: search and profile. Section: <c>students</c>; money only with finance access.</summary>
[McpServerToolType]
public sealed class StudentTools(McpToolContext t)
{
    [McpServerTool(Name = "students_search", Title = "O'quvchilarni qidirish",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("O'quvchilar ro'yxati: ism/telefon bo'yicha qidirish, sinf, yo'nalish guruhi, holat, arxiv va qarz filtrlari. "
        + "Balans va qarz ustunlari faqat moliya ruxsati bo'lsa chiqadi. Sahifalangan (ko'pi bilan 200 qator). "
        + "Search pupils by name/phone with class, track group, status, archive and debt filters.")]
    public async Task<string> StudentsSearch(
        [Description("Ism, familiya yoki telefon bo'lagi")] string? search = null,
        [Description("Sinf nomi, masalan 5-A")] string? className = null,
        [Description("Yo'nalish/o'quv guruhi id (study_groups.id, class_list/track_groups_list dan)")] string? groupId = null,
        [Description("active (sukut) | archived | all")] string? state = null,
        [Description("Holat id (student statuses)")] string? statusId = null,
        [Description("Faqat qarzdorlar: true (moliya ruxsati kerak)")] bool? debtorsOnly = null,
        [Description("Kamida shuncha qarzi borlar, so'm (moliya ruxsati kerak)")] decimal? minDebt = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Students);
        var finance = t.CanReadFinance;
        if ((debtorsOnly == true || minDebt is not null) && !finance) t.Require(McpAreas.Debtors);

        var filter = new StudentListFilter
        {
            Search = search,
            ClassName = className,
            State = state,
            GroupId = ParseGuid(groupId, "groupId"),
            StatusId = ParseGuid(statusId, "statusId"),
            BalanceState = debtorsOnly == true ? "debt" : null,
            MinDebt = minDebt,
            Page = McpToolContext.Page(page),
            PageSize = McpToolContext.PageSize(pageSize),
        };
        var result = await new StudentListQuery(t.Db).RunAsync(filter, ct);

        var items = result.Items.Select(r => new
        {
            r.Id, r.FullName, r.ClassName, r.Grade, r.Gender, r.BirthDate, r.Age, r.Phone,
            r.ParentFullName, r.ParentPhone, r.Language, r.EnrollmentDate,
            status = r.StatusName, r.HasContract, r.ContractNumber, r.IsArchived, r.ArchivedAt, r.ArchiveReason,
            balance = finance ? r.Balance : (decimal?)null,
        }).ToList();

        return t.Json(new
        {
            total = result.Total, page = result.Page, pageSize = result.PageSize,
            totalDebt = finance ? result.TotalDebt : (decimal?)null,
            totalAdvance = finance ? result.TotalCredit : (decimal?)null,
            note = finance ? null : "Balans ko'rsatilmadi: moliya ruxsati yo'q.",
            items,
        }, items.Count);
    }

    [McpServerTool(Name = "student_profile", Title = "O'quvchi kartochkasi",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Bitta o'quvchi haqida to'liq ma'lumot: sinf, sinf rahbari, yo'nalish guruhlari, vasiylar, holat, "
        + "davomat xulosasi, baholar (fan va chorak bo'yicha), intizom ballari; moliya ruxsati bo'lsa balans va obunalar. "
        + "id yoki ism bo'yicha topiladi — bir nechta mos kelsa, nomzodlar ro'yxati qaytadi.")]
    public async Task<string> StudentProfile(
        [Description("O'quvchi id (students_search natijasidan)")] string? studentId = null,
        [Description("Yoki to'liq/qisman ism")] string? name = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Students);
        var db = t.Db;
        Student? st = null;
        if (!string.IsNullOrWhiteSpace(studentId))
            st = await db.Students.FirstOrDefaultAsync(s => s.Id == studentId, ct);
        else if (!string.IsNullOrWhiteSpace(name))
        {
            var q = name.Trim().ToLower();
            var matches = await db.Students.Where(s => s.FullName.ToLower().Contains(q))
                .OrderBy(s => s.IsArchived).ThenBy(s => s.FullName).Take(11).ToListAsync(ct);
            if (matches.Count > 1)
                return t.Json(new
                {
                    message = "Bir nechta o'quvchi mos keldi — studentId bilan qayta so'rang.",
                    candidates = matches.Take(10).Select(m => new { m.Id, m.FullName, m.ClassName, m.IsArchived }),
                }, matches.Count);
            st = matches.FirstOrDefault();
        }
        else throw new McpException("studentId yoki name kerak.");
        if (st is null) return t.Json(new { message = "O'quvchi topilmadi." }, 0);

        var nb = await StudentProfileBuilder.BuildAsync(db, st);
        var finance = t.CanReadFinance;

        var subjectName = nb.Subjects.ToDictionary(s => s.Id, s => s.Name);
        var grades = nb.Grades.Select(g => new
        {
            subject = subjectName.GetValueOrDefault(g.Key, g.Key),
            quarters = g.Value.OrderBy(q => q.Key).ToDictionary(q => q.Key.ToString(), q => Math.Round(q.Value, 2)),
        }).ToList();

        var guardians = await (from sg in db.StudentGuardians
                               join g in db.Guardians on sg.GuardianId equals g.Id
                               where sg.StudentId == st.Id
                               orderby sg.IsPrimary descending
                               select new { g.FullName, g.Phone, sg.Relation, sg.IsPrimary }).ToListAsync(ct);

        var groups = await (from m in db.StudyGroupMembers
                            join g in db.StudyGroups on m.GroupId equals g.Id
                            where m.StudentId == st.Id && m.LeftOn == null
                            select new { g.Id, g.Name, g.IsTrack, m.JoinedOn }).ToListAsync(ct);

        var status = st.StatusId is { } sid
            ? await db.StudentStatuses.Where(s => s.Id == sid).Select(s => s.Name).FirstOrDefaultAsync(ct)
            : null;

        object? subscriptions = null;
        if (finance)
        {
            var today = McpToolContext.Today;
            subscriptions = await (from s in db.StudentSubscriptions
                                   join c in db.FeeCategories on s.CategoryId equals c.Id
                                   where s.StudentId == st.Id && (s.EndsOn == null || s.EndsOn >= today)
                                   select new { category = c.Name, s.MonthlyAmount, s.StartsOn, s.EndsOn, s.Detail })
                .ToListAsync(ct);
        }

        return t.Json(new
        {
            nb.Id, nb.FullName, nb.ClassName, nb.HomeroomTeacher, nb.Gender, nb.BirthDate, nb.EnrollmentDate,
            nb.Address, phone = st.Phone, st.Language, status, st.IsArchived, st.ArchivedAt, st.ArchiveReason,
            parent = new { name = nb.ParentFullName, phone = nb.ParentPhone },
            guardians,
            groups,
            attendance = new
            {
                lessonsConducted = nb.Conducted, lessonsAttended = nb.Attended, percent = nb.AttendancePct,
                byReason = nb.Reasons.Select(r => new { r.Name, r.IsLate, r.Count }),
            },
            grades = new { average = Math.Round(nb.AvgGrade, 2), bySubject = grades },
            discipline = new { score = nb.DisciplineScore, plus = nb.DisciplinePlus, minus = nb.DisciplineMinus },
            homework = new { done = nb.HomeworkDone, missed = nb.HomeworkMissed },
            balance = finance ? nb.Balance : (decimal?)null,
            subscriptions,
            note = finance ? null : "Balans va obunalar ko'rsatilmadi: moliya ruxsati yo'q.",
        }, 1);
    }

    internal static Guid? ParseGuid(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Guid.TryParse(value.Trim(), out var g) ? g : throw new McpException($"«{name}» noto'g'ri id.");
    }
}

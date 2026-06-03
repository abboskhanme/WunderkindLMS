using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// Jurnal yozuvlaridan reyting/ko'rsatkichlarni hisoblovchi yordamchi.
/// Baholar JournalEntry.Grade, davomat esa ReasonId belgilangan yozuvlardan olinadi.
/// </summary>
public static class Analytics
{
    public record ClassResult(List<SubjectDto> Subjects, List<ClassStudentRowDto> Rows);

    private static double Round1(double v) => Math.Round(v, 1);

    private static StudentDto Map(Student s) => new(
        s.Id, s.FullName, s.BirthDate, s.Address, s.Gender,
        s.ParentFullName, s.ParentPhone, s.ClassName, s.EnrollmentDate, s.Balance);

    public static ClassResult BuildClass(
        SchoolClass cls,
        IReadOnlyList<Student> allStudents,
        IReadOnlyList<Subject> allSubjects,
        IReadOnlyList<ScheduleTemplate> classTemplates,
        IReadOnlyList<JournalEntry> classEntries,
        IReadOnlyList<LessonNote> classNotes,
        IReadOnlyList<QuarterGrade>? classQuarterGrades = null,
        IReadOnlyCollection<string>? lateReasonIds = null)
    {
        // Rasmiy chorak bahosi kunlik o'rtacha o'rnini bosadi (faqat berilganda — hisobotlarda).
        // Berilmaganda (dashboard/reyting) eski xulq saqlanadi.
        var quarterGrades = classQuarterGrades ?? [];
        // "Kech keldi" turidagi sabablar davomatsizlik (absence) sifatida hisoblanmaydi.
        var lateSet = lateReasonIds is null ? new HashSet<string>() : new HashSet<string>(lateReasonIds);
        var students = allStudents.Where(s => s.ClassName == cls.Name).ToList();

        // Davomat FAQAT o'tilgan darslar bo'yicha (ptichka/baho/davomat). Bir kun ichidagi har dars
        // (sana+dars raqami) alohida hisoblanadi. SubGroup saqlanadi — bo'lingan darslarda har
        // o'quvchi faqat o'z guruhi (yoki butun sinf, SubGroup=0) darslari bo'yicha baholanadi.
        var conductedNotes = classNotes.Where(n => n.Conducted)
            .Select(n => (n.SubjectId, n.Date, n.Period, n.SubGroup)).ToHashSet();

        var fromSchedule = classTemplates.SelectMany(t => t.Lessons).Select(l => l.SubjectId).Distinct().ToList();
        var subjectIds = fromSchedule.Count > 0 ? fromSchedule : allSubjects.Select(s => s.Id).ToList();
        var subjects = subjectIds
            .Select(id => allSubjects.FirstOrDefault(s => s.Id == id))
            .Where(s => s is not null)
            .Select(s => new SubjectDto(s!.Id, s.Name))
            .ToList();

        var rows = students.Select(student =>
        {
            var studentEntries = classEntries.Where(e => e.StudentId == student.Id).ToList();
            var studentQGrades = quarterGrades.Where(g => g.StudentId == student.Id).ToList();

            var grades = new Dictionary<string, double>();
            foreach (var subj in subjects)
            {
                var explicitForSubj = studentQGrades.Where(g => g.SubjectId == subj.Id).ToList();
                if (explicitForSubj.Count == 0)
                {
                    // Chorak bahosi yo'q — kunlik baholar o'rtachasi (eski xulq).
                    var subjectGrades = studentEntries
                        .Where(e => e.SubjectId == subj.Id && e.Grade.HasValue)
                        .Select(e => (double)e.Grade!.Value)
                        .ToList();
                    grades[subj.Id] = subjectGrades.Count > 0 ? Round1(subjectGrades.Average()) : 0;
                }
                else
                {
                    // Chorak bo'yicha: rasmiy baho bo'lsa shu, bo'lmasa shu chorak kunlik o'rtachasi.
                    var dailyByQ = studentEntries
                        .Where(e => e.SubjectId == subj.Id && e.Grade.HasValue)
                        .GroupBy(e => e.Quarter)
                        .ToDictionary(g => g.Key, g => g.Average(e => (double)e.Grade!.Value));
                    var explicitByQ = explicitForSubj.ToDictionary(g => g.Quarter, g => (double)g.Grade);
                    var vals = dailyByQ.Keys.Union(explicitByQ.Keys)
                        .Select(q => explicitByQ.TryGetValue(q, out var ex) ? ex : dailyByQ[q])
                        .ToList();
                    grades[subj.Id] = vals.Count > 0 ? Round1(vals.Average()) : 0;
                }
            }

            // O'rtacha: chorak bahosi bor o'quvchida fan baholarining o'rtachasi; aks holda kunlik o'rtacha.
            double average;
            if (studentQGrades.Count > 0)
            {
                var subjVals = grades.Values.Where(v => v > 0).ToList();
                average = subjVals.Count > 0 ? Round1(subjVals.Average()) : 0;
            }
            else
            {
                var allGrades = studentEntries.Where(e => e.Grade.HasValue).Select(e => (double)e.Grade!.Value).ToList();
                average = allGrades.Count > 0 ? Round1(allGrades.Average()) : 0;
            }

            // Shu o'quvchi qatnashadigan o'tilgan darslar: butun sinf (SubGroup=0) YOKI o'quvchining
            // o'z guruhi. Boshqa guruh darslari maxrajga (va davomatsizlikka) kirmaydi.
            var studentConducted = conductedNotes
                .Where(c => c.SubGroup == 0 || c.SubGroup == student.SubGroup)
                .Select(c => (c.SubjectId, c.Date, c.Period))
                .ToHashSet();
            var conducted = studentConducted.Count;

            // O'tilgan darslardagi davomatsizliklar (sababli/sababsiz/kasal — KECH KELDI bundan mustasno).
            // Dars o'tilmagan bo'lsa — null.
            var absent = studentEntries.Count(e =>
                e.ReasonId != null && !lateSet.Contains(e.ReasonId)
                && studentConducted.Contains((e.SubjectId, e.Date, e.Period)));
            double? attendance = conducted > 0 ? Math.Round((double)(conducted - absent) / conducted * 100) : null;

            return new ClassStudentRowDto(Map(student), grades, average, attendance);
        }).ToList();

        return new ClassResult(subjects, rows);
    }
}

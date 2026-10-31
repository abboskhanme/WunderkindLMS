using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// Block-test certificate credits (client, 2026-10-31): "Sertifikati bor o'quvchilar sertifikat
/// olgan fanidan imtixondan ozod qilinadi va sertifikat natijasiga teng ball avtomatik qo'shib
/// beriladi".
///
/// <para>
/// A pupil is credited in a section when, on the exam day, they hold a valid certificate
/// (issued on or before, not expired) in the section's subject — its main subject or one of
/// its <see cref="CertificateSubject"/> rows — and the certificate's type has a
/// <see cref="CertificateType.ExamCredits"/> rule matching its level or score. The credit is
/// that percent of the section's ceiling; with several certificates the best one wins.
/// </para>
/// </summary>
public static class ExamCertificateCredits
{
    public sealed record Credit(Guid CertificateId, string Label, decimal Points);

    /// <summary>"IELTS 7.5", "Milliy sertifikat A+" — the type with the result.</summary>
    public static string LabelOf(string typeName, string? level, decimal? score) =>
        level is { Length: > 0 } ? $"{typeName} {level}"
        : score is { } s ? $"{typeName} {s.ToString("0.##", CultureInfo.InvariantCulture)}"
        : typeName;

    /// <summary>The day credits are judged on: the paper date, the window's opening, or today.</summary>
    public static DateOnly DayOf(Exam exam)
    {
        if (DateOnly.TryParseExact(exam.ExamDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var day)) return day;
        return DateOnly.FromDateTime(exam.OpensAt ?? AppClock.Now);
    }

    /// <summary>participantId → sectionId → credit, for the pupils among <paramref name="people"/>.</summary>
    public static async Task<Dictionary<string, Dictionary<string, Credit>>> ForAsync(
        IAppDbContext db, Exam exam, IReadOnlyCollection<ExamSection> sections,
        IReadOnlyCollection<(string ParticipantId, string? StudentId)> people, CancellationToken ct = default)
    {
        var result = new Dictionary<string, Dictionary<string, Credit>>(StringComparer.Ordinal);
        var studentIds = people.Select(p => p.StudentId).OfType<string>().Distinct().ToList();
        if (studentIds.Count == 0 || sections.Count == 0) return result;

        var day = DayOf(exam);
        var certs = await (from c in db.Certificates.AsNoTracking()
                           join t in db.CertificateTypes.AsNoTracking() on c.TypeId equals t.Id
                           where studentIds.Contains(c.StudentId) && t.ExamCredits != null
                                 && c.IssuedOn <= day && (c.ExpiresOn == null || c.ExpiresOn >= day)
                           select new { c.Id, c.StudentId, c.SubjectId, c.Level, c.Score, t.Name, t.ExamCredits })
            .ToListAsync(ct);
        if (certs.Count == 0) return result;

        var certIds = certs.Select(c => c.Id).ToList();
        var extraSubjects = (await db.CertificateSubjects.AsNoTracking()
                .Where(cs => certIds.Contains(cs.CertificateId))
                .ToListAsync(ct))
            .ToLookup(cs => cs.CertificateId, cs => cs.SubjectId);

        // studentId → subjectId → best (percent, certificate)
        var best = new Dictionary<(string Student, string Subject), (decimal Percent, Guid Id, string Label)>();
        foreach (var c in certs)
        {
            if (CertificateService.ExamCreditPercent(c.ExamCredits, c.Level, c.Score) is not { } pct) continue;
            var subjects = extraSubjects[c.Id].Append(c.SubjectId).OfType<string>().Distinct(StringComparer.Ordinal);
            foreach (var subject in subjects)
            {
                var key = (c.StudentId, subject);
                if (best.TryGetValue(key, out var had) && had.Percent >= pct) continue;
                best[key] = (pct, c.Id, LabelOf(c.Name, c.Level, c.Score));
            }
        }

        foreach (var (participantId, studentId) in people)
        {
            if (studentId is null) continue;
            foreach (var section in sections)
            {
                if (!best.TryGetValue((studentId, section.SubjectId), out var b)) continue;
                var points = ExamScoringService.RoundPoints(section.MaxScore * Math.Min(b.Percent, 100m) / 100m);
                if (!result.TryGetValue(participantId, out var mine))
                    result[participantId] = mine = new Dictionary<string, Credit>(StringComparer.Ordinal);
                mine[section.Id] = new Credit(b.Id, b.Label, points);
            }
        }
        return result;
    }
}

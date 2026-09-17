using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// G-2 (docs/modules/students-parity.md §2.1.3): bo'lingan dars (til, jismoniy tarbiya) — bitta
/// sana, dars raqami va fanda IKKITA <c>lesson_notes</c> qatori, farqi faqat <c>sub_group</c> (1 va 2).
///
/// <para>
/// Ilgari o'quvchi bosh sahifasi (<c>GET /api/student/dashboard</c>) va ota-ona Mini App'i
/// (<c>GET /api/tg/parent/children/{id}/overview</c>) izohlarni
/// <c>ToDictionary((Date, Period, SubjectId))</c> ga yig'ardi — ikkinchi qator kalitni
/// takrorlagani uchun HTTP 500. Tuzatilgandan keyin o'quvchi O'Z guruhining mavzusini ko'radi,
/// butun sinf izohi (<c>sub_group = 0</c>) esa hammaga tegishli.
/// </para>
/// <para>
/// Telegram o'qituvchi yo'qlamasi (<c>GET /api/tg/teacher/roster</c>) izohni guruh filtrisiz
/// olardi — 2-guruh o'qituvchisi 1-guruhning mavzusini ko'rishi mumkin edi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class SplitLessonNoteTests(ApiFixture fixture)
{
    /// <summary>Bo'lingan dars raqami.</summary>
    private const int SplitPeriod = 3;

    /// <summary>Butun sinf darsi raqami.</summary>
    private const int WholePeriod = 4;

    // =====================================================================
    //  1. O'quvchi bosh sahifasi
    // =====================================================================

    [Fact]
    public async Task Oquvchi_bosh_sahifasi_bolingan_darsda_yiqilmaydi_va_oz_guruhi_mavzusini_koradi()
    {
        var w = await SeedAsync(AppClock.Today.ToString("yyyy-MM-dd"));
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        foreach (var (studentId, expected) in new[] { (w.Group1StudentId, w.Group1Topic), (w.Group2StudentId, w.Group2Topic) })
        {
            var response = await admin.GetAsync($"/api/student/dashboard?studentId={studentId}");
            Assert.True(response.StatusCode == HttpStatusCode.OK,
                $"dashboard → {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var grades = body.RootElement.GetProperty("todayGrades").EnumerateArray()
                .ToDictionary(g => g.GetProperty("period").GetInt32(), g => g.GetProperty("topic").GetString());
            Assert.Equal(expected, grades[SplitPeriod]);
            Assert.Equal(w.WholeTopic, grades[WholePeriod]);
        }
    }

    // =====================================================================
    //  2. Ota-ona Mini App bosh sahifasi
    // =====================================================================

    [Fact]
    public async Task Ota_ona_mini_app_bosh_sahifasi_bolingan_darsda_yiqilmaydi_va_farzand_guruhini_koradi()
    {
        var w = await SeedAsync(AppClock.Today.ToString("yyyy-MM-dd"));
        var parentUserId = await SeedGuardianAsync(w.Tag, w.Group1StudentId, w.Group2StudentId);
        using var parent = fixture.Api.ClientWithToken(fixture.Api.TokenFor("parent", parentUserId, "Ota-ona"));

        await SchoolMetaFlags.WithAsync(fixture.Api, m => m.ShowLearningProgressInParentDashboard = true, async () =>
        {
            foreach (var (studentId, expected) in new[] { (w.Group1StudentId, w.Group1Topic), (w.Group2StudentId, w.Group2Topic) })
            {
                var response = await parent.GetAsync($"/api/tg/parent/children/{studentId}/overview");
                Assert.True(response.StatusCode == HttpStatusCode.OK,
                    $"overview → {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var grades = body.RootElement.GetProperty("todayGrades").EnumerateArray()
                    .ToDictionary(g => g.GetProperty("period").GetInt32(), g => g.GetProperty("topic").GetString());
                Assert.Equal(expected, grades[SplitPeriod]);
                Assert.Equal(w.WholeTopic, grades[WholePeriod]);
            }
        });
    }

    // =====================================================================
    //  3. Telegram o'qituvchi yo'qlamasi
    // =====================================================================

    /// <summary>
    /// Har o'qituvchi o'z guruhining mavzusini ko'radi: guruh jadvaldan (shu sinf, fan, hafta
    /// kuni va dars raqamidagi O'Z darsidan) aniqlanadi, yoki <c>subGroup</c> bilan aniq beriladi.
    /// </summary>
    [Fact]
    public async Task Telegram_oqituvchi_yoqlamasi_oz_guruhi_mavzusini_koradi()
    {
        // 2026-09-15 — seshanba (Day = 1).
        const string date = "2026-09-15";
        var w = await SeedAsync(date);

        var (teacher1, client1) = await TeacherAsync();
        var (teacher2, client2) = await TeacherAsync();
        using (client1)
        using (client2)
        {
            await fixture.Api.WithDbAsync(async db =>
            {
                var tpl = new ScheduleTemplate { ClassId = w.ClassId, Name = "Asosiy " + w.Tag };
                tpl.Lessons.Add(new ScheduleLesson { Day = 1, Period = SplitPeriod, SubjectId = w.SubjectId, TeacherId = teacher1, SubGroup = 1 });
                tpl.Lessons.Add(new ScheduleLesson { Day = 1, Period = SplitPeriod, SubjectId = w.SubjectId, TeacherId = teacher2, SubGroup = 2 });
                tpl.Lessons.Add(new ScheduleLesson { Day = 1, Period = WholePeriod, SubjectId = w.SubjectId, TeacherId = teacher1, SubGroup = 0 });
                db.ScheduleTemplates.Add(tpl);
                await db.SaveChangesAsync();
            });

            var baseUrl = $"/api/tg/teacher/roster?classId={w.ClassId}&subjectId={w.SubjectId}&quarter=1&date={date}";

            Assert.Equal(w.Group1Topic, await TopicAsync(client1, $"{baseUrl}&period={SplitPeriod}"));
            Assert.Equal(w.Group2Topic, await TopicAsync(client2, $"{baseUrl}&period={SplitPeriod}"));
            Assert.Equal(w.WholeTopic, await TopicAsync(client1, $"{baseUrl}&period={WholePeriod}"));

            // Aniq guruh berilsa — o'sha guruhning izohi (jadvaldan qat'i nazar).
            Assert.Equal(w.Group2Topic, await TopicAsync(client1, $"{baseUrl}&period={SplitPeriod}&subGroup=2"));
        }
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private sealed record World(
        string Tag, string ClassId, string SubjectId,
        string Group1StudentId, string Group2StudentId,
        string Group1Topic, string Group2Topic, string WholeTopic);

    /// <summary>
    /// Sinf, fan, ikki o'quvchi (1- va 2-guruh), berilgan sanada ikkala darsga baho, va izohlar:
    /// <see cref="SplitPeriod"/> da 1- va 2-guruhniki, <see cref="WholePeriod"/> da butun sinfniki.
    /// </summary>
    private async Task<World> SeedAsync(string date)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var cls = new SchoolClass { Name = $"SG-{tag}", Grade = 5 };
        var subject = new Subject { Name = $"Ingliz tili {tag}" };
        var s1 = GeneralSettingsFlagsTests.NewStudent($"Birinchi {tag}", cls.Name, "+998900000011");
        s1.SubGroup = 1;
        var s2 = GeneralSettingsFlagsTests.NewStudent($"Ikkinchi {tag}", cls.Name, "+998900000012");
        s2.SubGroup = 2;
        var w = new World(tag, cls.Id, subject.Id, s1.Id, s2.Id,
            $"1-guruh mavzusi {tag}", $"2-guruh mavzusi {tag}", $"Butun sinf mavzusi {tag}");

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(cls);
            db.Subjects.Add(subject);
            db.Students.AddRange(s1, s2);
            db.LessonNotes.AddRange(
                Note(cls.Id, subject.Id, date, SplitPeriod, 1, w.Group1Topic),
                Note(cls.Id, subject.Id, date, SplitPeriod, 2, w.Group2Topic),
                Note(cls.Id, subject.Id, date, WholePeriod, 0, w.WholeTopic));
            foreach (var s in new[] { s1, s2 })
                foreach (var period in new[] { SplitPeriod, WholePeriod })
                    db.JournalEntries.Add(new JournalEntry
                    {
                        ClassId = cls.Id, SubjectId = subject.Id, Quarter = 1, StudentId = s.Id,
                        Date = date, Period = period, Grade = 5,
                        SubGroup = period == SplitPeriod ? s.SubGroup : 0,
                    });
            await db.SaveChangesAsync();
        });

        return w;
    }

    private static LessonNote Note(string classId, string subjectId, string date, int period, int subGroup, string topic) => new()
    {
        ClassId = classId, SubjectId = subjectId, Quarter = 1, Date = date, Period = period,
        SubGroup = subGroup, Topic = topic, Homework = "Uyga " + topic, Conducted = true,
    };

    /// <summary>Ikkala farzandga bog'langan vasiy va uning <c>parent</c> akkaunti.</summary>
    private async Task<string> SeedGuardianAsync(string tag, params string[] studentIds)
    {
        var phone = "+99897" + Random.Shared.Next(1_000_000, 9_999_999);
        var (user, _) = await fixture.Api.SeedUserAsync("parent", email: phone);
        await fixture.Api.WithDbAsync(async db =>
        {
            var guardian = new Guardian { FullName = $"Vasiy {tag}", Phone = phone, UserId = user.Id };
            db.Guardians.Add(guardian);
            foreach (var id in studentIds)
                db.StudentGuardians.Add(new StudentGuardian
                {
                    StudentId = id, GuardianId = guardian.Id, IsPrimary = true,
                });
            await db.SaveChangesAsync();
        });
        return user.Id;
    }

    /// <summary>Jurnal ruxsati bor o'qituvchi va uning tokeni bilan klient.</summary>
    private async Task<(string TeacherId, HttpClient Client)> TeacherAsync()
    {
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        string teacherId = "";
        await fixture.Api.WithDbAsync(async db =>
        {
            var t = await db.Teachers.SingleAsync(x => x.UserId == user.Id);
            t.Permissions = [TeacherPermissions.Journal];
            await db.SaveChangesAsync();
            teacherId = t.Id;
        });
        return (teacherId, fixture.Api.ClientWithToken(
            fixture.Api.TokenFor(Roles.Teacher, user.Id, user.FullName, user.Email)));
    }

    private static async Task<string?> TopicAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"{url} → {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var topic = body.RootElement.GetProperty("topic");
        return topic.ValueKind == JsonValueKind.Null ? null : topic.GetString();
    }
}

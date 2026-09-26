using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// §5.5 — umumiy sozlamaning to'rtta bayrog'i: endpoint ruxsati va HAR BIR bayroqning
/// xatti-harakatni HAQIQATAN almashtirishi.
///
/// <para>
/// <b>Nega har bayroq uchun "yoqiq/o'chiq" juftligi.</b> Ko'rinadigan, lekin hech narsa
/// qilmaydigan tugma tugma yo'qligidan yomonroq. Shuning uchun har test bir xil amalni
/// IKKI holatda bajaradi va natija farq qilishini tekshiradi — faqat "yoqiq bo'lsa rad"
/// yetarli emas, u bayroqdan qat'i nazar rad etayotgan kodni ham yashil ko'rsatardi.
/// </para>
/// <para>
/// <b>Umumiy baza.</b> Ilova <c>ApiFixture</c> bazasiga ulangan, <c>school_meta</c> esa
/// bitta qator — ya'ni bayroq butun ilovaga ta'sir qiladi. <see cref="SchoolMetaFlags"/>
/// uni test tugashi bilan (yiqilsa ham) asl holatiga qaytaradi; kolleksiya ichidagi
/// testlar ketma-ket yuradi, shuning uchun qo'shni test o'zgargan bayroqni ko'rmaydi.
/// Arxivlash qarzdorlik bayrog'i <see cref="StudentArchiveTests"/> da.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class GeneralSettingsFlagsTests(ApiFixture fixture)
{
    private const string General = "/api/admin/settings/general";
    private const string Journal = "/api/admin/journal";
    private const string Notes = "/api/admin/journal/notes";
    private const string Date = "2026-09-01";

    // =====================================================================
    //  1. RUXSAT — faqat admin va superadmin
    // =====================================================================

    /// <summary>
    /// 2026-09-26: "Umumiy sozlamalar" ham rol orqali (Boshqaruv → Rollar). "Faqat ko'rish"
    /// (<c>settings:view</c>) bayroqlarni ko'radi, lekin saqlay olmaydi; ruxsatsiz xodim ham
    /// saqlay olmaydi. Saqlash — to'liq <c>settings</c> ruxsati (AdminPerm). Umumiy baza
    /// bayroqlari bu yerda ataylab o'zgartirilmaydi — parallel testlarga ta'sir qilardi.
    /// </summary>
    [Fact]
    public async Task Bayroqlar_faqat_korishda_oqiladi_saqlanmaydi()
    {
        using var viewer = await fixture.Api.ClientAsAsync(Roles.Staff, "settings:view");
        using var outsider = await fixture.Api.ClientAsAsync(Roles.Staff, "students");

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(General)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await viewer.PutAsJsonAsync(General, Flags(false, false, false, false))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await outsider.PutAsJsonAsync(General, Flags(false, false, false, false))).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    [InlineData("parent")]
    public async Task Boshqa_rollar_403(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role, "settings");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(General)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PutAsJsonAsync(General, Flags(false, false, false, false))).StatusCode);
    }

    [Fact]
    public async Task Tokensiz_401()
    {
        using var client = fixture.Api.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(General)).StatusCode);
    }

    /// <summary>Admin va superadmin: o'qiydi, yozadi, yozgani qaytib o'qiladi.</summary>
    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.SuperAdmin)]
    public async Task Admin_va_superadmin_oqiydi_va_yozadi(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role);

        await SchoolMetaFlags.WithAsync(fixture.Api, _ => { }, async () =>
        {
            var put = await client.PutAsJsonAsync(General, Flags(false, true, true, false));
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);

            using var json = await JsonAsync(client, General);
            var root = json.RootElement;
            Assert.False(root.GetProperty("archiveOnlyNonDebtorStudents").GetBoolean());
            Assert.True(root.GetProperty("makeAttendanceReasonRequired").GetBoolean());
            Assert.True(root.GetProperty("isStudentGradeRequired").GetBoolean());
            Assert.False(root.GetProperty("showLearningProgressInParentDashboard").GetBoolean());
        });
    }

    /// <summary>
    /// <c>school_meta</c> qatori umuman yo'q bo'lganda ham <c>false</c> <c>false</c> bo'lib
    /// saqlanadi. Tuzoq: ikkita bayroqda baza DEFAULT'i <c>true</c>, EF esa YANGI qatorda
    /// CLR sukutiga teng qiymatni INSERT'dan tushirib qoldiradi — "o'chirdim" jimgina
    /// "yoqiq" bo'lib yozilardi. Sukutlar ham shu yerda tekshiriladi.
    /// </summary>
    [Fact]
    public async Task Qator_yoq_bolsa_sukutlar_va_birinchi_saqlash_togri()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        await SchoolMetaFlags.WithoutRowAsync(fixture.Api, async () =>
        {
            using (var defaults = await JsonAsync(client, General))
            {
                var d = defaults.RootElement;
                Assert.True(d.GetProperty("archiveOnlyNonDebtorStudents").GetBoolean());
                Assert.False(d.GetProperty("makeAttendanceReasonRequired").GetBoolean());
                Assert.False(d.GetProperty("isStudentGradeRequired").GetBoolean());
                Assert.True(d.GetProperty("showLearningProgressInParentDashboard").GetBoolean());
            }

            var put = await client.PutAsJsonAsync(General, Flags(false, false, false, false));
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);

            await fixture.Api.WithDbAsync(async db =>
            {
                var meta = await db.SchoolMeta.AsNoTracking().SingleAsync();
                Assert.False(meta.ArchiveOnlyNonDebtorStudents);
                Assert.False(meta.ShowLearningProgressInParentDashboard);
            });
        });
    }

    // =====================================================================
    //  2. make_attendance_reason_required
    // =====================================================================

    /// <summary>
    /// Bizda sabab — holatning o'zi, shuning uchun "sababsiz yo'qlik" = yaroqsiz sabab id'si
    /// (bo'sh satr yoki katalogda yo'q). O'chiq bayroqda bugungidek qabul qilinadi, yoqiq
    /// bayroqda rad etiladi; haqiqiy sabab va "keldi" (null) ikkala holatda ham o'tadi.
    /// </summary>
    [Fact]
    public async Task Davomat_sababi_bayrogi_yaroqsiz_sababni_rad_etadi()
    {
        var w = await SeedClassAsync(students: 1);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var s = w.StudentIds[0];

        await SchoolMetaFlags.WithAsync(fixture.Api, m => m.MakeAttendanceReasonRequired = false, async () =>
        {
            Assert.Equal(HttpStatusCode.NoContent, (await PutEntry(admin, w, s, period: 1, reasonId: "")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent,
                (await PutEntry(admin, w, s, period: 2, reasonId: "yoq-sabab-" + w.Tag)).StatusCode);
        });

        await SchoolMetaFlags.WithAsync(fixture.Api, m => m.MakeAttendanceReasonRequired = true, async () =>
        {
            var blank = await PutEntry(admin, w, s, period: 3, reasonId: "");
            Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
            Assert.Equal(JournalSettingsGuard.ReasonRequiredMessage, await MessageAsync(blank));

            var unknown = await PutEntry(admin, w, s, period: 4, reasonId: "yoq-sabab-" + w.Tag);
            Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

            Assert.Equal(HttpStatusCode.NoContent,
                (await PutEntry(admin, w, s, period: 5, reasonId: w.AbsentReasonId)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent,
                (await PutEntry(admin, w, s, period: 6, grade: 5)).StatusCode);
        });

        // Rad etilgan kataklar bazaga TUSHMAGAN; o'chiq bayroqdagilari tushgan.
        await fixture.Api.WithDbAsync(async db =>
        {
            var periods = await db.JournalEntries.AsNoTracking()
                .Where(e => e.StudentId == s).Select(e => e.Period).OrderBy(p => p).ToListAsync();
            Assert.Equal([1, 2, 5, 6], periods);
        });
    }

    // =====================================================================
    //  3. is_student_grade_required
    // =====================================================================

    /// <summary>
    /// Yoqiq bayroqda: (1) "Dars o'tildi" baholarsiz rad etiladi; (2) avtomatik belgi ham
    /// qo'yilmaydi, lekin baho YOZILADI; (3) kechikkan o'quvchidan baho kutiladi, kelmagan
    /// o'quvchidan kutilmaydi; (4) oxirgi katak to'lishi bilan dars o'zi "o'tildi" bo'ladi.
    /// </summary>
    [Fact]
    public async Task Baho_bayrogi_yoqiq_baholarsiz_darsni_yopmaydi()
    {
        var w = await SeedClassAsync(students: 2);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var (a, b) = (w.StudentIds[0], w.StudentIds[1]);

        await SchoolMetaFlags.WithAsync(fixture.Api, m => m.IsStudentGradeRequired = true, async () =>
        {
            var close = await PutNote(admin, w, conducted: true);
            Assert.Equal(HttpStatusCode.BadRequest, close.StatusCode);
            Assert.Equal(JournalSettingsGuard.GradesRequiredMessage, await MessageAsync(close));
            Assert.False(await ConductedAsync(w));

            // Birinchi baho yoziladi, dars esa hali "o'tildi" emas.
            Assert.Equal(HttpStatusCode.NoContent, (await PutEntry(admin, w, a, period: 1, grade: 5)).StatusCode);
            Assert.False(await ConductedAsync(w));

            // Kechikkan — darsda bo'lgan, undan baho kutiladi.
            Assert.Equal(HttpStatusCode.NoContent,
                (await PutEntry(admin, w, b, period: 1, reasonId: w.LateReasonId)).StatusCode);
            Assert.False(await ConductedAsync(w));
            Assert.Equal(HttpStatusCode.BadRequest, (await PutNote(admin, w, conducted: true)).StatusCode);

            // Kelmagan — bahodan ozod; oxirgi katak to'ldi va dars o'zi yopildi.
            Assert.Equal(HttpStatusCode.NoContent,
                (await PutEntry(admin, w, b, period: 1, reasonId: w.AbsentReasonId)).StatusCode);
            Assert.True(await ConductedAsync(w));

            Assert.Equal(HttpStatusCode.NoContent, (await PutNote(admin, w, conducted: true)).StatusCode);
        });

        await fixture.Api.WithDbAsync(async db =>
            Assert.Equal(5, (await db.JournalEntries.AsNoTracking().SingleAsync(e => e.StudentId == a)).Grade));
    }

    /// <summary>O'chiq bayroq — bugungi xatti-harakat: baholarsiz ham yopiladi, birinchi katak darsni yopadi.</summary>
    [Fact]
    public async Task Baho_bayrogi_ochiq_darsni_baholarsiz_yopadi()
    {
        var w = await SeedClassAsync(students: 2);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        await SchoolMetaFlags.WithAsync(fixture.Api, m => m.IsStudentGradeRequired = false, async () =>
        {
            Assert.Equal(HttpStatusCode.NoContent, (await PutNote(admin, w, conducted: true)).StatusCode);
            Assert.True(await ConductedAsync(w));
        });

        var other = await SeedClassAsync(students: 2);
        await SchoolMetaFlags.WithAsync(fixture.Api, m => m.IsStudentGradeRequired = false, async () =>
        {
            Assert.Equal(HttpStatusCode.NoContent,
                (await PutEntry(admin, other, other.StudentIds[0], period: 1, grade: 4)).StatusCode);
            Assert.True(await ConductedAsync(other));
        });
    }

    // =====================================================================
    //  4. show_learning_progress_in_parent_dashboard
    // =====================================================================

    /// <summary>
    /// O'chirilganda ota-ona: baholar, reyting, daftar va topshiriq ballari — 403; uyga vazifa
    /// ro'yxati qoladi, lekin BAHO bo'shaydi; Mini App qobig'i (<c>/api/tg/me</c>) buni
    /// biladi. O'quvchining O'ZI esa hammasini ko'raveradi. Yoqilganda — ota-ona ham ko'radi.
    /// </summary>
    [Fact]
    public async Task Ota_onaga_ozlashtirish_bayrogi_baholarni_yashiradi()
    {
        var p = await SeedPortalAsync();
        using var parent = fixture.Api.ClientWithToken(
            fixture.Api.TokenFor("parent", p.ParentUserId, "Ota-ona", p.ParentPhone));
        using var student = fixture.Api.ClientWithToken(
            fixture.Api.TokenFor(Roles.Student, p.StudentUserId, "O'quvchi"));

        await SchoolMetaFlags.WithAsync(fixture.Api, m => m.ShowLearningProgressInParentDashboard = true, async () =>
        {
            Assert.Equal(4, HomeworkGrade(await JsonAsync(parent, "/api/student/homework?quarter=1")));
            Assert.Equal(HttpStatusCode.OK, (await parent.GetAsync("/api/student/grades")).StatusCode);
            using var me = await JsonAsync(parent, "/api/tg/me");
            Assert.True(me.RootElement.GetProperty("showLearningProgress").GetBoolean());
        });

        await SchoolMetaFlags.WithAsync(fixture.Api, m => m.ShowLearningProgressInParentDashboard = false, async () =>
        {
            // Ota-ona: sof o'zlashtirish endpointlari yopiq.
            foreach (var url in new[]
                     {
                         "/api/student/grades", "/api/student/notebook", "/api/student/rating",
                         "/api/student/assignment-scores", $"/api/tg/parent/children/{p.StudentId}/grades",
                     })
                Assert.Equal(HttpStatusCode.Forbidden, (await parent.GetAsync(url)).StatusCode);

            // Ota-ona: uyga vazifa qoladi, baho yo'q.
            using (var hw = await JsonAsync(parent, "/api/student/homework?quarter=1"))
            {
                var row = hw.RootElement.EnumerateArray().Single();
                Assert.Equal("Kasrlar " + p.Tag, row.GetProperty("topic").GetString());
                Assert.Equal(JsonValueKind.Null, row.GetProperty("grade").ValueKind);
            }

            using (var me = await JsonAsync(parent, "/api/tg/me"))
                Assert.False(me.RootElement.GetProperty("showLearningProgress").GetBoolean());

            // O'quvchining o'zi — bayroq unga tegmaydi.
            Assert.Equal(4, HomeworkGrade(await JsonAsync(student, "/api/student/homework?quarter=1")));
            Assert.Equal(HttpStatusCode.OK, (await student.GetAsync("/api/student/grades")).StatusCode);
            using (var me = await JsonAsync(student, "/api/tg/me"))
                Assert.True(me.RootElement.GetProperty("showLearningProgress").GetBoolean());
        });
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private sealed record ClassWorld(
        string Tag, string ClassId, string SubjectId, List<string> StudentIds,
        string AbsentReasonId, string LateReasonId);

    private sealed record PortalWorld(
        string Tag, string StudentId, string StudentUserId, string ParentUserId, string ParentPhone);

    private static object Flags(bool archive, bool reason, bool grade, bool progress) => new
    {
        archiveOnlyNonDebtorStudents = archive,
        makeAttendanceReasonRequired = reason,
        isStudentGradeRequired = grade,
        showLearningProgressInParentDashboard = progress,
    };

    private async Task<ClassWorld> SeedClassAsync(int students)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var cls = new SchoolClass { Name = $"FL-{tag}", Grade = 5 };
        var subject = new Subject { Name = $"Matematika {tag}" };
        var absent = new AbsenceReason { Name = $"Kelmadi {tag}", Short = "K", IsLate = false };
        var late = new AbsenceReason { Name = $"Kechikdi {tag}", Short = "Kch", IsLate = true };
        var ids = new List<string>();

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(cls);
            db.Subjects.Add(subject);
            db.AbsenceReasons.AddRange(absent, late);
            for (var i = 0; i < students; i++)
            {
                var s = NewStudent($"Bayroq {i} {tag}", cls.Name, "+998900000000");
                db.Students.Add(s);
                ids.Add(s.Id);
            }
            await db.SaveChangesAsync();
        });

        return new ClassWorld(tag, cls.Id, subject.Id, ids, absent.Id, late.Id);
    }

    private async Task<PortalWorld> SeedPortalAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        // Ota-ona portali farzandni login (telefon) orqali topadi — takrorlanmas raqam.
        var phone = "99891" + Random.Shared.Next(1_000_000, 9_999_999);

        var (parentUser, _) = await fixture.Api.SeedUserAsync("parent", email: phone);
        var (studentUser, _) = await fixture.Api.SeedUserAsync(Roles.Student);

        var cls = new SchoolClass { Name = $"PR-{tag}", Grade = 6 };
        var subject = new Subject { Name = $"Algebra {tag}" };
        var student = NewStudent($"Portal {tag}", cls.Name, phone);
        student.UserId = studentUser.Id;

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(cls);
            db.Subjects.Add(subject);
            db.Students.Add(student);
            db.LessonNotes.Add(new LessonNote
            {
                ClassId = cls.Id, SubjectId = subject.Id, Quarter = 1, Date = Date, Period = 1,
                Topic = "Kasrlar " + tag, Homework = "12-mashq", Conducted = true,
            });
            db.JournalEntries.Add(new JournalEntry
            {
                ClassId = cls.Id, SubjectId = subject.Id, Quarter = 1, StudentId = student.Id,
                Date = Date, Period = 1, Grade = 4,
            });
            await db.SaveChangesAsync();
        });

        return new PortalWorld(tag, student.Id, studentUser.Id, parentUser.Id, phone);
    }

    private static int? HomeworkGrade(JsonDocument doc)
    {
        using (doc)
        {
            var row = doc.RootElement.EnumerateArray().Single();
            return row.GetProperty("grade").ValueKind == JsonValueKind.Null ? null : row.GetProperty("grade").GetInt32();
        }
    }

    private static Task<HttpResponseMessage> PutEntry(
        HttpClient client, ClassWorld w, string studentId, int period, int? grade = null, string? reasonId = null) =>
        client.PutAsJsonAsync(Journal, new
        {
            classId = w.ClassId, subjectId = w.SubjectId, quarter = 1, studentId, date = Date, period,
            grade, reasonId, homework = 0, behavior = 0, mastery = (int?)null,
        });

    private static Task<HttpResponseMessage> PutNote(HttpClient client, ClassWorld w, bool conducted) =>
        client.PutAsJsonAsync(Notes, new
        {
            classId = w.ClassId, subjectId = w.SubjectId, quarter = 1, date = Date, period = 1,
            topic = "Mavzu", homework = (string?)null, conducted, subGroup = 0,
        });

    private async Task<bool> ConductedAsync(ClassWorld w)
    {
        var conducted = false;
        await fixture.Api.WithDbAsync(async db =>
            conducted = await db.LessonNotes.AsNoTracking().AnyAsync(n =>
                n.ClassId == w.ClassId && n.SubjectId == w.SubjectId && n.Date == Date
                && n.Period == 1 && n.Conducted));
        return conducted;
    }

    private static async Task<JsonDocument> JsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.True(response.IsSuccessStatusCode,
            $"{url}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("message").GetString() ?? "";
    }

    internal static Student NewStudent(string fullName, string className, string parentPhone) => new()
    {
        FullName = fullName,
        LastName = "Familiya",
        FirstName = "Ism",
        MiddleName = "Otasi",
        BirthDate = "2012-01-01",
        Address = "Toshkent",
        Gender = "male",
        ParentFullName = "Ota-ona",
        ParentLastName = "Familiya",
        ParentFirstName = "Ism",
        ParentMiddleName = "Otasi",
        ParentPhone = parentPhone,
        ClassName = className,
        EnrollmentDate = "2025-09-01",
    };
}

/// <summary>
/// <c>school_meta</c> bayroqlarini test davomida o'zgartirib, keyin ASL holatiga qaytaradi —
/// test yiqilsa ham. Ilova umumiy bazaga ulangani uchun bu majburiy: qaytarilmagan bayroq
/// keyingi test klassining xatti-harakatini jimgina o'zgartirardi.
/// </summary>
internal static class SchoolMetaFlags
{
    public static async Task WithAsync(ApiFactory api, Action<SchoolMeta> set, Func<Task> body)
    {
        string? createdId = null;
        (bool Archive, bool Reason, bool Grade, bool Progress) before = default;

        await api.WithDbAsync(async db =>
        {
            var meta = await db.SchoolMeta.OrderBy(m => m.Id).FirstOrDefaultAsync();
            if (meta is null)
            {
                // Avval INSERT, keyin UPDATE: `true` DEFAULT'li bayroqqa yangi qatorda `false`
                // yozib bo'lmaydi (EF uni "berilmagan" deb tushirib qoldiradi).
                meta = new SchoolMeta();
                db.SchoolMeta.Add(meta);
                await db.SaveChangesAsync();
                createdId = meta.Id;
            }
            before = (meta.ArchiveOnlyNonDebtorStudents, meta.MakeAttendanceReasonRequired,
                meta.IsStudentGradeRequired, meta.ShowLearningProgressInParentDashboard);
            set(meta);
            await db.SaveChangesAsync();
        });

        try
        {
            await body();
        }
        finally
        {
            await api.WithDbAsync(async db =>
            {
                if (createdId is not null)
                {
                    await db.SchoolMeta.Where(m => m.Id == createdId).ExecuteDeleteAsync();
                    return;
                }
                var meta = await db.SchoolMeta.OrderBy(m => m.Id).FirstAsync();
                meta.ArchiveOnlyNonDebtorStudents = before.Archive;
                meta.MakeAttendanceReasonRequired = before.Reason;
                meta.IsStudentGradeRequired = before.Grade;
                meta.ShowLearningProgressInParentDashboard = before.Progress;
                await db.SaveChangesAsync();
            });
        }
    }

    /// <summary>
    /// <c>school_meta</c> qatori UMUMAN YO'Q holatda ishlaydi. Mavjud qatorlar vaqtincha
    /// olib qo'yiladi va keyin barcha ustunlari bilan qaytariladi.
    /// </summary>
    public static async Task WithoutRowAsync(ApiFactory api, Func<Task> body)
    {
        var saved = new List<SchoolMeta>();
        await api.WithDbAsync(async db =>
        {
            saved = await db.SchoolMeta.AsNoTracking().ToListAsync();
            await db.SchoolMeta.ExecuteDeleteAsync();
        });

        try
        {
            await body();
        }
        finally
        {
            await api.WithDbAsync(async db =>
            {
                await db.SchoolMeta.ExecuteDeleteAsync();
                foreach (var row in saved)
                {
                    var flags = (row.ArchiveOnlyNonDebtorStudents, row.ShowLearningProgressInParentDashboard);
                    db.SchoolMeta.Add(row);
                    await db.SaveChangesAsync();
                    // INSERT `true` DEFAULT'ni qo'yib yuborgan bo'lishi mumkin — asl qiymat UPDATE bilan.
                    (row.ArchiveOnlyNonDebtorStudents, row.ShowLearningProgressInParentDashboard) = flags;
                    db.Entry(row).Property(r => r.ArchiveOnlyNonDebtorStudents).IsModified = true;
                    db.Entry(row).Property(r => r.ShowLearningProgressInParentDashboard).IsModified = true;
                    await db.SaveChangesAsync();
                }
            });
        }
    }
}

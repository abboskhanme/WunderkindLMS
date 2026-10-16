using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// "O'quvchilar oqimi" (docs/modules/student-flow.md, 2026-10-15): every way in and out writes a
/// <c>student_events</c> row with the right class and reason; the backfill is re-run safe; the
/// report's totals, reasons, classes and "activated" (first payment in the period); RBAC.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class StudentFlowTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Students = "/api/admin/students";
    private const string Report = "/api/admin/reports/student-flow";

    // Catalog rows seeded by parity_wave2_seed.sql.
    private static readonly Guid Graduated = new("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid OtherSchool = new("00000000-0000-0000-0000-0000000000a2");
    private static readonly Guid Moved = new("00000000-0000-0000-0000-0000000000a3");
    private static readonly Guid Family = new("00000000-0000-0000-0000-0000000000a4");
    private static readonly Guid Payment = new("00000000-0000-0000-0000-0000000000a5");

    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var cs in _connectionStrings) NpgsqlConnection.ClearPool(new NpgsqlConnection(cs));
        return Task.CompletedTask;
    }

    // =====================================================================
    //  Writers
    // =====================================================================

    [Fact]
    public async Task Yaratish_arxiv_qaytarish_sana_tahriri_va_ochirish_hodisa_yozadi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        var cls = await SeedClassAsync($"5-{tag}", 5);

        var payload = Payload(tag, cls.Name);
        var id = (await JsonAsync(await admin.PostAsJsonAsync(Students, payload))).GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"{Students}/{id}/archive",
            new { reason = "Yangi maktab", archiveReasonId = OtherSchool })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"{Students}/{id}/restore", new { })).StatusCode);

        // The enrolment day edited on the card moves the arrival.
        payload["className"] = "";
        payload["enrollmentDate"] = "2026-08-15";
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"{Students}/{id}", payload)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Students}/{id}")).StatusCode);

        await fixture.Api.WithDbAsync(async db =>
        {
            var events = await db.StudentEvents.AsNoTracking().Where(e => e.StudentId == id)
                .OrderBy(e => e.CreatedAt).ToListAsync();
            Assert.Equal([StudentEventKind.Enrolled, StudentEventKind.Left, StudentEventKind.Returned, StudentEventKind.Left],
                events.Select(e => e.Kind));

            var enrolled = events[0];
            Assert.Equal((new DateOnly(2026, 8, 15), cls.Id, cls.Name, StudentEventSource.Manual),
                (enrolled.OnDate, enrolled.ClassId, enrolled.ClassName, enrolled.Source));

            // The class is the one BEFORE archiving emptied it.
            var left = events[1];
            Assert.Equal((cls.Id, cls.Name, (Guid?)OtherSchool, "Yangi maktab", StudentEventSource.Manual, AppClock.Today),
                (left.ClassId, left.ClassName, left.ReasonId, left.ReasonText, left.Source, left.OnDate));
            Assert.False(string.IsNullOrEmpty(left.ActorId));

            Assert.Equal(StudentEventSource.Manual, events[2].Source);
            Assert.Equal(StudentEventSource.Delete, events[3].Source);
            Assert.False(await db.Students.AnyAsync(s => s.Id == id));
        });
    }

    [Fact]
    public async Task Ommaviy_va_sinf_arxivi_sababi_va_sinfi_bilan()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        var (bulkClass, a, b) = await SeedClassWithPupilsAsync($"6-{tag}", 6);
        var (wholeClass, c, d) = await SeedClassWithPupilsAsync($"7-{tag}", 7);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"{Students}/archive-many",
            new { studentIds = new[] { a, b }, reason = "To'lov", archiveReasonId = Payment })).StatusCode);

        // The class archive now takes a catalog reason too.
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/admin/classes/{wholeClass.Id}/archive",
            new { archiveReasonId = Moved, reason = "Ko'chishdi" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/admin/classes/{wholeClass.Id}/unarchive", null)).StatusCode);

        await fixture.Api.WithDbAsync(async db =>
        {
            var bulk = await db.StudentEvents.AsNoTracking().Where(e => e.StudentId == a || e.StudentId == b).ToListAsync();
            Assert.Equal(2, bulk.Count);
            Assert.All(bulk, e => Assert.Equal(
                (StudentEventKind.Left, StudentEventSource.Bulk, bulkClass.Id, bulkClass.Name, (Guid?)Payment, "To'lov"),
                (e.Kind, e.Source, e.ClassId, e.ClassName, e.ReasonId, e.ReasonText)));

            var whole = await db.StudentEvents.AsNoTracking().Where(e => e.StudentId == c || e.StudentId == d)
                .OrderBy(e => e.CreatedAt).ToListAsync();
            Assert.Equal(4, whole.Count);
            Assert.All(whole.Where(e => e.Kind == StudentEventKind.Left), e => Assert.Equal(
                (StudentEventSource.ClassArchive, wholeClass.Id, (Guid?)Moved, "Ko'chishdi"),
                (e.Source, e.ClassId, e.ReasonId, e.ReasonText)));
            Assert.All(whole.Where(e => e.Kind == StudentEventKind.Returned), e => Assert.Equal(
                (StudentEventSource.ClassArchive, wholeClass.Id, wholeClass.Name), (e.Source, e.ClassId, e.ClassName)));
            Assert.Equal(2, whole.Count(e => e.Kind == StudentEventKind.Returned));
        });
    }

    [Fact]
    public async Task Bitiruvchilar_maktabni_bitirdi_sababi_bilan_ketadi()
    {
        await using var db = await FreshAsync("flow_graduation");
        db.SchoolMeta.Add(new SchoolMeta { CurrentYear = "2026/2027", Name = "Test" });
        var cls = new SchoolClass { Name = "11-A", Grade = 11 };
        db.Classes.Add(cls);
        var paid = Pupil("To'lagan Bitiruvchi", "11-A");
        var unpaid = Pupil("To'lamagan Bitiruvchi", "11-A");
        db.Students.AddRange(paid, unpaid);
        var cashier = await CashierAsync(db);
        db.Payments.Add(new Payment
        {
            ReceiptNo = 1, StudentId = paid.Id, Amount = 100_000m, Method = PaymentMethod.Cash,
            CashierId = cashier, ReceivedAt = AppClock.NowInstant,
        });
        await db.SaveChangesAsync();

        var controller = new AcademicYearController(db, new AuditService(db, new HttpContextAccessor()));
        var result = await controller.Rollover(new RolloverRequest("2027/2028", true, false, false, false, false));
        Assert.Equal(2, result.Value!.Graduated);

        var events = await db.StudentEvents.AsNoTracking().ToListAsync();
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal(
            (StudentEventKind.Left, StudentEventSource.Graduation, cls.Id, "11-A", (Guid?)Graduated, "Bitirdi"),
            (e.Kind, e.Source, e.ClassId, e.ClassName, e.ReasonId, e.ReasonText)));
        // The deleted graduate keeps a trace; the archived one gets the catalog reason on the card.
        Assert.False(await db.Students.AnyAsync(s => s.Id == unpaid.Id));
        Assert.Equal(Graduated, (await db.Students.AsNoTracking().SingleAsync(s => s.Id == paid.Id)).ArchiveReasonId);
    }

    // =====================================================================
    //  Backfill
    // =====================================================================

    [Fact]
    public async Task Backfill_tarixdan_yozadi_va_qayta_yurganda_hech_narsa_qoshmaydi()
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("flow_backfill");
        _connectionStrings.Add(database.OwnerConnectionString);
        await using (var db = PostgresFixture.NewContext(database.OwnerConnectionString))
        {
            var five = new SchoolClass { Name = "5-A", Grade = 5 };
            db.Classes.Add(five);
            var active = Pupil("Faol Bola", "5-A", enrolled: "2025-09-01");
            var noDate = Pupil("Sanasiz Bola", "5-A", enrolled: "");          // no date, no membership — no arrival
            var left = Pupil("Ketgan Bola", "", enrolled: "2024-09-01");
            left.IsArchived = true;
            left.ArchivedAt = "2026-03-10";
            left.ArchiveReason = "Boshqa maktab";
            left.ArchiveReasonId = OtherSchool;
            var graduate = Pupil("Bitirgan Bola", "11-B", enrolled: "2015-09-01");
            graduate.IsArchived = true;
            graduate.ArchivedAt = "2026-06-01";
            graduate.ArchiveReason = "Bitirdi";
            var returned = Pupil("Qaytgan Bola", "5-A", enrolled: "2023-09-01");
            db.Students.AddRange(active, noDate, left, graduate, returned);
            db.ClassMemberships.Add(new ClassMembership
            {
                StudentId = left.Id, ClassId = five.Id, JoinedOn = new DateOnly(2024, 9, 1), LeftOn = new DateOnly(2026, 3, 10),
            });
            var deletedId = Guid.NewGuid().ToString();
            var deletedArchivedId = Guid.NewGuid().ToString();
            db.AuditLogs.AddRange(
                Audit(AuditService.EntityStudentDiscount, returned.Id, "2026-01-20T09:00:00", "O'quvchi arxivga ko'chirildi (Qaytgan Bola): \"oilaviy\""),
                Audit(AuditService.EntityStudentDiscount, returned.Id, "2026-02-01T09:00:00", "O'quvchi arxivdan qaytarildi (Qaytgan Bola)"),
                Audit(AuditService.EntityStudentDiscount, left.Id, "2026-03-10T11:00:00", "O'quvchi arxivga ko'chirildi (Ketgan Bola): \"Boshqa maktab\"", actor: "Kotiba"),
                Audit(AuditService.EntityStudentDelete, deletedId, "2026-04-05T12:00:00", "O'quvchi butunlay o'chirildi (O'chgan Bola, 5-A)",
                    before: """{"FullName":"O'chgan Bola","ClassName":"5-A","IsArchived":false}"""),
                Audit(AuditService.EntityStudentDelete, deletedArchivedId, "2026-04-06T12:00:00", "O'quvchi butunlay o'chirildi (Arxivdan O'chgan, )",
                    before: """{"FullName":"Arxivdan O'chgan","ClassName":"","IsArchived":true}"""));
            await db.SaveChangesAsync();

            await RunBackfillAsync(database.OwnerConnectionString);
            var first = await db.StudentEvents.AsNoTracking().ToListAsync();
            await RunBackfillAsync(database.OwnerConnectionString);
            Assert.Equal(first.Count, await db.StudentEvents.CountAsync());

            Assert.All(first, e => Assert.Equal(StudentEventSource.Backfill, e.Source));
            // Arrivals: everyone with a readable date.
            Assert.Equal(
                new[] { active.Id, graduate.Id, left.Id, returned.Id }.Order(),
                first.Where(e => e.Kind == StudentEventKind.Enrolled).Select(e => e.StudentId).Order());
            Assert.Equal(9, first.Count);
            Assert.Equal(new DateOnly(2025, 9, 1), first.Single(e => e.Kind == StudentEventKind.Enrolled && e.StudentId == active.Id).OnDate);

            var current = first.Single(e => e.Kind == StudentEventKind.Left && e.StudentId == left.Id);
            Assert.Equal((new DateOnly(2026, 3, 10), five.Id, "5-A", (Guid?)OtherSchool, "Boshqa maktab", "Kotiba"),
                (current.OnDate, current.ClassId, current.ClassName, current.ReasonId, current.ReasonText, current.ActorName));
            var grad = first.Single(e => e.Kind == StudentEventKind.Left && e.StudentId == graduate.Id);
            Assert.Equal(("11-B", (Guid?)Graduated), (grad.ClassName, grad.ReasonId));

            var earlier = first.Single(e => e.Kind == StudentEventKind.Left && e.StudentId == returned.Id);
            Assert.Equal((new DateOnly(2026, 1, 20), "oilaviy"), (earlier.OnDate, earlier.ReasonText));
            Assert.Equal(new DateOnly(2026, 2, 1), first.Single(e => e.Kind == StudentEventKind.Returned).OnDate);

            var deleted = first.Single(e => e.StudentId == deletedId);
            Assert.Equal((StudentEventKind.Left, "O'chgan Bola", five.Id, new DateOnly(2026, 4, 5)),
                (deleted.Kind, deleted.StudentName, deleted.ClassId, deleted.OnDate));
            Assert.DoesNotContain(first, e => e.StudentId == deletedArchivedId || e.StudentId == noDate.Id);
        }
    }

    // =====================================================================
    //  Report
    // =====================================================================

    [Fact]
    public async Task Hisobot_jami_sabab_sinf_va_faollashganlar()
    {
        await using var db = await FreshAsync("flow_report");
        var five = new SchoolClass { Name = "5-A", Grade = 5 };
        var six = new SchoolClass { Name = "6-A", Grade = 6 };
        db.Classes.AddRange(five, six);
        var a1 = Pupil("A1 Yangi", "5-A");
        var a2 = Pupil("A2 Eski", "5-A");
        var p1 = Pupil("P1 Tolagan", "6-A");
        var p2 = Pupil("P2 Oldin", "6-A");
        var r1 = Pupil("R1 Qaytgan", "");
        var l1 = Pupil("L1 Ketgan", ""); l1.IsArchived = true;
        var l2 = Pupil("L2 Ketgan", ""); l2.IsArchived = true;
        var l3 = Pupil("L3 Keyin", ""); l3.IsArchived = true;
        db.Students.AddRange(a1, a2, p1, p2, r1, l1, l2, l3);

        var old = new DateOnly(2025, 9, 1);
        StudentEvent Ev(Student s, string kind, DateOnly on, SchoolClass? c = null, Guid? reason = null) => new()
        {
            StudentId = s.Id, StudentName = s.FullName, Kind = kind, OnDate = on, ClassId = c?.Id, ClassName = c?.Name,
            ReasonId = reason, Source = StudentEventSource.Manual, CreatedAt = AppClock.NowInstant,
        };
        db.StudentEvents.AddRange(
            Ev(a1, StudentEventKind.Enrolled, new DateOnly(2026, 10, 3), five),
            Ev(a2, StudentEventKind.Enrolled, old, five), Ev(p1, StudentEventKind.Enrolled, old, six),
            Ev(p2, StudentEventKind.Enrolled, old, six), Ev(r1, StudentEventKind.Enrolled, old, six),
            Ev(l1, StudentEventKind.Enrolled, old, five), Ev(l2, StudentEventKind.Enrolled, old, six),
            Ev(l3, StudentEventKind.Enrolled, old, six),
            Ev(l1, StudentEventKind.Left, new DateOnly(2026, 10, 10), five, OtherSchool),
            Ev(l2, StudentEventKind.Left, new DateOnly(2026, 10, 12), six),
            Ev(r1, StudentEventKind.Left, new DateOnly(2026, 9, 15), six),
            Ev(r1, StudentEventKind.Returned, new DateOnly(2026, 10, 20)),
            Ev(l3, StudentEventKind.Left, new DateOnly(2026, 11, 2), six));

        var cashier = await CashierAsync(db);
        long receipt = 1;
        Payment Pay(Student s, DateOnly day, Guid? reversalOf = null) => new()
        {
            ReceiptNo = receipt++, StudentId = s.Id, Amount = 100_000m, Method = PaymentMethod.Cash,
            CashierId = cashier, ReceivedAt = AppClock.InstantOn(day).AddHours(10), ReversalOf = reversalOf,
        };
        db.Payments.AddRange(Pay(p1, new DateOnly(2026, 10, 5)), Pay(p2, new DateOnly(2026, 9, 1)), Pay(p2, new DateOnly(2026, 10, 7)));
        await db.SaveChangesAsync();

        var r = await StudentFlowReport.BuildAsync(db, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), null);
        Assert.Equal(new StudentFlowTotalsDto(New: 1, Activated: 1, Left: 2, Returned: 1, Net: 0, HeadcountStart: 6, HeadcountEnd: 6), r.Totals);
        Assert.Equal(p1.Id, Assert.Single(r.ActivatedList).StudentId);
        Assert.Equal([("Boshqa", 1, 50.0), ("Boshqa maktabga o'tdi", 1, 50.0)],
            r.ByReason.Select(x => (x.Name, x.Count, x.Percent)).OrderBy(x => x.Name, StringComparer.Ordinal));
        Assert.Equal(["5-A", "6-A", "Sinfsiz"], r.ByClass.Select(c => c.ClassName));
        var x5 = r.ByClass.Single(c => c.ClassName == "5-A");
        Assert.Equal((1, 1, 0), (x5.New, x5.Left, x5.Returned));
        Assert.Equal(1, r.ByClass.Single(c => c.ClassName == "Sinfsiz").Returned);
        Assert.Equal([l1.Id, l2.Id], r.LeftList.Select(x => x.StudentId));
        Assert.Equal("Boshqa maktabga o'tdi", r.LeftList[0].Reason);

        // One class: its own arrivals and departures, its headcount walked back the same way.
        var c5 = await StudentFlowReport.BuildAsync(db, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), five.Id);
        Assert.Equal(new StudentFlowTotalsDto(1, 0, 1, 0, 0, 2, 2), c5.Totals);
    }

    // =====================================================================
    //  API: RBAC, reason edit, export
    // =====================================================================

    [Fact]
    public async Task Hisobot_ruxsati_va_sababni_keyin_ozgartirish()
    {
        var period = $"?from={AppClock.Today:yyyy-MM-dd}&to={AppClock.Today:yyyy-MM-dd}";
        using var anonymous = fixture.Api.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Report + period)).StatusCode);
        using var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync(Report + period)).StatusCode);
        // Read gated: another admin key does not open the pupils' departures.
        using var other = await fixture.Api.ClientAsAsync(Roles.Staff, "finance");
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync(Report + period)).StatusCode);
        using var viewer = await fixture.Api.ClientAsAsync(Roles.Staff, "students" + Roles.ViewSuffix);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(Report + period)).StatusCode);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"{Report}?from=2026-10-31&to=2026-10-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync(Report)).StatusCode);

        // A pupil archived without a catalog reason; the reason is set later from the report.
        var tag = Tag();
        var cls = await SeedClassAsync($"8-{tag}", 8);
        var payload = Payload(tag, cls.Name);
        payload["enrollmentDate"] = AppClock.Today.ToString("yyyy-MM-dd");
        var id = (await JsonAsync(await admin.PostAsJsonAsync(Students, payload))).GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"{Students}/{id}/archive", new { reason = "ketdi" })).StatusCode);

        var report = await JsonAsync(await admin.GetAsync(Report + period));
        var row = report.GetProperty("leftList").EnumerateArray().Single(r => r.GetProperty("studentId").GetString() == id);
        Assert.Equal("Boshqa", report.GetProperty("byReason").EnumerateArray()
            .First(r => r.GetProperty("reasonId").ValueKind == JsonValueKind.Null).GetProperty("name").GetString());
        var eventId = row.GetProperty("eventId").GetString()!;
        var enrolledId = report.GetProperty("newList").EnumerateArray().Single(r => r.GetProperty("studentId").GetString() == id)
            .GetProperty("eventId").GetString()!;

        var url = $"{Report}/events/{eventId}/reason";
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync(url, new { reasonId = Family, reasonText = "oila" })).StatusCode);
        using var editor = await fixture.Api.ClientAsAsync(Roles.Staff, "students");
        var saved = await editor.PutAsJsonAsync(url, new { reasonId = Family, reasonText = "oila" });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal("Oilaviy sabablarga ko'ra", (await JsonAsync(saved)).GetProperty("reason").GetString());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PutAsJsonAsync($"{Report}/events/{enrolledId}/reason", new { reasonId = Family })).StatusCode);

        await fixture.Api.WithDbAsync(async db =>
        {
            var ev = await db.StudentEvents.AsNoTracking().SingleAsync(e => e.Id == Guid.Parse(eventId));
            Assert.Equal(((Guid?)Family, "oila"), (ev.ReasonId, ev.ReasonText));
            // Still archived, latest departure — the card follows.
            var pupil = await db.Students.AsNoTracking().SingleAsync(s => s.Id == id);
            Assert.Equal(((Guid?)Family, "oila"), (pupil.ArchiveReasonId, pupil.ArchiveReason));
            Assert.True(await db.AuditLogs.AnyAsync(a => a.EntityType == AuditService.EntityStudentEvent && a.EntityId == eventId));
        });

        var file = await admin.GetAsync($"{Report}/export{period}");
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.Content.Headers.ContentType?.MediaType);
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    private static string Tag() => "SF" + Guid.NewGuid().ToString("N")[..8];

    private static Student Pupil(string fullName, string className, string enrolled = "2025-09-01") => new()
    {
        FullName = fullName, LastName = fullName.Split(' ')[^1], FirstName = fullName.Split(' ')[0],
        BirthDate = "2012-01-01", Gender = "male", ClassName = className, EnrollmentDate = enrolled,
    };

    private static AuditLog Audit(string type, string studentId, string at, string summary, string? before = null, string? actor = "Admin") => new()
    {
        EntityType = type, EntityId = studentId, Action = "update", Timestamp = at, ActorName = actor,
        Summary = summary, Before = before, StudentId = studentId,
    };

    private async Task<SchoolClass> SeedClassAsync(string name, int grade)
    {
        var cls = new SchoolClass { Name = name, Grade = grade };
        await fixture.Api.WithDbAsync(async db => { db.Classes.Add(cls); await db.SaveChangesAsync(); });
        return cls;
    }

    private async Task<(SchoolClass Class, string A, string B)> SeedClassWithPupilsAsync(string name, int grade)
    {
        var cls = new SchoolClass { Name = name, Grade = grade };
        var a = Pupil($"Birinchi {name}", name);
        var b = Pupil($"Ikkinchi {name}", name);
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(cls);
            db.Students.AddRange(a, b);
            foreach (var s in new[] { a, b })
                db.ClassMemberships.Add(new ClassMembership
                {
                    StudentId = s.Id, ClassId = cls.Id, JoinedOn = new DateOnly(2025, 9, 1), CreatedAt = AppClock.NowInstant,
                });
            await db.SaveChangesAsync();
        });
        return (cls, a.Id, b.Id);
    }

    private static Dictionary<string, object?> Payload(string tag, string className) => new()
    {
        ["fullName"] = $"Oqim {tag}",
        ["birthDate"] = "2014-04-04",
        ["address"] = "Toshkent",
        ["gender"] = "female",
        ["parentFullName"] = $"Ota-ona {tag}",
        ["parentPhone"] = "+998" + Random.Shared.Next(100_000_000, 999_999_999),
        ["className"] = className,
        ["enrollmentDate"] = "2026-09-01",
    };

    private async Task<AppDbContext> FreshAsync(string name)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync(name);
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }

    private static async Task<string> CashierAsync(AppDbContext db)
    {
        var user = new AppUser { FullName = "Kassir", Role = Roles.Admin, Email = $"flow.{Guid.NewGuid():N}" };
        user.SetInitialPassword("Test-" + Guid.NewGuid().ToString("N")[..10]);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>The migration's backfill file, run as the migration runs it (owner, plain SQL).</summary>
    private static async Task RunBackfillAsync(string connectionString)
    {
        const string resource = "SchoolLms.Infrastructure.Migrations.Sql.student_events_backfill.sql";
        await using var stream = typeof(AppDbContext).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException(resource);
        var sql = await new StreamReader(stream).ReadToEndAsync();
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }
}

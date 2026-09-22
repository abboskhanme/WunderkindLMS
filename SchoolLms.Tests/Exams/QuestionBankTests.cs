using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Exams;

// ===========================================================================
//  Test bazasi — the admission question bank over real HTTP.
//  docs/modules/admission-and-testing.md §4.3 (read gating), §5.1–§5.3,
//  §6.2 (contract), §8.2 (the bank cannot shrink under a live exam). Unit: B1.
// ===========================================================================
//
//  1. RBAC. Every other admin section lets any `staff` account READ. The bank
//     must not: its question list is the answer key of a live entrance exam
//     (§4.3). So the matrix checks GETs as hard as writes — staff without
//     `admission` gets 403 on every GET, staff with it gets 200, admin and
//     superadmin always get 200. One `[InlineData]` per cell, the
//     `RbacMatrixTests` / `SalesMarketingRbacTests` shape; a refused cell also
//     checks the body is empty and nothing was written.
//
//  2. BANKS — create / 409 / state / list / delete.
//  3. QUESTIONS — option order, the one-correct rule, the two-pass key move,
//     and the three 409s that protect exam papers.
//
//  Each test makes its own subject, so the shared database's other banks never
//  collide with `ux_question_banks_live`.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class QuestionBankTests(ApiFixture fixture)
{
    private const string Anonymous = "anonymous";
    private const string StaffAdmission = "staff+admission";
    private const string StaffLeads = "staff+leads";
    private const string Parent = "parent";

    private const string Banks = QuestionBankKit.Banks;
    private const string Questions = QuestionBankKit.Questions;

    private AppDbContext NewDb() => PostgresFixture.NewContext(fixture.Database.OwnerConnectionString);

    // =====================================================================
    //  1. RBAC — reads are gated (§4.3)
    // =====================================================================

    [Theory]
    [InlineData(Anonymous, HttpStatusCode.Unauthorized)]
    [InlineData(Roles.Teacher, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Cashier, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Staff, HttpStatusCode.Forbidden)]
    [InlineData(StaffLeads, HttpStatusCode.Forbidden)]
    [InlineData(StaffAdmission, HttpStatusCode.OK)]
    [InlineData(Roles.Admin, HttpStatusCode.OK)]
    [InlineData(Roles.SuperAdmin, HttpStatusCode.OK)]
    [InlineData(Parent, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Student, HttpStatusCode.Forbidden)]
    public async Task Rbac_GET_bank_questions_with_the_answer_key(string who, HttpStatusCode expected)
    {
        var bank = await QuestionBankKit.SeedBankAsync(fixture.Api);
        var question = await QuestionBankKit.SeedQuestionAsync(fixture.Api, bank.Id, "RBAC savol", ["A", "B", "C"], correct: 2);
        using var actor = await ActorAsync(who);

        var response = await actor.Client.GetAsync($"{Banks}/{bank.Id}/questions");

        if (await RefusedAsync(response, expected)) return;
        using var doc = await JsonAsync(response);
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(question.Id, item.GetProperty("id").GetString());
        var flags = item.GetProperty("options").EnumerateArray().Select(o => o.GetProperty("isCorrect").GetBoolean()).ToList();
        Assert.Equal(new[] { false, false, true }, flags);
    }

    [Theory]
    [InlineData(Anonymous, HttpStatusCode.Unauthorized)]
    [InlineData(Roles.Teacher, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Cashier, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Staff, HttpStatusCode.Forbidden)]
    [InlineData(StaffLeads, HttpStatusCode.Forbidden)]
    [InlineData(StaffAdmission, HttpStatusCode.OK)]
    [InlineData(Roles.Admin, HttpStatusCode.OK)]
    [InlineData(Roles.SuperAdmin, HttpStatusCode.OK)]
    [InlineData(Parent, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Student, HttpStatusCode.Forbidden)]
    public async Task Rbac_GET_bank_list(string who, HttpStatusCode expected)
    {
        var bank = await QuestionBankKit.SeedBankAsync(fixture.Api);
        using var actor = await ActorAsync(who);

        var response = await actor.Client.GetAsync($"{Banks}?subjectId={bank.SubjectId}");

        if (await RefusedAsync(response, expected)) return;
        using var doc = await JsonAsync(response);
        var row = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(bank.Id, row.GetProperty("id").GetString());
    }

    [Theory]
    [InlineData(Anonymous, HttpStatusCode.Unauthorized)]
    [InlineData(Roles.Teacher, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Staff, HttpStatusCode.Forbidden)]
    [InlineData(StaffLeads, HttpStatusCode.Forbidden)]
    [InlineData(StaffAdmission, HttpStatusCode.OK)]
    [InlineData(Roles.Admin, HttpStatusCode.OK)]
    [InlineData(Roles.SuperAdmin, HttpStatusCode.OK)]
    public async Task Rbac_GET_one_bank(string who, HttpStatusCode expected)
    {
        var bank = await QuestionBankKit.SeedBankAsync(fixture.Api);
        using var actor = await ActorAsync(who);

        var response = await actor.Client.GetAsync($"{Banks}/{bank.Id}");

        if (await RefusedAsync(response, expected)) return;
        using var doc = await JsonAsync(response);
        Assert.Equal(bank.Id, doc.RootElement.GetProperty("id").GetString());
    }

    [Theory]
    [InlineData(Anonymous, HttpStatusCode.Unauthorized)]
    [InlineData(Roles.Teacher, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Staff, HttpStatusCode.Forbidden)]
    [InlineData(StaffLeads, HttpStatusCode.Forbidden)]
    [InlineData(StaffAdmission, HttpStatusCode.OK)]
    [InlineData(Roles.Admin, HttpStatusCode.OK)]
    [InlineData(Roles.SuperAdmin, HttpStatusCode.OK)]
    public async Task Rbac_GET_import_template(string who, HttpStatusCode expected)
    {
        var bank = await QuestionBankKit.SeedBankAsync(fixture.Api);
        using var actor = await ActorAsync(who);

        var response = await actor.Client.GetAsync($"{Questions}/import/template?bankId={bank.Id}");

        if (await RefusedAsync(response, expected)) return;
        Assert.Equal(QuestionImportService.XlsxMime, response.Content.Headers.ContentType?.MediaType);
    }

    // =====================================================================
    //  1b. RBAC — writes need `admission` (as everywhere)
    // =====================================================================

    [Theory]
    [InlineData(Anonymous, HttpStatusCode.Unauthorized)]
    [InlineData(Roles.Teacher, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Staff, HttpStatusCode.Forbidden)]
    [InlineData(StaffLeads, HttpStatusCode.Forbidden)]
    [InlineData(StaffAdmission, HttpStatusCode.OK)]
    [InlineData(Roles.Admin, HttpStatusCode.OK)]
    public async Task Rbac_POST_bank(string who, HttpStatusCode expected)
    {
        var subject = await QuestionBankKit.SeedSubjectAsync(fixture.Api);
        using var actor = await ActorAsync(who);

        var response = await actor.Client.PostAsJsonAsync(Banks, new { grade = 5, subjectId = subject.Id });

        await using var db = NewDb();
        var row = await db.QuestionBanks.AsNoTracking().SingleOrDefaultAsync(b => b.SubjectId == subject.Id);
        if (await RefusedAsync(response, expected))
        {
            Assert.Null(row);
            return;
        }
        Assert.NotNull(row);
        Assert.Equal(actor.User!.Id, row.CreatedByUserId);
    }

    [Theory]
    [InlineData(Anonymous, HttpStatusCode.Unauthorized)]
    [InlineData(Roles.Teacher, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Staff, HttpStatusCode.Forbidden)]
    [InlineData(StaffLeads, HttpStatusCode.Forbidden)]
    [InlineData(StaffAdmission, HttpStatusCode.OK)]
    [InlineData(Roles.Admin, HttpStatusCode.OK)]
    public async Task Rbac_PUT_bank_settings(string who, HttpStatusCode expected)
    {
        var bank = await QuestionBankKit.SeedBankAsync(fixture.Api);
        using var actor = await ActorAsync(who);

        var response = await actor.Client.PutAsJsonAsync($"{Banks}/{bank.Id}",
            new { questionsPerTest = 7, timeLimitMin = 40, pointsPerCorrect = 2m });

        await using var db = NewDb();
        var row = await db.QuestionBanks.AsNoTracking().SingleAsync(b => b.Id == bank.Id);
        if (await RefusedAsync(response, expected))
        {
            Assert.Null(row.QuestionsPerTest);
            return;
        }
        Assert.Equal(7, row.QuestionsPerTest);
    }

    [Theory]
    [InlineData(Anonymous, HttpStatusCode.Unauthorized)]
    [InlineData(Roles.Teacher, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Staff, HttpStatusCode.Forbidden)]
    [InlineData(StaffLeads, HttpStatusCode.Forbidden)]
    [InlineData(StaffAdmission, HttpStatusCode.NoContent)]
    [InlineData(Roles.Admin, HttpStatusCode.NoContent)]
    public async Task Rbac_DELETE_bank(string who, HttpStatusCode expected)
    {
        var bank = await QuestionBankKit.SeedBankAsync(fixture.Api);
        using var actor = await ActorAsync(who);

        var response = await actor.Client.DeleteAsync($"{Banks}/{bank.Id}");

        await using var db = NewDb();
        var exists = await db.QuestionBanks.AnyAsync(b => b.Id == bank.Id);
        if (await RefusedAsync(response, expected))
        {
            Assert.True(exists);
            return;
        }
        Assert.False(exists);
    }

    [Theory]
    [InlineData(Anonymous, HttpStatusCode.Unauthorized)]
    [InlineData(Roles.Teacher, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Staff, HttpStatusCode.Forbidden)]
    [InlineData(StaffLeads, HttpStatusCode.Forbidden)]
    [InlineData(StaffAdmission, HttpStatusCode.OK)]
    [InlineData(Roles.Admin, HttpStatusCode.OK)]
    public async Task Rbac_POST_question(string who, HttpStatusCode expected)
    {
        var bank = await QuestionBankKit.SeedBankAsync(fixture.Api);
        using var actor = await ActorAsync(who);

        var response = await actor.Client.PostAsJsonAsync(Questions, QuestionBankKit.QuestionBody(bank.Id, "Yangi savol"));

        await using var db = NewDb();
        var count = await db.Questions.CountAsync(q => q.BankId == bank.Id);
        if (await RefusedAsync(response, expected))
        {
            Assert.Equal(0, count);
            return;
        }
        Assert.Equal(1, count);
    }

    [Theory]
    [InlineData(Anonymous, HttpStatusCode.Unauthorized)]
    [InlineData(Roles.Teacher, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Staff, HttpStatusCode.Forbidden)]
    [InlineData(StaffLeads, HttpStatusCode.Forbidden)]
    [InlineData(StaffAdmission, HttpStatusCode.OK)]
    [InlineData(Roles.Admin, HttpStatusCode.OK)]
    public async Task Rbac_PUT_question(string who, HttpStatusCode expected)
    {
        var bank = await QuestionBankKit.SeedBankAsync(fixture.Api);
        var question = await QuestionBankKit.SeedQuestionAsync(fixture.Api, bank.Id, "Eski matn", ["A", "B"], correct: 0);
        using var actor = await ActorAsync(who);

        var response = await actor.Client.PutAsJsonAsync($"{Questions}/{question.Id}", new
        {
            text = "Yangi matn",
            imageUrl = (string?)null,
            options = question.Options.OrderBy(o => o.Order)
                .Select(o => new { id = o.Id, text = o.Text, isCorrect = o.IsCorrect }).ToArray(),
        });

        await using var db = NewDb();
        var text = await db.Questions.Where(q => q.Id == question.Id).Select(q => q.Text).SingleAsync();
        if (await RefusedAsync(response, expected))
        {
            Assert.Equal("Eski matn", text);
            return;
        }
        Assert.Equal("Yangi matn", text);
    }

    [Theory]
    [InlineData(Anonymous, HttpStatusCode.Unauthorized)]
    [InlineData(Roles.Teacher, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Staff, HttpStatusCode.Forbidden)]
    [InlineData(StaffLeads, HttpStatusCode.Forbidden)]
    [InlineData(StaffAdmission, HttpStatusCode.NoContent)]
    [InlineData(Roles.Admin, HttpStatusCode.NoContent)]
    public async Task Rbac_DELETE_question(string who, HttpStatusCode expected)
    {
        var bank = await QuestionBankKit.SeedBankAsync(fixture.Api);
        var question = await QuestionBankKit.SeedQuestionAsync(fixture.Api, bank.Id, "O'chiriladi", ["A", "B"], correct: 1);
        using var actor = await ActorAsync(who);

        var response = await actor.Client.DeleteAsync($"{Questions}/{question.Id}");

        await using var db = NewDb();
        var exists = await db.Questions.AnyAsync(q => q.Id == question.Id);
        if (await RefusedAsync(response, expected))
        {
            Assert.True(exists);
            return;
        }
        Assert.False(exists);
    }

    [Theory]
    [InlineData(Anonymous, HttpStatusCode.Unauthorized)]
    [InlineData(Roles.Teacher, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Staff, HttpStatusCode.Forbidden)]
    [InlineData(StaffLeads, HttpStatusCode.Forbidden)]
    [InlineData(StaffAdmission, HttpStatusCode.OK)]
    [InlineData(Roles.Admin, HttpStatusCode.OK)]
    public async Task Rbac_POST_import(string who, HttpStatusCode expected)
    {
        var bank = await QuestionBankKit.SeedBankAsync(fixture.Api);
        using var actor = await ActorAsync(who);
        var sheet = QuestionBankKit.Sheet([QuestionBankKit.Row("2+2?", ["3", "4"], "B")]);

        var response = await actor.Client.PostAsync($"{Questions}/import",
            QuestionBankKit.ImportForm(sheet, bank.Id, dryRun: false));

        await using var db = NewDb();
        var count = await db.Questions.CountAsync(q => q.BankId == bank.Id);
        if (await RefusedAsync(response, expected))
        {
            Assert.Equal(0, count);
            return;
        }
        Assert.Equal(1, count);
    }

    // =====================================================================
    //  2. Banks
    // =====================================================================

    [Fact]
    public async Task Create_bank_starts_unconfigured_and_a_second_live_bank_for_the_same_grade_and_subject_is_409()
    {
        var subject = await QuestionBankKit.SeedSubjectAsync(fixture.Api);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var created = await admin.PostAsJsonAsync(Banks, new { grade = 5, subjectId = subject.Id });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using (var doc = await JsonAsync(created))
        {
            var bank = doc.RootElement;
            Assert.Equal(5, bank.GetProperty("grade").GetInt32());
            Assert.Equal(subject.Id, bank.GetProperty("subjectId").GetString());
            Assert.Equal(subject.Name, bank.GetProperty("subjectName").GetString());
            Assert.Equal(0, bank.GetProperty("questionsCount").GetInt32());
            Assert.Equal(JsonValueKind.Null, bank.GetProperty("questionsPerTest").ValueKind);
            Assert.Equal(JsonValueKind.Null, bank.GetProperty("timeLimitMin").ValueKind);
            Assert.Equal(JsonValueKind.Null, bank.GetProperty("pointsPerCorrect").ValueKind);
            Assert.Equal(QuestionBankState.Unconfigured, bank.GetProperty("state").GetString());
        }

        var duplicate = await admin.PostAsJsonAsync(Banks, new { grade = 5, subjectId = subject.Id, questionsPerTest = 3 });
        await AssertErrorAsync(duplicate, HttpStatusCode.Conflict, QuestionBankService.Codes.BankExists,
            QuestionBankService.BankExistsMessage);

        // Another grade of the same subject, and grade 0 — the preparatory "nol sinf" is real.
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync(Banks, new { grade = 6, subjectId = subject.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync(Banks, new { grade = 0, subjectId = subject.Id })).StatusCode);

        await using var db = NewDb();
        Assert.Equal(1, await db.QuestionBanks.CountAsync(b => b.SubjectId == subject.Id && b.Grade == 5));
        var bankId = await db.QuestionBanks.Where(b => b.SubjectId == subject.Id && b.Grade == 5).Select(b => b.Id).SingleAsync();
        Assert.True(await db.AuditLogs.AnyAsync(a =>
            a.EntityType == QuestionBankService.AuditEntityBank && a.EntityId == bankId && a.Action == "create"));
    }

    [Theory]
    [InlineData(null, null, null, null, QuestionBankService.GradeRequiredMessage)]
    [InlineData(12, null, null, null, QuestionBankService.GradeRangeMessage)]
    [InlineData(-1, null, null, null, QuestionBankService.GradeRangeMessage)]
    [InlineData(5, 0, null, null, QuestionBankService.QuestionsPerTestMessage)]
    [InlineData(5, null, 0, null, QuestionBankService.TimeLimitMessage)]
    [InlineData(5, null, 601, null, QuestionBankService.TimeLimitMessage)]
    [InlineData(5, null, null, "0", QuestionBankService.PointsMessage)]
    [InlineData(5, null, null, "1.555", QuestionBankService.PointsMessage)]
    [InlineData(5, null, null, "10000", QuestionBankService.PointsMessage)]
    public async Task Create_bank_refuses_bad_values_with_the_field_sentence(
        int? grade, int? questionsPerTest, int? timeLimitMin, string? points, string message)
    {
        var subject = await QuestionBankKit.SeedSubjectAsync(fixture.Api);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await admin.PostAsJsonAsync(Banks, new
        {
            grade,
            subjectId = subject.Id,
            questionsPerTest,
            timeLimitMin,
            pointsPerCorrect = points is null ? (decimal?)null : decimal.Parse(points, System.Globalization.CultureInfo.InvariantCulture),
        });

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, QuestionBankService.Codes.Validation, message);
        await using var db = NewDb();
        Assert.False(await db.QuestionBanks.AnyAsync(b => b.SubjectId == subject.Id));
    }

    [Fact]
    public async Task Create_bank_for_an_unknown_subject_is_404()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await admin.PostAsJsonAsync(Banks, new { grade = 5, subjectId = Guid.NewGuid().ToString() });

        await AssertErrorAsync(response, HttpStatusCode.NotFound, QuestionBankService.Codes.SubjectNotFound,
            QuestionBankService.SubjectNotFoundMessage);
    }

    /// <summary>
    /// §8.1 rule 3 / §3.1: grey until all three settings are set, amber while
    /// the bank is smaller than one sitting, green when it is big enough. And a
    /// PUT that changes nothing writes no audit row.
    /// </summary>
    [Fact]
    public async Task State_follows_the_settings_and_the_question_count()
    {
        var bank = await QuestionBankKit.SeedBankAsync(fixture.Api);
        await QuestionBankKit.SeedQuestionAsync(fixture.Api, bank.Id, "Birinchi", ["A", "B"], correct: 0);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        Assert.Equal(QuestionBankState.Unconfigured, await StateAsync(admin, bank.Id));

        // Two of three set — still unconfigured: publish would refuse it.
        var partial = await admin.PutAsJsonAsync($"{Banks}/{bank.Id}",
            new { questionsPerTest = 2, timeLimitMin = 30, pointsPerCorrect = (decimal?)null });
        Assert.Equal(HttpStatusCode.OK, partial.StatusCode);
        Assert.Equal(QuestionBankState.Unconfigured, await StateOfAsync(partial));

        var configured = await admin.PutAsJsonAsync($"{Banks}/{bank.Id}",
            new { questionsPerTest = 2, timeLimitMin = 30, pointsPerCorrect = 1.5m });
        using (var doc = await JsonAsync(configured))
        {
            Assert.Equal(QuestionBankState.NotEnough, doc.RootElement.GetProperty("state").GetString());
            Assert.Equal(1, doc.RootElement.GetProperty("questionsCount").GetInt32());
            Assert.Equal(1.5m, doc.RootElement.GetProperty("pointsPerCorrect").GetDecimal());
        }

        await QuestionBankKit.SeedQuestionAsync(fixture.Api, bank.Id, "Ikkinchi", ["A", "B"], correct: 1);
        Assert.Equal(QuestionBankState.Ready, await StateAsync(admin, bank.Id));

        int AuditRows(AppDbContext db) => db.AuditLogs.Count(a =>
            a.EntityType == QuestionBankService.AuditEntityBank && a.EntityId == bank.Id && a.Action == "update");
        int before;
        await using (var db = NewDb()) before = AuditRows(db);

        var same = await admin.PutAsJsonAsync($"{Banks}/{bank.Id}",
            new { questionsPerTest = 2, timeLimitMin = 30, pointsPerCorrect = 1.50m });
        Assert.Equal(QuestionBankState.Ready, await StateOfAsync(same));
        await using (var db = NewDb()) Assert.Equal(before, AuditRows(db));

        // `null` clears a value.
        var cleared = await admin.PutAsJsonAsync($"{Banks}/{bank.Id}",
            new { questionsPerTest = 2, timeLimitMin = (int?)null, pointsPerCorrect = 1.5m });
        Assert.Equal(QuestionBankState.Unconfigured, await StateOfAsync(cleared));
        await using (var db = NewDb())
        {
            Assert.Null(await db.QuestionBanks.Where(b => b.Id == bank.Id).Select(b => b.TimeLimitMin).SingleAsync());
            Assert.Equal(before + 1, AuditRows(db));
        }
    }

    [Fact]
    public async Task Bank_list_is_paged_searches_the_subject_name_caps_the_limit_and_hides_archived_banks()
    {
        var tag = QuestionBankKit.Tag();
        var subject = await QuestionBankKit.SeedSubjectAsync(fixture.Api, $"Kimyo {tag}");
        var live = await QuestionBankKit.SeedBankAsync(fixture.Api, subject, grade: 7);
        var archived = await QuestionBankKit.SeedBankAsync(fixture.Api, subject, grade: 7, archived: true);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        using (var doc = await JsonAsync(await admin.GetAsync($"{Banks}?search=KIMYO%20{tag.ToUpperInvariant()}")))
        {
            Assert.Equal(1, doc.RootElement.GetProperty("total").GetInt32());
            var row = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(live.Id, row.GetProperty("id").GetString());
        }

        using (var doc = await JsonAsync(await admin.GetAsync($"{Banks}?page=0&limit=500&subjectId={subject.Id}")))
        {
            Assert.Equal(1, doc.RootElement.GetProperty("page").GetInt32());
            Assert.Equal(QuestionBankService.MaxPageLimit, doc.RootElement.GetProperty("limit").GetInt32());
        }

        using (var doc = await JsonAsync(await admin.GetAsync($"{Banks}?grade=8&subjectId={subject.Id}")))
            Assert.Equal(0, doc.RootElement.GetProperty("total").GetInt32());

        // Archived: out of the register, still reachable by id.
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"{Banks}/{archived.Id}")).StatusCode);
        await AssertErrorAsync(await admin.GetAsync($"{Banks}/{Guid.NewGuid()}"), HttpStatusCode.NotFound,
            QuestionBankService.Codes.BankNotFound, QuestionBankService.BankNotFoundMessage);
    }

    [Fact]
    public async Task Delete_bank_is_409_while_an_exam_section_uses_it_and_cascades_to_questions_otherwise()
    {
        var used = await QuestionBankKit.SeedBankAsync(fixture.Api);
        await QuestionBankKit.SeedExamSectionAsync(fixture.Api, used, ExamStatus.Draft);
        var free = await QuestionBankKit.SeedBankAsync(fixture.Api);
        var question = await QuestionBankKit.SeedQuestionAsync(fixture.Api, free.Id, "Ketadi", ["A", "B", "C"], correct: 0);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        await AssertErrorAsync(await admin.DeleteAsync($"{Banks}/{used.Id}"), HttpStatusCode.Conflict,
            QuestionBankService.Codes.BankInUse, QuestionBankService.BankInUseMessage);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Banks}/{free.Id}")).StatusCode);

        await using var db = NewDb();
        Assert.True(await db.QuestionBanks.AnyAsync(b => b.Id == used.Id));
        Assert.False(await db.QuestionBanks.AnyAsync(b => b.Id == free.Id));
        Assert.False(await db.Questions.AnyAsync(q => q.Id == question.Id));
        Assert.False(await db.QuestionOptions.AnyAsync(o => o.QuestionId == question.Id));
        Assert.True(await db.AuditLogs.AnyAsync(a =>
            a.EntityType == QuestionBankService.AuditEntityBank && a.EntityId == free.Id && a.Action == "delete"));
    }

    // =====================================================================
    //  3. Questions
    // =====================================================================

    [Fact]
    public async Task Create_question_trims_keeps_array_order_as_option_order_and_appends_to_the_bank()
    {
        var bank = await QuestionBankKit.SeedBankAsync(fixture.Api);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var first = await admin.PostAsJsonAsync(Questions, new
        {
            bankId = bank.Id,
            text = "  2 + 2 = ?  ",
            imageUrl = "/uploads/0f8fad5b-d9cb-469f-a165-70867728950e.png",
            options = new[]
            {
                new { text = " 3 ", isCorrect = false },
                new { text = "4", isCorrect = true },
                new { text = "5", isCorrect = false },
            },
        });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using (var doc = await JsonAsync(first))
        {
            var q = doc.RootElement;
            Assert.Equal(bank.Id, q.GetProperty("bankId").GetString());
            Assert.Equal("2 + 2 = ?", q.GetProperty("text").GetString());
            Assert.Equal("/uploads/0f8fad5b-d9cb-469f-a165-70867728950e.png", q.GetProperty("imageUrl").GetString());
            Assert.Equal(0, q.GetProperty("order").GetInt32());
            var options = q.GetProperty("options").EnumerateArray().ToList();
            Assert.Equal(new[] { "3", "4", "5" }, options.Select(o => o.GetProperty("text").GetString()));
            Assert.Equal(new[] { 0, 1, 2 }, options.Select(o => o.GetProperty("order").GetInt32()));
            Assert.Equal(new[] { false, true, false }, options.Select(o => o.GetProperty("isCorrect").GetBoolean()));
        }

        var second = await admin.PostAsJsonAsync(Questions, QuestionBankKit.QuestionBody(bank.Id, "Keyingi"));
        using (var doc = await JsonAsync(second))
            Assert.Equal(1, doc.RootElement.GetProperty("order").GetInt32());

        await using var db = NewDb();
        var stored = await db.Questions.Include(q => q.Options).Where(q => q.BankId == bank.Id).ToListAsync();
        Assert.Equal(2, stored.Count);
        Assert.All(stored, q => Assert.Single(q.Options, o => o.IsCorrect));
    }

    public static TheoryData<string, object, string> InvalidQuestions => new()
    {
        { "blank text", new { text = "   ", options = Opts(("A", true), ("B", false)) }, QuestionBankService.TextRequiredMessage },
        { "one option", new { text = "Q", options = Opts(("A", true)) }, QuestionBankService.TooFewOptionsMessage },
        { "no options", new { text = "Q", options = (object?)null }, QuestionBankService.TooFewOptionsMessage },
        {
            "seven options",
            new { text = "Q", options = Opts(("1", true), ("2", false), ("3", false), ("4", false), ("5", false), ("6", false), ("7", false)) },
            QuestionBankService.TooManyOptionsMessage
        },
        { "blank B", new { text = "Q", options = Opts(("A", true), ("  ", false)) }, QuestionBankService.BlankOptionMessage(1) },
        { "A equals C", new { text = "Q", options = Opts(("x", true), ("y", false), (" x ", false)) }, QuestionBankService.DuplicateOptionMessage(0, 2) },
        { "no correct", new { text = "Q", options = Opts(("A", false), ("B", false)) }, QuestionBankService.NoCorrectMessage },
        { "two correct", new { text = "Q", options = Opts(("A", true), ("B", true)) }, QuestionBankService.ManyCorrectMessage },
        { "https image", new { text = "Q", imageUrl = "https://example.com/a.png", options = Opts(("A", true), ("B", false)) }, QuestionBankService.BadImageUrlMessage },
        { "escaping image", new { text = "Q", imageUrl = "/uploads/../appsettings.json", options = Opts(("A", true), ("B", false)) }, QuestionBankService.BadImageUrlMessage },
    };

    [Theory]
    [MemberData(nameof(InvalidQuestions))]
    public async Task Create_question_refuses_a_broken_question_and_writes_nothing(string name, object body, string message)
    {
        var bank = await QuestionBankKit.SeedBankAsync(fixture.Api);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        // The body without `bankId`, then with it: the bank is the one thing the case does not vary.
        var json = JsonSerializer.SerializeToNode(body, JsonSerializerOptions.Web)!.AsObject();
        json["bankId"] = bank.Id;
        var response = await admin.PostAsJsonAsync(Questions, json);

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{name}: {(int)response.StatusCode}");
        await AssertErrorAsync(response, HttpStatusCode.BadRequest, QuestionBankService.Codes.Validation, message);
        await using var db = NewDb();
        Assert.False(await db.Questions.AnyAsync(q => q.BankId == bank.Id));
    }

    [Fact]
    public async Task Create_question_in_an_unknown_bank_is_404()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await admin.PostAsJsonAsync(Questions, QuestionBankKit.QuestionBody(Guid.NewGuid().ToString(), "Q"));

        await AssertErrorAsync(response, HttpStatusCode.NotFound, QuestionBankService.Codes.BankNotFound,
            QuestionBankService.BankNotFoundMessage);
    }

    /// <summary>
    /// The whole §6.2 PUT contract in one request: the key moves A → B (the
    /// case the partial unique index rejects in a single batch), B's text
    /// changes, A moves to second place, C and D are dropped, a new option is
    /// added. Kept options keep their ids.
    /// </summary>
    [Fact]
    public async Task Update_question_moves_the_key_reorders_edits_adds_and_drops_options()
    {
        var bank = await QuestionBankKit.SeedBankAsync(fixture.Api);
        var question = await QuestionBankKit.SeedQuestionAsync(fixture.Api, bank.Id, "Eski", ["a", "b", "c", "d"], correct: 0);
        var a = question.Options.Single(o => o.Order == 0);
        var b = question.Options.Single(o => o.Order == 1);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await admin.PutAsJsonAsync($"{Questions}/{question.Id}", new
        {
            text = " Yangi ",
            imageUrl = (string?)null,
            options = new object[]
            {
                new { id = b.Id, text = "b2", isCorrect = true },
                new { id = a.Id, text = "a", isCorrect = false },
                new { text = "e", isCorrect = false },
            },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var doc = await JsonAsync(response))
        {
            Assert.Equal("Yangi", doc.RootElement.GetProperty("text").GetString());
            var options = doc.RootElement.GetProperty("options").EnumerateArray().ToList();
            Assert.Equal(new[] { "b2", "a", "e" }, options.Select(o => o.GetProperty("text").GetString()));
            Assert.Equal(new[] { true, false, false }, options.Select(o => o.GetProperty("isCorrect").GetBoolean()));
            Assert.Equal(b.Id, options[0].GetProperty("id").GetString());
            Assert.Equal(a.Id, options[1].GetProperty("id").GetString());
        }

        await using var db = NewDb();
        var stored = await db.QuestionOptions.AsNoTracking().Where(o => o.QuestionId == question.Id)
            .OrderBy(o => o.Order).ToListAsync();
        Assert.Equal(new[] { "b2", "a", "e" }, stored.Select(o => o.Text));
        Assert.Equal(new[] { 0, 1, 2 }, stored.Select(o => o.Order));
        Assert.Single(stored, o => o.IsCorrect);
        Assert.True(await db.AuditLogs.AnyAsync(l =>
            l.EntityType == QuestionBankService.AuditEntityQuestion && l.EntityId == question.Id && l.Action == "update"));
    }

    [Fact]
    public async Task Update_question_naming_an_option_it_does_not_own_is_409_and_changes_nothing()
    {
        var bank = await QuestionBankKit.SeedBankAsync(fixture.Api);
        var question = await QuestionBankKit.SeedQuestionAsync(fixture.Api, bank.Id, "Asl", ["a", "b"], correct: 0);
        var stranger = await QuestionBankKit.SeedQuestionAsync(fixture.Api, bank.Id, "Boshqa", ["x", "y"], correct: 0);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await admin.PutAsJsonAsync($"{Questions}/{question.Id}", new
        {
            text = "O'zgardi",
            options = new object[]
            {
                new { id = stranger.Options[0].Id, text = "x", isCorrect = true },
                new { text = "new", isCorrect = false },
            },
        });

        await AssertErrorAsync(response, HttpStatusCode.Conflict, QuestionBankService.Codes.QuestionChanged,
            QuestionBankService.QuestionChangedMessage);
        await using var db = NewDb();
        Assert.Equal("Asl", await db.Questions.Where(q => q.Id == question.Id).Select(q => q.Text).SingleAsync());
        Assert.Equal(2, await db.QuestionOptions.CountAsync(o => o.QuestionId == question.Id));
    }

    /// <summary>
    /// A candidate chose option C. Dropping C is refused (the answer would point
    /// at nothing); editing C and moving the key onto it is allowed — a wrong
    /// key must be fixable mid-season.
    /// </summary>
    [Fact]
    public async Task Update_question_cannot_drop_an_option_a_candidate_chose_but_can_edit_it()
    {
        var bank = await QuestionBankKit.SeedBankAsync(fixture.Api);
        var question = await QuestionBankKit.SeedQuestionAsync(fixture.Api, bank.Id, "Tanlangan", ["a", "b", "c"], correct: 0);
        var chosen = question.Options.Single(o => o.Order == 2);
        await QuestionBankKit.SeedPaperAsync(fixture.Api, bank, question.Id, chosen.Id, ExamStatus.Published);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var a = question.Options.Single(o => o.Order == 0);
        var b = question.Options.Single(o => o.Order == 1);

        var drop = await admin.PutAsJsonAsync($"{Questions}/{question.Id}", new
        {
            text = "Tanlangan",
            options = new object[]
            {
                new { id = a.Id, text = "a", isCorrect = true },
                new { id = b.Id, text = "b", isCorrect = false },
            },
        });
        await AssertErrorAsync(drop, HttpStatusCode.Conflict, QuestionBankService.Codes.OptionAnswered,
            QuestionBankService.OptionAnsweredMessage);

        var edit = await admin.PutAsJsonAsync($"{Questions}/{question.Id}", new
        {
            text = "Tanlangan",
            options = new object[]
            {
                new { id = a.Id, text = "a", isCorrect = false },
                new { id = b.Id, text = "b", isCorrect = false },
                new { id = chosen.Id, text = "c (tuzatildi)", isCorrect = true },
            },
        });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);

        await using var db = NewDb();
        var key = await db.QuestionOptions.SingleAsync(o => o.QuestionId == question.Id && o.IsCorrect);
        Assert.Equal(chosen.Id, key.Id);
        Assert.Equal("c (tuzatildi)", key.Text);
    }

    [Fact]
    public async Task Delete_question_on_a_candidate_paper_is_409()
    {
        var bank = await QuestionBankKit.SeedBankAsync(fixture.Api);
        var question = await QuestionBankKit.SeedQuestionAsync(fixture.Api, bank.Id, "Varaqda", ["a", "b"], correct: 0);
        // Drawn onto a paper but not answered yet — pinned all the same.
        await QuestionBankKit.SeedPaperAsync(fixture.Api, bank, question.Id, selectedOptionId: null, ExamStatus.Closed);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        await AssertErrorAsync(await admin.DeleteAsync($"{Questions}/{question.Id}"), HttpStatusCode.Conflict,
            QuestionBankService.Codes.QuestionAnswered, QuestionBankService.QuestionAnsweredMessage);

        await using var db = NewDb();
        Assert.True(await db.Questions.AnyAsync(q => q.Id == question.Id));
    }

    /// <summary>§8.2 — the bank cannot shrink under a live exam; it can once the exam is closed.</summary>
    [Fact]
    public async Task Delete_question_is_409_while_its_bank_feeds_a_published_exam_and_allowed_after_it_closes()
    {
        var bank = await QuestionBankKit.SeedBankAsync(fixture.Api);
        var question = await QuestionBankKit.SeedQuestionAsync(fixture.Api, bank.Id, "Jonli", ["a", "b"], correct: 1);
        var examId = await QuestionBankKit.SeedExamSectionAsync(fixture.Api, bank, ExamStatus.Published);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        await AssertErrorAsync(await admin.DeleteAsync($"{Questions}/{question.Id}"), HttpStatusCode.Conflict,
            QuestionBankService.Codes.BankInPublishedExam, QuestionBankService.BankInPublishedExamMessage);

        await fixture.Api.WithDbAsync(async db =>
        {
            var exam = await db.Exams.SingleAsync(e => e.Id == examId);
            exam.Status = ExamStatus.Closed;
            await db.SaveChangesAsync();
        });

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Questions}/{question.Id}")).StatusCode);
        await using var check = NewDb();
        Assert.False(await check.Questions.AnyAsync(q => q.Id == question.Id));
        Assert.False(await check.QuestionOptions.AnyAsync(o => o.QuestionId == question.Id));
        Assert.True(await check.AuditLogs.AnyAsync(l =>
            l.EntityType == QuestionBankService.AuditEntityQuestion && l.EntityId == question.Id && l.Action == "delete"));
    }

    [Fact]
    public async Task Question_list_accepts_limit_200_pages_and_searches_the_text()
    {
        var bank = await QuestionBankKit.SeedBankAsync(fixture.Api);
        await QuestionBankKit.SeedQuestionAsync(fixture.Api, bank.Id, "Alfa savol", ["a", "b"], correct: 0, order: 0);
        await QuestionBankKit.SeedQuestionAsync(fixture.Api, bank.Id, "Beta savol", ["a", "b"], correct: 0, order: 1);
        await QuestionBankKit.SeedQuestionAsync(fixture.Api, bank.Id, "Gamma savol", ["a", "b"], correct: 0, order: 2);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        using (var doc = await JsonAsync(await admin.GetAsync($"{Banks}/{bank.Id}/questions?limit=200")))
        {
            Assert.Equal(200, doc.RootElement.GetProperty("limit").GetInt32());
            Assert.Equal(3, doc.RootElement.GetProperty("total").GetInt32());
            Assert.Equal(new[] { "Alfa savol", "Beta savol", "Gamma savol" },
                doc.RootElement.GetProperty("items").EnumerateArray().Select(q => q.GetProperty("text").GetString()));
        }

        using (var doc = await JsonAsync(await admin.GetAsync($"{Banks}/{bank.Id}/questions?limit=2&page=2")))
        {
            Assert.Equal(3, doc.RootElement.GetProperty("total").GetInt32());
            var only = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal("Gamma savol", only.GetProperty("text").GetString());
        }

        using (var doc = await JsonAsync(await admin.GetAsync($"{Banks}/{bank.Id}/questions?search=BETA")))
        {
            var only = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal("Beta savol", only.GetProperty("text").GetString());
        }

        await AssertErrorAsync(await admin.GetAsync($"{Banks}/{Guid.NewGuid()}/questions"), HttpStatusCode.NotFound,
            QuestionBankService.Codes.BankNotFound, QuestionBankService.BankNotFoundMessage);
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    private static object[] Opts(params (string Text, bool IsCorrect)[] options) =>
        options.Select(o => (object)new { text = o.Text, isCorrect = o.IsCorrect }).ToArray();

    private sealed record Actor(HttpClient Client, AppUser? User) : IDisposable
    {
        public void Dispose() => Client.Dispose();
    }

    private async Task<Actor> ActorAsync(string who)
    {
        if (who == Anonymous) return new Actor(fixture.Api.AnonymousClient(), null);

        var role = who is StaffAdmission or StaffLeads ? Roles.Staff : who;
        string[]? permissions = who switch
        {
            StaffAdmission => ["admission"],
            // A staff account with a DIFFERENT key — proves the gate checks
            // `admission` itself, not "has any permission".
            StaffLeads => ["leads"],
            _ => null,
        };
        var (user, _) = await fixture.Api.SeedUserAsync(role, permissions: permissions);

        if (role == Roles.Student)
        {
            // Without a pupil row the token is revoked (401) before the gate
            // is ever asked — the cell would test the wrong thing.
            await fixture.Api.WithDbAsync(async db =>
            {
                var pupil = GeneralSettingsFlagsTests.NewStudent(user.FullName, "QB-1", "+998900000078");
                pupil.UserId = user.Id;
                db.Students.Add(pupil);
                await db.SaveChangesAsync();
            });
        }

        return new Actor(
            fixture.Api.ClientWithToken(fixture.Api.TokenFor(role, user.Id, user.FullName, user.Email)), user);
    }

    /// <summary>
    /// A refused cell: the status and an EMPTY body, then <c>true</c>. Otherwise
    /// asserts the expected success status and returns <c>false</c>.
    /// </summary>
    private static async Task<bool> RefusedAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(expected == response.StatusCode,
            $"Expected {(int)expected}, got {(int)response.StatusCode}: {text}");
        if (expected is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            Assert.Empty(text);
            return true;
        }
        return false;
    }

    private static async Task AssertErrorAsync(
        HttpResponseMessage response, HttpStatusCode status, string code, string message)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(status == response.StatusCode, $"Expected {(int)status}, got {(int)response.StatusCode}: {text}");
        using var doc = JsonDocument.Parse(text);
        Assert.Equal(code, doc.RootElement.GetProperty("code").GetString());
        Assert.Equal(message, doc.RootElement.GetProperty("message").GetString());
    }

    private static async Task<JsonDocument> JsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text);
    }

    private static async Task<string?> StateOfAsync(HttpResponseMessage response)
    {
        using var doc = await JsonAsync(response);
        return doc.RootElement.GetProperty("state").GetString();
    }

    private static async Task<string?> StateAsync(HttpClient client, string bankId) =>
        await StateOfAsync(await client.GetAsync($"{Banks}/{bankId}"));
}

/// <summary>
/// Seeding and request helpers shared by <see cref="QuestionBankTests"/> and
/// <see cref="QuestionImportTests"/>. Everything is written through the OWNER
/// connection, as the other kits do.
/// </summary>
internal static class QuestionBankKit
{
    public const string Banks = "/api/admin/admission/banks";
    public const string Questions = "/api/admin/admission/questions";

    public static string Tag() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>A fresh subject — each test's banks stay clear of `ux_question_banks_live`.</summary>
    public static async Task<Subject> SeedSubjectAsync(ApiFactory api, string? name = null)
    {
        var subject = new Subject { Name = name ?? $"QB fan {Tag()}" };
        await api.WithDbAsync(async db =>
        {
            db.Subjects.Add(subject);
            await db.SaveChangesAsync();
        });
        return subject;
    }

    public static async Task<QuestionBank> SeedBankAsync(
        ApiFactory api, Subject? subject = null, int grade = 5, bool archived = false)
    {
        subject ??= await SeedSubjectAsync(api);
        var bank = new QuestionBank { Grade = grade, SubjectId = subject.Id, IsArchived = archived };
        await api.WithDbAsync(async db =>
        {
            db.QuestionBanks.Add(bank);
            await db.SaveChangesAsync();
        });
        return bank;
    }

    public static async Task<Question> SeedQuestionAsync(
        ApiFactory api, string bankId, string text, string[] options, int correct, int order = 0)
    {
        var question = new Question { BankId = bankId, Text = text, Order = order };
        for (var i = 0; i < options.Length; i++)
        {
            question.Options.Add(new QuestionOption
            {
                QuestionId = question.Id,
                Text = options[i],
                IsCorrect = i == correct,
                Order = i,
            });
        }
        await api.WithDbAsync(async db =>
        {
            db.Questions.Add(question);
            await db.SaveChangesAsync();
        });
        return question;
    }

    /// <summary>An exam of the given status with one section drawing from <paramref name="bank"/>. Returns the exam id.</summary>
    public static async Task<string> SeedExamSectionAsync(ApiFactory api, QuestionBank bank, string status)
    {
        var (exam, _) = await SeedExamAsync(api, bank, status);
        return exam.Id;
    }

    /// <summary>
    /// A whole online paper: exam + section + participant + invitation +
    /// attempt, and one <c>exam_answers</c> row for <paramref name="questionId"/>
    /// (chosen or not yet). The minimum that makes the RESTRICT keys bite.
    /// </summary>
    public static async Task SeedPaperAsync(
        ApiFactory api, QuestionBank bank, string questionId, string? selectedOptionId, string examStatus)
    {
        var (exam, section) = await SeedExamAsync(api, bank, examStatus);
        var participant = new ExamParticipant
        {
            ExamId = exam.Id,
            ParticipantKind = ExamParticipantKind.Lead, // lead_id may be NULL (an enrolled candidate)
            Status = ExamParticipantStatus.InProgress,
        };
        var invitation = new ExamInvitation
        {
            ParticipantId = participant.Id,
            TokenHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            TokenHint = Tag()[..6],
            ValidFrom = AppClock.Now.AddDays(-1),
            ValidUntil = AppClock.Now.AddDays(1),
        };
        var attempt = new ExamAttempt
        {
            ParticipantId = participant.Id,
            InvitationId = invitation.Id,
            DeadlineAt = AppClock.Now.AddHours(1),
        };
        var answer = new ExamAnswer
        {
            AttemptId = attempt.Id,
            QuestionId = questionId,
            SectionId = section.Id,
            Order = 0,
            SelectedOptionId = selectedOptionId,
            AnsweredAt = selectedOptionId is null ? null : AppClock.Now,
        };

        await api.WithDbAsync(async db =>
        {
            db.ExamParticipants.Add(participant);
            db.ExamInvitations.Add(invitation);
            db.ExamAttempts.Add(attempt);
            db.ExamAnswers.Add(answer);
            await db.SaveChangesAsync();
        });
    }

    private static async Task<(Exam Exam, ExamSection Section)> SeedExamAsync(
        ApiFactory api, QuestionBank bank, string status)
    {
        // Online, with the window and time limit `ck_exams_online_window` demands
        // of a published or closed exam.
        var exam = new Exam
        {
            Title = $"QB imtihon {Tag()}",
            Kind = ExamKind.Admission,
            Delivery = ExamDelivery.Online,
            Grade = bank.Grade,
            OpensAt = AppClock.Now.AddDays(-1),
            ClosesAt = AppClock.Now.AddDays(1),
            TimeLimitMin = 60,
            Status = status,
        };
        var section = new ExamSection
        {
            ExamId = exam.Id,
            SubjectId = bank.SubjectId,
            BankId = bank.Id,
            QuestionCount = 1,
            PointsPerCorrect = 1m,
            MaxScore = 1m,
        };
        await api.WithDbAsync(async db =>
        {
            db.Exams.Add(exam);
            db.ExamSections.Add(section);
            await db.SaveChangesAsync();
        });
        return (exam, section);
    }

    /// <summary>A valid two-option question body for <c>POST /questions</c>.</summary>
    public static object QuestionBody(string bankId, string text) => new
    {
        bankId,
        text,
        imageUrl = (string?)null,
        options = new[]
        {
            new { text = "Ha", isCorrect = true },
            new { text = "Yo'q", isCorrect = false },
        },
    };

    /// <summary>One sheet row in template order: text, A–F (missing = blank), correct letter, image.</summary>
    public static IReadOnlyList<string> Row(string text, string[] options, string correct, string image = "")
    {
        var cells = new string[QuestionImportService.Headers.Length];
        cells[0] = text;
        for (var i = 0; i < 6; i++) cells[1 + i] = i < options.Length ? options[i] : "";
        cells[7] = correct;
        cells[8] = image;
        return cells;
    }

    /// <summary>An .xlsx whose first sheet is the import sheet, with the template headers.</summary>
    public static byte[] Sheet(IEnumerable<IReadOnlyList<string>> rows, IReadOnlyList<string>? headers = null) =>
        ExcelExport.Build(QuestionImportService.SheetName, headers ?? QuestionImportService.Headers, rows);

    /// <summary>The multipart body the client sends: <c>file</c>, <c>bankId</c>, <c>dryRun</c> (omitted when null).</summary>
    public static MultipartFormDataContent ImportForm(
        byte[] bytes, string bankId, bool? dryRun, string fileName = "savollar.xlsx")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(QuestionImportService.XlsxMime);
        var form = new MultipartFormDataContent
        {
            { file, "file", fileName },
            { new StringContent(bankId), "bankId" },
        };
        if (dryRun is { } value) form.Add(new StringContent(value ? "true" : "false"), "dryRun");
        return form;
    }
}

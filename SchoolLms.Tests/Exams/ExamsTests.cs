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
//  Imtihonlar (unit B2) — docs/modules/admission-and-testing.md §6.3, §8.1,
//  §8.4, §8.5, §13 Q7, and the 2026-09-22 rule that an enrolled candidate's
//  lead is DELETED while the sitting survives (§2.2).
//
//  Everything goes through HTTP against the real pipeline, on the shared
//  database: every test creates its own subjects, classes, pupils and leads
//  under a random tag and never relies on another test's rows. Question banks
//  are seeded straight into the database (B1 owns their endpoints) — one live
//  bank per grade × subject, so every bank gets a subject of its own.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class ExamsTests(ApiFixture fixture)
{
    private const string Exams = "/api/admin/exams";
    private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private AppDbContext NewDb() => PostgresFixture.NewContext(fixture.Database.OwnerConnectionString);

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    private static string Today => AppClock.Today.ToString("yyyy-MM-dd");

    // =====================================================================
    //  1. Exam types (§5.4)
    // =====================================================================

    /// <summary>
    /// A name is unique case- and whitespace-insensitively (409); a missing
    /// <c>isActive</c> leaves the flag alone; a type an exam uses cannot be
    /// deleted (409 — deactivate instead); an unused one can (204).
    /// </summary>
    [Fact]
    public async Task Imtihon_turi_nomi_takrorlanmaydi_ishlatilgani_ochirilmaydi()
    {
        var tag = Tag();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var type = await OkJsonAsync(await admin.PostAsJsonAsync($"{Exams}/types",
            new { name = $"Choraklik {tag}", description = "Har chorak oxirida" }));
        var typeId = type.GetProperty("id").GetString()!;
        Assert.True(type.GetProperty("isActive").GetBoolean());

        await ErrorAsync(await admin.PostAsJsonAsync($"{Exams}/types",
            new { name = $"  CHORAKLIK {tag.ToUpperInvariant()} ", description = "" }),
            HttpStatusCode.Conflict, "exam_type_name_taken");

        var off = await OkJsonAsync(await admin.PutAsJsonAsync($"{Exams}/types/{typeId}",
            new { name = $"Choraklik {tag}", description = "", isActive = false }));
        Assert.False(off.GetProperty("isActive").GetBoolean());
        var stillOff = await OkJsonAsync(await admin.PutAsJsonAsync($"{Exams}/types/{typeId}",
            new { name = $"Choraklik {tag}", description = "yangi izoh" }));
        Assert.False(stillOff.GetProperty("isActive").GetBoolean());

        var subject = await SeedSubjectAsync($"Tarix {tag}");
        await OkJsonAsync(await admin.PostAsJsonAsync(Exams,
            BlockExam($"Blok {tag}", Today, [(subject, 50m)], examTypeId: typeId)));
        await ErrorAsync(await admin.DeleteAsync($"{Exams}/types/{typeId}"),
            HttpStatusCode.Conflict, "exam_type_in_use");

        var spare = await OkJsonAsync(await admin.PostAsJsonAsync($"{Exams}/types",
            new { name = $"Yillik {tag}", description = "" }));
        var deleted = await admin.DeleteAsync($"{Exams}/types/{spare.GetProperty("id").GetString()}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var list = await OkJsonAsync(await admin.GetAsync($"{Exams}/types"));
        var ids = list.EnumerateArray().Select(t => t.GetProperty("id").GetString()).ToList();
        Assert.Contains(typeId, ids); // inactive types are listed too
        Assert.DoesNotContain(spare.GetProperty("id").GetString(), ids);
    }

    // =====================================================================
    //  2. Block exam: create, classes → participants, publish freezes sections
    // =====================================================================

    [Fact]
    public async Task Blok_test_sinf_qoshish_idempotent_eʼlon_qilish_fanlarni_muzlatadi()
    {
        var tag = Tag();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var math = await SeedSubjectAsync($"Matematika {tag}");
        var physics = await SeedSubjectAsync($"Fizika {tag}");
        var (classId, className, pupils) = await SeedClassAsync(9, ["Aliyev", "Botirov", "Karimov"], archived: 1);

        var exam = await OkJsonAsync(await admin.PostAsJsonAsync(Exams,
            BlockExam($"1-chorak blok {tag}", Today, [(math, 50m), (physics, 30m)])));
        var examId = exam.GetProperty("id").GetString()!;
        Assert.Equal("draft", exam.GetProperty("status").GetString());
        var sections = exam.GetProperty("sections").EnumerateArray().ToList();
        Assert.Equal(new[] { 50m, 30m }, sections.Select(s => s.GetProperty("maxScore").GetDecimal()));

        // Classes expand to non-archived pupils, with the class snapshot; a repeat adds nobody.
        var first = await OkJsonAsync(await admin.PostAsJsonAsync($"{Exams}/{examId}/participants", new { classIds = new[] { classId } }));
        Assert.Equal((3, 0), (first.GetProperty("added").GetInt32(), first.GetProperty("skipped").GetInt32()));
        var again = await OkJsonAsync(await admin.PostAsJsonAsync($"{Exams}/{examId}/participants",
            new { classIds = new[] { classId }, studentIds = new[] { pupils[0].Id } }));
        Assert.Equal((0, 3), (again.GetProperty("added").GetInt32(), again.GetProperty("skipped").GetInt32()));

        var roster = await OkJsonAsync(await admin.GetAsync($"{Exams}/{examId}/participants?limit=10"));
        Assert.Equal(3, roster.GetProperty("total").GetInt32());
        Assert.All(roster.GetProperty("items").EnumerateArray(), p =>
        {
            Assert.Equal(classId, p.GetProperty("classId").GetString());
            Assert.Equal(className, p.GetProperty("className").GetString());
            Assert.Equal("assigned", p.GetProperty("status").GetString());
        });

        var listed = await OkJsonAsync(await admin.GetAsync($"{Exams}?search={Uri.EscapeDataString(tag)}&kind=block"));
        var row = Assert.Single(listed.GetProperty("items").EnumerateArray());
        Assert.Equal((2, 3), (row.GetProperty("sectionCount").GetInt32(), row.GetProperty("participantCount").GetInt32()));

        var published = await OkJsonAsync(await admin.PostAsync($"{Exams}/{examId}/publish", null));
        Assert.Equal("published", published.GetProperty("status").GetString());

        // Frozen: a changed ceiling is refused; the same sections with a new title are fine.
        await ErrorAsync(await admin.PutAsJsonAsync($"{Exams}/{examId}",
            BlockExam($"1-chorak blok {tag}", Today, [(math, 60m), (physics, 30m)])),
            HttpStatusCode.Conflict, "sections_frozen");
        var renamed = await OkJsonAsync(await admin.PutAsJsonAsync($"{Exams}/{examId}",
            BlockExam($"1-chorak blok (tahrir) {tag}", Today, [(math, 50m), (physics, 30m)])));
        Assert.Equal($"1-chorak blok (tahrir) {tag}", renamed.GetProperty("title").GetString());

        await ErrorAsync(await admin.PostAsync($"{Exams}/{examId}/publish", null), HttpStatusCode.Conflict, "not_draft");

        // §13 Q4 — the block test is a paper exam for now.
        var online = new
        {
            title = $"Onlayn blok {tag}", kind = "block", delivery = "online", sections = Array.Empty<object>(),
        };
        await ErrorAsync(await admin.PostAsJsonAsync(Exams, online), HttpStatusCode.BadRequest, "validation");
    }

    // =====================================================================
    //  3. Manual entry (§8.4), draft freeze by results, results register
    // =====================================================================

    /// <summary>
    /// Out of range is a 400 that names the pupil and the subject, and writes
    /// nothing. A valid save is rounded half-up and summarised by the server
    /// over the WHOLE exam's maximum; absent clears; repeating the same values
    /// changes nothing; typed results freeze a draft's sections; the results
    /// register and its export show the same numbers.
    /// </summary>
    [Fact]
    public async Task Qolda_kiritish_oraliqdan_tashqari_400_togrisi_server_hisoblaydi()
    {
        var tag = Tag();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var mathName = $"Matematika {tag}";
        var math = await SeedSubjectAsync(mathName);
        var physics = await SeedSubjectAsync($"Fizika {tag}");
        var (classId, _, pupils) = await SeedClassAsync(7, ["Aliyev", "Botirov", "Karimov"]);

        var examId = (await OkJsonAsync(await admin.PostAsJsonAsync(Exams,
            BlockExam($"Kiritish {tag}", Today, [(math, 50m), (physics, 30m)])))).GetProperty("id").GetString()!;
        await OkJsonAsync(await admin.PostAsJsonAsync($"{Exams}/{examId}/participants", new { classIds = new[] { classId } }));

        var table = await OkJsonAsync(await admin.GetAsync($"{Exams}/{examId}/entry-table"));
        var columns = table.GetProperty("columns").EnumerateArray().ToList();
        var mathSection = columns.Single(c => c.GetProperty("subjectId").GetString() == math).GetProperty("sectionId").GetString()!;
        var physicsSection = columns.Single(c => c.GetProperty("subjectId").GetString() == physics).GetProperty("sectionId").GetString()!;
        var pid = await ParticipantIdsAsync(examId);
        var (p1, p2, p3) = (pid[pupils[0].Id], pid[pupils[1].Id], pid[pupils[2].Id]);
        Assert.All(table.GetProperty("rows").EnumerateArray(), r =>
            Assert.Equal(JsonValueKind.Null, r.GetProperty("scores").GetProperty(mathSection).ValueKind));

        // ---- out of range: 400, names who and what, nothing written ----
        var tooHigh = await ErrorAsync(await admin.PostAsJsonAsync($"{Exams}/{examId}/entry-table", new
        {
            rows = new object[]
            {
                new { participantId = p2, scores = new[] { new { sectionId = physicsSection, points = 20m } }, absent = false },
                new { participantId = p1, scores = new[] { new { sectionId = mathSection, points = 55m } }, absent = false },
            },
        }), HttpStatusCode.BadRequest, "validation");
        var message = tooHigh.GetProperty("message").GetString()!;
        Assert.Contains(pupils[0].Name, message);
        Assert.Contains(mathName, message);
        Assert.Contains("0 dan 50 gacha", message);
        await using (var db = NewDb())
            Assert.False(await db.ExamSectionScores.AnyAsync(s => s.ParticipantId == p1 || s.ParticipantId == p2));

        // ---- valid: rounded, summarised over the whole exam; absent clears ----
        var saved = await OkJsonAsync(await admin.PostAsJsonAsync($"{Exams}/{examId}/entry-table", new
        {
            rows = new object[]
            {
                new
                {
                    participantId = p1, absent = false,
                    scores = new[] { new { sectionId = mathSection, points = 42.125m }, new { sectionId = physicsSection, points = 30m } },
                },
                new { participantId = p2, scores = new[] { new { sectionId = physicsSection, points = 12m } }, absent = false },
                new { participantId = p3, scores = Array.Empty<object>(), absent = true },
            },
        }));
        Assert.Equal(3, saved.GetProperty("saved").GetInt32());

        table = await OkJsonAsync(await admin.GetAsync($"{Exams}/{examId}/entry-table"));
        var rows = table.GetProperty("rows").EnumerateArray().ToDictionary(r => r.GetProperty("participantId").GetString()!);
        Assert.Equal("finished", rows[p1].GetProperty("status").GetString());
        Assert.Equal(42.13m, rows[p1].GetProperty("scores").GetProperty(mathSection).GetDecimal());
        Assert.Equal(72.13m, rows[p1].GetProperty("totalPoints").GetDecimal());
        Assert.Equal(80m, rows[p1].GetProperty("maxPoints").GetDecimal());
        Assert.Equal(90.16m, rows[p1].GetProperty("percent").GetDecimal());
        // One subject of two entered: 12 of 80, not 12 of 30.
        Assert.Equal(15m, rows[p2].GetProperty("percent").GetDecimal());
        Assert.Equal(JsonValueKind.Null, rows[p2].GetProperty("scores").GetProperty(mathSection).ValueKind);
        Assert.Equal("absent", rows[p3].GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, rows[p3].GetProperty("totalPoints").ValueKind);

        await using (var db = NewDb())
        {
            var stored = await db.ExamParticipants.AsNoTracking().SingleAsync(p => p.Id == p1);
            Assert.NotNull(stored.ScoredByUserId);
            Assert.Null(stored.CorrectCount); // no questions on paper
            Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.EntityType == ExamService.AuditEntityExamResult && a.EntityId == p1));
        }

        // ---- the same values again: accepted, nothing counted, no new audit row ----
        var repeat = await OkJsonAsync(await admin.PostAsJsonAsync($"{Exams}/{examId}/entry-table", new
        {
            rows = new object[] { new { participantId = p1, scores = new[] { new { sectionId = mathSection, points = 42.13m } }, absent = false } },
        }));
        Assert.Equal(0, repeat.GetProperty("saved").GetInt32());

        // ---- absent with points is contradictory ----
        await ErrorAsync(await admin.PostAsJsonAsync($"{Exams}/{examId}/entry-table", new
        {
            rows = new object[] { new { participantId = p1, scores = new[] { new { sectionId = mathSection, points = 1m } }, absent = true } },
        }), HttpStatusCode.BadRequest, "validation");

        // ---- a DRAFT with typed results has frozen sections too ----
        await ErrorAsync(await admin.PutAsJsonAsync($"{Exams}/{examId}",
            BlockExam($"Kiritish {tag}", Today, [(math, 40m), (physics, 30m)])),
            HttpStatusCode.Conflict, "sections_have_results");
        await OkJsonAsync(await admin.PutAsJsonAsync($"{Exams}/{examId}",
            BlockExam($"Kiritish (yangi nom) {tag}", Today, [(math, 50m), (physics, 30m)])));

        // ---- results register: finished only, with per-subject scores ----
        var results = await OkJsonAsync(await admin.GetAsync($"{Exams}/results?examId={examId}&status=finished"));
        Assert.Equal(2, results.GetProperty("total").GetInt32());
        var r1 = results.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("participantId").GetString() == p1);
        Assert.Equal(pupils[0].Name, r1.GetProperty("fullName").GetString());
        Assert.Equal(Today, r1.GetProperty("examDate").GetString());
        Assert.Equal(new[] { 42.13m, 30m }, r1.GetProperty("scores").EnumerateArray().Select(s => s.GetProperty("points").GetDecimal()));
        var bySubject = await OkJsonAsync(await admin.GetAsync($"{Exams}/results?subjectId={math}&status=absent"));
        Assert.Equal(p3, Assert.Single(bySubject.GetProperty("items").EnumerateArray()).GetProperty("participantId").GetString());
        await ErrorAsync(await admin.GetAsync($"{Exams}/results?status=done"), HttpStatusCode.BadRequest, "validation");

        // ---- export: the whole filter, real numbers, the §6.3 file name ----
        var export = await admin.GetAsync($"{Exams}/results/export?examId={examId}&status=finished");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal(XlsxMime, export.Content.Headers.ContentType?.MediaType);
        var fileName = export.Content.Headers.ContentDisposition?.FileNameStar ?? export.Content.Headers.ContentDisposition?.FileName;
        Assert.StartsWith($"natijalar_{examId[..8]}_", fileName?.Trim('"'));
        var sheet = ExcelImport.ReadRows(new MemoryStream(await export.Content.ReadAsByteArrayAsync()), 10);
        Assert.Equal("Jami ball", sheet[0][7]);
        var line = sheet.Single(r => r[2] == pupils[0].Name);
        Assert.Equal("72.13", line[7]);
        Assert.Equal("90.16", line[9]);

        // ---- remove a participant: 204, gone from the grid ----
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Exams}/{examId}/participants/{p3}")).StatusCode);
        table = await OkJsonAsync(await admin.GetAsync($"{Exams}/{examId}/entry-table"));
        Assert.Equal(2, table.GetProperty("rows").GetArrayLength());
    }

    // =====================================================================
    //  4. Publishing an online exam (§8.1, §13 Q7)
    // =====================================================================

    /// <summary>
    /// Refused with the specific reason: too few questions in the bank
    /// (<c>not_enough_questions</c>), a broken question (<c>bad_questions</c>),
    /// more than 200 questions in one sitting (<c>too_many_questions</c>).
    /// Accepted when ready: points and ceiling copied into the section, time
    /// limit summed from the banks — and a later edit of the bank does not
    /// change the published exam.
    /// </summary>
    [Fact]
    public async Task Onlayn_eʼlon_qilish_qoidalari_va_200_cheklov()
    {
        var tag = Tag();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        const int grade = 5;

        // Too few questions: the bank wants 10, holds 3.
        var s1 = await SeedSubjectAsync($"Ona tili {tag}");
        var thin = await SeedBankAsync(grade, s1, perTest: 10, minutes: 20, perCorrect: 1m, questions: 3);
        var e1 = await CreateOnlineAsync(admin, $"Kam savol {tag}", grade, [(thin, null)]);
        var refused = await ErrorAsync(await admin.PostAsync($"{Exams}/{e1}/publish", null),
            HttpStatusCode.Conflict, "not_enough_questions");
        Assert.Contains("bazada 3 ta savol bor, testga 10 ta kerak", refused.GetProperty("message").GetString());

        // A question with no correct option.
        var s2 = await SeedSubjectAsync($"Kimyo {tag}");
        var broken = await SeedBankAsync(grade, s2, perTest: 2, minutes: 10, perCorrect: 1m, questions: 2, brokenQuestions: 1);
        var e2 = await CreateOnlineAsync(admin, $"Buzuq savol {tag}", grade, [(broken, null)]);
        await ErrorAsync(await admin.PostAsync($"{Exams}/{e2}/publish", null), HttpStatusCode.Conflict, "bad_questions");

        // 201 questions in one sitting (§13 Q7) — every bank ready, still refused.
        var s3 = await SeedSubjectAsync($"Ingliz tili {tag}");
        var big = await SeedBankAsync(grade, s3, perTest: 201, minutes: 300, perCorrect: 1m, questions: 201);
        var e3 = await CreateOnlineAsync(admin, $"Katta test {tag}", grade, [(big, null)]);
        var tooMany = await ErrorAsync(await admin.PostAsync($"{Exams}/{e3}/publish", null),
            HttpStatusCode.Conflict, "too_many_questions");
        Assert.Contains("201", tooMany.GetProperty("message").GetString());
        await using (var db = NewDb())
            Assert.Equal(ExamStatus.Draft, (await db.Exams.AsNoTracking().SingleAsync(e => e.Id == e3)).Status);

        // Ready: 4 questions per test × 2.5 points; the time limit comes from the bank.
        var s4 = await SeedSubjectAsync($"Biologiya {tag}");
        var ready = await SeedBankAsync(grade, s4, perTest: 4, minutes: 25, perCorrect: 2.5m, questions: 5);
        var e4 = await CreateOnlineAsync(admin, $"Tayyor {tag}", grade, [(ready, null)], timeLimitMin: null);
        var published = await OkJsonAsync(await admin.PostAsync($"{Exams}/{e4}/publish", null));
        Assert.Equal("published", published.GetProperty("status").GetString());
        Assert.Equal(25, published.GetProperty("timeLimitMin").GetInt32());
        var section = Assert.Single(published.GetProperty("sections").EnumerateArray());
        Assert.Equal(4, section.GetProperty("questionCount").GetInt32());
        Assert.Equal(2.5m, section.GetProperty("pointsPerCorrect").GetDecimal());
        Assert.Equal(10m, section.GetProperty("maxScore").GetDecimal());

        await using (var db = NewDb())
        {
            var bank = await db.QuestionBanks.SingleAsync(b => b.Id == ready);
            bank.PointsPerCorrect = 5m;
            await db.SaveChangesAsync();
            var stored = await db.ExamSections.AsNoTracking().SingleAsync(s => s.ExamId == e4);
            Assert.Equal((2.5m, 10m), (stored.PointsPerCorrect, stored.MaxScore));
        }

        // The engine scores online exams; the grid refuses to type into one.
        await ErrorAsync(await admin.PostAsJsonAsync($"{Exams}/{e4}/entry-table", new { rows = Array.Empty<object>() }),
            HttpStatusCode.Conflict, "exam_online");
    }

    // =====================================================================
    //  5. The lead is deleted on enrolment; the sitting survives (§2.2)
    // =====================================================================

    [Fact]
    public async Task Nomzod_oquvchi_bolib_lid_ochirilsa_natija_qoladi()
    {
        var tag = Tag();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var subject = await SeedSubjectAsync($"Mantiq {tag}");
        var name = $"Nomzod Aziza {tag}";
        var leadId = await SeedLeadAsync(name);

        var examId = await CreateAdmissionPaperAsync(admin, $"Qabul {tag}", subject);
        var added = await OkJsonAsync(await admin.PostAsJsonAsync($"{Exams}/{examId}/participants", new { leadIds = new[] { leadId } }));
        Assert.Equal(1, added.GetProperty("added").GetInt32());
        await using (var db = NewDb())
            Assert.Equal(LeadAdmissionStatus.Invited, (await db.Leads.AsNoTracking().SingleAsync(l => l.Id == leadId)).AdmissionStatus);

        var table = await OkJsonAsync(await admin.GetAsync($"{Exams}/{examId}/entry-table"));
        var sectionId = table.GetProperty("columns")[0].GetProperty("sectionId").GetString()!;
        var row = Assert.Single(table.GetProperty("rows").EnumerateArray());
        Assert.Equal(name, row.GetProperty("fullName").GetString());
        var participantId = row.GetProperty("participantId").GetString()!;

        await OkJsonAsync(await admin.PostAsJsonAsync($"{Exams}/{examId}/entry-table", new
        {
            rows = new object[] { new { participantId, scores = new[] { new { sectionId, points = 77.5m } }, absent = false } },
        }));
        await using (var db = NewDb())
            Assert.Equal(LeadAdmissionStatus.Tested, (await db.Leads.AsNoTracking().SingleAsync(l => l.Id == leadId)).AdmissionStatus);

        // Enrol — the existing endpoint DELETES the lead.
        var enrol = await admin.PostAsJsonAsync($"/api/admin/leads/{leadId}/enrol", new
        {
            student = new
            {
                fullName = name, birthDate = "2016-03-14", address = "", gender = "female",
                parentFullName = "Karimov Aziz", parentPhone = "+998901234567", className = "5-A",
                enrollmentDate = (string?)null,
            },
        });
        Assert.True(enrol.IsSuccessStatusCode, $"{(int)enrol.StatusCode}: {await enrol.Content.ReadAsStringAsync()}");

        await using (var db = NewDb())
        {
            Assert.False(await db.Leads.AnyAsync(l => l.Id == leadId));
            var survivor = await db.ExamParticipants.AsNoTracking().SingleAsync(p => p.Id == participantId);
            Assert.Null(survivor.LeadId);
            Assert.Equal(ExamParticipantKind.Lead, survivor.ParticipantKind);
            Assert.Equal(77.5m, survivor.TotalPoints);
            Assert.Equal(1, await db.ExamSectionScores.CountAsync(s => s.ParticipantId == participantId));
        }

        var results = await OkJsonAsync(await admin.GetAsync($"{Exams}/results?examId={examId}"));
        var result = Assert.Single(results.GetProperty("items").EnumerateArray());
        Assert.Equal(ExamService.DeletedCandidateName, result.GetProperty("fullName").GetString());
        Assert.Equal("lead", result.GetProperty("participantKind").GetString());
        Assert.Equal(77.5m, result.GetProperty("totalPoints").GetDecimal());
        Assert.Equal(77.5m, Assert.Single(result.GetProperty("scores").EnumerateArray()).GetProperty("points").GetDecimal());

        var after = await OkJsonAsync(await admin.GetAsync($"{Exams}/{examId}/entry-table"));
        Assert.Equal(ExamService.DeletedCandidateName,
            Assert.Single(after.GetProperty("rows").EnumerateArray()).GetProperty("fullName").GetString());
    }

    // =====================================================================
    //  6. Cancel (§5.5, §8.5)
    // =====================================================================

    /// <summary>
    /// A reason is required. Unfinished participants become cancelled, a
    /// graded one keeps its result; a <c>testing</c> candidate goes back to
    /// <c>invited</c>, a <c>tested</c> one stays; live links are revoked.
    /// A cancelled exam cannot be cancelled again nor receive scores.
    /// </summary>
    [Fact]
    public async Task Bekor_qilish_tugallanmaganlarni_bekor_qiladi_natijani_saqlaydi()
    {
        var tag = Tag();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var subject = await SeedSubjectAsync($"Geografiya {tag}");
        var graded = await SeedLeadAsync($"Baholangan {tag}");
        var waiting = await SeedLeadAsync($"Kutayotgan {tag}");

        var examId = await CreateAdmissionPaperAsync(admin, $"Bekor {tag}", subject);
        await OkJsonAsync(await admin.PostAsJsonAsync($"{Exams}/{examId}/participants", new { leadIds = new[] { graded, waiting } }));
        var table = await OkJsonAsync(await admin.GetAsync($"{Exams}/{examId}/entry-table"));
        var sectionId = table.GetProperty("columns")[0].GetProperty("sectionId").GetString()!;
        string gradedPid, waitingPid;
        await using (var db = NewDb())
        {
            gradedPid = (await db.ExamParticipants.SingleAsync(p => p.ExamId == examId && p.LeadId == graded)).Id;
            waitingPid = (await db.ExamParticipants.SingleAsync(p => p.ExamId == examId && p.LeadId == waiting)).Id;
            // What B3 does when an online attempt starts — simulated here.
            (await db.Leads.SingleAsync(l => l.Id == waiting)).AdmissionStatus = LeadAdmissionStatus.Testing;
            db.ExamInvitations.Add(new ExamInvitation
            {
                ParticipantId = waitingPid, TokenHash = Guid.NewGuid().ToString("N") + tag, TokenHint = tag[..6],
                ValidFrom = AppClock.Now, ValidUntil = AppClock.Now.AddDays(1),
            });
            await db.SaveChangesAsync();
        }
        await OkJsonAsync(await admin.PostAsJsonAsync($"{Exams}/{examId}/entry-table", new
        {
            rows = new object[] { new { participantId = gradedPid, scores = new[] { new { sectionId, points = 64m } }, absent = false } },
        }));

        await ErrorAsync(await admin.PostAsJsonAsync($"{Exams}/{examId}/cancel", new { reason = "  " }),
            HttpStatusCode.BadRequest, "validation");
        var cancelled = await OkJsonAsync(await admin.PostAsJsonAsync($"{Exams}/{examId}/cancel",
            new { reason = "Imtihon boshqa kunga ko'chirildi" }));
        Assert.Equal("cancelled", cancelled.GetProperty("status").GetString());

        await using (var db = NewDb())
        {
            var gradedRow = await db.ExamParticipants.AsNoTracking().SingleAsync(p => p.Id == gradedPid);
            Assert.Equal((ExamParticipantStatus.Finished, (decimal?)64m), (gradedRow.Status, gradedRow.TotalPoints));
            Assert.Equal(ExamParticipantStatus.Cancelled,
                (await db.ExamParticipants.AsNoTracking().SingleAsync(p => p.Id == waitingPid)).Status);
            Assert.Equal(LeadAdmissionStatus.Tested, (await db.Leads.AsNoTracking().SingleAsync(l => l.Id == graded)).AdmissionStatus);
            Assert.Equal(LeadAdmissionStatus.Invited, (await db.Leads.AsNoTracking().SingleAsync(l => l.Id == waiting)).AdmissionStatus);
            Assert.False(await db.ExamInvitations.AnyAsync(i => i.ParticipantId == waitingPid && i.RevokedAt == null));
        }

        await ErrorAsync(await admin.PostAsJsonAsync($"{Exams}/{examId}/cancel", new { reason = "yana" }),
            HttpStatusCode.Conflict, "exam_not_cancellable");
        await ErrorAsync(await admin.PostAsJsonAsync($"{Exams}/{examId}/entry-table", new
        {
            rows = new object[] { new { participantId = gradedPid, scores = new[] { new { sectionId, points = 1m } }, absent = false } },
        }), HttpStatusCode.Conflict, "exam_cancelled");
    }

    // =====================================================================
    //  7. Excel import (§6.3, §8.4) — dry run, then write
    // =====================================================================

    [Fact]
    public async Task Import_avval_tekshiradi_keyin_yaroqli_qatorlarni_yozadi()
    {
        var tag = Tag();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var subject = await SeedSubjectAsync($"Algebra {tag}");
        var (classId, _, pupils) = await SeedClassAsync(8, ["Aliyev", "Botirov", "Karimov"]);
        var examId = (await OkJsonAsync(await admin.PostAsJsonAsync(Exams,
            BlockExam($"Import {tag}", Today, [(subject, 50m)])))).GetProperty("id").GetString()!;
        await OkJsonAsync(await admin.PostAsJsonAsync($"{Exams}/{examId}/participants", new { classIds = new[] { classId } }));
        var pid = await ParticipantIdsAsync(examId);

        // The template: ID | F.I.SH | Sinf | Algebra … (0–50) | Kelmadi, one row per pupil.
        var template = await admin.GetAsync($"{Exams}/{examId}/entry-table/template");
        Assert.Equal(HttpStatusCode.OK, template.StatusCode);
        var sheet = ExcelImport.ReadRows(new MemoryStream(await template.Content.ReadAsByteArrayAsync()), 5);
        Assert.Equal(new[] { "ID", "F.I.SH", "Sinf", $"Algebra {tag} (0–50)", "Kelmadi" }, sheet[0]);
        Assert.Equal(4, sheet.Count);

        // Sheet rows: 1 header, 2 valid, 3 out of range, 4 absent (valid), 5 unknown id.
        string[] header = sheet[0];
        var file = ExcelExport.Build("Natijalar", header,
        [
            [pid[pupils[0].Id], pupils[0].Name, "", "45", ""],
            [pid[pupils[1].Id], pupils[1].Name, "", "60", ""],
            [pid[pupils[2].Id], pupils[2].Name, "", "", "ha"],
            ["no-such-participant", "Begona", "", "10", ""],
        ]);

        var check = await OkJsonAsync(await admin.PostAsync($"{Exams}/{examId}/entry-table/import", ImportForm(file, "natijalar.xlsx", dryRun: true)));
        Assert.Equal((4, 2, 2, 0), (check.GetProperty("totalRows").GetInt32(), check.GetProperty("validCount").GetInt32(),
            check.GetProperty("errorCount").GetInt32(), check.GetProperty("imported").GetInt32()));
        var errors = check.GetProperty("errors").EnumerateArray()
            .ToDictionary(e => e.GetProperty("reason").GetString()!, e => e.GetProperty("rows").EnumerateArray().Select(r => r.GetInt32()).ToList());
        Assert.Equal(new[] { 5 }, errors["unknownParticipant"]);
        Assert.Equal(new[] { 3 }, errors["outOfRange"]);
        await using (var db = NewDb())
            Assert.False(await db.ExamSectionScores.AnyAsync(s => pid.Values.Contains(s.ParticipantId)));

        var write = await OkJsonAsync(await admin.PostAsync($"{Exams}/{examId}/entry-table/import", ImportForm(file, "natijalar.xlsx", dryRun: false)));
        Assert.Equal(2, write.GetProperty("imported").GetInt32());
        await using (var db = NewDb())
        {
            var rows = await db.ExamParticipants.AsNoTracking().Where(p => p.ExamId == examId).ToDictionaryAsync(p => p.Id);
            Assert.Equal((ExamParticipantStatus.Finished, (decimal?)45m), (rows[pid[pupils[0].Id]].Status, rows[pid[pupils[0].Id]].TotalPoints));
            Assert.Equal(ExamParticipantStatus.Assigned, rows[pid[pupils[1].Id]].Status);
            Assert.Equal(ExamParticipantStatus.Absent, rows[pid[pupils[2].Id]].Status);
        }

        // Re-importing the refreshed template changes nothing.
        var refreshed = await (await admin.GetAsync($"{Exams}/{examId}/entry-table/template")).Content.ReadAsByteArrayAsync();
        var noop = await OkJsonAsync(await admin.PostAsync($"{Exams}/{examId}/entry-table/import", ImportForm(refreshed, "shablon.xlsx", dryRun: false)));
        Assert.Equal(0, noop.GetProperty("imported").GetInt32());
        Assert.Equal(0, noop.GetProperty("errorCount").GetInt32());

        // File-level refusals.
        var renamed = ExcelExport.Build("Natijalar", ["ID", "F.I.SH", "Sinf", "Geometriya", "Kelmadi"],
            [[pid[pupils[0].Id], "", "", "1", ""]]);
        await ErrorAsync(await admin.PostAsync($"{Exams}/{examId}/entry-table/import", ImportForm(renamed, "x.xlsx", dryRun: true)),
            HttpStatusCode.BadRequest, "unknown_column");
        await ErrorAsync(await admin.PostAsync($"{Exams}/{examId}/entry-table/import", ImportForm(file, "natijalar.xls", dryRun: true)),
            HttpStatusCode.BadRequest, "not_xlsx");
    }

    // =====================================================================
    //  8. RBAC (§4.4, §6.3)
    // =====================================================================

    /// <summary>
    /// Reads are not gated for this module; writes need <c>exams</c>; adding
    /// candidates to an admission exam needs <c>admission</c> as well; other
    /// roles are refused; anonymous is 401.
    /// </summary>
    [Fact]
    public async Task Ruxsatlar_exams_yozish_uchun_admission_nomzod_uchun()
    {
        var tag = Tag();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        using var bare = await fixture.Api.ClientAsAsync(Roles.Staff);
        using var examsOnly = await fixture.Api.ClientAsAsync(Roles.Staff, "exams");
        using var both = await fixture.Api.ClientAsAsync(Roles.Staff, "exams", "admission");
        using var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher);
        using var anonymous = fixture.Api.AnonymousClient();
        var subject = await SeedSubjectAsync($"Adabiyot {tag}");

        Assert.Equal(HttpStatusCode.OK, (await bare.GetAsync(Exams)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bare.GetAsync($"{Exams}/types")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bare.GetAsync($"{Exams}/results")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await bare.PostAsJsonAsync(Exams, BlockExam($"Ruxsatsiz {tag}", Today, [(subject, 10m)]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await bare.PostAsJsonAsync($"{Exams}/types", new { name = $"Ruxsatsiz {tag}", description = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync(Exams)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Exams)).StatusCode);

        await OkJsonAsync(await examsOnly.PostAsJsonAsync(Exams, BlockExam($"Xodim {tag}", Today, [(subject, 10m)])));

        var admission = await CreateAdmissionPaperAsync(admin, $"Qabul ruxsat {tag}", subject);
        var lead = await SeedLeadAsync($"Ruxsat nomzod {tag}");
        var refused = await ErrorAsync(await examsOnly.PostAsJsonAsync($"{Exams}/{admission}/participants", new { leadIds = new[] { lead } }),
            HttpStatusCode.Forbidden, "forbidden");
        Assert.Equal(ExamService.AdmissionPermMessage, refused.GetProperty("message").GetString());
        var ok = await OkJsonAsync(await both.PostAsJsonAsync($"{Exams}/{admission}/participants", new { leadIds = new[] { lead } }));
        Assert.Equal(1, ok.GetProperty("added").GetInt32());
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    private static object BlockExam(
        string title, string examDate, (string SubjectId, decimal Max)[] sections, string? examTypeId = null) => new
    {
        title,
        kind = "block",
        delivery = "manual",
        examTypeId,
        grade = (int?)null,
        examDate,
        opensAt = (string?)null,
        closesAt = (string?)null,
        timeLimitMin = (int?)null,
        sections = sections.Select((s, i) => new
        {
            subjectId = s.SubjectId, bankId = (string?)null, questionCount = (int?)null, maxScore = s.Max, order = i,
        }).ToArray(),
    };

    /// <summary>An admission exam sat on paper — the kind B2 can score without B3's online engine.</summary>
    private async Task<string> CreateAdmissionPaperAsync(HttpClient admin, string title, string subjectId)
    {
        var body = new
        {
            title, kind = "admission", delivery = "manual", grade = 5, examDate = Today,
            sections = new[] { new { subjectId, maxScore = 100m, order = 0 } },
        };
        return (await OkJsonAsync(await admin.PostAsJsonAsync(Exams, body))).GetProperty("id").GetString()!;
    }

    private async Task<string> CreateOnlineAsync(
        HttpClient admin, string title, int grade, (string BankId, int? QuestionCount)[] sections, int? timeLimitMin = 60)
    {
        var body = new
        {
            title, kind = "admission", delivery = "online", grade,
            opensAt = AppClock.Now.AddDays(1).ToString("yyyy-MM-ddTHH:mm:ss"),
            closesAt = AppClock.Now.AddDays(2).ToString("yyyy-MM-ddTHH:mm:ss"),
            timeLimitMin,
            sections = sections.Select((s, i) => new { bankId = s.BankId, questionCount = s.QuestionCount, order = i }).ToArray(),
        };
        return (await OkJsonAsync(await admin.PostAsJsonAsync(Exams, body))).GetProperty("id").GetString()!;
    }

    private async Task<string> SeedSubjectAsync(string name)
    {
        await using var db = NewDb();
        var subject = new Subject { Name = name };
        db.Subjects.Add(subject);
        await db.SaveChangesAsync();
        return subject.Id;
    }

    /// <summary>A class of pupils named "&lt;name&gt; &lt;tag&gt;", plus <paramref name="archived"/> archived pupils.</summary>
    private async Task<(string ClassId, string ClassName, List<(string Id, string Name)> Pupils)> SeedClassAsync(
        int grade, string[] names, int archived = 0)
    {
        var tag = Tag();
        await using var db = NewDb();
        var cls = new SchoolClass { Name = $"{grade}-EX-{tag}", Grade = grade };
        db.Classes.Add(cls);
        var pupils = names.Select(n => new Student { FullName = $"{n} {tag}", ClassName = cls.Name }).ToList();
        db.Students.AddRange(pupils);
        for (var i = 0; i < archived; i++)
            db.Students.Add(new Student { FullName = $"Arxiv {i} {tag}", ClassName = cls.Name, IsArchived = true });
        await db.SaveChangesAsync();
        return (cls.Id, cls.Name, pupils.Select(p => (p.Id, p.FullName)).ToList());
    }

    private async Task<string> SeedLeadAsync(string name)
    {
        await using var db = NewDb();
        var stage = new LeadStage { Title = $"Exam {Tag()}", Color = "blue", Order = 950 };
        db.LeadStages.Add(stage);
        var lead = new Lead
        {
            FullName = name, Gender = "female", BirthDate = "2016-03-14",
            ParentFullName = "Karimov Aziz", ParentPhone = "+998901234567", TargetGrade = 5, Stage = stage.Id,
        };
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        return lead.Id;
    }

    /// <summary>A bank with <paramref name="questions"/> two-option questions; the first <paramref name="brokenQuestions"/> have no correct option.</summary>
    private async Task<string> SeedBankAsync(
        int grade, string subjectId, int? perTest, int? minutes, decimal? perCorrect, int questions, int brokenQuestions = 0)
    {
        await using var db = NewDb();
        var bank = new QuestionBank
        {
            Grade = grade, SubjectId = subjectId, QuestionsPerTest = perTest, TimeLimitMin = minutes, PointsPerCorrect = perCorrect,
        };
        db.QuestionBanks.Add(bank);
        for (var i = 0; i < questions; i++)
        {
            var question = new Question { BankId = bank.Id, Text = $"Savol {i + 1}", Order = i };
            question.Options.Add(new QuestionOption { Text = "A", IsCorrect = i >= brokenQuestions, Order = 0 });
            question.Options.Add(new QuestionOption { Text = "B", IsCorrect = false, Order = 1 });
            db.Questions.Add(question);
        }
        await db.SaveChangesAsync();
        return bank.Id;
    }

    /// <summary><c>student_id → participant id</c> for one exam.</summary>
    private async Task<Dictionary<string, string>> ParticipantIdsAsync(string examId)
    {
        await using var db = NewDb();
        return await db.ExamParticipants.AsNoTracking()
            .Where(p => p.ExamId == examId && p.StudentId != null)
            .ToDictionaryAsync(p => p.StudentId!, p => p.Id);
    }

    private static MultipartFormDataContent ImportForm(byte[] file, string fileName, bool dryRun)
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue(XlsxMime);
        form.Add(content, "file", fileName);
        form.Add(new StringContent(dryRun ? "true" : "false"), "dryRun");
        return form;
    }

    private static async Task<JsonElement> OkJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    private static async Task<JsonElement> ErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"expected {(int)status}, got {(int)response.StatusCode}: {text}");
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement.Clone();
        Assert.Equal(code, root.GetProperty("code").GetString());
        return root;
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

// ===========================================================================
//  The score's start day (client, 2026-10-30: "yangi o'quv yili uchun hisob yangidan boshlanadi").
//  SchoolMeta.DisciplineScoreSince: entries before it stay in the history but no longer move the
//  100-based score — manual points by CreatedAt, attendance-reason days by Date. Empty = everything.
//  The setting is school-wide, so every test restores it.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class DisciplineScoreSinceTests(ApiFixture fixture)
{
    private const string Base = "/api/admin/discipline";

    [Fact]
    public async Task Start_day_drops_older_points_from_every_score_and_clearing_brings_them_back()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        // Whole history: 100 − 5 (absence, old) − 4 (manual, old) + 3 (manual, new) = 94.
        Assert.Equal(94, await RemainingAsync(admin, w.PupilId));

        await WithSinceRestoredAsync(async () =>
        {
            var put = await admin.PutAsJsonAsync($"{Base}/score-since", new { since = w.Since });
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
            Assert.Equal(w.Since, (await JsonAsync(await admin.GetAsync($"{Base}/score-since"))).GetProperty("since").GetString());

            // Ballar nazorati, the shared totals and the parent card agree: only the new +3 counts.
            Assert.Equal(103, await RemainingAsync(admin, w.PupilId));
            await fixture.Api.WithDbAsync(async db =>
            {
                var totals = await DisciplineIncidents.TotalsAsync(db, [w.PupilId]);
                Assert.Equal((3, 0), totals.GetValueOrDefault(w.PupilId));
            });
            var card = await JsonAsync(await admin.GetAsync($"/api/student/discipline?studentId={w.PupilId}"));
            Assert.Equal(103, card.GetProperty("remaining").GetInt32());
            Assert.Equal(1, card.GetProperty("items").GetArrayLength());

            // Nothing was deleted: the history keeps all three entries.
            var history = await JsonAsync(await admin.GetAsync($"{Base}/points?studentId={w.PupilId}"));
            Assert.Equal(3, history.GetArrayLength());

            // Clearing it counts the whole history again.
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"{Base}/score-since", new { since = "" })).StatusCode);
            Assert.Equal(94, await RemainingAsync(admin, w.PupilId));
        });
    }

    [Fact]
    public async Task Start_day_refuses_a_future_or_malformed_date_and_a_teacher()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tomorrow = AppClock.Today.AddDays(1).ToString("yyyy-MM-dd");

        await WithSinceRestoredAsync(async () =>
        {
            Assert.Equal(HttpStatusCode.BadRequest,
                (await admin.PutAsJsonAsync($"{Base}/score-since", new { since = tomorrow })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,
                (await admin.PutAsJsonAsync($"{Base}/score-since", new { since = "30.10.2026" })).StatusCode);

            using var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher, "discipline");
            Assert.Equal(HttpStatusCode.Forbidden,
                (await teacher.PutAsJsonAsync($"{Base}/score-since", new { since = "" })).StatusCode);
        });
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    private sealed record World(string PupilId, string Since);

    /// <summary>
    /// One pupil: an absence (−5) and a manual −4 before the start day, a manual +3 on it.
    /// </summary>
    private async Task<World> SeedAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var since = AppClock.Today.AddDays(-10);
        var old = since.AddDays(-5).ToString("yyyy-MM-dd");
        var cls = new SchoolClass { Name = $"8-{tag}", Grade = 8 };
        var subject = new Subject { Name = $"Fan {tag}" };
        var absence = new AbsenceReason { Name = $"Sababsiz {tag}", Short = "N", Points = -5 };
        var minus = new DisciplineReason { Name = $"Jazo {tag}", Points = -4, IsActive = true };
        var plus = new DisciplineReason { Name = $"Rag'bat {tag}", Points = 3, IsActive = true };
        var pupil = GeneralSettingsFlagsTests.NewStudent($"Hisob {tag}", cls.Name, "+99890" + Random.Shared.Next(1_000_000, 9_999_999));

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(cls);
            db.Subjects.Add(subject);
            db.AbsenceReasons.Add(absence);
            db.DisciplineReasons.AddRange(minus, plus);
            db.Students.Add(pupil);
            db.JournalEntries.Add(new JournalEntry
            {
                ClassId = cls.Id, SubjectId = subject.Id, Quarter = 1, StudentId = pupil.Id,
                Date = old, Period = 1, ReasonId = absence.Id,
            });
            db.DisciplinePoints.AddRange(
                new DisciplinePoint
                {
                    StudentId = pupil.Id, ReasonId = minus.Id, ReasonName = minus.Name, Points = -4,
                    Note = "", CreatedBy = "Test", CreatedAt = old + "T10:00:00",
                },
                new DisciplinePoint
                {
                    StudentId = pupil.Id, ReasonId = plus.Id, ReasonName = plus.Name, Points = 3,
                    Note = "", CreatedBy = "Test", CreatedAt = since.ToString("yyyy-MM-dd") + "T08:00:00",
                });
            await db.SaveChangesAsync();
        });

        return new World(pupil.Id, since.ToString("yyyy-MM-dd"));
    }

    /// <summary>Runs the body and puts the school's start day back as it was.</summary>
    private async Task WithSinceRestoredAsync(Func<Task> body)
    {
        var before = "";
        string? createdId = null;
        await fixture.Api.WithDbAsync(async db =>
        {
            var meta = await db.SchoolMeta.OrderBy(m => m.Id).FirstOrDefaultAsync();
            if (meta is null)
            {
                meta = new SchoolMeta();
                db.SchoolMeta.Add(meta);
                await db.SaveChangesAsync();
                createdId = meta.Id;
            }
            before = meta.DisciplineScoreSince;
        });
        try
        {
            await body();
        }
        finally
        {
            await fixture.Api.WithDbAsync(async db =>
            {
                if (createdId is not null)
                    await db.SchoolMeta.Where(m => m.Id == createdId).ExecuteDeleteAsync();
                else
                    await db.SchoolMeta.ExecuteUpdateAsync(s => s.SetProperty(m => m.DisciplineScoreSince, before));
            });
        }
    }

    private static async Task<int> RemainingAsync(HttpClient client, string studentId)
    {
        var rows = await JsonAsync(await client.GetAsync($"{Base}/scores"));
        return rows.EnumerateArray()
            .Single(r => r.GetProperty("studentId").GetString() == studentId)
            .GetProperty("remaining").GetInt32();
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode,
            $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}

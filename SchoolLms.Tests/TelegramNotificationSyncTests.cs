using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// The school's Telegram notifications follow the accounts signed in on a chat (client, 2026-10-08:
/// "ota-onalar login parol bilan bo'lsa ham kiryapti ... telegrami bog'lanyapti ... shunda ularga
/// boraversin"). Every notifier reads <c>telegram_registrations</c>; signing in with a pupil's login
/// registers the pupil and the family, a teacher's — the teacher. Rows from the old phone sharing
/// are never touched; rows made here go when their account leaves the chat.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class TelegramNotificationSyncTests(ApiFixture fixture)
{
    private static long _telegram = 8_500_000_000 + Random.Shared.Next(1, 90_000_000);

    [Fact]
    public async Task Oquvchi_logini_bilan_kirish_oilani_royxatga_oladi_chiqish_olib_tashlaydi()
    {
        var phone = "+99891" + Random.Shared.Next(1_000_000, 9_999_999);
        var (user, a) = await PupilAsync("Aka", phone);
        var (_, b) = await PupilAsync("Uka", phone);
        var (_, phoneOnly) = await PupilAsync("Eski", "+99892" + Random.Shared.Next(1_000_000, 9_999_999));
        var chat = Interlocked.Increment(ref _telegram);
        var key = "";
        await fixture.Api.WithDbAsync(async db =>
        {
            var account = new TelegramAccount { TelegramUserId = chat, UserId = user.Id, DisplayName = "Ota" };
            db.TelegramAccounts.Add(account);
            key = TelegramPanelKeys.Issue(account);
            // A row from the old phone sharing — it must survive whatever happens below.
            db.TelegramRegistrations.Add(new TelegramRegistration { StudentId = phoneOnly.Id, ChatId = chat, Phone = "998920000000" });
            await db.SaveChangesAsync();
        });

        // Opening the panel signs in — the pupil and the brother start receiving notifications.
        using var anonymous = fixture.Api.AnonymousClient();
        var opened = await anonymous.PostAsJsonAsync("/api/tg/auth/key", new { key });
        Assert.True(opened.IsSuccessStatusCode, await opened.Content.ReadAsStringAsync());
        Assert.Equal(new[] { a.Id, b.Id, phoneOnly.Id }.Order().ToArray(), await StudentsOfChatAsync(chat));

        // "Chiqish" from the last account — the login rows go, the phone row stays.
        var token = (await opened.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("token").GetString()!;
        using var signedIn = fixture.Api.ClientWithToken(token);
        Assert.True((await signedIn.DeleteAsync("/api/tg/link")).IsSuccessStatusCode);
        Assert.Equal([phoneOnly.Id], await StudentsOfChatAsync(chat));
    }

    [Fact]
    public async Task Oqituvchi_logini_oqituvchini_royxatga_oladi()
    {
        var (teacher, _) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        var chat = Interlocked.Increment(ref _telegram);
        string teacherId = "";
        await fixture.Api.WithDbAsync(async db =>
        {
            teacherId = (await db.Teachers.SingleAsync(t => t.UserId == teacher.Id)).Id;
            db.TelegramAccounts.Add(new TelegramAccount { TelegramUserId = chat, UserId = teacher.Id, DisplayName = "Ustoz" });
            await db.SaveChangesAsync();
            await TelegramSavedAccounts.SyncRegistrationsAsync(db, chat);
        });

        await fixture.Api.WithDbAsync(async db =>
        {
            var row = await db.TelegramRegistrations.SingleAsync(r => r.ChatId == chat);
            Assert.Equal(teacherId, row.TeacherId);
            Assert.Equal("", row.StudentId);
        });
    }

    // =================================================================

    private async Task<(AppUser User, Student Student)> PupilAsync(string name, string parentPhone)
    {
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Student);
        var student = new Student
        {
            FullName = $"{name} {Guid.NewGuid().ToString("N")[..6]}",
            ClassName = "3-B",
            EnrollmentDate = "2026-09-01",
            ParentPhone = parentPhone,
            UserId = user.Id,
        };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Students.Add(student);
            await db.SaveChangesAsync();
        });
        return (user, student);
    }

    private async Task<string[]> StudentsOfChatAsync(long chat)
    {
        string[] ids = [];
        await fixture.Api.WithDbAsync(async db =>
            ids = [.. (await db.TelegramRegistrations.Where(r => r.ChatId == chat).Select(r => r.StudentId).ToListAsync()).Order()]);
        return ids;
    }
}

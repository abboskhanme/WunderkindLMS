using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// GURUHGA YETISH — ikkita teshik, ikkalasi ham ota-onaga boradigan yo'lda
/// (docs/modules/students-parity.md §2.1.6 G-17 va §2.1.1 "Roster page").
///
/// <list type="number">
///   <item><b>O'quvchi portali guruh chati</b> — 2026-10-11 dan BUTUNLAY yo'q (mijoz:
///     "umumiy chat ota-onalar, o'qituvchilar akkauntlarida ham ko'rinmasligi kerak").
///     Mini App'ning "Maktab e'lonlari" kartasi endi chatni emas, haqiqiy e'lonlarni
///     o'qiydi (<c>GET /api/student/announcements</c>).</item>
///   <item><b>E'lon guruh ota-onalariga yetmasdi.</b>
///     <c>POST /api/admin/messages/broadcast</c> da <c>scope: "group"</c> yo'q edi;
///     guruh bir nechta sinfdan yig'ilgani uchun "sinfga e'lon" uning yarmiga
///     yetmasdi.</item>
/// </list>
///
/// <para>
/// <b>E'lon — HAQIQIY ota-onaga Telegram xabari.</b> Shuning uchun uchta qoida
/// test bilan mahkamlanadi: (1) hech narsa CHAQIRUVCHI so'ramaguncha yuborilmaydi —
/// qamrov faqat <c>scope: "group"</c> + guruh id'si bilan ishlaydi; (2) testda hech
/// qayerga so'rov ketmaydi — <c>ApiFactory</c> <c>Telegram__BotToken</c> ni BO'SH
/// qiladi, ya'ni <c>TelegramService.IsConfigured</c> false va
/// <c>SendMessageAsync</c> tarmoqqa chiqmasdan <c>false</c> qaytaradi;
/// (3) javob NECHTA chat haqiqatan olganini aytadi (<c>sentCount</c>), mos kelgan
/// chatlar soni esa alohida (<c>recipientCount</c>).
/// </para>
/// <para>
/// Testlar UMUMIY bazada yuradi: har biri o'z ma'lumotini TAKRORLANMAS nom bilan
/// ajratadi va har so'rov aniq id bilan chaqiriladi. Cut-over o'chirgichi
/// <see cref="WithGroupLessonsAsync"/> bilan yoqiladi va <c>finally</c> da ASL
/// holatiga qaytariladi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class GroupChatAndBroadcastTests(ApiFixture fixture)
{
    private const string Broadcast = "/api/admin/messages/broadcast";

    // =====================================================================
    //  1. O'quvchi portali — guruh chati yo'q, "Maktab e'lonlari" — e'lonlar
    // =====================================================================

    /// <summary>
    /// Guruh chati o'quvchi va ota-ona yuzasidan olib tashlandi (2026-10-11): o'quvchining
    /// o'zi ham, uning nomidan admin ham chat yo'llarini topa olmaydi.
    /// </summary>
    [Fact]
    public async Task Oquvchi_portalida_guruh_chati_yoq()
    {
        var w = await SeedAsync();
        var (pupil, _) = await fixture.Api.SeedUserAsync(Roles.Student);
        await fixture.Api.WithDbAsync(async db =>
        {
            (await db.Students.SingleAsync(s => s.Id == w.MemberId)).UserId = pupil.Id;
            await db.SaveChangesAsync();
        });
        using var student = fixture.Api.ClientWithToken(fixture.Api.TokenFor(Roles.Student, pupil.Id, "A'zo"));
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        foreach (var client in new[] { student, admin })
        {
            Assert.Equal(HttpStatusCode.NotFound,
                (await client.GetAsync($"/api/student/chat?studentId={w.MemberId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await client.GetAsync($"/api/student/chat/channels?studentId={w.MemberId}")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound,
            (await student.PostAsJsonAsync("/api/student/chat", new { text = "Salom" })).StatusCode);
    }

    /// <summary>
    /// Mini App'ning "Maktab e'lonlari" kartasi: shu bolaga yuborilgan e'lonlar (oluvchilar
    /// ro'yxati bo'yicha), formatlash belgilarisiz. Sinf chatidagi xabar bu yerga TUSHMAYDI,
    /// boshqa bolaga yuborilgan e'lon ham.
    /// </summary>
    [Fact]
    public async Task Maktab_elonlari_chat_emas_bolaga_yuborilgan_elonlar()
    {
        var w = await SeedAsync();
        var tag = Guid.NewGuid().ToString("N")[..6];
        var mine = new Broadcast { ClassName = "Tanlanganlar", Text = $"**Majlis** {tag}", SenderName = "Admin" };
        var others = new Broadcast { ClassName = "Tanlanganlar", Text = $"Boshqaga {tag}", SenderName = "Admin" };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Broadcasts.AddRange(mine, others);
            db.BroadcastRecipients.AddRange(
                new BroadcastRecipient { BroadcastId = mine.Id, StudentId = w.MemberId },
                new BroadcastRecipient { BroadcastId = others.Id, StudentId = w.OutsiderId });
            await db.SaveChangesAsync();
        });

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var response = await admin.GetAsync($"/api/student/announcements?studentId={w.MemberId}");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var texts = (await response.Content.ReadFromJsonAsync<List<Message>>())!.Select(m => m.Text).ToList();

        Assert.Contains($"Majlis {tag}", texts);
        Assert.DoesNotContain($"Boshqaga {tag}", texts);
        Assert.DoesNotContain(w.ClassMessage, texts);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/student/announcements")).StatusCode);
    }

    // =====================================================================
    //  2. E'lon — guruh qamrovi
    // =====================================================================

    /// <summary>
    /// <c>scope: "group"</c> guruhning FAOL a'zolarining ro'yxatdan o'tgan
    /// chatlarini oladi — BOQUVCHI sinfdan qat'i nazar. Seedda guruhga IKKI
    /// sinfdan bittadan bola kirgan va ikkalasining ham ota-onasi botda; sinfdosh
    /// (guruhsiz) va guruhdan CHIQIB ketgan bola esa e'lonni OLMAYDI.
    ///
    /// <para>
    /// <c>sentCount = 0</c> — testda bot sozlanmagan, ya'ni hech qayerga so'rov
    /// ketmadi; <c>recipientCount = 2</c> esa "haqiqiy hayotda ikkita chat olardi"
    /// degani. Ekran ham aynan shu ikki raqamni ko'rsatadi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Guruh_qamrovi_faol_azolarning_ota_onalariga_boradi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await admin.PostAsJsonAsync(Broadcast, new
        {
            scope = "group",
            groupId = w.GroupId,
            onlyDebtors = false,
            text = "Ertaga guruh darsi bo'lmaydi",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal($"Guruh: {w.GroupName}", body.GetProperty("className").GetString());
        Assert.Equal(2, body.GetProperty("recipientCount").GetInt32());
        Assert.Equal(0, body.GetProperty("sentCount").GetInt32());
    }

    /// <summary>
    /// Guruh qamrovi <c>group_lessons_enabled</c> o'chirgichiga BOG'LIQ EMAS:
    /// o'chirgich guruh DARSLARINI ushlab turadi, guruhning ro'yxati esa undan
    /// oldin ham bor va ekranda ko'rinadi. Hech qanday ro'yxat kengaymaydi —
    /// qamrov faqat chaqiruvchi guruhni ANIQ ko'rsatganda ishlaydi.
    /// </summary>
    [Fact]
    public async Task Guruh_qamrovi_ochirgichdan_mustaqil()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        await WithGroupLessonsAsync(async () =>
        {
            var response = await admin.PostAsJsonAsync(Broadcast, new
            {
                scope = "group", groupId = w.GroupId, onlyDebtors = false, text = "Salom",
            });
            var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal(2, body.GetProperty("recipientCount").GetInt32());
        });
    }

    /// <summary>
    /// Guruh ko'rsatilmasa yoki topilmasa — hech narsa yuborilmaydi va e'lon
    /// tarixga ham yozilmaydi (400 / 404).
    /// </summary>
    [Fact]
    public async Task Guruhsiz_yoki_notogri_guruh_bilan_elon_yuborilmaydi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var noGroup = await admin.PostAsJsonAsync(Broadcast, new
        {
            scope = "group", onlyDebtors = false, text = "Salom",
        });
        Assert.Equal(HttpStatusCode.BadRequest, noGroup.StatusCode);

        var missing = await admin.PostAsJsonAsync(Broadcast, new
        {
            scope = "group", groupId = Guid.NewGuid().ToString(), onlyDebtors = false, text = "Salom",
        });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    /// <summary>
    /// Darvoza o'zgarmadi: <c>AdminPerm("messages")</c> ruxsatisiz rol guruhga
    /// ham e'lon yubora olmaydi (403), token'siz so'rov — 401.
    /// </summary>
    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    public async Task Ruxsatsiz_rol_guruhga_elon_yubora_olmaydi(string role)
    {
        var w = await SeedAsync();
        using var client = await fixture.Api.ClientAsAsync(role, "messages");

        var response = await client.PostAsJsonAsync(Broadcast, new
        {
            scope = "group", groupId = w.GroupId, onlyDebtors = false, text = "Salom",
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var anonymous = fixture.Api.AnonymousClient();
        var unauthorized = await anonymous.PostAsJsonAsync(Broadcast, new
        {
            scope = "group", groupId = w.GroupId, onlyDebtors = false, text = "Salom",
        });
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
    }

    // =====================================================================
    //  3. E'lon — filtr qamrovi (S-6, students-parity.md §2.3.3)
    // =====================================================================

    /// <summary>
    /// <c>scope: "filter"</c> — o'quvchilar ro'yxati ekranidagi JORIY filtrga
    /// mos BARCHA o'quvchi, tanlangan qatorlardan MUSTAQIL (EduSchool'dagi
    /// "barcha sahifalar"). Qamrov AYNAN <c>StudentListQuery</c> orqali
    /// hisoblanadi — shu yerda sinf nomi bo'yicha filtrlanadi: mos sinfdagi
    /// ikkita bola ota-onasi oladi, boshqa sinfdagi bola OLMAYDI.
    /// </summary>
    [Fact]
    public async Task Filtr_qamrovi_joriy_filtrga_mos_barchaga_boradi()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var className = $"7A-{tag}";
        var inClass1 = GeneralSettingsFlagsTests.NewStudent($"Ichkari 1 {tag}", className, "+99890" + Rnd());
        var inClass2 = GeneralSettingsFlagsTests.NewStudent($"Ichkari 2 {tag}", className, "+99890" + Rnd());
        var outside = GeneralSettingsFlagsTests.NewStudent($"Tashqari {tag}", $"7B-{tag}", "+99890" + Rnd());

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.AddRange(
                new SchoolClass { Name = className, Grade = 7 },
                new SchoolClass { Name = $"7B-{tag}", Grade = 7 });
            db.Students.AddRange(inClass1, inClass2, outside);
            foreach (var id in new[] { inClass1.Id, inClass2.Id, outside.Id })
                db.TelegramRegistrations.Add(new TelegramRegistration
                {
                    StudentId = id, ChatId = Random.Shared.NextInt64(1, long.MaxValue),
                    ParentName = "Ota-ona", Phone = "+99890" + Rnd(),
                });
            await db.SaveChangesAsync();
        });

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var response = await admin.PostAsJsonAsync(Broadcast, new
        {
            scope = "filter",
            onlyDebtors = false,
            text = "Ertaga ota-onalar yig'ilishi",
            filter = new { className },
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(2, body.GetProperty("recipientCount").GetInt32());
        Assert.Contains("Filtr bo'yicha", body.GetProperty("className").GetString());
    }

    /// <summary>Filtrga mos hech kim topilmasa — 400, hech narsa tarixga yozilmaydi.</summary>
    [Fact]
    public async Task Filtr_qamrovi_mos_kelmasa_400()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var response = await admin.PostAsJsonAsync(Broadcast, new
        {
            scope = "filter",
            onlyDebtors = false,
            text = "Salom",
            filter = new { className = $"yoq-sinf-{Guid.NewGuid():N}" },
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Darvoza o'zgarmadi: <c>AdminPerm("messages")</c> ruxsatisiz rol filtr
    /// qamroviga ham e'lon yubora olmaydi (403), token'siz so'rov — 401. Bu
    /// qamrov butun maktabga yetishi mumkin — darvozasi eng kamida boshqalar
    /// bilan bir xil bo'lishi shart.
    /// </summary>
    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    public async Task Ruxsatsiz_rol_filtr_qamroviga_elon_yubora_olmaydi(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role, "messages");
        var response = await client.PostAsJsonAsync(Broadcast, new
        {
            scope = "filter", onlyDebtors = false, text = "Salom",
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var anonymous = fixture.Api.AnonymousClient();
        var unauthorized = await anonymous.PostAsJsonAsync(Broadcast, new
        {
            scope = "filter", onlyDebtors = false, text = "Salom",
        });
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private sealed record Message(string Text);

    /// <summary>
    /// Cut-over o'chirgichini yoqib turadi va ASL holatiga qaytaradi
    /// (<c>finally</c> da: test yiqilsa ham yoqilgan qolib ketmasin).
    /// </summary>
    private async Task WithGroupLessonsAsync(Func<Task> body)
    {
        var before = false;
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
            before = meta.GroupLessonsEnabled;
            meta.GroupLessonsEnabled = true;
            await db.SaveChangesAsync();
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
                {
                    await db.SchoolMeta.Where(m => m.Id == createdId).ExecuteDeleteAsync();
                    return;
                }
                var meta = await db.SchoolMeta.OrderBy(m => m.Id).FirstAsync();
                meta.GroupLessonsEnabled = before;
                await db.SaveChangesAsync();
            });
        }
    }

    private sealed record World(
        string ClassName, string GroupId, string GroupName, string GroupChannel,
        string MemberId, string OtherClassMemberId, string OutsiderId, string LeftMemberId,
        string ClassMessage, string GroupMessage);

    /// <summary>
    /// IKKI sinf va bitta guruh. Guruhda: birinchi sinfdan <c>Member</c>, ikkinchi
    /// sinfdan <c>OtherClassMember</c> (ikkalasining ota-onasi botda) va CHIQIB
    /// ketgan <c>LeftMember</c>. Guruhsiz sinfdosh — <c>Outsider</c> (u ham botda).
    /// Ya'ni "sinfga e'lon" guruhning yarmini o'tkazib yuborardi, "guruhga e'lon"
    /// esa aynan ikkitasiga boradi.
    /// </summary>
    private async Task<World> SeedAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var classA = new SchoolClass { Name = $"8A-{tag}", Grade = 8 };
        var classB = new SchoolClass { Name = $"8B-{tag}", Grade = 8 };
        var english = new Subject { Name = $"Ingliz tili {tag}", IsGroupable = true };

        var member = GeneralSettingsFlagsTests.NewStudent($"A'zo {tag}", classA.Name, "+99890" + Rnd());
        var otherClassMember = GeneralSettingsFlagsTests.NewStudent(
            $"Boshqa sinf a'zosi {tag}", classB.Name, "+99890" + Rnd());
        var outsider = GeneralSettingsFlagsTests.NewStudent($"Guruhsiz {tag}", classA.Name, "+99890" + Rnd());
        var leftMember = GeneralSettingsFlagsTests.NewStudent($"Chiqqan {tag}", classA.Name, "+99890" + Rnd());

        var (creator, _) = await fixture.Api.SeedUserAsync(Roles.Admin);
        var group = new StudyGroup
        {
            Name = $"Kuchli ingliz {tag}",
            SubjectId = english.Id,
            CreatedBy = creator.Id,
            CreatedAt = AppClock.NowInstant,
        };
        var classMessage = $"Sinf xabari {tag}";
        var groupMessage = $"Guruh xabari {tag}";

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.AddRange(classA, classB);
            db.Subjects.Add(english);
            db.Students.AddRange(member, otherClassMember, outsider, leftMember);
            db.StudyGroups.Add(group);
            db.StudyGroupClasses.AddRange(
                new StudyGroupClass { GroupId = group.Id, ClassId = classA.Id },
                new StudyGroupClass { GroupId = group.Id, ClassId = classB.Id });

            var joined = new DateOnly(2026, 9, 1);
            foreach (var id in new[] { member.Id, otherClassMember.Id })
                db.StudyGroupMembers.Add(new StudyGroupMember
                {
                    GroupId = group.Id, SubjectId = english.Id, StudentId = id,
                    JoinedOn = joined, CreatedBy = creator.Id, CreatedAt = AppClock.NowInstant,
                });
            db.StudyGroupMembers.Add(new StudyGroupMember
            {
                GroupId = group.Id, SubjectId = english.Id, StudentId = leftMember.Id,
                JoinedOn = joined, LeftOn = new DateOnly(2026, 9, 10),
                LeaveReason = "Guruhdan chiqdi",
                CreatedBy = creator.Id, CreatedAt = AppClock.NowInstant,
            });

            // Botga ulangan ota-onalar: guruhdagi ikkisi + guruhsiz sinfdosh.
            // Chiqib ketganning ota-onasi ham botda — u e'lonni OLMASLIGI kerak.
            foreach (var id in new[] { member.Id, otherClassMember.Id, outsider.Id, leftMember.Id })
                db.TelegramRegistrations.Add(new TelegramRegistration
                {
                    StudentId = id, ChatId = Random.Shared.NextInt64(1, long.MaxValue),
                    ParentName = "Ota-ona", Phone = "+99890" + Rnd(),
                });

            db.ChatMessages.AddRange(
                new ChatMessage
                {
                    ClassName = classA.Name, SenderUserId = creator.Id, SenderName = "Admin",
                    SenderRole = Roles.Admin, Text = classMessage, CreatedAt = AppClock.Now,
                },
                new ChatMessage
                {
                    ClassName = ChatService.GroupChannel(group.Id), SenderUserId = creator.Id,
                    SenderName = "Admin", SenderRole = Roles.Admin, Text = groupMessage,
                    CreatedAt = AppClock.Now,
                });

            await db.SaveChangesAsync();
        });

        return new World(
            classA.Name, group.Id.ToString(), group.Name, ChatService.GroupChannel(group.Id),
            member.Id, otherClassMember.Id, outsider.Id, leftMember.Id,
            classMessage, groupMessage);
    }

    private static string Rnd() => Random.Shared.Next(1_000_000, 9_999_999).ToString();
}

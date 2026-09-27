using SchoolLms.Application.HrTasks;
using SchoolLms.Domain;

namespace SchoolLms.Tests.HrTasks;

// ===========================================================================
//  HR topshiriqlari — pure rules (no database, no network): recurrence,
//  reminders, the task state machine, the manager tree and the Telegram texts
//  (old tests/unit/hr/test_recurrence.py, test_domain.py, test_digest.py).
// ===========================================================================

public class HrTasksRulesTests
{
    private static readonly TimeZoneInfo Tz = HrTasksKit.Tz;

    // ---------------------------------------------------------------- recurrence

    [Theory]
    [InlineData(2026, 9, 28, 1)] // Monday
    [InlineData(2026, 10, 4, 7)] // Sunday
    [InlineData(2026, 10, 3, 6)] // Saturday
    public void Hafta_kuni_ISO_1_dushanba_7_yakshanba(int y, int m, int d, int iso) =>
        Assert.Equal(iso, HrRecurrenceRules.IsoWeekday(new DateOnly(y, m, d)));

    [Fact]
    public void Kunlik_va_haftalik_faqat_tanlangan_kunlarda()
    {
        var monday = new DateOnly(2026, 9, 28);
        Assert.True(HrRecurrenceRules.OccursOn(HrRecurrences.Weekly, [1, 3], [], monday));
        Assert.False(HrRecurrenceRules.OccursOn(HrRecurrences.Weekly, [1, 3], [], monday.AddDays(1)));
        Assert.True(HrRecurrenceRules.OccursOn(HrRecurrences.Weekly, [1, 3], [], monday.AddDays(2)));
        Assert.False(HrRecurrenceRules.OccursOn(HrRecurrences.Daily, [1, 2, 3, 4, 5], [], monday.AddDays(6))); // Sunday
        Assert.False(HrRecurrenceRules.OccursOn(HrRecurrences.Daily, [], [], monday)); // empty = not due, not an error
    }

    [Fact]
    public void Qisqa_oy_31_oyning_oxirgi_kuniga_suriladi()
    {
        Assert.True(HrRecurrenceRules.OccursOn(HrRecurrences.Monthly, [], [31], new DateOnly(2026, 4, 30)));
        Assert.False(HrRecurrenceRules.OccursOn(HrRecurrences.Monthly, [], [31], new DateOnly(2026, 4, 29)));
        Assert.True(HrRecurrenceRules.OccursOn(HrRecurrences.Monthly, [], [30], new DateOnly(2027, 2, 28)));
        Assert.True(HrRecurrenceRules.OccursOn(HrRecurrences.Monthly, [], [29], new DateOnly(2028, 2, 29))); // leap year

        Assert.Equal([new DateOnly(2026, 4, 14), new DateOnly(2026, 4, 30)],
            HrRecurrenceRules.MonthlyOccurrences([31, 14], new DateOnly(2026, 4, 1)));
        // {30, 31} in February collapse into ONE date
        Assert.Equal([new DateOnly(2027, 2, 28)], HrRecurrenceRules.MonthlyOccurrences([30, 31], new DateOnly(2027, 2, 10)));
    }

    [Fact]
    public void Davr_boshi_va_muddat()
    {
        Assert.Equal(new DateOnly(2026, 9, 1), HrRecurrenceRules.PeriodStartFor(HrRecurrences.Monthly, new DateOnly(2026, 9, 14)));
        Assert.Equal(new DateOnly(2026, 9, 14), HrRecurrenceRules.PeriodStartFor(HrRecurrences.Daily, new DateOnly(2026, 9, 14)));
        // no due time → 23:59 local = 18:59 UTC
        var due = HrRecurrenceRules.DueAtFor(new DateOnly(2026, 9, 14), null, Tz);
        Assert.Equal(new DateTimeOffset(2026, 9, 14, 18, 59, 0, TimeSpan.Zero), due);
        Assert.Equal(TimeSpan.Zero, due.Offset);
        Assert.Equal(new DateTimeOffset(2026, 9, 14, 13, 0, 0, TimeSpan.Zero),
            HrRecurrenceRules.DueAtFor(new DateOnly(2026, 9, 14), new TimeOnly(18, 0), Tz));
    }

    [Fact]
    public void Takroriylik_tekshiruvi_va_normallashuvi()
    {
        Assert.Equal("hr_invalid_recurrence",
            Assert.Throws<HrTasksException>(() => HrRecurrenceRules.Validate(HrRecurrences.Monthly, [1], [])).Code);
        Assert.Equal("hr_invalid_recurrence",
            Assert.Throws<HrTasksException>(() => HrRecurrenceRules.Validate(HrRecurrences.Daily, [8], [])).Code);
        Assert.Equal("hr_invalid_recurrence",
            Assert.Throws<HrTasksException>(() => HrRecurrenceRules.Validate(HrRecurrences.Monthly, [], [32])).Code);
        // the other list is emptied — no "ghost" schedule after switching the type
        var (week, month) = HrRecurrenceRules.Validate(HrRecurrences.Monthly, [1, 2], [28, 14, 14]);
        Assert.Empty(week);
        Assert.Equal([14, 28], month);
    }

    [Fact]
    public void Oylik_shablon_otgan_sanani_yaratmaydi()
    {
        var template = new HrTaskTemplate { Recurrence = HrRecurrences.Monthly, DaysOfMonth = [5, 25], DueTime = new TimeOnly(18, 0) };
        var now = HrTasksKit.At(2031, 4, 10, 9); // the 5th already passed
        Assert.Equal([new DateOnly(2031, 4, 25)], HrTasksService.Occurrences(template, new DateOnly(2031, 4, 10), now, Tz));
        var weekly = new HrTaskTemplate { Recurrence = HrRecurrences.Weekly, Weekdays = [2] };
        Assert.Empty(HrTasksService.Occurrences(weekly, new DateOnly(2031, 4, 14), now, Tz)); // Monday
        Assert.Single(HrTasksService.Occurrences(weekly, new DateOnly(2031, 4, 15), now, Tz)); // Tuesday
    }

    // ---------------------------------------------------------------- reminders

    [Fact]
    public void Eslatma_offsetlari_kattadan_kichikka_otgani_yoq()
    {
        var due = new DateTimeOffset(2026, 9, 14, 13, 0, 0, TimeSpan.Zero);
        Assert.Equal([120], HrReminderRules.DueOffsets(due, due.AddMinutes(-100), [120, 60, 30]));
        Assert.Equal([120, 60], HrReminderRules.DueOffsets(due, due.AddMinutes(-50), [30, 60, 120]));
        Assert.Empty(HrReminderRules.DueOffsets(due, due, [120, 60, 30])); // at the deadline — too late
        Assert.Empty(HrReminderRules.DueOffsets(due, due.AddMinutes(-200), [120]));
        Assert.True(HrReminderRules.IsEndOfDay(HrRecurrenceRules.DueAtFor(new DateOnly(2026, 9, 14), null, Tz), Tz));
        Assert.False(HrReminderRules.IsEndOfDay(due, Tz));
        Assert.Equal("1 soat 30 daqiqa", HrTasksTexts.HumanOffset(90));
        Assert.Equal("2 soat", HrTasksTexts.HumanOffset(120));
    }

    [Fact]
    public void Eslatma_sozlamasi_chegaralari()
    {
        Assert.Equal([120, 60, 30], HrSettings.CleanReminderOffsets([30, 120, 60, 60]).Select(x => (int)x));
        Assert.Throws<HrTasksException>(() => HrSettings.CleanReminderOffsets([4]));
        Assert.Throws<HrTasksException>(() => HrSettings.CleanReminderOffsets([1441]));
        Assert.Throws<HrTasksException>(() => HrSettings.CleanReminderOffsets([10, 20, 30, 40, 50, 60]));
        Assert.Empty(HrSettings.CleanReminderOffsets([]));
    }

    // ---------------------------------------------------------------- the state machine

    private static HrTask Task(string kind = HrTaskKinds.Check, decimal? target = null, string source = HrTaskSources.Director) => new()
    {
        EmployeeId = "e", Title = "Hisobot", Kind = kind, TargetAmount = target, Source = source,
        DueAt = new DateTimeOffset(2026, 9, 14, 13, 0, 0, TimeSpan.Zero), PeriodStart = new DateOnly(2026, 9, 14),
    };

    [Fact]
    public void Isbotsiz_yopib_bolmaydi_izoh_yoki_fayl_yetadi()
    {
        var now = new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
        var task = Task();
        Assert.Equal("hr_proof_required", Assert.Throws<HrTasksException>(() => task.Complete(now, "   ", 0)).Code);
        Assert.True(task.Complete(now, "Mijozga qo'ng'iroq qilindi", 0));
        Assert.Equal(HrTaskStatuses.Done, task.Status);
        Assert.False(task.IsLate);
        Assert.NotNull(task.AcceptedAt); // done implies seen
        Assert.False(task.Complete(now, "yana", 0)); // idempotent

        var withFile = Task();
        Assert.True(withFile.Complete(now.AddHours(2), null, 1)); // a file alone is enough
        Assert.True(withFile.IsLate); // after due_at
    }

    [Fact]
    public void Qaytarish_faqat_bajarilgandan_bekor_faqat_ochiqdan()
    {
        var now = new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
        var task = Task();
        Assert.Equal("hr_task_not_editable", Assert.Throws<HrTasksException>(() => task.MarkReturned(now, "x")).Code);
        task.Complete(now, "ok", 0);
        Assert.True(task.MarkReturned(now, "Qayta qiling"));
        Assert.Equal(HrTaskStatuses.Returned, task.Status);
        Assert.NotNull(task.CompletedAt); // history kept
        Assert.True(task.Complete(now, "qayta qildim", 0)); // returned works as open
        Assert.Equal("hr_task_not_editable", Assert.Throws<HrTasksException>(() => task.Cancel(now)).Code);

        var open = Task();
        Assert.True(open.Cancel(now, "kerak emas"));
        Assert.Equal("hr_task_not_editable", Assert.Throws<HrTasksException>(() => open.Complete(now, "x", 0)).Code);
        Assert.Equal("hr_task_not_editable", Assert.Throws<HrTasksException>(() => open.Accept(now)).Code);
    }

    [Fact]
    public void KPI_maqsadga_yetganda_avtomatik_bajariladi()
    {
        var now = new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
        var task = Task(HrTaskKinds.Numeric, 200);
        task.AddAmount(now, 147);
        Assert.Equal(HrTaskStatuses.Open, task.Status);
        Assert.Equal("hr_invalid_amount", Assert.Throws<HrTasksException>(() => task.AddAmount(now, 0)).Code);
        task.AddAmount(now, -7); // a correction
        Assert.Equal(140m, task.CurrentAmount);
        task.AddAmount(now, 60);
        Assert.Equal(HrTaskStatuses.Done, task.Status);
        Assert.Equal(now, task.CompletedAt);
        Assert.Equal("hr_invalid_amount", Assert.Throws<HrTasksException>(() => Task().AddAmount(now, 5)).Code); // not numeric
    }

    [Fact]
    public void Muddat_otgani_ochiq_qoladi_close_faqat_eski_qatorlar()
    {
        var after = new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);
        var keep = Task();
        Assert.False(keep.MarkMissed(after));
        Assert.True(keep.IsOverdue(after));
        var legacy = Task();
        legacy.OverduePolicy = HrOverduePolicies.Close;
        Assert.False(legacy.MarkMissed(legacy.DueAt)); // not yet past
        Assert.True(legacy.MarkMissed(after));
        Assert.Equal(HrTaskStatuses.Missed, legacy.Status);
    }

    [Fact]
    public void Tasdiq_faqat_qolda_berilgan_ochiq_topshiriqda_kutiladi()
    {
        Assert.True(Task().NeedsAcceptance);
        Assert.False(Task(source: HrTaskSources.Template).NeedsAcceptance);
        Assert.False(Task(source: HrTaskSources.Self).NeedsAcceptance);
        var t = Task();
        var first = new DateTimeOffset(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);
        Assert.True(t.Accept(first));
        Assert.False(t.Accept(first.AddHours(1)));
        Assert.Equal(first, t.AcceptedAt);
    }

    [Fact]
    public void KPI_turi_va_miqdor_mosligi()
    {
        Assert.Throws<HrTasksException>(() => HrValues.KindAmount(HrTaskKinds.Numeric, null, null));
        Assert.Throws<HrTasksException>(() => HrValues.KindAmount(HrTaskKinds.Numeric, 0, null));
        Assert.Throws<HrTasksException>(() => HrValues.KindAmount(HrTaskKinds.Check, 5, null));
        Assert.Throws<HrTasksException>(() => HrValues.KindAmount(HrTaskKinds.Check, null, "dona"));
        Assert.Equal((12.35m, "lead"), HrValues.KindAmount(HrTaskKinds.Numeric, 12.345m, " lead "));
    }

    [Fact]
    public void Telefon_normal_shaklga_keltiriladi()
    {
        Assert.Equal("+998901234567", HrValues.NormalizePhone("+998 90 123-45-67"));
        Assert.Equal("+998901234567", HrValues.NormalizePhone("901234567"));
        Assert.Null(HrValues.NormalizePhone("12345"));
        Assert.Equal("+998 90 123 45 67", HrValues.DisplayPhone("+998901234567"));
    }

    // ---------------------------------------------------------------- tree / scope

    [Fact]
    public void Shox_rekursiv_va_halqaga_chidamli()
    {
        (string, string)[] edges = [("b", "a"), ("c", "b"), ("d", "c"), ("a", "d"), ("x", "y")]; // a→b→c→d→a cycle
        Assert.Equal(["a", "b", "c", "d"], HrTasksService.Subtree(edges, "a").OrderBy(x => x));
        Assert.Equal(["x", "y"], HrTasksService.Subtree(edges, "y").OrderBy(x => x));
        Assert.Equal(["x"], HrTasksService.Subtree(edges, "x"));
    }

    [Fact]
    public void Daraxt_ildiz_biriktirilmagan_va_kop_rahbarlik()
    {
        HrEmployeeDto E(string id) => new() { Id = id, FullName = id };
        var tree = HrTasksService.BuildTree([E("boss"), E("m1"), E("m2"), E("w"), E("lone")],
            [("m1", "boss"), ("m2", "boss"), ("w", "m1"), ("w", "m2")]);
        var root = Assert.Single(tree.Roots);
        Assert.Equal("boss", root.Id);
        Assert.Equal(["m1", "m2"], root.Children.Select(c => c.Id));
        Assert.All(root.Children, m => Assert.Equal("w", Assert.Single(m.Children).Id)); // w under both
        Assert.Equal("lone", Assert.Single(tree.Unassigned).Id);
    }

    // ---------------------------------------------------------------- texts

    private static HrDigestTask T(string title, string status, DateTimeOffset due, string? comment = null, int proofs = 0,
        bool late = false, string kind = HrTaskKinds.Check, decimal? target = null, decimal current = 0, string? unit = null) =>
        new(Guid.NewGuid().ToString(), title, status, kind, late, due, comment, proofs, target, current, unit);

    [Fact]
    public void Kunlik_yakun_matni_eski_shaklda()
    {
        var day = new DateOnly(2026, 9, 1);
        var due = HrTasksKit.At(2026, 9, 1, 18);
        var digest = new HrDigest(day,
        [
            new HrDigestEmployee("1", "Aliyev Aziz", "Marketolog", "u1",
                [T("Instagram post", HrTaskStatuses.Done, due, "3 ta post", 2),
                 T("Leadlar", HrTaskStatuses.Done, due, kind: HrTaskKinds.Numeric, target: 200, current: 147, unit: "lead")],
                [T("Reklama hisoboti", HrTaskStatuses.Missed, due)], []),
            new HrDigestEmployee("2", "Karimova <Dilnoza>", "Buxgalter", "u2", [],
                [T("Soliq & hisobot", HrTaskStatuses.Open, due)],
                [T("Eski ish", HrTaskStatuses.Returned, HrTasksKit.At(2026, 8, 30, 18))]),
            new HrDigestEmployee("3", "Bo'sh", null, "u3", [], [], []),
        ], SelfCreatedCount: 2, UnacceptedCount: 1);

        var text = Assert.Single(HrTasksTexts.Digest(digest, HrTasksKit.Tz));
        Assert.StartsWith("📊 <b>01.09.2026 — kunlik yakun</b>\n\n✅ Bajarilgan: 2 / 4 (50%)\n\n👤 <b>Aliyev Aziz</b> — Marketolog\n", text);
        Assert.Contains("   ✅ Instagram post (izoh: 3 ta post, 📎 2 ta isbot)", text);
        Assert.Contains("   ✅ Leadlar — 147/200 lead (73%)", text);
        Assert.Contains("   ❌ Reklama hisoboti — bajarilmadi", text);
        Assert.Contains("👤 <b>Karimova &lt;Dilnoza&gt;</b> — Buxgalter", text); // user text escaped
        Assert.Contains("   ⏳ Soliq &amp; hisobot — muddat 01.09 18:00, ochiq", text);
        Assert.Contains("   ⚠️ Eski ish — muddat 30.08 18:00 o'tdi, qaytarilgan", text);
        Assert.DoesNotContain("Bo'sh", text); // an employee without lines is not listed
        Assert.EndsWith("\n\n➕ O'zi qo'shgan topshiriqlar: 2 ta\n⚠️ Muddati o'tganlar: 1 ta\n👀 Tasdiqlanmagan: 1 ta", text);
    }

    [Fact]
    public void Bosh_yakun_yuborilmaydi()
    {
        var empty = new HrDigest(new DateOnly(2026, 9, 1), [new HrDigestEmployee("1", "A", null, "u", [], [], [])], 5);
        Assert.True(empty.IsEmpty);
        Assert.Empty(HrTasksTexts.Digest(empty, HrTasksKit.Tz));
    }

    [Fact]
    public void Uzun_yakun_4096_da_xodim_chegarasida_bolinadi()
    {
        var due = HrTasksKit.At(2026, 9, 1, 18);
        var employees = Enumerable.Range(1, 60).Select(i => new HrDigestEmployee(
            i.ToString(), $"Xodim {i:D2}", "Lavozim", "u",
            [.. Enumerable.Range(1, 4).Select(j => T($"Topshiriq {i}-{j} " + new string('x', 30), HrTaskStatuses.Done, due))], [], [])).ToList();
        var chunks = HrTasksTexts.Digest(new HrDigest(new DateOnly(2026, 9, 1), employees, 0), HrTasksKit.Tz);
        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Length <= HrTasksTexts.TelegramMaxLen));
        Assert.All(chunks.Skip(1), c => Assert.StartsWith("👤 <b>Xodim", c)); // a new chunk starts with an employee
        foreach (var i in Enumerable.Range(1, 60))
            Assert.Single(chunks, c => c.Contains($"<b>Xodim {i:D2}</b>")); // one employee never spans two messages
    }

    [Fact]
    public void Ertalabki_royxat_muddati_otganlar_qizil_bolimda()
    {
        var now = HrTasksKit.At(2026, 9, 8, 7, 30);
        var text = HrTasksTexts.Morning(
        [
            T("Oylik hisobot", HrTaskStatuses.Open, HrTasksKit.At(2026, 8, 31, 18)),
            T("Instagram post", HrTaskStatuses.Open, HrTasksKit.At(2026, 9, 8, 18)),
        ], HrTasksKit.Tz, now);
        Assert.Equal(
            "🌅 <b>Bugungi topshiriqlar</b>\n\n🔴 <b>MUDDATI O‘TGAN — 1 ta</b>\n"
            + "🔴 1. Oylik hisobot — muddat 31.08 18:00, 7 kun o‘tdi\n\n⏳ <b>Bugungi ishlar</b>\n"
            + "2. Instagram post — 08.09 18:00", text);
        Assert.Equal("", HrTasksTexts.Morning([], HrTasksKit.Tz, now));
    }

    [Fact]
    public void Bildirishnoma_matnlari_eski_shaklda()
    {
        Assert.Equal(
            "🆕 <b>Sizga yangi topshiriq</b>\n\nHisobot &lt;b&gt;\n⏰ Muddat: 14.09.2026 18:00\n\n"
            + "Bajargach «Topshiriqlarim» bo'limidan belgilang.",
            HrTasksTexts.Assigned("Hisobot <b>", HrTasksKit.At(2026, 9, 14, 18), HrTasksKit.Tz));
        Assert.Equal(
            "🆕 <b>Sizga 7 ta yangi topshiriq berildi</b>\n\n1. a\n2. b\n3. c\n4. d\n5. e\n…va yana 2 ta\n\n"
            + "Batafsil — «Topshiriqlarim» bo'limida.",
            HrTasksTexts.AssignedMany(7, ["a", "b", "c", "d", "e", "f", "g"]));
        Assert.StartsWith("🆕 <b>Sizga yangi topshiriq berildi</b>", HrTasksTexts.AssignedMany(1, ["a"]));
        Assert.Equal("↩️ <b>Topshiriq qaytarildi</b>\n\nHisobot\n\n💬 Rasm yo'q\n\nIltimos, qaytadan bajaring.",
            HrTasksTexts.Returned("Hisobot", "Rasm yo'q"));
        Assert.Equal("⏰ 30 daqiqadan keyin muddati tugaydi:\n• A\n• B",
            HrTasksTexts.Reminder([("A", 50), ("B", 30)]));
    }

    [Fact]
    public void Mijoz_vaqti_offsetsiz_bolsa_Toshkent_deb_oqiladi()
    {
        Assert.Equal(new DateTimeOffset(2026, 9, 2, 18, 59, 0, TimeSpan.Zero),
            HrTasksService.ParseInstant("2026-09-02T23:59:00", "dueAt", HrTasksKit.Tz));
        Assert.Equal(new DateTimeOffset(2026, 9, 2, 18, 59, 0, TimeSpan.Zero),
            HrTasksService.ParseInstant("2026-09-02T23:59:00+05:00", "dueAt", HrTasksKit.Tz));
        Assert.Equal(new DateTimeOffset(2026, 9, 2, 13, 0, 0, TimeSpan.Zero),
            HrTasksService.ParseInstant("2026-09-02T13:00:00Z", "dueAt", HrTasksKit.Tz));
        Assert.Throws<HrTasksException>(() => HrTasksService.ParseInstant("ertaga", "dueAt", HrTasksKit.Tz));
    }
}

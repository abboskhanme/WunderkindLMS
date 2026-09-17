using SchoolLms.Application.Services;

namespace SchoolLms.Tests;

// ===========================================================================
//  G-3 — CHORAK/HAFTA ARIFMETIKASI (docs/modules/students-parity.md §2.1.6)
// ===========================================================================
//
//  `ScheduleMath` — jadval, jurnal ustunlari va portal jadvalining YAGONA
//  kalendar manbai: chorakni haftalarga bo'ladi, haftadan dars sanasini
//  chiqaradi. Guruh darslariga o'tishda (§4.3, C1) shu fayl ham qayta
//  o'qiladi, shuning uchun BUGUNGI natijalari raqamma-raqam qadab qo'yiladi.
//
//  Bazasiz, sof funksiyalar — shuning uchun kolleksiyaga ham qo'shilmaydi.
//
//  KALENDAR TAYANCHI (2026): 1-yanvar — payshanba; 1-sentyabr — seshanba,
//  5-sentyabr — shanba, 6-sentyabr — yakshanba, 31-avgust — dushanba.
// ===========================================================================

/// <summary>
/// <see cref="ScheduleMath"/> ning bugungi xatti-harakati: hafta boshini topish,
/// chorakni haftalarga bo'lish, chorak chegarasiga qisish va yakshanba chekkasi.
/// </summary>
public class ScheduleMathTests
{
    // =====================================================================
    //  1. Hafta boshi va sana qo'shish
    // =====================================================================

    /// <summary>Hafta DUSHANBAdan boshlanadi: payshanba ham, dushanbaning o'zi ham bir xil javob beradi.</summary>
    [Theory]
    [InlineData("2026-09-14", "2026-09-14")] // dushanba — o'zi
    [InlineData("2026-09-17", "2026-09-14")] // payshanba
    [InlineData("2026-09-19", "2026-09-14")] // shanba
    public void Hafta_boshi_dushanba(string date, string expectedMonday) =>
        Assert.Equal(expectedMonday, ScheduleMath.MondayOfISO(date));

    /// <summary>
    /// YAKSHANBA kalendar haftaning OXIRI: uning dushanbasi — 6 kun ORQADA, ya'ni
    /// keyingi hafta emas. Dars kunlari sanog'i (0=Du … 5=Sha, 6=Yak) shunga tayanadi.
    /// </summary>
    [Fact]
    public void Yakshanbaning_dushanbasi_olti_kun_orqada()
    {
        Assert.Equal("2026-09-14", ScheduleMath.MondayOfISO("2026-09-20"));
    }

    /// <summary>Dars sanasi = haftaning dushanbasi + <c>ScheduleLesson.Day</c> (0=Du … 5=Sha).</summary>
    [Theory]
    [InlineData(0, "2026-09-14")]
    [InlineData(2, "2026-09-16")]
    [InlineData(5, "2026-09-19")]
    public void Dars_sanasi_dushanba_plyus_kun(int day, string expected) =>
        Assert.Equal(expected, ScheduleMath.AddDaysISO("2026-09-14", day));

    /// <summary>Oy va yil chegarasidan o'tadi.</summary>
    [Fact]
    public void Sana_qoshish_oy_va_yil_chegarasidan_otadi()
    {
        Assert.Equal("2026-10-01", ScheduleMath.AddDaysISO("2026-09-28", 3));
        Assert.Equal("2027-01-02", ScheduleMath.AddDaysISO("2026-12-31", 2));
    }

    // =====================================================================
    //  2. Chorakni haftalarga bo'lish
    // =====================================================================

    /// <summary>
    /// Chorak 2026-09-01 (seshanba) — 2026-10-31 (shanba): 9 hafta. Birinchi hafta
    /// chorak boshidan (dushanbadan EMAS), oxirgisi chorak oxirigacha.
    /// </summary>
    [Fact]
    public void Chorak_haftalarga_bolinadi_toqqiz_hafta()
    {
        var weeks = ScheduleMath.GetQuarterWeeks("2026-09-01", "2026-10-31");

        Assert.Equal(9, weeks.Count);
        Assert.Equal(Enumerable.Range(1, 9), weeks.Select(w => w.Week));

        // 1-hafta: dushanbasi (31-avgust) chorakdan tashqarida — boshi QISILGAN.
        Assert.Equal("2026-09-01", weeks[0].StartISO);
        Assert.Equal("2026-09-05", weeks[0].EndISO);

        // 2-hafta: to'liq dushanba–shanba.
        Assert.Equal("2026-09-07", weeks[1].StartISO);
        Assert.Equal("2026-09-12", weeks[1].EndISO);

        // Oxirgi hafta chorak oxiri bilan tugaydi.
        Assert.Equal("2026-10-26", weeks[8].StartISO);
        Assert.Equal("2026-10-31", weeks[8].EndISO);
    }

    /// <summary>
    /// QISILGAN hafta boshi endi har doim ham dushanba EMAS. Dars sanasini
    /// hisoblaganda <c>MondayOfISO(StartISO)</c> dan foydalanish SHART — aks holda
    /// 1-haftadagi dushanba darsi seshanbaga siljib ketardi.
    /// </summary>
    [Fact]
    public void Qisilgan_haftaning_dushanbasi_MondayOfISO_bilan_topiladi()
    {
        var first = ScheduleMath.GetQuarterWeeks("2026-09-01", "2026-10-31")[0];

        Assert.Equal("2026-09-01", first.StartISO);                        // seshanba
        Assert.Equal("2026-08-31", ScheduleMath.MondayOfISO(first.StartISO)); // dushanbasi — chorakdan tashqarida
    }

    /// <summary>Chorak hafta o'rtasida tugasa — oxirgi hafta o'sha kunda qisiladi.</summary>
    [Fact]
    public void Chorak_hafta_ortasida_tugasa_oxirgi_hafta_qisiladi()
    {
        var weeks = ScheduleMath.GetQuarterWeeks("2026-09-01", "2026-09-09"); // chorshanba

        Assert.Equal(2, weeks.Count);
        Assert.Equal(("2026-09-01", "2026-09-05"), (weeks[0].StartISO, weeks[0].EndISO));
        Assert.Equal(("2026-09-07", "2026-09-09"), (weeks[1].StartISO, weeks[1].EndISO));
    }

    /// <summary>
    /// Chorak YAKSHANBAda boshlansa — 1-hafta KEYINGI dushanbadan boshlanadi.
    /// Aks holda haftaning dushanbasi 6 kun orqada, butunlay chorakdan tashqarida
    /// qolardi va birinchi o'quv haftasi yo'qolardi.
    /// </summary>
    [Fact]
    public void Chorak_yakshanbada_boshlansa_birinchi_hafta_keyingi_dushanbadan()
    {
        var weeks = ScheduleMath.GetQuarterWeeks("2026-09-06", "2026-09-19"); // 6-sentyabr — yakshanba

        Assert.Equal(2, weeks.Count);
        Assert.Equal(("2026-09-07", "2026-09-12"), (weeks[0].StartISO, weeks[0].EndISO));
        Assert.Equal(("2026-09-14", "2026-09-19"), (weeks[1].StartISO, weeks[1].EndISO));
    }

    /// <summary>
    /// BUGUNGI XATTI-HARAKAT (g'alati, lekin qadab qo'yiladi): chorak SHANBAda
    /// boshlansa — 1-hafta BIR KUNLIK bo'ladi (faqat o'sha shanba). Yakshanba uchun
    /// qilingan maxsus tuzatish shanbaga qilinmagan.
    /// </summary>
    [Fact]
    public void Chorak_shanbada_boshlansa_birinchi_hafta_bir_kunlik_bugungi_xatti_harakat()
    {
        var weeks = ScheduleMath.GetQuarterWeeks("2026-09-05", "2026-09-19"); // 5-sentyabr — shanba

        Assert.Equal("2026-09-05", weeks[0].StartISO);
        Assert.Equal("2026-09-05", weeks[0].EndISO);
        Assert.Equal(1, weeks[0].Week);
    }

    /// <summary>Bitta kunlik chorak — bitta hafta.</summary>
    [Fact]
    public void Bir_kunlik_chorak_bitta_hafta_beradi()
    {
        var weeks = ScheduleMath.GetQuarterWeeks("2026-09-16", "2026-09-16");

        Assert.Single(weeks);
        Assert.Equal(("2026-09-16", "2026-09-16"), (weeks[0].StartISO, weeks[0].EndISO));
    }

    /// <summary>Noto'g'ri yoki teskari oraliq — bo'sh ro'yxat (istisno EMAS).</summary>
    [Theory]
    [InlineData("2026-10-31", "2026-09-01")] // boshi oxiridan keyin
    [InlineData("", "2026-09-01")]
    [InlineData("2026-09-01", "kechagi")]
    [InlineData("01.09.2026", "31.10.2026")] // ISO emas
    public void Notogri_oraliq_bosh_royxat(string start, string end) =>
        Assert.Empty(ScheduleMath.GetQuarterWeeks(start, end));
}

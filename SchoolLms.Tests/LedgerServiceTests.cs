using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Billing;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// <c>LedgerService</c> — pulni jurnalga yozadigan YAGONA kod (P1-07, SPEC §2.2).
///
/// <para>
/// Bu testlar ARIFMETIKANI emas, QOIDANI tekshiradi: balanslashmagan yozuv
/// bazaga tushmasin, noma'lum hisob kodi qabul qilinmasin, storno originalni
/// tegmasin va bir odam o'z yozuvini o'zi teskari qila olmasin.
/// </para>
/// <para>
/// Baza OWNER ulanishi bilan ochiladi: test ma'lumotini tayyorlash uchun
/// <c>users</c> ga yozish kerak, `app_rw` esa ba'zi jadvallarda cheklangan.
/// `app_rw` ning ledger'ga UPDATE/DELETE qila olmasligi BOSHQA joyda —
/// <c>tools/verify-billing-guards.sh</c> da va P1-22 da tekshiriladi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class LedgerServiceTests(ApiFixture fixture)
{
    private AppDbContext NewDb() => PostgresFixture.NewContext(fixture.Database.OwnerConnectionString);

    /// <summary>Har test o'z foydalanuvchisini yaratadi — testlar bir bazani bo'lishadi.</summary>
    private async Task<string> NewUserAsync(string role = Roles.Admin)
    {
        var (user, _) = await fixture.Api.SeedUserAsync(role);
        return user.Id;
    }

    // -----------------------------------------------------------------
    //  P1-07 qabul mezoni: 500 000 lik to'lov AYNAN ikki qator beradi.
    // -----------------------------------------------------------------
    [Fact]
    public async Task Tolov_qoyilganda_aynan_ikki_qator_debit_cash_va_credit_receivable_boladi()
    {
        var actorId = await NewUserAsync();
        var paymentId = Guid.NewGuid();

        await using var db = NewDb();
        var service = new LedgerService(db);

        var posted = await service.PostAsync(
        [
            new LedgerPosting(Accounts.Cash, LedgerDirection.Debit, 500_000m, LedgerRefType.Payment, paymentId),
            new LedgerPosting(Accounts.Receivable, LedgerDirection.Credit, 500_000m, LedgerRefType.Payment, paymentId),
        ], actorId);

        Assert.Equal(2, posted.Count);

        // Bazadan QAYTA o'qiymiz — xotiradagi obyekt emas, haqiqatan yozilgani muhim.
        await using var check = NewDb();
        var rows = await check.LedgerEntries.AsNoTracking()
            .Where(e => e.RefId == paymentId)
            .OrderBy(e => e.Id)
            .ToListAsync();

        Assert.Equal(2, rows.Count);

        var debit = Assert.Single(rows, r => r.Direction == LedgerDirection.Debit);
        Assert.Equal(Accounts.Cash, debit.Account);
        Assert.Equal(500_000m, debit.Amount);

        var credit = Assert.Single(rows, r => r.Direction == LedgerDirection.Credit);
        Assert.Equal(Accounts.Receivable, credit.Account);
        Assert.Equal(500_000m, credit.Amount);

        // Shaxs: `created_by` xizmatga berilgan actorId bo'lishi shart
        // (SPEC §4.4 — so'rov tanasidan EMAS).
        Assert.All(rows, r => Assert.Equal(actorId, r.CreatedBy));

        // Vaqt: `timestamptz` LAHZA saqlaydi, ofsetni emas — Postgres uni UTC
        // sifatida qaytaradi. Muhimi qiymat haqiqiy lahza bo'lishi.
        Assert.All(rows, r => Assert.Equal(TimeSpan.Zero, r.CreatedAt.Offset));
        Assert.All(rows, r => Assert.True(
            (DateTimeOffset.UtcNow - r.CreatedAt).Duration() < TimeSpan.FromMinutes(5),
            $"created_at hozirgi vaqtdan uzoq: {r.CreatedAt:O}"));
        Assert.All(rows, r => Assert.Equal(LedgerRefType.Payment, r.RefType));
        Assert.All(rows, r => Assert.Null(r.ReversalOf));
    }

    // -----------------------------------------------------------------
    //  Balans
    // -----------------------------------------------------------------
    [Fact]
    public async Task Balanslashmagan_yozuv_SaveChanges_gacha_yiqiladi_va_hech_narsa_saqlanmaydi()
    {
        var actorId = await NewUserAsync();
        var refId = Guid.NewGuid();

        await using var db = NewDb();
        var service = new LedgerService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PostAsync(
        [
            new LedgerPosting(Accounts.Cash, LedgerDirection.Debit, 500_000m, LedgerRefType.Payment, refId),
            new LedgerPosting(Accounts.Receivable, LedgerDirection.Credit, 400_000m, LedgerRefType.Payment, refId),
        ], actorId));

        Assert.Contains("balanslashmagan", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Eng muhimi: bazaga YARIM yozuv ham tushmagan bo'lishi kerak.
        await using var check = NewDb();
        Assert.False(await check.LedgerEntries.AnyAsync(e => e.RefId == refId));
    }

    [Fact]
    public async Task Bitta_satrli_yozuv_qabul_qilinmaydi()
    {
        var actorId = await NewUserAsync();
        await using var db = NewDb();
        var service = new LedgerService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PostAsync(
            [new LedgerPosting(Accounts.Cash, LedgerDirection.Debit, 100m, LedgerRefType.Payment, Guid.NewGuid())],
            actorId));
    }

    [Fact]
    public async Task Uch_satrli_balanslashgan_yozuv_qabul_qilinadi()
    {
        // Haqiqiy holat: bitta to'lov ikkita toifaga taqsimlangan.
        var actorId = await NewUserAsync();
        var refId = Guid.NewGuid();

        await using var db = NewDb();
        var posted = await new LedgerService(db).PostAsync(
        [
            new LedgerPosting(Accounts.Cash, LedgerDirection.Debit, 300_000m, LedgerRefType.Payment, refId),
            new LedgerPosting(Accounts.Receivable, LedgerDirection.Credit, 200_000m, LedgerRefType.Payment, refId),
            new LedgerPosting(Accounts.Receivable, LedgerDirection.Credit, 100_000m, LedgerRefType.Payment, refId),
        ], actorId);

        Assert.Equal(3, posted.Count);
    }

    // -----------------------------------------------------------------
    //  Yopiq hisoblar ro'yxati
    // -----------------------------------------------------------------
    [Fact]
    public async Task Nomalum_hisob_kodi_rad_etiladi()
    {
        var actorId = await NewUserAsync();
        await using var db = NewDb();
        var service = new LedgerService(db);

        // Haqiqiy xato: "tuition" da harf tushib qolgan.
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.PostAsync(
        [
            new LedgerPosting(Accounts.Cash, LedgerDirection.Debit, 100m, LedgerRefType.Payment, Guid.NewGuid()),
            new LedgerPosting("revenue:tution", LedgerDirection.Credit, 100m, LedgerRefType.Payment, Guid.NewGuid()),
        ], actorId));
    }

    [Fact]
    public void Hisoblar_royxati_SPEC_dagi_ontasi_bilan_bir_xil()
    {
        Assert.Equal(
            [
                "cash", "bank", "receivable",
                "revenue:tuition", "revenue:bus", "revenue:dormitory", "revenue:meals", "revenue:other",
                "expense:salary", "expense:other",
            ],
            Accounts.All);

        Assert.False(Accounts.IsKnown("revenue:tution"));
        Assert.False(Accounts.IsKnown(null));

        // Toifa kodi -> daromad hisobi; nostandart toifa "boshqa" ga tushadi.
        Assert.Equal(Accounts.RevenueBus, Accounts.RevenueFor("bus"));
        Assert.Equal(Accounts.RevenueOther, Accounts.RevenueFor("klub"));

        // To'lov usuli -> pul qayerga tushadi (mijoz javobi, SPEC §8.1 Q13).
        Assert.Equal(Accounts.Cash, Accounts.SettlementFor(PaymentMethod.Cash));
        Assert.Equal(Accounts.Bank, Accounts.SettlementFor(PaymentMethod.Card));
        Assert.Equal(Accounts.Bank, Accounts.SettlementFor(PaymentMethod.Transfer));
        Assert.Equal(Accounts.Bank, Accounts.SettlementFor(PaymentMethod.Online));
    }

    [Fact]
    public async Task Manfiy_yoki_nol_summa_rad_etiladi()
    {
        var actorId = await NewUserAsync();
        await using var db = NewDb();
        var service = new LedgerService(db);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.PostAsync(
        [
            new LedgerPosting(Accounts.Cash, LedgerDirection.Debit, 0m, LedgerRefType.Payment, Guid.NewGuid()),
            new LedgerPosting(Accounts.Receivable, LedgerDirection.Credit, 0m, LedgerRefType.Payment, Guid.NewGuid()),
        ], actorId));
    }

    [Fact]
    public async Task Aralash_RefId_li_partiya_rad_etiladi()
    {
        // Partiya = bitta biznes hodisasi. Aralashib ketsa, ReverseAsync
        // "qaysi satrlar birga" degan savolga javob topa olmaydi.
        var actorId = await NewUserAsync();
        await using var db = NewDb();

        await Assert.ThrowsAsync<InvalidOperationException>(() => new LedgerService(db).PostAsync(
        [
            new LedgerPosting(Accounts.Cash, LedgerDirection.Debit, 100m, LedgerRefType.Payment, Guid.NewGuid()),
            new LedgerPosting(Accounts.Receivable, LedgerDirection.Credit, 100m, LedgerRefType.Payment, Guid.NewGuid()),
        ], actorId));
    }

    [Fact]
    public async Task RefId_berilmasa_partiyaga_suniy_id_beriladi()
    {
        // Qo'lda tuzatishda manba yozuv yo'q, lekin partiya baribir
        // aniqlanadigan bo'lishi kerak — aks holda storno ishlamaydi.
        var actorId = await NewUserAsync();
        await using var db = NewDb();

        var posted = await new LedgerService(db).PostAsync(
        [
            new LedgerPosting(Accounts.Cash, LedgerDirection.Debit, 1_000m, LedgerRefType.Expense, Memo: "qo'lda"),
            new LedgerPosting(Accounts.ExpenseOther, LedgerDirection.Credit, 1_000m, LedgerRefType.Expense, Memo: "qo'lda"),
        ], actorId);

        Assert.All(posted, e => Assert.NotNull(e.RefId));
        Assert.Single(posted.Select(e => e.RefId).Distinct());
    }

    // -----------------------------------------------------------------
    //  Storno
    // -----------------------------------------------------------------
    [Fact]
    public async Task Storno_kozgu_qatorlar_qoshadi_va_originalni_TEGMAYDI()
    {
        var actorId = await NewUserAsync();
        var approverId = await NewUserAsync(Roles.SuperAdmin);
        var refId = Guid.NewGuid();

        await using var db = NewDb();
        var service = new LedgerService(db);

        var original = await service.PostAsync(
        [
            new LedgerPosting(Accounts.Cash, LedgerDirection.Debit, 250_000m, LedgerRefType.Payment, refId),
            new LedgerPosting(Accounts.Receivable, LedgerDirection.Credit, 250_000m, LedgerRefType.Payment, refId),
        ], actorId);

        var originalIds = original.Select(e => e.Id).ToList();
        var mirrors = await service.ReverseAsync(originalIds[0], "Kassir xato summa kiritdi", approverId);

        Assert.Equal(2, mirrors.Count);

        await using var check = NewDb();
        var all = await check.LedgerEntries.AsNoTracking()
            .Where(e => e.RefId == refId).OrderBy(e => e.Id).ToListAsync();
        Assert.Equal(4, all.Count);

        // Original TEGILMAGAN: summa, yo'nalish, sana, muallif — hammasi o'sha.
        foreach (var before in original)
        {
            var after = all.Single(e => e.Id == before.Id);
            Assert.Equal(before.Amount, after.Amount);
            Assert.Equal(before.Direction, after.Direction);
            Assert.Equal(before.EntryDate, after.EntryDate);
            Assert.Equal(before.CreatedBy, after.CreatedBy);
            Assert.Null(after.ReversalOf);
        }

        // Ko'zgu: teskari yo'nalish, o'sha summa, `reversal_of` original'ga ishora.
        foreach (var mirror in all.Where(e => e.ReversalOf is not null))
        {
            var source = all.Single(e => e.Id == mirror.ReversalOf!.Value);
            Assert.Contains(source.Id, originalIds);
            Assert.Equal(source.Amount, mirror.Amount);
            Assert.NotEqual(source.Direction, mirror.Direction);
            Assert.Equal(Accounts.Require(source.Account), mirror.Account);
            Assert.Equal(LedgerRefType.Reversal, mirror.RefType);
            Assert.Equal("Kassir xato summa kiritdi", mirror.Memo);
            Assert.Equal(approverId, mirror.CreatedBy);
        }

        // Natijada hisob qoldig'i nolga qaytadi — storno'ning butun ma'nosi shu.
        var cash = all.Where(e => e.Account == Accounts.Cash)
            .Sum(e => e.Direction == LedgerDirection.Debit ? e.Amount : -e.Amount);
        Assert.Equal(0m, cash);
    }

    [Fact]
    public async Task Ozi_yozgan_yozuvni_ozi_storno_qila_olmaydi()
    {
        // SPEC §4.5 — ikki qavatli nazorat. "Yozdim, keyin o'chirdim" bitta
        // odamning qo'lida qolsa, butun himoya ma'nosiz.
        var actorId = await NewUserAsync();
        var refId = Guid.NewGuid();

        await using var db = NewDb();
        var service = new LedgerService(db);

        var posted = await service.PostAsync(
        [
            new LedgerPosting(Accounts.Cash, LedgerDirection.Debit, 10_000m, LedgerRefType.Payment, refId),
            new LedgerPosting(Accounts.Receivable, LedgerDirection.Credit, 10_000m, LedgerRefType.Payment, refId),
        ], actorId);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ReverseAsync(posted[0].Id, "sabab", actorId));
        Assert.Contains("ikkinchi shaxs", ex.Message, StringComparison.OrdinalIgnoreCase);

        await using var check = NewDb();
        Assert.Equal(2, await check.LedgerEntries.CountAsync(e => e.RefId == refId));
    }

    [Fact]
    public async Task Ikki_marta_storno_qilib_bolmaydi()
    {
        var actorId = await NewUserAsync();
        var approverId = await NewUserAsync(Roles.SuperAdmin);
        var refId = Guid.NewGuid();

        await using var db = NewDb();
        var service = new LedgerService(db);

        var posted = await service.PostAsync(
        [
            new LedgerPosting(Accounts.Cash, LedgerDirection.Debit, 70_000m, LedgerRefType.Payment, refId),
            new LedgerPosting(Accounts.Receivable, LedgerDirection.Credit, 70_000m, LedgerRefType.Payment, refId),
        ], actorId);

        await service.ReverseAsync(posted[0].Id, "birinchi storno", approverId);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ReverseAsync(posted[1].Id, "ikkinchi storno", approverId));
        Assert.Contains("allaqachon storno", ex.Message, StringComparison.OrdinalIgnoreCase);

        await using var check = NewDb();
        Assert.Equal(4, await check.LedgerEntries.CountAsync(e => e.RefId == refId));
    }

    [Fact]
    public async Task Storno_sababi_majburiy()
    {
        var actorId = await NewUserAsync();
        var approverId = await NewUserAsync(Roles.SuperAdmin);
        var refId = Guid.NewGuid();

        await using var db = NewDb();
        var service = new LedgerService(db);

        var posted = await service.PostAsync(
        [
            new LedgerPosting(Accounts.Cash, LedgerDirection.Debit, 5_000m, LedgerRefType.Payment, refId),
            new LedgerPosting(Accounts.Receivable, LedgerDirection.Credit, 5_000m, LedgerRefType.Payment, refId),
        ], actorId);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.ReverseAsync(posted[0].Id, "   ", approverId));
    }

    // -----------------------------------------------------------------
    //  Qoldiqlar
    // -----------------------------------------------------------------
    [Fact]
    public async Task Qoldiq_va_hisoblar_kesimi_hisoblanadi()
    {
        var actorId = await NewUserAsync();
        var day = new DateOnly(2031, 3, 15);   // boshqa testlar tegmaydigan sana
        var refId = Guid.NewGuid();

        await using var db = NewDb();
        var service = new LedgerService(db);

        await service.PostAsync(
        [
            new LedgerPosting(Accounts.Cash, LedgerDirection.Debit, 120_000m, LedgerRefType.Payment, refId, day),
            new LedgerPosting(Accounts.RevenueTuition, LedgerDirection.Credit, 120_000m, LedgerRefType.Payment, refId, day),
        ], actorId);

        Assert.Equal(120_000m, await service.BalanceAsync(Accounts.Cash, day, day));
        Assert.Equal(-120_000m, await service.BalanceAsync(Accounts.RevenueTuition, day, day));

        var trial = await service.TrialBalanceAsync(day, day);

        // Kesim HAMMA hisobni qaytaradi (nollari ham) — hisobot jadvalida
        // qator "yo'qolib qolmasin".
        Assert.Equal(Accounts.All.Count, trial.Count);

        // Va butun jurnal balanslashgan bo'lishi shart.
        Assert.Equal(trial.Sum(t => t.Debit), trial.Sum(t => t.Credit));
    }
}

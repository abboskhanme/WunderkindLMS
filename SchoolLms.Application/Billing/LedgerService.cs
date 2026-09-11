using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

/// <summary>
/// Ikki yoqlama jurnalga yozadigan YAGONA kod (SPEC §2.2). Vazifa: P1-07.
///
/// <para>
/// <b>Nega chokepoint kerak.</b> Agar moliyaviy yozuv umumiy repozitoriy
/// orqali o'tsa, "jurnalga nima tushdi" degan savolga javob butun kod
/// bo'ylab sochilib ketadi va balans buzilganda uni kim buzganini topib
/// bo'lmaydi. Bu yerda esa hamma narsa bitta darvozadan o'tadi va shu
/// darvoza balansni TEKSHIRADI.
/// </para>
///
/// <para>
/// <b>Bu klassda o'zgartirish va o'chirish metodlari YO'Q — va bo'lmaydi.</b>
/// Uch qavat himoya: (1) <see cref="ILedgerService"/> da bunday metod yo'q;
/// (2) bu implementatsiya <c>Update</c>/<c>Remove</c> chaqirmaydi; (3) baza
/// darajasida <c>app_rw</c> rolida <c>ledger_entries</c> ga UPDATE/DELETE
/// huquqi yo'q (42501). Uchinchisi hal qiluvchi: qolgan ikkitasini kod
/// yozib chetlab o'tish mumkin, uchinchisini yo'q.
/// </para>
///
/// <para>
/// <b>PARTIYA (batch) tushunchasi.</b> Bitta <see cref="PostAsync"/> chaqiruvi
/// = bitta biznes hodisasi = bitta balanslashgan partiya. Partiyaning hamma
/// satri BIR XIL <c>RefType</c> va <c>RefId</c> ga ega bo'lishi shart. Shu
/// sabab <see cref="ReverseAsync"/> bitta satr id'sidan butun partiyani topa
/// oladi va uni BUTUNLIGICHA teskari qiladi — aks holda faqat bitta satrni
/// teskari qilish jurnalni nomutanosib qoldirardi.
/// </para>
/// </summary>
public sealed class LedgerService(IAppDbContext db) : ILedgerService
{
    /// <summary>Baza ustuni <c>numeric(14,2)</c> — hisob-kitob ham shu aniqlikda.</summary>
    private const int MoneyScale = 2;

    /// <inheritdoc />
    public async Task<IReadOnlyList<LedgerEntry>> PostAsync(
        IReadOnlyList<LedgerPosting> entries, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (string.IsNullOrWhiteSpace(actorId))
            throw new ArgumentException("Yozuvni kim qo'yayotgani noma'lum (actorId bo'sh).", nameof(actorId));
        if (entries.Count < 2)
            throw new InvalidOperationException(
                "Jurnal yozuvi kamida ikkita satrdan iborat bo'ladi (debet va kredit).");

        // ---- 1. Har satrni alohida tekshirish ----
        // Summani BAZA ANIQLIGIGA (2 kasr) shu yerda keltiramiz va balansni
        // AYNAN shu, saqlanadigan qiymatlarda tekshiramiz. Aks holda balans
        // xotirada to'g'ri, bazada esa yaxlitlash tufayli noto'g'ri bo'lib
        // qolishi mumkin edi — va buni hech kim sezmasdi.
        var rounded = new decimal[entries.Count];
        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            Accounts.Require(e.Account);

            if (e.Direction != LedgerDirection.Debit && e.Direction != LedgerDirection.Credit)
                throw new ArgumentOutOfRangeException(
                    nameof(entries), e.Direction,
                    $"Yo'nalish faqat '{LedgerDirection.Debit}' yoki '{LedgerDirection.Credit}' bo'ladi.");

            if (!LedgerRefType.All.Contains(e.RefType))
                throw new ArgumentOutOfRangeException(
                    nameof(entries), e.RefType,
                    $"Noma'lum manba turi. Ruxsat etilganlar: {string.Join(", ", LedgerRefType.All)}.");

            var amount = decimal.Round(e.Amount, MoneyScale);
            if (amount <= 0m)
                throw new ArgumentOutOfRangeException(
                    nameof(entries), e.Amount,
                    "Summa musbat bo'lishi shart; belgi yo'nalishdan (debit/credit) kelib chiqadi.");
            rounded[i] = amount;
        }

        // ---- 2. Partiya yaxlitligi ----
        var refType = entries[0].RefType;
        if (entries.Any(e => e.RefType != refType))
            throw new InvalidOperationException(
                "Bitta partiyadagi hamma satr bir xil RefType ga ega bo'lishi shart — "
                + "bitta chaqiruv = bitta biznes hodisasi.");

        var refIds = entries.Select(e => e.RefId).Distinct().ToList();
        if (refIds.Count > 1)
            throw new InvalidOperationException(
                "Bitta partiyadagi hamma satr bir xil RefId ga ega bo'lishi shart.");

        // Qo'lda tuzatishda manba yozuv yo'q. Shunda ham partiyaga SUN'IY
        // id beramiz: busiz ReverseAsync partiyani topa olmay qolardi va
        // jurnalni nomutanosib qilmasdan teskari qilish imkonsiz bo'lardi.
        var refId = refIds[0] ?? Guid.NewGuid();

        // ---- 3. Balans ----
        decimal debit = 0m, credit = 0m;
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].Direction == LedgerDirection.Debit) debit += rounded[i];
            else credit += rounded[i];
        }

        if (debit != credit)
            throw new InvalidOperationException(
                $"Jurnal yozuvi balanslashmagan: debet {debit}, kredit {credit} (farq {debit - credit}). "
                + "Hech narsa saqlanmadi.");
        if (debit == 0m)
            throw new InvalidOperationException("Jurnal yozuvining summasi nolga teng.");

        // ---- 4. Saqlash ----
        var now = AppClock.NowInstant;
        var today = AppClock.Today;
        var created = new List<LedgerEntry>(entries.Count);

        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            var entry = new LedgerEntry
            {
                EntryDate = e.EntryDate ?? today,
                Account = e.Account,
                Direction = e.Direction,
                Amount = rounded[i],
                RefType = e.RefType,
                RefId = refId,
                Memo = e.Memo,
                CreatedBy = actorId,
                CreatedAt = now,
            };
            db.LedgerEntries.Add(entry);
            created.Add(entry);
        }

        await db.SaveChangesAsync(ct);
        return created;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LedgerEntry>> ReverseAsync(
        long entryId, string reason, string approverId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Storno sababi majburiy (SPEC §4.3).", nameof(reason));
        if (string.IsNullOrWhiteSpace(approverId))
            throw new ArgumentException("Tasdiqlovchi noma'lum (approverId bo'sh).", nameof(approverId));

        var anchor = await db.LedgerEntries.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == entryId, ct)
            ?? throw new InvalidOperationException($"Jurnal yozuvi topilmadi: {entryId}.");

        if (anchor.ReversalOf is not null)
            throw new InvalidOperationException(
                $"{entryId} — bu yozuvning O'ZI storno. Storno'ni storno qilib bo'lmaydi; "
                + "kerak bo'lsa yangi to'g'ri yozuv qo'ying.");

        // Partiyaning hamma satri (§ klass izohiga qarang). Storno satrlari
        // RefType = 'reversal' bo'lgani uchun bu yerga tushmaydi.
        var batch = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.RefType == anchor.RefType && e.RefId == anchor.RefId && e.ReversalOf == null)
            .OrderBy(e => e.Id)
            .ToListAsync(ct);

        if (batch.Count == 0)
            throw new InvalidOperationException($"{entryId} uchun partiya topilmadi.");

        // Ikki marta storno qilish = pulni ikki marta "qaytarish".
        var batchIds = batch.Select(e => e.Id).ToList();
        var alreadyReversed = await db.LedgerEntries.AsNoTracking()
            .AnyAsync(e => e.ReversalOf != null && batchIds.Contains(e.ReversalOf.Value), ct);
        if (alreadyReversed)
            throw new InvalidOperationException(
                $"Bu partiya ({anchor.RefType}/{anchor.RefId}) allaqachon storno qilingan.");

        // SPEC §4.5 — ikki qavatli nazorat. Yozuvni qo'ygan odam uni o'zi
        // teskari qila olmaydi: aks holda "yozdim — o'chirdim" bitta odamning
        // qo'lida qolardi, ya'ni butun himoya ma'nosiz bo'lardi.
        if (batch.Any(e => string.Equals(e.CreatedBy, approverId, StringComparison.Ordinal)))
            throw new InvalidOperationException(
                "Yozuvni qo'ygan foydalanuvchi uni o'zi storno qila olmaydi (SPEC §4.5) — "
                + "ikkinchi shaxs tasdig'i kerak.");

        // Storno DOIM BUGUNGI sana bilan yoziladi, originalning sanasi bilan
        // emas: yopilgan davrni orqaga qarab o'zgartirish hisobotni qayta
        // yozib yuborardi.
        var now = AppClock.NowInstant;
        var today = AppClock.Today;
        var created = new List<LedgerEntry>(batch.Count);

        foreach (var original in batch)
        {
            var mirror = new LedgerEntry
            {
                EntryDate = today,
                Account = original.Account,
                Direction = original.Direction == LedgerDirection.Debit
                    ? LedgerDirection.Credit
                    : LedgerDirection.Debit,
                Amount = original.Amount,
                RefType = LedgerRefType.Reversal,
                RefId = original.RefId,
                Memo = reason,
                CreatedBy = approverId,
                CreatedAt = now,
                ReversalOf = original.Id,
            };
            db.LedgerEntries.Add(mirror);
            created.Add(mirror);
        }

        // DIQQAT: original yozuvlar `AsNoTracking` bilan o'qilgan va ularga
        // TEGILMAYDI. Bu yerda `Update` ham, `Remove` ham yo'q — ataylab.
        await db.SaveChangesAsync(ct);
        return created;
    }

    /// <inheritdoc />
    public async Task<decimal> BalanceAsync(
        string account, DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default)
    {
        Accounts.Require(account);

        var rows = await FilteredAsync(from, to)
            .Where(e => e.Account == account)
            .GroupBy(e => e.Direction)
            .Select(g => new { Direction = g.Key, Total = g.Sum(x => x.Amount) })
            .ToListAsync(ct);

        var debit = rows.FirstOrDefault(r => r.Direction == LedgerDirection.Debit)?.Total ?? 0m;
        var credit = rows.FirstOrDefault(r => r.Direction == LedgerDirection.Credit)?.Total ?? 0m;
        return debit - credit;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AccountBalanceDto>> TrialBalanceAsync(
        DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default)
    {
        // Bitta so'rov, keyin xotirada yig'ish — hisoblar soni 10 ta, ya'ni
        // N+1 ham, katta natija ham bo'lishi mumkin emas.
        var rows = await FilteredAsync(from, to)
            .GroupBy(e => new { e.Account, e.Direction })
            .Select(g => new { g.Key.Account, g.Key.Direction, Total = g.Sum(x => x.Amount) })
            .ToListAsync(ct);

        return [.. Accounts.All
            .Select(account =>
            {
                var debit = rows.FirstOrDefault(r => r.Account == account && r.Direction == LedgerDirection.Debit)?.Total ?? 0m;
                var credit = rows.FirstOrDefault(r => r.Account == account && r.Direction == LedgerDirection.Credit)?.Total ?? 0m;
                return new AccountBalanceDto(account, debit, credit, debit - credit);
            })];
    }

    private IQueryable<LedgerEntry> FilteredAsync(DateOnly? from, DateOnly? to)
    {
        var q = db.LedgerEntries.AsNoTracking();
        if (from is { } f) q = q.Where(e => e.EntryDate >= f);
        if (to is { } t) q = q.Where(e => e.EntryDate <= t);
        return q;
    }
}

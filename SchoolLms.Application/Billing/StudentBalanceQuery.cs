using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  O'quvchi qoldig'i — HISOBLANADI, saqlanmaydi. Vazifa: P1-21 (SPEC §3.7).
// ===========================================================================
//
//  NIMA O'RNIGA KELDI
//  ------------------
//  Eski `students.balance` ustuni o'zgaruvchan raqam edi va OLTI joyda qo'lda
//  yangilanardi (docs/TASKS.md §1.2). Bitta yangilanish o'tkazib yuborilsa,
//  qarz JIMGINA buzilardi: xato chiqmaydi, ekran "to'g'ri" ko'rinadi. Ustun
//  P1-21 migratsiyasida o'chdi; endi qoldiq HAR SAFAR manba jadvallardan
//  hisoblanadi.
//
//  QOIDA (SPEC §3.7 va `InvoiceService.ForStudentAsync` bilan AYNAN bir xil)
//  -----------------------------------------------------------------------
//      qarz   = Σ max(0, (amount − discount) − Σ taqsimotlar)   -- void KIRMAYDI
//      avans  = Σ (to'lov summasi − uning taqsimotlari)          -- storno KIRMAYDI
//      qoldiq = avans − qarz
//
//  Belgi eski ustundagi bilan bir xil saqlangan: MANFIY = qarzdor, MUSBAT =
//  avans. Shu sabab `{balans}` shabloni, o'quvchilar ro'yxatidagi ustun va
//  qarzdorlar filtri o'zgarishsiz ishlaydi — raqam o'sha, manbasi boshqa.
//
//  NEGA `max(0, ...)` HAR HISOB-FAKTURA UCHUN ALOHIDA
//  -------------------------------------------------
//  Ortiqcha to'langan bitta oy boshqa oyning qarzini "yopib" ko'rsatmasligi
//  kerak: ota-ona portali (`InvoiceService.ForStudentAsync`) qarzni AYNAN
//  shunday hisoblaydi, ikki ekran bir-biriga tiyinigacha mos tushsin. Ortiqcha
//  pul yo'qolmaydi — u taqsimlanmagan qoldiq bo'lib avansga tushadi.
//
//  UNUMDORLIK
//  ----------
//  Ikkita so'rov — o'quvchilar soniga BOG'LIQ EMAS, guruhlash BAZADA. Sikl
//  ichida `await` yo'q. Ro'yxat ekranlari <see cref="ForManyAsync"/> ni bir
//  marta chaqiradi va lug'atdan o'qiydi; <see cref="ForAsync"/> — bitta
//  o'quvchi uchun qulaylik ustqurmasi, siklda CHAQIRILMAYDI.
//
//  NEGA SERVICE EMAS, "QUERY"
//  --------------------------
//  Hech narsa yozmaydi (`Add` ham, `SaveChanges` ham yo'q, hamma so'rov
//  `AsNoTracking`), interfeysi ham yo'q — `FinanceReportQueries` (P1-13) bilan
//  bir xil uslub. Shu sabab controller uni `Program.cs` ga tegmasdan, so'rov
//  doirasidagi kontekst ustidan o'zi yaratadi.

/// <summary>
/// O'quvchining pul qoldig'i — <c>invoices</c> va <c>payment_allocations</c>
/// dan hisoblanadi. Batafsil: fayl boshidagi izoh.
///
/// <para>
/// <b>Belgi:</b> manfiy = qarzdor, 0 = qarzsiz, musbat = avans — eski
/// <c>students.balance</c> ustuni bilan bir xil.
/// </para>
/// </summary>
public sealed class StudentBalanceQuery(IAppDbContext db)
{
    /// <summary>Baza ustuni <c>numeric(14,2)</c> — javob ham shu aniqlikda.</summary>
    private const int MoneyScale = 2;

    /// <summary>
    /// Bitta o'quvchining qoldig'i. O'quvchi topilmasa yoki hech qanday
    /// hisob-faktura/to'lov bo'lmasa — <c>0</c>.
    ///
    /// <para>
    /// <b>Siklda chaqirmang.</b> Ro'yxat uchun <see cref="ForManyAsync"/> bor:
    /// u qancha o'quvchi bo'lsa ham ikkita so'rov yuboradi.
    /// </para>
    /// </summary>
    public async Task<decimal> ForAsync(string studentId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(studentId);
        var map = await ForManyAsync([studentId], ct);
        return map.GetValueOrDefault(studentId);
    }

    /// <summary>
    /// Bir necha (yoki barcha) o'quvchining qoldig'i.
    /// </summary>
    /// <param name="studentIds">
    /// Kerakli o'quvchilar. <c>null</c> = BARCHA o'quvchilar (o'quvchilar
    /// ro'yxati ekrani shu holatda ishlaydi).
    /// </param>
    /// <returns>
    /// <c>studentId → qoldiq</c>. Pul harakati bo'lmagan o'quvchi lug'atda
    /// UMUMAN bo'lmaydi — chaqiruvchi <c>GetValueOrDefault</c> bilan 0 oladi.
    /// </returns>
    public async Task<IReadOnlyDictionary<string, decimal>> ForManyAsync(
        IReadOnlyCollection<string>? studentIds = null, CancellationToken ct = default)
    {
        // Bo'sh ro'yxat — "hech kim", `null` esa "hamma". Farqi muhim: bo'sh
        // ro'yxat uchun `IN ()` yozib, butun jadvalni qaytarib yuborish oson xato.
        if (studentIds is { Count: 0 }) return new Dictionary<string, decimal>(StringComparer.Ordinal);

        var ids = studentIds?.Distinct(StringComparer.Ordinal).ToList();

        var debts = await DebtByStudentAsync(ids, ct);
        var credits = await CreditByStudentAsync(ids, ct);
        var refunds = await RefundsByStudentAsync(ids, ct);

        var result = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var (id, debt) in debts) result[id] = -debt;
        foreach (var (id, credit) in credits)
            result[id] = result.GetValueOrDefault(id) + credit;
        // F1.05 — qaytarilgan (posted, non-reversed) pul avansdan AYIRILADI:
        // u endi o'quvchining hisobida emas, ota-onaning qo'lida.
        foreach (var (id, refund) in refunds)
            result[id] = result.GetValueOrDefault(id) - refund;

        // Nol qoldiqni ham qoldiramiz: "lug'atda bor, qiymati 0" va "umuman
        // yo'q" chaqiruvchi uchun bir xil natija beradi (`GetValueOrDefault`).
        return result;
    }

    /// <summary>
    /// F1.05 — BITTA o'quvchining AVANSI (taqsimlanmagan pul, qaytarimlar
    /// ayirilgan), qarzdan ALOHIDA. <c>StudentRefundService</c> "so'ralgan
    /// summa ≤ joriy avans" qoidasini shu metoddan tekshiradi.
    ///
    /// <para>
    /// <b>Nega <see cref="ForAsync"/> yetmaydi.</b> U qarz va avansni bitta
    /// SOF qoldiqqa qo'shib beradi (eski <c>students.balance</c> bilan bir
    /// xil belgi uchun). Qaytarim esa faqat AVANSdan chiqishi mumkin — agar
    /// o'quvchida ham qarz, ham (boshqa toifadagi) avans bo'lsa, sof qoldiq
    /// avansdan KICHIK ko'rinadi va direktor haqiqatda mavjud pulni
    /// qaytarolmay qoladi.
    /// </para>
    /// </summary>
    public async Task<decimal> AdvanceForAsync(string studentId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(studentId);
        var credits = await CreditByStudentAsync([studentId], ct);
        var refunds = await RefundsByStudentAsync([studentId], ct);
        return decimal.Round(
            credits.GetValueOrDefault(studentId) - refunds.GetValueOrDefault(studentId), MoneyScale);
    }

    /// <summary>
    /// Qarz: har bir <c>void</c> BO'LMAGAN hisob-fakturaning to'lanmagan
    /// qoldig'i (manfiysi nolga siqiladi), o'quvchi kesimida yig'ilgan.
    /// Guruhlash bazada — natijada o'quvchilar soniga teng miqdorda qator qaytadi.
    /// </summary>
    private async Task<Dictionary<string, decimal>> DebtByStudentAsync(
        IReadOnlyCollection<string>? ids, CancellationToken ct)
    {
        var invoices = db.Invoices.AsNoTracking().Where(i => i.Status != InvoiceStatus.Void);
        if (ids is not null) invoices = invoices.Where(i => ids.Contains(i.StudentId));

        var rows = await invoices
            .Select(i => new
            {
                i.StudentId,
                // QAVSGA E'TIBOR BERING. `??` ning ustuvorligi `-` dan PAST:
                // qavssiz ifoda `(amount - discount - sum) ?? 0` bo'lib o'qilardi,
                // SQL da esa taqsimoti yo'q hisob-faktura uchun `SUM()` NULL
                // qaytaradi — natijada butun ifoda NULL, keyin 0. Ya'ni HALI
                // TO'LANMAGAN oy qarzsiz ko'rinardi. Eng yomon xato turi: jim,
                // va qarzni kam tomonga surib yuboradi.
                Remaining = i.Amount - i.Discount
                    - (EffectiveAllocations()
                        .Where(a => a.InvoiceId == i.Id)
                        .Sum(a => (decimal?)a.Amount) ?? 0m),
            })
            .GroupBy(x => x.StudentId)
            .Select(g => new
            {
                StudentId = g.Key,
                // Ortiqcha to'langan oy boshqa oyning qarzini yopmasin — SQL
                // darajasida `SUM(CASE WHEN ... THEN 0 ELSE ... END)`.
                Debt = g.Sum(x => x.Remaining < 0m ? 0m : x.Remaining),
            })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.StudentId, r => decimal.Round(r.Debt, MoneyScale), StringComparer.Ordinal);
    }

    /// <summary>
    /// Avans: taqsimlanmagan pul. Storno qatorining o'zi ham, storno qilingan
    /// asl to'lov ham chiqarib tashlanadi — bekor qilingan pul na qarzni
    /// yopadi, na avans bo'lib qoladi.
    /// </summary>
    private async Task<Dictionary<string, decimal>> CreditByStudentAsync(
        IReadOnlyCollection<string>? ids, CancellationToken ct)
    {
        var payments = EffectivePayments();
        if (ids is not null) payments = payments.Where(p => ids.Contains(p.StudentId));

        var rows = await payments
            .Select(p => new
            {
                p.StudentId,
                // Yuqoridagi bilan bir xil tuzoq: taqsimlanmagan (butunlay
                // avans) to'lovda `SUM()` NULL bo'ladi va qavssiz ifoda avansni
                // 0 ga aylantirib yuborardi.
                Unallocated = p.Amount
                    - (db.PaymentAllocations.AsNoTracking()
                        .Where(a => a.PaymentId == p.Id)
                        .Sum(a => (decimal?)a.Amount) ?? 0m),
            })
            .GroupBy(x => x.StudentId)
            .Select(g => new { StudentId = g.Key, Credit = g.Sum(x => x.Unallocated) })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.StudentId, r => decimal.Round(r.Credit, MoneyScale), StringComparer.Ordinal);
    }

    /// <summary>
    /// F1.05 — o'quvchiga QAYTARILGAN (jurnalga tushgan, ya'ni "posted") va
    /// hali STORNO QILINMAGAN pul, o'quvchi kesimida yig'ilgan.
    ///
    /// <para>
    /// <b>Nima kiradi, nima kirmaydi.</b> Faqat "oddiy" qaytarimlar
    /// (<c>ReversalOf == null</c>) sanaladi — storno QATORINING o'zi bu
    /// yerga tushmaydi (u pulni "qaytarish" emas, "qaytarimni bekor qilish").
    /// Tasdiqlanmagan (<c>ApprovedBy == null</c>) yoki rad etilgan qaytarim
    /// hali pul harakati EMAS, shuning uchun ham chiqarib tashlanadi. Va
    /// nihoyat — o'zi storno qilingan (ya'ni unga ishora qiluvchi TASDIQLANGAN
    /// storno qatori bor) qaytarim ham hisobga olinmaydi: pul javonga qaytdi.
    /// </para>
    /// </summary>
    private async Task<Dictionary<string, decimal>> RefundsByStudentAsync(
        IReadOnlyCollection<string>? ids, CancellationToken ct)
    {
        var refunds = db.StudentRefunds.AsNoTracking()
            .Where(r => r.ReversalOf == null && r.ApprovedBy != null && r.RejectedReason == null);
        if (ids is not null) refunds = refunds.Where(r => ids.Contains(r.StudentId));

        var rows = await refunds
            .Where(r => !db.StudentRefunds.Any(rev =>
                rev.ReversalOf == r.Id && rev.ApprovedBy != null))
            .GroupBy(r => r.StudentId)
            .Select(g => new { StudentId = g.Key, Total = g.Sum(r => r.Amount) })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.StudentId, r => decimal.Round(r.Total, MoneyScale), StringComparer.Ordinal);
    }

    /// <summary>
    /// HAQIQATAN kuchda bo'lgan to'lovlar: storno qatori ham, storno qilingan
    /// asl to'lov ham chiqib ketadi. <c>payments.reversal_of</c> ustida
    /// shartli unikal indeks bor (P1-05), shuning uchun ikkinchi shart arzon.
    /// </summary>
    private IQueryable<Payment> EffectivePayments() =>
        db.Payments.AsNoTracking()
            .Where(p => p.ReversalOf == null)
            .Where(p => !db.Payments.Any(r => r.ReversalOf == p.Id));

    /// <summary>
    /// Kuchdagi to'lovlarning taqsimotlari — qoida
    /// <see cref="FinanceReportQueries"/> dagi bilan AYNAN bir xil (bekor
    /// qilingan to'lov hisobotda "to'langan" bo'lib qolmasligi uchun).
    /// </summary>
    private IQueryable<PaymentAllocation> EffectiveAllocations() =>
        db.PaymentAllocations.AsNoTracking()
            .Where(a => !db.Payments.Any(p => p.Id == a.PaymentId && p.ReversalOf != null))
            .Where(a => !db.Payments.Any(r => r.ReversalOf == a.PaymentId));
}

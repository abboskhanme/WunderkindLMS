using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  To'lov qabul qilish, taqsimlash, storno — SPEC §3.7, §4. Vazifa: P1-11.
// ===========================================================================
//
//  BU MODULNING ASOSIY TALABI: bitta to'lov bir necha TOIFAGA taqsimlanadi
//  (o'qish + avtobus + yotoqxona). Natija: BITTA chek raqami, N ta
//  `payment_allocations` qatori (SPEC §6 "tayyor deb hisoblanadi" jumlasi).
//
//  UCH QOIDA — HAR BIRI BUZILSA PUL YO'QOLADI
//  ------------------------------------------
//  1. HAMMASI BITTA TRANZAKSIYADA. Bitta `payments`, N ta
//     `payment_allocations`, 2 ta `ledger_entries` va tegilgan
//     hisob-fakturalarning `status` i — yo hammasi, yo hech biri.
//     `LedgerService.PostAsync` o'z `SaveChanges` ini chaqiradi, ya'ni aniq
//     tranzaksiyasiz bu IKKI alohida commit bo'lardi va orada jarayon o'lsa
//     bazada LEDGERSIZ TO'LOV qolardi (docs/PENDING_WIRING.md §8).
//  2. SHAXS SERVERDAN. `cashierId` chaqiruvchidan ALOHIDA parametr sifatida
//     keladi (controller uni JWT'dan oladi), `AcceptPaymentRequest` ichida
//     bunday maydon YO'Q va bo'lmaydi — SPEC §4.4.
//  3. HECH NARSA O'CHIRILMAYDI VA TAHRIRLANMAYDI. Bu klassda `payments`,
//     `payment_allocations`, `ledger_entries` ustida faqat INSERT bor.
//     Xato to'lov <see cref="PaymentService.ReverseAsync"/> bilan tuzatiladi.
//     Baza ham shunday deydi: `app_rw` da bu uch jadvalga UPDATE/DELETE yo'q.
//
//  "TO'LANGAN" QANDAY HISOBLANADI — STORNO BILAN
//  ---------------------------------------------
//  Taqsimotlar o'zgarmas, ya'ni storno ularni O'CHIRA OLMAYDI. Shuning uchun
//  hisob-fakturaga to'langan summa "HAQIQIY taqsimotlar" yig'indisi:
//  storno qatorining O'ZI hech narsa taqsimlamaydi va storno qilingan
//  to'lovning taqsimotlari HISOBGA OLINMAYDI (<see cref="PaymentService"/>
//  ichidagi `EffectiveAllocations`). Bitta joyda, bitta so'rovda.
// ===========================================================================

/// <summary>
/// Xizmat rad etgan holat turi. Controller shu turdan HTTP statusini oladi —
/// status kodi ilova qatlamida saqlanmaydi, chunki bu xizmatni HTTP'siz
/// (fon xizmati, import) chaqirish ham mumkin.
/// </summary>
public enum PaymentError
{
    /// <summary>So'rov noto'g'ri: summa, usul yoki taqsimot. → 400.</summary>
    Invalid,

    /// <summary>So'ralgan to'lov yo'q. → 404.</summary>
    NotFound,

    /// <summary>Holat mos kelmaydi: allaqachon storno qilingan, kassa faol emas va h.k. → 409.</summary>
    Conflict,

    /// <summary>Ikki qavatli nazorat (SPEC §4.5) buzildi. → 403.</summary>
    DualControl,
}

/// <summary>
/// To'lov xizmatining boshqariladigan xatosi. <see cref="Code"/> — MASHINA
/// uchun kalit (<c>cash_box_inactive</c>, <c>already_reversed</c>, ...): UI
/// shunga qarab xabar ko'rsatadi, matn esa o'zgarishi mumkin.
/// <c>no_open_shift</c> ENDI YO'Q — "smena" tushunchasi PaymentService'dan
/// butunlay uzilgan (kassalar modeli, 2026-09).
/// </summary>
public sealed class PaymentException(PaymentError error, string code, string message)
    : Exception(message)
{
    public PaymentError Error { get; } = error;

    /// <summary>Barqaror mashina kaliti (snake_case).</summary>
    public string Code { get; } = code;

    public static PaymentException Invalid(string code, string message) =>
        new(PaymentError.Invalid, code, message);

    public static PaymentException NotFound(string code, string message) =>
        new(PaymentError.NotFound, code, message);

    public static PaymentException Conflict(string code, string message) =>
        new(PaymentError.Conflict, code, message);

    public static PaymentException DualControl(string code, string message) =>
        new(PaymentError.DualControl, code, message);
}

/// <summary>
/// To'lov qabul qilish, taqsimlash va storno (P1-11). Batafsil: fayl boshidagi izoh.
///
/// <para>
/// <b>"Smena" endi YO'Q (kassalar modeli, 2026-09).</b> Mijoz javobi: "smena
/// degan tushuncha umuman bo'lmasin". Bu klass endi <c>ICashShiftService</c>
/// ga UMUMAN bog'lanmaydi — <c>RequireOpenShiftAsync</c> va uning chaqiruvchilari
/// olib tashlandi. O'rniga: to'lov bitta KASSAGA (<c>cash_box_id</c>) tushadi —
/// so'rovda ko'rsatilgan, yoki ko'rsatilmasa SUKUT (default) kassaga
/// (<see cref="CashBoxService.DefaultBoxIdAsync"/>). Chek raqami endi SMENA
/// emas, KASSA bo'yicha uzluksiz (<see cref="CashBoxService.NextReceiptNoAsync"/>).
/// <c>CashBoxService</c> DI orqali OLINMAYDI (u faqat <c>db</c> talab qiladi,
/// <c>CashHandoversController</c> dagi bilan bir xil naqsh) — Program.cs ga
/// yangi registratsiya kerak EMAS.
/// </para>
/// <para>
/// <b>Jurnal</b> <see cref="ILedgerService"/> orqali yoziladi (P1-07) — bu
/// pulni jurnalga yozishga haqli yagona kod (SPEC §2.2). Bu klass
/// <c>db.LedgerEntries.Add(...)</c> qilmaydi.
/// </para>
/// </summary>
public sealed class PaymentService(
    IAppDbContext db,
    ILedgerService ledger) : IPaymentService
{
    /// <summary>Baza ustuni <c>numeric(14,2)</c> — arifmetika ham shu aniqlikda.</summary>
    private const int MoneyScale = 2;

    /// <summary>Audit qatoridagi <c>actor_name</c> uchun — keshlangan (P1-14).</summary>
    private readonly ActorNames actors = new(db);

    /// <summary>
    /// Kassa xizmati — DI'siz, shu yerda quriladi (<c>CashHandoversController</c>
    /// dagi "nega DI'dan emas" izohi bilan bir xil sabab, endi servis darajasida:
    /// yangi DI registratsiyasi kerak emas, <c>db</c> allaqachon bor).
    /// </summary>
    private readonly CashBoxService boxes = new(db);

    /// <summary>
    /// Ro'yxat so'rovining yuqori chegarasi. Kassa oynasi va o'quvchi
    /// kartochkasi doim tor filtr bilan keladi; chegarasiz so'rov esa bir kun
    /// 50 000 qatorni xotiraga tortib, 3 GB serverni yiqitadi. Hisobot kerak
    /// bo'lsa — P1-13 dagi maxsus hisobotlar (ular jamlanma qaytaradi).
    /// </summary>
    private const int MaxRows = 1000;

    // Baza darajasidagi qulflar. Ularni ilova YENGIB O'TMAYDI — faqat
    // TUSHUNARLI xatoga o'giradi. Matnlar `Migrations/Sql/billing_guards.sql`
    // dan va `BillingModel.cs` / `CashBoxModel.cs` dagi indeks nomlaridan
    // aynan olingan.
    private const string AllocationTriggerMessage = "Allocation exceeds payment amount";
    private const string ReversalUniqueIndex = "ix_payments_reversal_of";
    private const string ReceiptUniqueIndex = "ix_payments_cash_box_id_receipt_no";

    /// <summary>
    /// Maktab mintaqasi ofseti (UTC+5, yozgi vaqt yo'q) — <c>received_at</c>
    /// (<c>timestamptz</c>) ni KALENDAR KUNI bo'yicha filtrlash uchun.
    /// <see cref="AppClock"/> dan hisoblab olinadi, qo'lda "+5" yozilmaydi.
    /// </summary>
    private static readonly TimeSpan SchoolOffset =
        AppClock.ToLocal(DateTimeOffset.UnixEpoch) - DateTimeOffset.UnixEpoch.UtcDateTime;

    // -----------------------------------------------------------------
    //  To'lov qabul qilish
    // -----------------------------------------------------------------

    /// <inheritdoc />
    public async Task<PaymentDto> AcceptAsync(
        AcceptPaymentRequest request, string cashierId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(cashierId))
            throw new ArgumentException(
                "Kassir noma'lum (cashierId bo'sh). U JWT claim'idan olinadi — SPEC §4.4.",
                nameof(cashierId));

        // ---- 1. So'rovning o'zi (bazaga tegmasdan) ----
        var amount = Money(request.Amount, "To'lov summasi");
        if (amount <= 0m)
            throw PaymentException.Invalid("invalid_amount", "To'lov summasi musbat bo'lishi shart.");

        if (!PaymentMethod.All.Contains(request.Method, StringComparer.Ordinal))
            throw PaymentException.Invalid("invalid_method",
                $"Noma'lum to'lov usuli: '{request.Method}'. "
                + $"Ruxsat etilganlar: {string.Join(", ", PaymentMethod.All)}.");

        var lines = request.Allocations ?? [];
        var invoiceIds = new List<Guid>(lines.Count);
        var allocated = 0m;
        foreach (var line in lines)
        {
            var lineAmount = Money(line.Amount, "Taqsimot summasi");
            if (lineAmount <= 0m)
                throw PaymentException.Invalid("invalid_allocation_amount",
                    "Taqsimotdagi har bir summa musbat bo'lishi shart.");
            if (invoiceIds.Contains(line.InvoiceId))
                throw PaymentException.Invalid("duplicate_invoice",
                    "Bitta hisob-faktura taqsimotda ikki marta uchramasligi kerak — "
                    + "qatorlarni birlashtiring.");
            invoiceIds.Add(line.InvoiceId);
            allocated += lineAmount;
        }

        // Taqsimlanmagan qoldiq RUXSAT ETILADI (avans, docs/ASSUMPTIONS.md Q15) —
        // oshib ketishi esa yo'q. Bu tekshiruv ikkinchi qavat: birinchisi bazadagi
        // `payment_allocations_total` trigger'i, u ilova chetlab o'tilsa ham ishlaydi.
        if (allocated > amount)
            throw PaymentException.Invalid("allocation_exceeds_amount",
                $"Taqsimot yig'indisi ({allocated}) to'lov summasidan ({amount}) oshib ketdi.");

        // ---- 2. O'quvchi ----
        var student = await db.Students.AsNoTracking()
            .Where(s => s.Id == request.StudentId)
            .Select(s => new { s.Id, s.FullName })
            .FirstOrDefaultAsync(ct)
            ?? throw PaymentException.Invalid("student_not_found",
                "O'quvchi topilmadi. Bitta to'lov — bitta o'quvchi (SPEC §8.1 Q14).");

        // ---- 3. Kassa (endi SMENA emas — mijoz javobi, 2026-09) ----
        // So'rovda ko'rsatilgan, aks holda SUKUT (default) kassa. Bo'lmagan
        // yoki faol bo'lmagan kassa — 404/409, va bitta ham pul qatori yozilmaydi.
        var cashBoxId = request.CashBoxId ?? await CashBoxService.DefaultBoxIdAsync(db, ct);
        await RequireActiveBoxAsync(cashBoxId, ct);

        // ---- 4. BITTA TRANZAKSIYA ----
        await using var tx = await db.BeginTransactionAsync(ct);

        // Hisob-fakturalar TRANZAKSIYA ICHIDA o'qiladi — qoldiqni yozuvdan
        // imkon qadar yaqin nuqtada tekshirish uchun. Qator qulfi qo'yilmaydi:
        // `SELECT ... FOR UPDATE` uchun EF'da xom SQL kerak bo'lardi va
        // Application qatlamida Relational paketi yo'q. Ya'ni bir xil
        // hisob-fakturaga BIR VAQTDA ikki kassa to'lov yozsa, ikkalasi ham
        // o'tib ketishi mumkin — natijasi "ortiqcha to'langan hisob-faktura",
        // storno bilan tuzatiladigan holat. To'lov summasi bo'yicha invariant
        // esa qat'iy: uni bazadagi trigger qo'riqlaydi.
        // Kuzatiladigan (tracked) holda o'qiymiz: status keyin shu obyektlarda yangilanadi.
        var invoices = await db.Invoices.Where(i => invoiceIds.Contains(i.Id)).ToListAsync(ct);
        var paidBefore = await PaidByInvoiceAsync(invoiceIds, ct);

        foreach (var line in lines)
        {
            var invoice = invoices.FirstOrDefault(i => i.Id == line.InvoiceId)
                ?? throw PaymentException.Invalid("invoice_not_found",
                    $"Hisob-faktura topilmadi: {line.InvoiceId}.");

            if (!string.Equals(invoice.StudentId, student.Id, StringComparison.Ordinal))
                throw PaymentException.Invalid("invoice_other_student",
                    "Hisob-faktura boshqa o'quvchiga tegishli. Bitta to'lov — bitta o'quvchi.");

            if (invoice.Status == InvoiceStatus.Void)
                throw PaymentException.Invalid("invoice_void",
                    "Bekor qilingan (void) hisob-fakturaga pul taqsimlab bo'lmaydi.");

            var remaining = invoice.Amount - invoice.Discount - paidBefore.GetValueOrDefault(invoice.Id);
            if (line.Amount > remaining)
                throw PaymentException.Invalid("allocation_exceeds_invoice",
                    $"Hisob-fakturaga ({invoice.PeriodMonth:yyyy-MM}) qoldig'idan ({remaining}) "
                    + $"ko'p summa ({line.Amount}) yo'naltirildi. Ortiqcha pul taqsimlanmagan "
                    + "qoldiq (avans) bo'lib qolishi mumkin.");
        }

        // Chek raqami ham SHU tranzaksiyada olinadi: bekor qilingan so'rov
        // raqamda teshik qoldirmasin (SPEC §4.2, uzluksizlik — endi KASSA
        // bo'yicha, smena bo'yicha emas).
        var receiptNo = await boxes.NextReceiptNoAsync(cashBoxId, ct);
        var receivedAt = AppClock.NowInstant;

        var payment = new Payment
        {
            ReceiptNo = receiptNo,
            StudentId = student.Id,
            Amount = amount,
            Method = request.Method,
            // "Smena" endi YO'Q — yangi to'lovda HAR DOIM null (Domain/Billing.cs
            // izohi). Pul endi kassaga bog'lanadi.
            CashShiftId = null,
            CashBoxId = cashBoxId,
            CashierId = cashierId,
            Note = Trim(request.Note),
            ReceivedAt = receivedAt,
            ReversalOf = null,
        };
        db.Payments.Add(payment);

        foreach (var line in lines)
            db.PaymentAllocations.Add(new PaymentAllocation
            {
                PaymentId = payment.Id,
                InvoiceId = line.InvoiceId,
                Amount = line.Amount,
            });

        await SaveAsync(ct);

        // ---- 5. Tegilgan hisob-fakturalar statusi ----
        await RefreshStatusesAsync(invoices, ct);

        // ---- 6. Jurnal: AYNAN ikki qator ----
        // Debet: pul qayerga tushdi (naqd — kassaga, qolgani — bankka, §8.1 Q13).
        // Kredit: o'quvchining qarzi (receivable) shuncha kamaydi.
        // Toifalar bo'yicha DAROMAD hisob-faktura yozilganda tan olinadi (P1-09),
        // shuning uchun bu yerda taqsimot nechta bo'lsa ham jurnal qatori ikkita.
        await ledger.PostAsync(
        [
            new LedgerPosting(
                Accounts.SettlementFor(payment.Method), LedgerDirection.Debit, payment.Amount,
                LedgerRefType.Payment, payment.Id, AppClock.LocalDateOf(receivedAt),
                $"Chek #{receiptNo} — {student.FullName}"),
            new LedgerPosting(
                Accounts.Receivable, LedgerDirection.Credit, payment.Amount,
                LedgerRefType.Payment, payment.Id, AppClock.LocalDateOf(receivedAt),
                $"Chek #{receiptNo} — {student.FullName}"),
        ], cashierId, ct);

        // ---- 7. Audit (SPEC §4.6) ----
        // TRANZAKSIYA ICHIDA, commit'dan OLDIN: orqaga qaytgan to'lov
        // olinmagan pul haqida audit izi qoldirmasin
        // (docs/PENDING_WIRING.md §11 — P1-11 shu shartni yozib qoldirgan).
        db.AuditLogs.Add(AuditService.Entry(
            AuditService.EntityPayment, payment.Id.ToString("D"), "create",
            $"To'lov qabul qilindi: chek #{receiptNo}, {AuditService.Money(amount)} so'm "
            + $"({payment.Method}) — {student.FullName}",
            actorId: cashierId,
            actorName: await actors.OfAsync(cashierId, ct),
            after: Snapshot(payment, lines),
            studentId: student.Id));
        await SaveAsync(ct);

        await tx.CommitAsync(ct);

        return (await ToDtosAsync([payment], ct))[0];
    }

    // -----------------------------------------------------------------
    //  Storno (SPEC §4.1, §4.3, §4.5)
    // -----------------------------------------------------------------

    /// <inheritdoc />
    public async Task<PaymentDto> ReverseAsync(
        Guid paymentId, string reason, string approverId, Guid? cashBoxId = null, CancellationToken ct = default)
    {
        var cleanReason = Trim(reason);
        if (cleanReason is null)
            throw PaymentException.Invalid("reason_required",
                "Storno sababi majburiy (SPEC §4.3) — u chekda va hisobotda ko'rinadi.");
        if (string.IsNullOrWhiteSpace(approverId))
            throw new ArgumentException(
                "Tasdiqlovchi noma'lum (approverId bo'sh). U JWT claim'idan olinadi — SPEC §4.4.",
                nameof(approverId));

        var original = await db.Payments.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paymentId, ct)
            ?? throw PaymentException.NotFound("payment_not_found", "To'lov topilmadi.");

        if (original.ReversalOf is not null)
            throw PaymentException.Conflict("cannot_reverse_reversal",
                "Bu qatorning O'ZI storno. Storno'ni storno qilib bo'lmaydi — "
                + "kerak bo'lsa yangi to'lov qabul qiling.");

        // Ikki marta storno = pulni ikki marta "qaytarish". Bazada ham shunday:
        // `ix_payments_reversal_of` qisman UNIKAL indeks (BillingModel.cs).
        if (await db.Payments.AnyAsync(p => p.ReversalOf == paymentId, ct))
            throw PaymentException.Conflict("already_reversed",
                "Bu to'lov allaqachon storno qilingan.");

        // SPEC §4.5 — ikki qavatli nazorat. "Pulni oldim, keyin o'zim storno
        // qildim" — tavsifdagi firibgarlikning aynan o'zi. LedgerService ham
        // shu qoidani mustaqil tekshiradi; bu yerda tekshirish sababni ANIQ
        // aytish uchun (u yerda umumiy xato matni chiqardi).
        if (string.Equals(original.CashierId, approverId, StringComparison.Ordinal))
            throw PaymentException.DualControl("own_payment_reversal",
                "O'zingiz qabul qilgan to'lovni o'zingiz storno qila olmaysiz (SPEC §4.5) — "
                + "ikkinchi shaxs tasdig'i kerak.");

        // Jurnal partiyasining "langar" satri: LedgerService shundan butun
        // partiyani topadi va BUTUNLIGICHA teskari qiladi.
        var anchorId = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.RefType == LedgerRefType.Payment
                        && e.RefId == paymentId
                        && e.ReversalOf == null)
            .OrderBy(e => e.Id)
            .Select(e => (long?)e.Id)
            .FirstOrDefaultAsync(ct)
            ?? throw PaymentException.Conflict("no_ledger_batch",
                "To'lovning jurnal yozuvi topilmadi — storno jurnalni nomutanosib "
                + "qoldirardi. Moliya mas'uliga murojaat qiling.");

        // Storno qatori QAYSI kassaga qaytishi — so'rovda ko'rsatilgan, aks
        // holda SUKUT kassa ("smena" endi yo'q — mijoz javobi, 2026-09).
        var box = cashBoxId ?? await CashBoxService.DefaultBoxIdAsync(db, ct);
        await RequireActiveBoxAsync(box, ct);

        await using var tx = await db.BeginTransactionAsync(ct);

        var storno = new Payment
        {
            ReceiptNo = await boxes.NextReceiptNoAsync(box, ct),
            StudentId = original.StudentId,
            // Summa MUSBAT — qator "storno" ekanini `ReversalOf` bildiradi
            // (`ck_payments_amount: amount > 0`).
            Amount = original.Amount,
            Method = original.Method,
            CashShiftId = null,
            CashBoxId = box,
            CashierId = approverId,
            Note = cleanReason,
            ReceivedAt = AppClock.NowInstant,
            ReversalOf = original.Id,
        };
        db.Payments.Add(storno);
        await SaveAsync(ct);

        // Ko'zgu jurnal yozuvlari. Original TEGILMAYDI (LedgerService'da
        // `Update` ham, `Remove` ham yo'q).
        try
        {
            await ledger.ReverseAsync(anchorId, cleanReason, approverId, ct);
        }
        catch (InvalidOperationException ex)
        {
            // Jurnal darajasidagi qoidalar (partiya allaqachon teskari
            // qilingan, yozuvchining o'zi tasdiqlamoqda) yuqorida tekshirilgan.
            // Bu yerga tushish — ma'lumot nomutanosibligi; 500 o'rniga sababni
            // ko'rsatgan 409 foydaliroq.
            throw PaymentException.Conflict("ledger_reversal_refused", ex.Message);
        }

        // Storno qilingan to'lovning taqsimotlari endi HISOBGA OLINMAYDI —
        // tegilgan hisob-fakturalar qayta "open"/"partial" bo'lishi mumkin.
        var touched = await db.PaymentAllocations.AsNoTracking()
            .Where(a => a.PaymentId == paymentId)
            .Select(a => a.InvoiceId)
            .Distinct()
            .ToListAsync(ct);
        var invoices = await db.Invoices.Where(i => touched.Contains(i.Id)).ToListAsync(ct);
        await RefreshStatusesAsync(invoices, ct);

        // Audit (SPEC §4.6) — commit'dan oldin. `before` = original to'lov,
        // `after` = storno qatori. Ikkalasi ham bazada QOLADI: bu yerda hech
        // narsa o'chirilmaydi va tahrirlanmaydi.
        db.AuditLogs.Add(AuditService.Entry(
            AuditService.EntityPayment, storno.Id.ToString("D"), "reverse",
            $"To'lov STORNO qilindi: original chek #{original.ReceiptNo}, "
            + $"{AuditService.Money(original.Amount)} so'm — sabab: {cleanReason}",
            actorId: approverId,
            actorName: await actors.OfAsync(approverId, ct),
            before: Snapshot(original, null),
            after: Snapshot(storno, null),
            studentId: original.StudentId));
        await SaveAsync(ct);

        await tx.CommitAsync(ct);

        return (await ToDtosAsync([storno], ct))[0];
    }

    // -----------------------------------------------------------------
    //  O'qish
    // -----------------------------------------------------------------

    /// <inheritdoc />
    public async Task<PaymentDto?> GetAsync(Guid paymentId, CancellationToken ct = default)
    {
        var payment = await db.Payments.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paymentId, ct);
        return payment is null ? null : (await ToDtosAsync([payment], ct))[0];
    }

    /// <summary>
    /// To'lovlar ro'yxati (yangisidan eskisiga). Eng ko'pi bilan
    /// <see cref="MaxRows"/> qator qaytadi — filtrni toraytiring.
    /// </summary>
    public async Task<IReadOnlyList<PaymentDto>> ListAsync(
        PaymentQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var q = db.Payments.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.StudentId)) q = q.Where(p => p.StudentId == query.StudentId);
        if (!string.IsNullOrWhiteSpace(query.CashierId)) q = q.Where(p => p.CashierId == query.CashierId);
        // `CashShiftId` — faqat ESKI qatorlar uchun tarixiy filtr (SPEC izohi:
        // "smena" endi yo'q, yangi to'lovda bu ustun har doim null).
        if (query.CashShiftId is { } shiftId) q = q.Where(p => p.CashShiftId == shiftId);
        if (query.CashBoxId is { } boxIdFilter) q = q.Where(p => p.CashBoxId == boxIdFilter);
        if (!string.IsNullOrWhiteSpace(query.Method)) q = q.Where(p => p.Method == query.Method);
        if (query.OnlyReversals) q = q.Where(p => p.ReversalOf != null);
        if (query.From is { } from) { var lo = StartOfSchoolDay(from); q = q.Where(p => p.ReceivedAt >= lo); }
        if (query.To is { } to) { var hi = StartOfSchoolDay(to.AddDays(1)); q = q.Where(p => p.ReceivedAt < hi); }

        var rows = await q
            .OrderByDescending(p => p.ReceivedAt)
            .ThenByDescending(p => p.ReceiptNo)
            .Take(MaxRows)
            .ToListAsync(ct);

        return await ToDtosAsync(rows, ct);
    }

    /// <summary>
    /// Taqsimot TAKLIFI: eng eski to'lanmagan oydan boshlab (FIFO).
    /// Barcha ochiq hisob-fakturalar qaytadi — pul yetmagani
    /// <c>Suggested = 0</c> bilan, chunki kassir ekranda qatorlar orasida
    /// summani ko'chira olishi kerak. <paramref name="amount"/> musbat
    /// bo'lmasa — bo'sh ro'yxat (foydalanuvchi hali summani yozmagan).
    /// </summary>
    public async Task<IReadOnlyList<AllocationSuggestionDto>> SuggestAllocationAsync(
        string studentId, decimal amount, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentId) || amount <= 0m) return [];

        var rows = await (from i in db.Invoices.AsNoTracking()
                          join c in db.FeeCategories.AsNoTracking() on i.CategoryId equals c.Id
                          where i.StudentId == studentId && i.Status != InvoiceStatus.Void
                          orderby i.PeriodMonth, c.Code
                          select new
                          {
                              i.Id,
                              i.CategoryId,
                              CategoryCode = c.Code,
                              CategoryName = c.Name,
                              i.PeriodMonth,
                              Payable = i.Amount - i.Discount,
                          })
                         .ToListAsync(ct);

        var paid = await PaidByInvoiceAsync(rows.Select(r => r.Id).ToList(), ct);

        var left = decimal.Round(amount, MoneyScale);
        var result = new List<AllocationSuggestionDto>(rows.Count);
        foreach (var r in rows)
        {
            var remaining = r.Payable - paid.GetValueOrDefault(r.Id);
            if (remaining <= 0m) continue;

            var suggested = left > 0m ? Math.Min(remaining, left) : 0m;
            left -= suggested;

            result.Add(new AllocationSuggestionDto(
                r.Id, r.CategoryId, r.CategoryCode, r.CategoryName, r.PeriodMonth,
                remaining, suggested));
        }

        return result;
    }

    // -----------------------------------------------------------------
    //  Ichki yordamchilar
    // -----------------------------------------------------------------

    /// <summary>
    /// HAQIQIY taqsimotlar: storno qatorlarining o'zi (<c>reversal_of</c> to'la)
    /// hech narsa taqsimlamaydi, storno qilingan to'lovning taqsimotlari esa
    /// hisobga olinmaydi. Taqsimot qatorini O'CHIRIB bo'lmaydi (o'zgarmas
    /// jadval), shuning uchun "to'langan" degan savolning javobi AYNAN shu
    /// so'rov — bitta joyda.
    /// </summary>
    private IQueryable<PaymentAllocation> EffectiveAllocations() =>
        from a in db.PaymentAllocations.AsNoTracking()
        join p in db.Payments.AsNoTracking() on a.PaymentId equals p.Id
        where p.ReversalOf == null && !db.Payments.Any(r => r.ReversalOf == p.Id)
        select a;

    /// <summary>Hisob-faktura → to'langan summa. Bitta guruhlangan so'rov (N+1 yo'q).</summary>
    private async Task<Dictionary<Guid, decimal>> PaidByInvoiceAsync(
        List<Guid> invoiceIds, CancellationToken ct)
    {
        if (invoiceIds.Count == 0) return [];

        var rows = await EffectiveAllocations()
            .Where(a => invoiceIds.Contains(a.InvoiceId))
            .GroupBy(a => a.InvoiceId)
            .Select(g => new { InvoiceId = g.Key, Total = g.Sum(x => x.Amount) })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.InvoiceId, r => r.Total);
    }

    /// <summary>
    /// Tegilgan hisob-fakturalarning <c>status</c> ini QAYTA HISOBLAYDI
    /// (open | partial | paid). <c>void</c> tegilmaydi: u qarzga umuman
    /// kirmaydi va uni faqat P1-09 qo'yadi.
    ///
    /// <para>
    /// DIQQAT: status bu yerda TAXMIN qilinmaydi, taqsimotlar yig'indisidan
    /// hisoblanadi — shuning uchun storno'dan keyin ham (yig'indi kamayadi)
    /// to'g'ri qiymat chiqadi.
    /// </para>
    /// </summary>
    private async Task RefreshStatusesAsync(List<Invoice> invoices, CancellationToken ct)
    {
        if (invoices.Count == 0) return;

        var paid = await PaidByInvoiceAsync(invoices.Select(i => i.Id).ToList(), ct);

        foreach (var invoice in invoices)
        {
            if (invoice.Status == InvoiceStatus.Void) continue;

            var payable = invoice.Amount - invoice.Discount;
            var total = paid.GetValueOrDefault(invoice.Id);

            invoice.Status = total <= 0m
                ? InvoiceStatus.Open
                : total >= payable ? InvoiceStatus.Paid : InvoiceStatus.Partial;
        }

        await SaveAsync(ct);
    }

    /// <summary>
    /// Kassa mavjud va FAOL ekanini tekshiradi ("smena" o'rniga — kassalar
    /// modeli, 2026-09). Bo'lmasa 404, faol bo'lmasa 409 — <c>PaymentException</c>
    /// shaklida (bu klassning o'z xato tili, <c>BillingRuleException</c> emas).
    /// </summary>
    private async Task RequireActiveBoxAsync(Guid boxId, CancellationToken ct)
    {
        var box = await db.CashBoxes.AsNoTracking()
            .Where(b => b.Id == boxId)
            .Select(b => new { b.IsActive })
            .FirstOrDefaultAsync(ct)
            ?? throw PaymentException.NotFound("cash_box_not_found", "Kassa topilmadi.");

        if (!box.IsActive)
            throw PaymentException.Conflict("cash_box_inactive", "Bu kassa faol emas.");
    }

    /// <summary>
    /// To'lovlarni DTO'ga o'giradi. Qator soni qanday bo'lsin — HAR DOIM
    /// beshta so'rov (taqsimot, o'quvchi, kassir, storno havolasi) + kassalar
    /// nomlari (oltinchisi). Moliya entity'larida navigatsiya xossalari YO'Q
    /// (BillingModel.cs FK'larni navigatsiyasiz e'lon qiladi), shuning uchun
    /// "Include" o'rniga partiyalab o'qish — N+1 ning oldini olish yo'li shu.
    /// </summary>
    private async Task<List<PaymentDto>> ToDtosAsync(
        IReadOnlyList<Payment> payments, CancellationToken ct)
    {
        if (payments.Count == 0) return [];

        var ids = payments.Select(p => p.Id).ToList();

        var allocations = await (from a in db.PaymentAllocations.AsNoTracking()
                                 join i in db.Invoices.AsNoTracking() on a.InvoiceId equals i.Id
                                 join c in db.FeeCategories.AsNoTracking() on i.CategoryId equals c.Id
                                 where ids.Contains(a.PaymentId)
                                 select new
                                 {
                                     a.Id,
                                     a.PaymentId,
                                     a.InvoiceId,
                                     i.CategoryId,
                                     CategoryCode = c.Code,
                                     CategoryName = c.Name,
                                     i.PeriodMonth,
                                     a.Amount,
                                 })
                                .ToListAsync(ct);

        var studentIds = payments.Select(p => p.StudentId).Distinct().ToList();
        var studentNames = await db.Students.AsNoTracking()
            .Where(s => studentIds.Contains(s.Id))
            .Select(s => new { s.Id, s.FullName })
            .ToDictionaryAsync(s => s.Id, s => s.FullName, ct);

        var userIds = payments.Select(p => p.CashierId).Distinct().ToList();
        var cashierNames = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName })
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        var reversedBy = await db.Payments.AsNoTracking()
            .Where(p => p.ReversalOf != null && ids.Contains(p.ReversalOf!.Value))
            .Select(p => new { StornoId = p.Id, OriginalId = p.ReversalOf!.Value })
            .ToDictionaryAsync(x => x.OriginalId, x => x.StornoId, ct);

        var boxIds = payments.Select(p => p.CashBoxId).Where(x => x is not null).Select(x => x!.Value)
            .Distinct().ToList();
        var boxNames = boxIds.Count == 0
            ? []
            : await db.CashBoxes.AsNoTracking()
                .Where(b => boxIds.Contains(b.Id))
                .Select(b => new { b.Id, b.Name })
                .ToDictionaryAsync(b => b.Id, b => b.Name, ct);

        return [.. payments.Select(p =>
        {
            var mine = allocations
                .Where(a => a.PaymentId == p.Id)
                .OrderBy(a => a.PeriodMonth)
                .ThenBy(a => a.CategoryCode, StringComparer.Ordinal)
                .Select(a => new PaymentAllocationDto(
                    a.Id, a.InvoiceId, a.CategoryId, a.CategoryCode, a.CategoryName,
                    a.PeriodMonth, a.Amount))
                .ToList();

            return new PaymentDto(
                p.Id, p.ReceiptNo,
                p.StudentId, studentNames.GetValueOrDefault(p.StudentId, "—"),
                p.Amount, p.Method,
                p.CashShiftId, p.CashierId, cashierNames.GetValueOrDefault(p.CashierId, "—"),
                p.Note, p.ReceivedAt,
                p.ReversalOf,
                reversedBy.TryGetValue(p.Id, out var storno) ? storno : null,
                // Taqsimlanmagan qoldiq — o'quvchi krediti. HECH QANDAY
                // o'zgaruvchan balans ustuniga yozilmaydi: u har doim
                // shu ayirmadan hisoblanadi.
                p.Amount - mine.Sum(a => a.Amount),
                mine,
                p.CashBoxId,
                p.CashBoxId is null ? null : boxNames.GetValueOrDefault(p.CashBoxId.Value, "—"));
        })];
    }

    /// <summary>
    /// <c>SaveChanges</c> + baza qulflarini TUSHUNARLI xatoga o'girish.
    ///
    /// <para>
    /// Bu yerda Npgsql tipiga bog'lanmaymiz (Application qatlamida u yo'q va
    /// bo'lmasligi ham kerak) — xabardagi BARQAROR belgilarga qaraymiz:
    /// trigger matni <c>billing_guards.sql</c> da qat'iy belgilangan, indeks
    /// nomi esa har qanday tildagi Postgres xabarida o'zgarmaydi.
    /// </para>
    /// </summary>
    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (Mentions(ex, AllocationTriggerMessage))
        {
            throw PaymentException.Invalid("allocation_exceeds_amount",
                "Taqsimot yig'indisi to'lov summasidan oshib ketdi (baza rad etdi).");
        }
        catch (DbUpdateException ex) when (Mentions(ex, ReversalUniqueIndex))
        {
            throw PaymentException.Conflict("already_reversed",
                "Bu to'lov allaqachon storno qilingan (baza rad etdi).");
        }
        catch (DbUpdateException ex) when (Mentions(ex, ReceiptUniqueIndex))
        {
            throw PaymentException.Conflict("receipt_no_taken",
                "Chek raqami band — boshqa kassa oynasi ayni shu lahzada to'lov yozdi. "
                + "Qaytadan urinib ko'ring.");
        }
    }

    private static bool Mentions(Exception? ex, string token)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e.Message.Contains(token, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>
    /// Audit uchun snapshot (<c>before</c>/<c>after</c>). Taqsimotlar SHU YERDA
    /// bo'lishi shart: ular <c>payment_allocations</c> da yashaydi va
    /// "pul qaysi oyga ketdi" degan savolga javob beradi — to'lov qatorining
    /// o'zi buni ko'rsatmaydi.
    /// </summary>
    private static object Snapshot(Payment p, IReadOnlyList<AllocationRequest>? allocations) => new
    {
        p.Id,
        p.ReceiptNo,
        p.StudentId,
        p.Amount,
        p.Method,
        p.CashShiftId,
        p.CashierId,
        p.Note,
        p.ReceivedAt,
        p.ReversalOf,
        Allocations = allocations?.Select(a => new { a.InvoiceId, a.Amount }).ToList(),
    };

    /// <summary>
    /// Pul qiymati baza aniqligiga (2 kasr) MOS bo'lishi shart. Yaxlitlab
    /// yubormaymiz: 1000.005 kabi qiymat "qabul qilindi" deb ko'rinib, bazada
    /// boshqa raqam bo'lib qolardi — va farqni keyin hech kim topa olmasdi.
    /// </summary>
    private static decimal Money(decimal value, string what) =>
        decimal.Round(value, MoneyScale) == value
            ? value
            : throw PaymentException.Invalid("invalid_amount",
                $"{what} tiyin aniqligida (2 kasr) bo'lishi shart: {value}.");

    /// <summary>Maktab kunining boshlanish lahzasi (kalendar kuni bo'yicha filtr uchun).</summary>
    private static DateTimeOffset StartOfSchoolDay(DateOnly day) =>
        new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), SchoolOffset).ToUniversalTime();

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

/// <summary>
/// Hisob-fakturalar va oylik hisoblash (accrual) — SPEC §3.7. Vazifa: P1-09.
///
/// <para>
/// Eski <c>TuitionService.AccrueMonth</c> ning vorisi. Ikki tub farqi bor:
/// (1) narx sinfdan emas, <c>student_subscriptions</c> dan olinadi, ya'ni bitta
/// o'quvchida bir vaqtda o'qish, avtobus va ovqat bo'lishi mumkin — har biriga
/// ALOHIDA hisob-faktura; (2) hisoblash <c>students.balance</c> ni o'zgartirmaydi
/// (u P1-21 da o'ladi) — qarz <c>invoices</c> va <c>payment_allocations</c> dan
/// HISOBLANADI.
/// </para>
///
/// <para>
/// <b>IDEMPOTENTLIK — xotiradagi ro'yxat bilan EMAS.</b> Oyni ikki marta
/// hisoblash bitta qator bilan tugashini unikal indeks
/// (<c>student_id, category_id, period_month</c> — <c>BillingModel.cs</c>)
/// kafolatlaydi. Koddagi "allaqachon bor" filtri faqat TEZLIK uchun: u ikkita
/// parallel jarayonni (fon xizmati + admin qo'lda bosgan tugma) to'xtata olmaydi,
/// chunki ikkalasi ham bir xil "bo'sh" holatni o'qiy oladi. Haqiqiy to'siq —
/// <c>23505</c>, va u <see cref="InsertWithLedgerAsync"/> da "skipped" deb
/// qabul qilinadi.
/// </para>
///
/// <para>
/// <b>LEDGERSIZ HISOB-FAKTURA BO'LMAYDI.</b> Har qator BITTA tranzaksiyada
/// <c>debit receivable / credit revenue:&lt;toifa&gt;</c> jufti bilan yoziladi
/// (SPEC §2.2 — pulni faqat <see cref="ILedgerService"/> yozadi). YAGONA istisno:
/// to'lanadigan summa 0 bo'lsa (100% chegirma yoki bepul o'qish) — jurnal 0 li
/// satrni qabul qilmaydi (<c>ck_ledger_entries_amount: amount &gt; 0</c>) va
/// qo'yadigan hech narsa ham yo'q: na qarz, na daromad. Bunday hisob-faktura
/// hisobotda ko'rinib tursin deb baribir yoziladi (eski xulq ham shunday edi).
/// </para>
///
/// <para>
/// <b>To'lov muddati kodda EMAS.</b> <c>due_on</c> va "muddati o'tgan" chegarasi
/// <c>billing_settings</c> dan o'qiladi (mijoz javobi, SPEC §8.1 Q6). Bu yerda
/// 10 yoki 15 raqami YOZILMAGAN — sozlama qatori topilmasa domendagi sukut
/// qiymatlar (<see cref="BillingSettings"/>) ishlatiladi.
/// </para>
/// </summary>
public sealed class InvoiceService(IAppDbContext db, ILedgerService ledger) : IInvoiceService
{
    /// <summary>Baza ustuni <c>numeric(14,2)</c> — hisob-kitob ham shu aniqlikda.</summary>
    private const int MoneyScale = 2;

    /// <summary>
    /// <see cref="ListAsync"/> qaytaradigan eng ko'p qator. Muzlatilgan
    /// <see cref="InvoiceQuery"/> da sahifalash yo'q (P1-06), 1000 o'quvchi × 3 toifa
    /// × 12 oy esa 36 000 qator — filtrsiz so'rov brauzerni ham, serverni ham
    /// bo'g'adi. Eng YANGI oylardan boshlab kesamiz; sahifalash kerak bo'lganda
    /// <see cref="InvoiceQuery"/> ga <c>Page</c>/<c>PageSize</c> qo'shiladi
    /// (docs/PENDING_WIRING.md).
    /// </summary>
    public const int MaxListRows = 2000;

    // =================================================================
    //  Oylik hisoblash
    // =================================================================

    /// <inheritdoc />
    public async Task<AccrualResultDto> AccrueMonthAsync(
        DateOnly periodMonth, string actorId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actorId))
            throw new ArgumentException("Hisoblashni kim boshlagani noma'lum (actorId bo'sh).", nameof(actorId));

        var monthStart = FirstDayOf(periodMonth);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        var settings = await SettingsAsync(ct);
        var dueOn = DayInMonth(monthStart, settings.PaymentDueDay);

        // ---- 1. Shu oyda kuchda bo'lgan obunalar ----
        // Arxivlangan o'quvchi (oylik hisoblanmaydi) va o'chirilgan toifa
        // (`is_active = false` = yangi hisob-faktura ochilmaydi) SO'ROVDA tushib
        // qoladi — keyinroq `if` bilan emas.
        var candidates = await (
            from sub in db.StudentSubscriptions.AsNoTracking()
            join st in db.Students.AsNoTracking() on sub.StudentId equals st.Id
            join cat in db.FeeCategories.AsNoTracking() on sub.CategoryId equals cat.Id
            where !st.IsArchived
                  && cat.IsActive
                  // Mijoz javobi (Q12): to'liq bo'lmagan oy ham TO'LIQ oy — obuna
                  // oy bilan KESISHSA yetarli, kunlar bo'yicha bo'linmaydi.
                  && sub.StartsOn <= monthEnd
                  && (sub.EndsOn == null || sub.EndsOn >= monthStart)
            select new
            {
                sub.StudentId,
                StudentName = st.FullName,
                sub.CategoryId,
                CategoryCode = cat.Code,
                CategoryName = cat.Name,
                sub.MonthlyAmount,
                sub.StartsOn,
                sub.CreatedAt,
            }).ToListAsync(ct);

        // Bitta (o'quvchi, toifa) juftiga bir oyda BITTA qator sig'adi (unikal
        // indeks). Oy o'rtasida obuna almashgan bo'lsa (eskisi tugab, yangisi
        // boshlangan) — KEYINGI boshlanganini, ya'ni joriy narxni olamiz.
        var subscriptions = candidates
            .GroupBy(x => (x.StudentId, x.CategoryId))
            .Select(g => g
                .OrderByDescending(x => x.StartsOn)
                .ThenByDescending(x => x.CreatedAt)
                .First())
            .ToList();

        if (subscriptions.Count == 0)
            return new AccrualResultDto(monthStart, 0, 0, 0m);

        // ---- 2. Allaqachon hisoblanganlar (TEZLIK filtri, kafolat EMAS) ----
        var existing = (await db.Invoices.AsNoTracking()
                .Where(i => i.PeriodMonth == monthStart)
                .Select(i => new { i.StudentId, i.CategoryId })
                .ToListAsync(ct))
            .Select(x => (x.StudentId, x.CategoryId))
            .ToHashSet();

        // ---- 3. FAQAT TASDIQLANGAN chegirmalar (mijoz javobi, SPEC §8.1 Q5) ----
        // `status = 'approved'` VA `approved_by is not null` — ikkalasi ham.
        // Tasdiq kutayotgan chegirma hisobni kamaytirmasligi SHART.
        var discounts = await db.Discounts.AsNoTracking()
            .Where(d => d.Status == DiscountStatus.Approved
                        && d.ApprovedBy != null
                        && d.StartsOn <= monthEnd
                        && (d.EndsOn == null || d.EndsOn >= monthStart))
            .Select(d => new { d.StudentId, d.CategoryId, d.Percent, d.Amount, d.StartsOn, d.CreatedAt })
            .ToListAsync(ct);

        var discountsByStudent = discounts
            .GroupBy(d => d.StudentId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        // ---- 4. Yozish ----
        var created = 0;
        var skipped = 0;
        var total = 0m;

        foreach (var s in subscriptions)
        {
            if (existing.Contains((s.StudentId, s.CategoryId)))
            {
                skipped++;
                continue;
            }

            var gross = decimal.Round(s.MonthlyAmount, MoneyScale);

            // Eng ANIQ chegirma yutadi: toifaga tegishlisi umumiysidan ustun,
            // tenglashsa — keyinroq boshlangani. Chegirmalar QO'SHILMAYDI: eski
            // modelda o'quvchida bitta foiz + bitta summa bor edi va uni jimgina
            // "yig'indi" ga aylantirish hisobni tushuntirib bo'lmaydigan qilardi.
            var applied = discountsByStudent.TryGetValue(s.StudentId, out var forStudent)
                ? forStudent
                    .Where(d => d.CategoryId == null || d.CategoryId == s.CategoryId)
                    .OrderByDescending(d => d.CategoryId != null)
                    .ThenByDescending(d => d.StartsOn)
                    .ThenByDescending(d => d.CreatedAt)
                    .FirstOrDefault()
                : null;

            var discount = applied is null
                ? 0m
                : DiscountMath.DiscountFor(gross, applied.Percent, applied.Amount);
            var payable = gross - discount;

            var invoice = new Invoice
            {
                StudentId = s.StudentId,
                CategoryId = s.CategoryId,
                PeriodMonth = monthStart,
                Amount = gross,
                Discount = discount,
                DueOn = dueOn,
                // Status HECH QACHON qo'lda qo'yilmaydi — u taqsimotlardan
                // keltirib chiqariladi (yangi qatorda taqsimot 0 ta).
                Status = StatusFor(payable, 0m),
                CreatedAt = AppClock.NowInstant,
            };

            var memo = string.Create(CultureInfo.InvariantCulture,
                $"{monthStart:yyyy-MM} · {s.CategoryName} · {s.StudentName}");

            if (await InsertWithLedgerAsync(invoice, s.CategoryCode, memo, actorId, ct))
            {
                created++;
                total += payable;
            }
            else
            {
                skipped++;
            }
        }

        return new AccrualResultDto(monthStart, created, skipped, decimal.Round(total, MoneyScale));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AccrualResultDto>> AccrueDueAsync(
        string actorId, CancellationToken ct = default)
    {
        // Oy oralig'ini eski kodning O'ZI hisoblaydi — `TuitionService.MonthRange`
        // va `AcademicYearStartMonthAsync` qayta yozilmaydi (docs/TASKS.md §1.3).
        var current = TuitionService.CurrentMonth();
        var start = await TuitionService.AcademicYearStartMonthAsync(db);

        // Obuna o'quv yili boshidan OLDIN boshlangan bo'lsa (tarix ko'chirilgan
        // yoki chorak sanalari kech qo'yilgan) — o'sha oydan boshlaymiz, aks holda
        // o'sha oylar hech qachon hisoblanmay qolardi.
        var earliest = await db.StudentSubscriptions.AsNoTracking()
            .Select(s => (DateOnly?)s.StartsOn)
            .MinAsync(ct);
        if (earliest is { } first)
        {
            var firstMonth = first.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            if (string.CompareOrdinal(firstMonth, start) < 0) start = firstMonth;
        }

        // O'quv yili hali boshlanmagan bo'lsa — kelajakni hisoblab qo'ymaymiz.
        if (string.CompareOrdinal(start, current) > 0) start = current;

        var results = new List<AccrualResultDto>();
        foreach (var month in TuitionService.MonthRange(start, current))
            results.Add(await AccrueMonthAsync(ParseMonth(month), actorId, ct));

        return results;
    }

    /// <summary>
    /// Hisob-fakturani va uning jurnal juftini BITTA tranzaksiyada yozadi.
    /// Unikal indeks urilsa <c>false</c> (= "allaqachon bor") qaytaradi.
    ///
    /// <para>
    /// <b>Boshqa har qanday xato yuqoriga chiqadi va shu bilan hisoblash to'xtaydi
    /// — ataylab.</b> Tranzaksiya orqaga qaytganini EF konteksti "bilmaydi":
    /// xotirada hisob-faktura saqlangandek ko'rinib turaveradi. Shuning uchun
    /// xatodan keyin shu kontekstda ishni davom ettirish mumkin emas; fon xizmati
    /// har tsiklda YANGI scope (yangi kontekst) oladi va hisoblash idempotent
    /// bo'lgani uchun keyingi yurish yarim qolgan oyni oxiriga yetkazadi.
    /// </para>
    /// </summary>
    private async Task<bool> InsertWithLedgerAsync(
        Invoice invoice, string categoryCode, string memo, string actorId, CancellationToken ct)
    {
        var payable = invoice.Amount - invoice.Discount;

        // Tranzaksiya MAJBURIY: `LedgerService.PostAsync` o'z `SaveChanges` ini
        // chaqiradi, ya'ni tranzaksiyasiz bu ikki alohida commit bo'lardi va
        // orada jarayon o'lsa bazada JURNALSIZ hisob-faktura qolardi.
        await using var tx = await db.BeginTransactionAsync(ct);

        db.Invoices.Add(invoice);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // IDEMPOTENTLIK AYNAN SHU YERDA. Oy allaqachon hisoblangan (yoki
            // parallel jarayon bizdan oldin ulgurgan) — bu xato emas, kutilgan holat.
            await tx.RollbackAsync(ct);
            // `Added` holatidagi entity'ni kuzatuvdan chiqaramiz, aks holda u
            // keyingi `SaveChanges` da qayta urinib, butun tsiklni yiqitardi.
            db.Invoices.Remove(invoice);
            return false;
        }

        if (payable > 0m)
        {
            // Buxgalteriya sanasi — HISOBLANAYOTGAN OY, bugun emas: sentyabrda
            // to'ldirilgan iyul oyi daromadi iyulda turishi kerak, aks holda
            // oylik P&L qayta hisoblanganda boshqa raqam chiqadi.
            await ledger.PostAsync(
            [
                new LedgerPosting(Accounts.Receivable, LedgerDirection.Debit, payable,
                    LedgerRefType.Invoice, invoice.Id, invoice.PeriodMonth, memo),
                new LedgerPosting(Accounts.RevenueFor(categoryCode), LedgerDirection.Credit, payable,
                    LedgerRefType.Invoice, invoice.Id, invoice.PeriodMonth, memo),
            ], actorId, ct);
        }

        await tx.CommitAsync(ct);
        return true;
    }

    // =================================================================
    //  O'qish
    // =================================================================

    /// <inheritdoc />
    public async Task<IReadOnlyList<InvoiceDto>> ListAsync(
        InvoiceQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var settings = await SettingsAsync(ct);
        var q = db.Invoices.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.StudentId))
            q = q.Where(i => i.StudentId == query.StudentId);
        if (query.CategoryId is { } categoryId)
            q = q.Where(i => i.CategoryId == categoryId);
        if (query.FromMonth is { } fromMonth)
        {
            var from = FirstDayOf(fromMonth);
            q = q.Where(i => i.PeriodMonth >= from);
        }
        if (query.ToMonth is { } toMonth)
        {
            var to = FirstDayOf(toMonth);
            q = q.Where(i => i.PeriodMonth <= to);
        }
        if (!string.IsNullOrWhiteSpace(query.Status))
            q = q.Where(i => i.Status == query.Status);
        if (!string.IsNullOrWhiteSpace(query.ClassName))
            q = q.Where(i => db.Students.Any(s => s.Id == i.StudentId && s.ClassName == query.ClassName));

        if (query.OnlyOverdue)
        {
            // Qo'pol filtr BAZADA (indeks: status, due_on) — aniq javob to'langan
            // summa ma'lum bo'lgandan keyin, DTO darajasida beriladi.
            var today = AppClock.Today;
            q = q.Where(i => (i.Status == InvoiceStatus.Open || i.Status == InvoiceStatus.Partial)
                             && i.DueOn < today);
        }

        var invoices = await q
            .OrderByDescending(i => i.PeriodMonth)
            .ThenBy(i => i.StudentId)
            .ThenBy(i => i.Id)
            .Take(MaxListRows)
            .ToListAsync(ct);

        var dtos = await ToDtosAsync(invoices, settings, ct);
        return query.OnlyOverdue ? [.. dtos.Where(d => d.IsOverdue)] : dtos;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InvoiceDto>> OpenForStudentAsync(
        string studentId, CancellationToken ct = default)
    {
        var settings = await SettingsAsync(ct);

        var invoices = await db.Invoices.AsNoTracking()
            .Where(i => i.StudentId == studentId
                        && (i.Status == InvoiceStatus.Open || i.Status == InvoiceStatus.Partial))
            // Kassir ekrani: ENG ESKI qarz birinchi (FIFO taklifi ham shundan boshlanadi).
            .OrderBy(i => i.PeriodMonth)
            .ThenBy(i => i.CategoryId)
            .ToListAsync(ct);

        var dtos = await ToDtosAsync(invoices, settings, ct);
        return [.. dtos.Where(d => d.Remaining > 0m)];
    }

    /// <inheritdoc />
    public async Task<StudentBillingDto?> ForStudentAsync(
        string studentId, CancellationToken ct = default)
    {
        var student = await db.Students.AsNoTracking()
            .Where(s => s.Id == studentId)
            .Select(s => new { s.Id, s.FullName, s.ClassName })
            .FirstOrDefaultAsync(ct);
        if (student is null) return null;

        var settings = await SettingsAsync(ct);
        var today = AppClock.Today;

        // ---- Obunalar ----
        var subscriptions = await (
            from sub in db.StudentSubscriptions.AsNoTracking()
            join cat in db.FeeCategories.AsNoTracking() on sub.CategoryId equals cat.Id
            join u in db.Users.AsNoTracking() on sub.CreatedBy equals u.Id
            where sub.StudentId == studentId
            orderby sub.StartsOn descending
            select new StudentSubscriptionDto(
                sub.Id, sub.StudentId, student.FullName,
                cat.Id, cat.Code, cat.Name,
                sub.MonthlyAmount, sub.Detail,
                sub.StartsOn, sub.EndsOn,
                sub.StartsOn <= today && (sub.EndsOn == null || sub.EndsOn >= today),
                u.FullName, sub.CreatedAt)).ToListAsync(ct);

        // ---- Hisob-fakturalar ----
        var invoices = await db.Invoices.AsNoTracking()
            .Where(i => i.StudentId == studentId)
            .OrderByDescending(i => i.PeriodMonth)
            .ThenBy(i => i.CategoryId)
            .ToListAsync(ct);
        var invoiceDtos = await ToDtosAsync(invoices, settings, ct);

        // ---- To'lovlar ----
        var payments = await PaymentsForStudentAsync(studentId, student.FullName, ct);

        // Qarz = to'lanmagan qoldiqlar yig'indisi. Bekor qilingan (void) oy qarzga
        // KIRMAYDI. `students.balance` bu yerda umuman o'qilmaydi (P1-21).
        var debt = invoiceDtos
            .Where(i => i.Status != InvoiceStatus.Void)
            .Sum(i => Math.Max(0m, i.Remaining));

        // Avans = taqsimlanmagan pul (docs/ASSUMPTIONS.md, Q15: yillik oldindan
        // to'lov 12 ta kelajak hisob-fakturasi emas, taqsimlanmagan qoldiq).
        // Storno qatori ham, storno qilingan to'lov ham chiqarib tashlanadi —
        // AYNAN `PaidByInvoiceAsync` dagi qoida (bekor qilingan pul na qarzni
        // yopadi, na avans bo'lib qoladi).
        var credit = payments
            .Where(p => p.ReversalOf is null && p.ReversedBy is null)
            .Sum(p => p.Unallocated);

        return new StudentBillingDto(
            student.Id, student.FullName, student.ClassName,
            decimal.Round(debt, MoneyScale),
            decimal.Round(credit, MoneyScale),
            subscriptions, invoiceDtos, payments);
    }

    /// <inheritdoc />
    public async Task<InvoiceDto> VoidAsync(
        Guid invoiceId, string reason, string actorId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Bekor qilish sababi majburiy (SPEC §4.3).", nameof(reason));
        if (string.IsNullOrWhiteSpace(actorId))
            throw new ArgumentException("Bekor qiluvchi noma'lum (actorId bo'sh).", nameof(actorId));

        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new InvalidOperationException($"Hisob-faktura topilmadi: {invoiceId}.");

        if (invoice.Status == InvoiceStatus.Void)
            throw new InvalidOperationException("Bu hisob-faktura allaqachon bekor qilingan.");

        // To'lov tushgan oyni bekor qilib bo'lmaydi: pul qayerga ketganini
        // tushuntirib bo'lmay qolardi. Avval to'lovni storno qilish kerak.
        if (await db.PaymentAllocations.AnyAsync(a => a.InvoiceId == invoiceId, ct))
            throw new InvalidOperationException(
                "Taqsimoti bor hisob-fakturani bekor qilib bo'lmaydi — avval to'lovni storno qiling.");

        await using var tx = await db.BeginTransactionAsync(ct);

        // AVVAL JURNAL, keyin status — tartib muhim.
        //
        // Jurnalni qaytarmasak, qarz (receivable) bekor qilingan oy uchun osilib
        // qolardi. `ReverseAsync` esa ikki qavatli nazoratni tekshiradi (SPEC §4.5):
        // yozuvni qo'ygan odam uni o'zi teskari qila olmaydi, ya'ni hisoblashni
        // boshlagan admin o'sha oyni o'zi bekor qila olmaydi.
        //
        // Teskari tartibda (avval status, keyin jurnal) rad etilgan urinish
        // bazada orqaga qaytardi, lekin XOTIRADAGI entity "void" bo'lib qolardi
        // — EF tranzaksiya qaytganda kuzatilayotgan obyektni tiklamaydi. Shu
        // kontekstdagi keyingi (haqli) urinish "allaqachon bekor qilingan" deb
        // yiqilardi, bazada esa qator tirik turardi.
        var anchor = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.RefType == LedgerRefType.Invoice && e.RefId == invoiceId && e.ReversalOf == null)
            .OrderBy(e => e.Id)
            .FirstOrDefaultAsync(ct);
        if (anchor is not null)
            await ledger.ReverseAsync(anchor.Id, reason, actorId, ct);

        invoice.Status = InvoiceStatus.Void;
        await db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);

        var settings = await SettingsAsync(ct);
        var dtos = await ToDtosAsync([invoice], settings, ct);
        return dtos[0];
    }

    // =================================================================
    //  Umumiy hisob-kitob qoidalari
    // =================================================================

    /// <summary>
    /// Hisob-faktura holati — FAQAT taqsimotlar yig'indisidan keltirib chiqariladi
    /// (P1-09 qabul mezoni: holatni hech kim qo'lda qo'ymaydi). To'lov qabul qilgan
    /// kod (P1-11) ham AYNAN shuni chaqirsin, o'z <c>if</c> zanjirini yozmasin.
    ///
    /// <para>
    /// <c>void</c> bu yerdan CHIQMAYDI: u hisob-kitob natijasi emas, odam qabul
    /// qilgan qaror (<see cref="VoidAsync"/>) va terminal holat.
    /// </para>
    /// </summary>
    /// <param name="payable">To'lanishi kerak = amount − discount.</param>
    /// <param name="allocated">Shu hisob-fakturaga taqsimlangan summa.</param>
    public static string StatusFor(decimal payable, decimal allocated)
    {
        // To'lanadigan narsa yo'q (100% chegirma yoki bepul o'qish) — ochiq qarz
        // bo'lib osilib turmasligi kerak.
        if (payable <= 0m) return InvoiceStatus.Paid;
        if (allocated <= 0m) return InvoiceStatus.Open;
        return allocated >= payable ? InvoiceStatus.Paid : InvoiceStatus.Partial;
    }

    /// <summary>
    /// Hisob-faktura muddati o'tganmi. Chegara <c>overdue_after_day</c> sozlamasidan
    /// (mijoz javobi, SPEC §8.1 Q6), lekin HECH QACHON <c>due_on</c> dan oldin emas:
    /// sozlama keyin o'zgartirilsa, eski qatorlar birdan qarzdorga aylanib qolmasin.
    /// </summary>
    private static bool IsOverdue(Invoice invoice, decimal remaining, BillingSettings settings)
    {
        if (invoice.Status == InvoiceStatus.Void || remaining <= 0m) return false;
        var boundary = DayInMonth(invoice.PeriodMonth, settings.OverdueAfterDay);
        if (boundary < invoice.DueOn) boundary = invoice.DueOn;
        return AppClock.Today > boundary;
    }

    // =================================================================
    //  Ichki yordamchilar
    // =================================================================

    /// <summary>
    /// Sozlamalar BAZADAN o'qiladi — kodda 10/15 raqami yo'q (SPEC §8.1 Q6).
    /// Qator topilmasa (seed o'chirilgan bo'lsa) domendagi sukut qiymatlar ishlatiladi.
    /// </summary>
    private async Task<BillingSettings> SettingsAsync(CancellationToken ct) =>
        await db.BillingSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new BillingSettings();

    /// <summary>
    /// Hisob-fakturalarni DTO ga o'giradi. To'langan summa BITTA guruhlangan
    /// so'rov bilan olinadi — har qator uchun alohida so'rov (N+1) YO'Q.
    /// </summary>
    private async Task<List<InvoiceDto>> ToDtosAsync(
        List<Invoice> invoices, BillingSettings settings, CancellationToken ct)
    {
        if (invoices.Count == 0) return [];

        var studentIds = invoices.Select(i => i.StudentId).Distinct().ToList();
        var categoryIds = invoices.Select(i => i.CategoryId).Distinct().ToList();
        var invoiceIds = invoices.Select(i => i.Id).Distinct().ToList();

        var students = await db.Students.AsNoTracking()
            .Where(s => studentIds.Contains(s.Id))
            .Select(s => new { s.Id, s.FullName })
            .ToDictionaryAsync(s => s.Id, s => s.FullName, StringComparer.Ordinal, ct);

        var categories = await db.FeeCategories.AsNoTracking()
            .Where(c => categoryIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Code, c.Name })
            .ToDictionaryAsync(c => c.Id, c => (c.Code, c.Name), ct);

        var paid = await PaidByInvoiceAsync(invoiceIds, ct);

        var result = new List<InvoiceDto>(invoices.Count);
        foreach (var i in invoices)
        {
            var payable = i.Amount - i.Discount;
            var allocated = paid.GetValueOrDefault(i.Id);
            var remaining = payable - allocated;
            var category = categories.GetValueOrDefault(i.CategoryId);

            result.Add(new InvoiceDto(
                i.Id, i.StudentId, students.GetValueOrDefault(i.StudentId) ?? string.Empty,
                i.CategoryId, category.Code ?? string.Empty, category.Name ?? string.Empty,
                i.PeriodMonth, i.Amount, i.Discount,
                payable, allocated, remaining,
                i.DueOn, i.Status,
                IsOverdue(i, remaining, settings),
                i.CreatedAt));
        }

        return result;
    }

    /// <summary>
    /// Hisob-faktura bo'yicha HAQIQATAN to'langan summa.
    ///
    /// <para>
    /// Storno qatori ham, storno qilingan asl to'lov ham hisobga OLINMAYDI.
    /// Sabab: <c>payment_allocations.amount &gt; 0</c> (check constraint), ya'ni
    /// storno "manfiy taqsimot" yoza olmaydi va taqsimotni o'chirib ham bo'lmaydi
    /// (jadval o'zgarmas). Bekor qilingan to'lovni chiqarib tashlashning yagona
    /// to'g'ri joyi — shu so'rov.
    /// </para>
    /// </summary>
    private async Task<Dictionary<Guid, decimal>> PaidByInvoiceAsync(
        List<Guid> invoiceIds, CancellationToken ct)
    {
        var rows = await db.PaymentAllocations.AsNoTracking()
            .Where(a => invoiceIds.Contains(a.InvoiceId))
            .Join(db.Payments.AsNoTracking()
                    .Where(p => p.ReversalOf == null && !db.Payments.Any(r => r.ReversalOf == p.Id)),
                a => a.PaymentId, p => p.Id, (a, _) => a)
            .GroupBy(a => a.InvoiceId)
            .Select(g => new { InvoiceId = g.Key, Total = g.Sum(x => x.Amount) })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.InvoiceId, r => r.Total);
    }

    /// <summary>O'quvchining to'lovlari, taqsimoti bilan — uchta so'rov, N+1 siz.</summary>
    private async Task<List<PaymentDto>> PaymentsForStudentAsync(
        string studentId, string studentName, CancellationToken ct)
    {
        var payments = await db.Payments.AsNoTracking()
            .Where(p => p.StudentId == studentId)
            .OrderByDescending(p => p.ReceivedAt)
            .ToListAsync(ct);
        if (payments.Count == 0) return [];

        var paymentIds = payments.Select(p => p.Id).ToList();
        var cashierIds = payments.Select(p => p.CashierId).Distinct().ToList();

        var cashiers = await db.Users.AsNoTracking()
            .Where(u => cashierIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName })
            .ToDictionaryAsync(u => u.Id, u => u.FullName, StringComparer.Ordinal, ct);

        var allocations = await (
            from a in db.PaymentAllocations.AsNoTracking()
            join inv in db.Invoices.AsNoTracking() on a.InvoiceId equals inv.Id
            join cat in db.FeeCategories.AsNoTracking() on inv.CategoryId equals cat.Id
            where paymentIds.Contains(a.PaymentId)
            select new
            {
                a.PaymentId,
                Dto = new PaymentAllocationDto(a.Id, a.InvoiceId, cat.Id, cat.Code, cat.Name, inv.PeriodMonth, a.Amount),
            }).ToListAsync(ct);

        var byPayment = allocations
            .GroupBy(x => x.PaymentId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Dto).ToList());

        // "Bu to'lov keyin storno qilinganmi" — o'qilgan qatorlardan, har qator
        // uchun alohida so'rovsiz.
        var reversedBy = payments
            .Where(p => p.ReversalOf is not null)
            .ToDictionary(p => p.ReversalOf!.Value, p => p.Id);

        return [.. payments.Select(p =>
        {
            var lines = byPayment.GetValueOrDefault(p.Id) ?? [];
            var unallocated = p.Amount - lines.Sum(l => l.Amount);
            return new PaymentDto(
                p.Id, p.ReceiptNo, p.StudentId, studentName, p.Amount, p.Method,
                p.CashShiftId, p.CashierId, cashiers.GetValueOrDefault(p.CashierId) ?? string.Empty,
                // DIQQAT: `GetValueOrDefault` bu yerda YARAMAYDI — `Dictionary<Guid,Guid>`
                // topilmaganda `Guid.Empty` qaytaradi, `null` emas. Natijada har bir
                // to'lov "storno qilingan" bo'lib ko'rinardi va `ForStudentAsync` dagi
                // `ReversedBy is null` filtri hech qachon o'tmay, o'quvchi avansi (Credit)
                // doim 0 bo'lib qolardi. `PaymentService.ToDtosAsync` dagi bilan bir xil.
                p.Note, p.ReceivedAt, p.ReversalOf,
                reversedBy.TryGetValue(p.Id, out var storno) ? storno : null,
                decimal.Round(unallocated, MoneyScale), lines);
        })];
    }

    /// <summary>
    /// Postgres unikal indeks buzilishi (23505). Npgsql tipiga bog'lanmaydi —
    /// <c>DbException.SqlState</c> .NET 5 dan beri standart (Application qatlami
    /// ma'lumotlar bazasi drayveriga havola qilmaydi).
    /// </summary>
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is DbException { SqlState: "23505" };

    /// <summary>Oyning birinchi kuni — <c>period_month</c> ustuni AYNAN shunday saqlanadi
    /// (<c>ck_invoices_period_first_day</c>).</summary>
    private static DateOnly FirstDayOf(DateOnly date) => new(date.Year, date.Month, 1);

    /// <summary>Oydagi berilgan kun. Sozlama 1..28 oralig'ida (baza tekshiradi), lekin
    /// qiymat baribir oy uzunligiga siqiladi — 31-kun fevralda mavjud emas.</summary>
    private static DateOnly DayInMonth(DateOnly month, int day) =>
        new(month.Year, month.Month, Math.Clamp(day, 1, DateTime.DaysInMonth(month.Year, month.Month)));

    /// <summary>"yyyy-MM" (TuitionService oralig'i) → oyning birinchi kuni.</summary>
    private static DateOnly ParseMonth(string month) => new(
        int.Parse(month[..4], CultureInfo.InvariantCulture),
        int.Parse(month[5..], CultureInfo.InvariantCulture),
        1);
}

/// <summary>
/// Chegirma arifmetikasi — <b>butun tizimda YAGONA nusxa</b>.
///
/// <para>
/// Qoida eski <c>TuitionService.ChargeFor</c> dan AYNAN ko'chirilgan (qayta
/// yozilmagan): avval foiz, keyin aniq summa, quyi chegara 0, natija 2 kasrga
/// yaxlitlanadi. Yagona farq — foiz endi <c>decimal</c> (<c>discounts.percent</c>
/// ustuni <c>numeric(5,2)</c>, ya'ni 12.5% yozish mumkin), eski kodda esa
/// <c>int</c> edi. P1-23 ikkalasini bir xil kirishlarda solishtiradi.
/// </para>
///
/// <para>
/// <b>P1-08 ga eslatma:</b> <c>IDiscountService.ChargeFor</c> / <c>DiscountFor</c>
/// shu yerga delegat qilsin, o'z nusxasini yozmasin — pul formulasi ikki joyda
/// yashasa, ular bir kun ajralib ketadi va farqni faqat mijoz sezadi
/// (docs/PENDING_WIRING.md).
/// </para>
/// </summary>
public static class DiscountMath
{
    private const int MoneyScale = 2;

    /// <summary>Chegirmadan KEYINGI to'lanadigan summa.</summary>
    public static decimal ChargeFor(decimal grossAmount, decimal percent, decimal amount)
    {
        if (grossAmount <= 0m) return 0m;
        var pct = Math.Clamp(percent, 0m, 100m);
        var flat = Math.Max(0m, amount);
        var charge = grossAmount * (100m - pct) / 100m - flat;
        return charge < 0m ? 0m : decimal.Round(charge, MoneyScale);
    }

    /// <summary>Chegirma summasi = gross − <see cref="ChargeFor"/>. Hech qachon gross'dan
    /// oshmaydi (<c>ck_invoices_discount: discount &lt;= amount</c>).</summary>
    public static decimal DiscountFor(decimal grossAmount, decimal percent, decimal amount)
    {
        if (grossAmount <= 0m) return 0m;
        return decimal.Round(grossAmount - ChargeFor(grossAmount, percent, amount), MoneyScale);
    }
}

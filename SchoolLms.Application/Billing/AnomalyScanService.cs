using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  Tungi tekshiruv — SPEC §4.6. Vazifa: P1-14.
// ===========================================================================
//
//  SPEC §4.6 to'rtta shartni sanaydi va ularning HAMMASI shu faylda:
//    1. nomuvofiqlik bilan yopilgan smena      -> AnomalyKind.ShiftVariance
//    2. 24 soat ichidagi storno                -> AnomalyKind.FastReversal
//    3. ish vaqtidan tashqari to'lov           -> AnomalyKind.OffHoursPayment
//    4. taqsimotsiz "to'langan" hisob-faktura  -> AnomalyKind.PaidWithoutAllocation
//
//  IDEMPOTENTLIK — ENG MUHIM XOSSA
//  -------------------------------
//  Tekshiruv har tunda BIR XIL hodisalarni qayta ko'radi (90 kunlik oyna).
//  Agar u har yurishda yangi qator yozsa, direktor paneli bir hafta ichida
//  yuzlab bir xil bayroq ko'rsatardi va hisoblagich butunlay ma'nosiz bo'lardi.
//  Shuning uchun BITTA hodisa = BITTA qator, va buni ILOVA EMAS, BAZA
//  kafolatlaydi: `ux_finance_anomaly_flags_kind_ref` unikal indeksi
//  (kind, ref_id) ustida. Ilovadagi tekshiruv — faqat ortiqcha xatoni
//  oldini olish uchun.
//
//  NEGA ANIQ TRANZAKSIYA YO'Q
//  --------------------------
//  Butun yurish BITTA `SaveChanges` bilan tugaydi (u o'zi atomar). Aniq
//  tranzaksiya kerak emas va zararli ham: prod'dagi `AppDbContext`
//  `EnableRetryOnFailure` bilan sozlangan, u esa foydalanuvchi ochgan
//  tranzaksiyani qo'llab-quvvatlamaydi — fon xizmati birinchi tsikldayoq
//  yiqilardi, hech kim ko'rmaydigan joyda.

/// <inheritdoc cref="IAnomalyService" />
public sealed class AnomalyService(IAppDbContext db) : IAnomalyService
{
    /// <summary>Ro'yxat so'rovining yuqori chegarasi (panel baribir filtrlaydi).</summary>
    public const int MaxListRows = 500;

    /// <summary>Audit qatoridagi <c>actor_name</c> uchun — keshlangan.</summary>
    private readonly ActorNames actors = new(db);

    // =====================================================================
    //  Tekshiruv
    // =====================================================================

    /// <inheritdoc />
    public async Task<AnomalyScanResult> ScanAsync(
        DateTimeOffset? since = null, CancellationToken ct = default)
    {
        var now = AppClock.NowInstant;
        var windowStart = since ?? now - AnomalySettings.ScanLookback;

        var candidates = new List<FinanceAnomalyFlag>();
        candidates.AddRange(await ShiftVarianceAsync(windowStart, now, ct));
        candidates.AddRange(await FastReversalAsync(windowStart, now, ct));
        candidates.AddRange(await OffHoursPaymentAsync(windowStart, now, ct));
        candidates.AddRange(await PaidWithoutAllocationAsync(now, ct));

        var created = await InsertMissingAsync(candidates, ct);

        return new AnomalyScanResult(
            windowStart,
            created.Count,
            created.GroupBy(f => f.Kind).ToDictionary(g => g.Key, g => g.Count()));
    }

    /// <summary>
    /// 1-shart: nomuvofiqlik bilan yopilgan smena (SPEC §4.2, §4.6).
    ///
    /// <para>
    /// <c>variance</c> — bazada hisoblanadigan ustun
    /// (<c>counted_cash - expected_cash</c>), ya'ni bu yerda hech narsa qayta
    /// hisoblanmaydi: ilova nomuvofiqlikni YOZA olmaydi, demak uni "tuzatib"
    /// bayroqdan qochib ham bo'lmaydi.
    /// </para>
    /// </summary>
    private async Task<List<FinanceAnomalyFlag>> ShiftVarianceAsync(
        DateTimeOffset since, DateTimeOffset now, CancellationToken ct)
    {
        var rows = await db.CashShifts.AsNoTracking()
            .Where(s => s.Status == CashShiftStatus.Closed
                        && s.ClosedAt != null && s.ClosedAt >= since
                        && s.Variance != null && s.Variance != 0m)
            .Select(s => new
            {
                s.Id,
                s.CashierId,
                s.ClosedAt,
                s.OpeningFloat,
                s.ExpectedCash,
                s.CountedCash,
                s.Variance,
            })
            .ToListAsync(ct);

        if (rows.Count == 0) return [];

        var names = await NamesOfAsync(rows.Select(r => r.CashierId), ct);

        return [.. rows.Select(r =>
        {
            var variance = r.Variance ?? 0m;
            var label = variance < 0m ? "kamomad" : "ortiqcha";
            return new FinanceAnomalyFlag
            {
                Kind = AnomalyKind.ShiftVariance,
                RefType = AnomalyRefType.CashShift,
                RefId = r.Id,
                OccurredAt = r.ClosedAt ?? now,
                DetectedAt = now,
                Amount = variance,
                Summary =
                    $"Smena {label} bilan yopildi: kutilgan {AuditService.Money(r.ExpectedCash ?? 0m)}, "
                    + $"sanalgan {AuditService.Money(r.CountedCash ?? 0m)} — farq "
                    + $"{AuditService.Money(variance)} so'm. Kassir: {names.GetValueOrDefault(r.CashierId, "Noma'lum")}.",
                Details = AuditService.Json(new
                {
                    CashShiftId = r.Id,
                    r.CashierId,
                    r.OpeningFloat,
                    r.ExpectedCash,
                    r.CountedCash,
                    Variance = variance,
                }),
            };
        })];
    }

    /// <summary>
    /// 2-shart: storno original to'lovdan keyin 24 soat ichida (SPEC §4.6).
    ///
    /// <para>
    /// Vaqt farqi XOTIRADA hisoblanadi: <c>timestamptz</c> ayirmasini LINQ
    /// provayderi <c>interval</c> ga o'girishi kafolatlanmagan, storno qatorlari
    /// esa kamyob (oynada o'nlab) — ya'ni bu yerda "hamma qatorni tortib olish"
    /// muammosi yo'q. Filtr baribir SQL'da: faqat <c>reversal_of is not null</c>
    /// bo'lgan va oynaga tushgan qatorlar o'qiladi.
    /// </para>
    /// </summary>
    private async Task<List<FinanceAnomalyFlag>> FastReversalAsync(
        DateTimeOffset since, DateTimeOffset now, CancellationToken ct)
    {
        var pairs = await (
            from storno in db.Payments.AsNoTracking()
            where storno.ReversalOf != null && storno.ReceivedAt >= since
            join original in db.Payments.AsNoTracking() on storno.ReversalOf equals original.Id
            select new
            {
                StornoId = storno.Id,
                storno.Amount,
                StornoAt = storno.ReceivedAt,
                StornoBy = storno.CashierId,
                storno.StudentId,
                storno.Note,
                OriginalId = original.Id,
                OriginalAt = original.ReceivedAt,
                OriginalReceipt = original.ReceiptNo,
                OriginalCashier = original.CashierId,
            }).ToListAsync(ct);

        var fast = pairs
            .Where(p => p.StornoAt - p.OriginalAt < AnomalySettings.FastReversalWindow)
            .ToList();
        if (fast.Count == 0) return [];

        var names = await NamesOfAsync(
            fast.Select(p => p.StornoBy).Concat(fast.Select(p => p.OriginalCashier)), ct);

        return [.. fast.Select(p =>
        {
            var hours = (p.StornoAt - p.OriginalAt).TotalHours;
            return new FinanceAnomalyFlag
            {
                Kind = AnomalyKind.FastReversal,
                RefType = AnomalyRefType.Payment,
                RefId = p.StornoId,
                OccurredAt = p.StornoAt,
                DetectedAt = now,
                Amount = p.Amount,
                Summary =
                    $"Chek #{p.OriginalReceipt} ({AuditService.Money(p.Amount)} so'm) qabul qilinganidan "
                    + $"{hours:0.#} soat keyin storno qilindi. Qabul qilgan: "
                    + $"{names.GetValueOrDefault(p.OriginalCashier, "Noma'lum")}, storno qilgan: "
                    + $"{names.GetValueOrDefault(p.StornoBy, "Noma'lum")}.",
                Details = AuditService.Json(new
                {
                    StornoPaymentId = p.StornoId,
                    OriginalPaymentId = p.OriginalId,
                    p.OriginalReceipt,
                    OriginalAt = p.OriginalAt,
                    StornoAt = p.StornoAt,
                    HoursBetween = Math.Round(hours, 2),
                    p.Amount,
                    p.StudentId,
                    Reason = p.Note,
                }),
            };
        })];
    }

    /// <summary>
    /// 3-shart: kassirning odatdagi ish soatlaridan tashqarida qabul qilingan
    /// to'lov (SPEC §4.6).
    ///
    /// <para>
    /// <b>"Odatdagi soat" qayerdan olinadi.</b> Sxemada kassirning ish jadvali
    /// YO'Q, shuning uchun chegara <see cref="AnomalySettings"/> da:
    /// 08:00–20:00 (maktab mintaqasi). Bu ataylab keng oyna — maqsad "kech
    /// qolgan kassirni" emas, tunda yozilgan to'lovni topish.
    /// </para>
    /// <para>
    /// Soat XOTIRADA tekshiriladi: <c>received_at</c> — <c>timestamptz</c>, va
    /// "Toshkent kalendar soati" ni LINQ provayderi xom <c>AT TIME ZONE</c>
    /// siz ifodalay olmaydi (bir xil sabab <c>CashShiftService</c> da ham
    /// yozilgan). SQL faqat oynani kesadi va beshta ustunni o'qiydi.
    /// </para>
    /// <para>
    /// Storno qatorlari ham kiradi: tunda yozilgan storno ham xuddi shunday
    /// tushuntirishga muhtoj. Ikkita bayroq (tez storno + tungi to'lov) chiqsa —
    /// bu takror emas, ikki xil savol.
    /// </para>
    /// </summary>
    private async Task<List<FinanceAnomalyFlag>> OffHoursPaymentAsync(
        DateTimeOffset since, DateTimeOffset now, CancellationToken ct)
    {
        var rows = await db.Payments.AsNoTracking()
            .Where(p => p.ReceivedAt >= since)
            .Select(p => new
            {
                p.Id,
                p.ReceiptNo,
                p.Amount,
                p.Method,
                p.CashierId,
                p.StudentId,
                p.ReceivedAt,
                p.ReversalOf,
            })
            .ToListAsync(ct);

        var offHours = rows.Where(p => AnomalySettings.IsOutsideWorkHours(p.ReceivedAt)).ToList();
        if (offHours.Count == 0) return [];

        var names = await NamesOfAsync(offHours.Select(p => p.CashierId), ct);

        return [.. offHours.Select(p =>
        {
            var local = AppClock.ToLocal(p.ReceivedAt);
            return new FinanceAnomalyFlag
            {
                Kind = AnomalyKind.OffHoursPayment,
                RefType = AnomalyRefType.Payment,
                RefId = p.Id,
                OccurredAt = p.ReceivedAt,
                DetectedAt = now,
                Amount = p.Amount,
                Summary =
                    $"Chek #{p.ReceiptNo} ish vaqtidan tashqarida yozilgan: {local:yyyy-MM-dd HH:mm} "
                    + $"(odatdagi oyna {AnomalySettings.WorkDayStart:HH\\:mm}–{AnomalySettings.WorkDayEnd:HH\\:mm}). "
                    + $"{AuditService.Money(p.Amount)} so'm, kassir: "
                    + $"{names.GetValueOrDefault(p.CashierId, "Noma'lum")}.",
                Details = AuditService.Json(new
                {
                    PaymentId = p.Id,
                    p.ReceiptNo,
                    p.Amount,
                    p.Method,
                    p.CashierId,
                    p.StudentId,
                    ReceivedAtLocal = local.ToString("yyyy-MM-ddTHH:mm:ss"),
                    IsReversal = p.ReversalOf != null,
                }),
            };
        })];
    }

    /// <summary>
    /// 4-shart: hisob-faktura <c>paid</c>, lekin unga HAQIQIY taqsimot yo'q
    /// (SPEC §4.6).
    ///
    /// <para>
    /// <b>"Haqiqiy taqsimot"</b> — <c>PaymentService.EffectiveAllocations</c>
    /// dagi AYNAN o'sha ta'rif: storno qatorining o'zi hech narsa taqsimlamaydi
    /// va storno qilingan to'lovning taqsimotlari hisobga olinmaydi. Boshqacha
    /// ta'rif ishlatilsa, tekshiruv storno qilingan pulni "to'langan" deb
    /// ko'rib, aynan topishi kerak bo'lgan holatni o'tkazib yuborardi.
    /// </para>
    /// <para>
    /// <b>To'lanadigan summasi 0 bo'lgan hisob-faktura CHIQARIB TASHLANADI.</b>
    /// 100% chegirma yoki bepul o'qishda <c>InvoiceService.StatusFor</c> uni
    /// ATAYLAB <c>paid</c> qiladi (aks holda qarz bo'lib osilib turardi) —
    /// bu qonuniy holat, bayroq emas.
    /// </para>
    /// <para>
    /// <b>Bu shartda 90 kunlik oyna YO'Q.</b> Qolgan uchtasi HODISA
    /// (smena yopildi, to'lov yozildi), bu esa HOLAT: eski hisob-faktura
    /// bugun ham noto'g'ri holatga tushishi mumkin. So'rov esa arzon —
    /// tanlov juda tor (`status = 'paid'` + anti-join), natija esa amalda nol
    /// qator.
    /// </para>
    /// </summary>
    private async Task<List<FinanceAnomalyFlag>> PaidWithoutAllocationAsync(
        DateTimeOffset now, CancellationToken ct)
    {
        var effective =
            from a in db.PaymentAllocations
            join p in db.Payments on a.PaymentId equals p.Id
            where p.ReversalOf == null && !db.Payments.Any(r => r.ReversalOf == p.Id)
            select a;

        var rows = await db.Invoices.AsNoTracking()
            .Where(i => i.Status == InvoiceStatus.Paid
                        && i.Amount - i.Discount > 0m
                        && !effective.Any(a => a.InvoiceId == i.Id))
            .Select(i => new
            {
                i.Id,
                i.StudentId,
                i.CategoryId,
                i.PeriodMonth,
                i.Amount,
                i.Discount,
                i.CreatedAt,
            })
            .ToListAsync(ct);

        if (rows.Count == 0) return [];

        return [.. rows.Select(i => new FinanceAnomalyFlag
        {
            Kind = AnomalyKind.PaidWithoutAllocation,
            RefType = AnomalyRefType.Invoice,
            RefId = i.Id,
            // HODISA emas, HOLAT: qachon boshlangani noma'lum, shuning uchun
            // topilgan lahza yoziladi (hisob-faktura yaratilgan sana bu yerda
            // chalg'itardi — muammo ancha keyin paydo bo'lgan bo'lishi mumkin).
            OccurredAt = now,
            DetectedAt = now,
            Amount = i.Amount - i.Discount,
            Summary =
                $"Hisob-faktura \"to'langan\" deb belgilangan, lekin unga birorta to'lov "
                + $"taqsimlanmagan: {i.PeriodMonth:yyyy-MM}, "
                + $"{AuditService.Money(i.Amount - i.Discount)} so'm.",
            Details = AuditService.Json(new
            {
                InvoiceId = i.Id,
                i.StudentId,
                i.CategoryId,
                i.PeriodMonth,
                i.Amount,
                i.Discount,
                Payable = i.Amount - i.Discount,
                i.CreatedAt,
            }),
        })];
    }

    /// <summary>
    /// Faqat HALI YO'Q bayroqlarni yozadi. Takrorlanish ikki qavatda
    /// to'xtatiladi: bu yerdagi to'plam va bazadagi unikal indeks.
    /// </summary>
    private async Task<List<FinanceAnomalyFlag>> InsertMissingAsync(
        List<FinanceAnomalyFlag> candidates, CancellationToken ct)
    {
        if (candidates.Count == 0) return [];

        var fresh = await FilterExistingAsync(candidates, ct);
        if (fresh.Count == 0) return [];

        db.FinanceAnomalyFlags.AddRange(fresh);
        try
        {
            await db.SaveChangesAsync(ct);
            return fresh;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Ikkita tekshiruv bir vaqtda yurgan (odatda: direktor "qayta
            // tekshirish" tugmasini ikki marta bosgan). Bitta `SaveChanges`
            // atomar, ya'ni hech narsa yozilmadi — kuzatuvni tozalab, bazadagi
            // YANGI holat bo'yicha bir marta qayta urinamiz. Ikkinchi urinish
            // ham yiqilsa — bu boshqa xato, uni yashirmaymiz.
            foreach (var flag in fresh) db.FinanceAnomalyFlags.Entry(flag).State = EntityState.Detached;

            var retry = await FilterExistingAsync(candidates, ct);
            if (retry.Count == 0) return [];

            db.FinanceAnomalyFlags.AddRange(retry);
            await db.SaveChangesAsync(ct);
            return retry;
        }
    }

    /// <summary>Bazada allaqachon bor <c>(kind, ref_id)</c> juftlarini chiqarib tashlaydi.</summary>
    private async Task<List<FinanceAnomalyFlag>> FilterExistingAsync(
        List<FinanceAnomalyFlag> candidates, CancellationToken ct)
    {
        var refIds = candidates.Select(c => c.RefId).Distinct().ToList();

        var existing = await db.FinanceAnomalyFlags.AsNoTracking()
            .Where(f => refIds.Contains(f.RefId))
            .Select(f => new { f.Kind, f.RefId })
            .ToListAsync(ct);

        var seen = existing.Select(e => (e.Kind, e.RefId)).ToHashSet();
        // `Add` false qaytarsa — bu juft allaqachon bor (bazada yoki shu
        // ro'yxatning oldingi elementida). Ya'ni bitta o'tishda ikkala
        // dublikat turi ham kesiladi.
        return [.. candidates.Where(c => seen.Add((c.Kind, c.RefId)))];
    }

    // =====================================================================
    //  O'qish va yopish
    // =====================================================================

    /// <inheritdoc />
    public async Task<AnomalyFlagsDto> ListAsync(
        bool unresolved = false, string? kind = null, int limit = 200, CancellationToken ct = default)
    {
        if (kind is not null && !AnomalyKind.All.Contains(kind, StringComparer.Ordinal))
            throw BillingRuleException.Invalid(
                "invalid_kind",
                $"Noma'lum tur '{kind}'. Ruxsat etilganlar: {string.Join(", ", AnomalyKind.All)}.");

        // ---- Hisoblagichlar: BITTA guruhlangan so'rov, ro'yxatdan MUSTAQIL ----
        // Panel hisoblagichi ro'yxat uzunligidan hisoblanmasligi kerak
        // (ro'yxat filtrlangan va chegaralangan) — SPEC §4.6: hisoblagichni
        // bekor qilib bo'lmaydi, demak u har doim to'liq bo'lishi shart.
        var stats = await db.FinanceAnomalyFlags.AsNoTracking()
            .GroupBy(f => f.Kind)
            .Select(g => new
            {
                Kind = g.Key,
                Total = g.Count(),
                Unresolved = g.Count(f => f.ResolvedAt == null),
            })
            .ToListAsync(ct);

        var unresolvedAmount = await db.FinanceAnomalyFlags.AsNoTracking()
            .Where(f => f.ResolvedAt == null && f.Amount != null)
            .SumAsync(f => Math.Abs(f.Amount!.Value), ct);

        // Ro'yxatning shakli BARQAROR: to'rtta tur har doim bor, bo'sh bo'lsa
        // ham. UI shunda qatorlar sakrab-sakrab paydo bo'lmaydi.
        var byKind = AnomalyKind.All
            .Select(k =>
            {
                var row = stats.FirstOrDefault(s => s.Kind == k);
                return new AnomalyKindCountDto(k, AnomalyLabels.For(k), row?.Unresolved ?? 0, row?.Total ?? 0);
            })
            .ToList();

        // ---- Ro'yxatning o'zi ----
        var q = db.FinanceAnomalyFlags.AsNoTracking();
        if (unresolved) q = q.Where(f => f.ResolvedAt == null);
        if (kind is not null) q = q.Where(f => f.Kind == kind);

        var rows = await q
            .OrderByDescending(f => f.OccurredAt).ThenByDescending(f => f.DetectedAt)
            .Take(Math.Clamp(limit, 1, MaxListRows))
            .ToListAsync(ct);

        return new AnomalyFlagsDto(
            Unresolved: stats.Sum(s => s.Unresolved),
            Total: stats.Sum(s => s.Total),
            UnresolvedAmount: unresolvedAmount,
            ByKind: byKind,
            Items: await ToDtosAsync(rows, ct));
    }

    /// <inheritdoc />
    public async Task<AnomalyFlagDto> ResolveAsync(
        Guid flagId, string reason, string resolvedByUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(resolvedByUserId))
            throw new ArgumentException(
                "Bayroqni kim yopayotgani noma'lum (JWT claim'i bo'sh) — SPEC §4.4.",
                nameof(resolvedByUserId));

        // SPEC §4.6: "cannot be dismissed, only resolved with a written reason".
        // Bo'sh yoki faqat probeldan iborat sabab — 400. Baza ham shuni aytadi
        // (`ck_finance_anomaly_flags_resolution`), bu yerdagi tekshiruv 500
        // o'rniga tushunarli javob berish uchun.
        var clean = (reason ?? string.Empty).Trim();
        if (clean.Length == 0)
            throw BillingRuleException.Invalid(
                "reason_required",
                "Bayroqni yopish uchun yozma sabab majburiy (SPEC §4.6) — uni shunchaki "
                + "bekor qilib bo'lmaydi.");

        var flag = await db.FinanceAnomalyFlags.FirstOrDefaultAsync(f => f.Id == flagId, ct)
            ?? throw BillingRuleException.NotFound("flag_not_found", "Bayroq topilmadi.");

        if (flag.ResolvedAt is not null)
            throw BillingRuleException.Conflict(
                "flag_already_resolved",
                "Bu bayroq allaqachon yopilgan. Yopilgan bayroqni qayta ochib bo'lmaydi — "
                + "yangi holat bo'lsa, keyingi tekshiruv yangi bayroq beradi.");

        var before = Snapshot(flag);
        flag.ResolvedAt = AppClock.NowInstant;
        flag.ResolvedBy = resolvedByUserId;
        flag.ResolvedReason = clean;

        // DIQQAT: bu yerda FAQAT uchta xossa o'zgaradi, shuning uchun EF
        // yozadigan UPDATE ham faqat uchta ustunga tegadi. `app_rw` da AYNAN
        // o'sha uchta ustunga UPDATE berilgan (ustun darajasidagi GRANT,
        // `Migrations/Sql/anomaly_guards.sql`) — boshqa ustunni o'zgartirishga
        // urinish 42501 bilan yiqiladi. Ya'ni bayroqning matni yoki summasi
        // keyinchalik "tuzatib" qo'yilishi mumkin emas.
        db.AuditLogs.Add(AuditService.Entry(
            AuditService.EntityAnomalyFlag, flag.Id.ToString("D"), "resolve",
            $"Tekshiruv bayrog'i yopildi ({AnomalyLabels.For(flag.Kind)}): {clean}",
            actorId: resolvedByUserId,
            actorName: await actors.OfAsync(resolvedByUserId, ct),
            before: before,
            after: Snapshot(flag)));

        await db.SaveChangesAsync(ct);

        return (await ToDtosAsync([flag], ct))[0];
    }

    // =====================================================================
    //  Ichki yordamchilar
    // =====================================================================

    /// <summary>
    /// Qator soni qanday bo'lsin — BITTA qo'shimcha so'rov (ismlar). Moliya
    /// entity'larida navigatsiya xossalari yo'q (BillingModel.cs), shuning
    /// uchun partiyalab o'qish — N+1 ning oldini olish yo'li.
    /// </summary>
    private async Task<List<AnomalyFlagDto>> ToDtosAsync(
        IReadOnlyList<FinanceAnomalyFlag> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];

        var names = await NamesOfAsync(rows.Select(f => f.ResolvedBy).Where(id => id is not null)!, ct);

        return [.. rows.Select(f => new AnomalyFlagDto(
            f.Id, f.Kind, AnomalyLabels.For(f.Kind), f.RefType, f.RefId,
            f.OccurredAt, f.DetectedAt, f.Amount, f.Summary, f.Details,
            f.ResolvedAt, f.ResolvedBy,
            f.ResolvedBy is null ? null : names.GetValueOrDefault(f.ResolvedBy, "Noma'lum"),
            f.ResolvedReason))];
    }

    /// <summary>users.id → to'liq ism, bitta so'rovda.</summary>
    private async Task<Dictionary<string, string>> NamesOfAsync(
        IEnumerable<string> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0) return [];

        return await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName })
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
    }

    /// <summary>Audit uchun snapshot (<c>before</c>/<c>after</c>).</summary>
    private static object Snapshot(FinanceAnomalyFlag f) => new
    {
        f.Id,
        f.Kind,
        f.RefType,
        f.RefId,
        f.OccurredAt,
        f.Amount,
        f.Summary,
        f.ResolvedAt,
        f.ResolvedBy,
        f.ResolvedReason,
    };

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is DbException { SqlState: "23505" };
}

/// <summary>
/// Fon xizmati: tekshiruvni ishga tushirishda bir marta, keyin HAR TUNDA
/// <see cref="AnomalySettings.NightlyRunAt"/> da yurgizadi (SPEC §4.6 —
/// "nightly job"). Vazifa: P1-14.
///
/// <para>
/// Shakli ATAYLAB <see cref="BillingAccrualService"/> bilan bir xil: bir xil
/// scope olish tartibi, bir xil xato ushlash, bir xil jurnal uslubi. Farqi —
/// davr: hisoblash sutkada ikki marta yuradi, tekshiruv esa aniq soatda,
/// kassa yopilgandan keyin.
/// </para>
/// <para>
/// <b>Nega ishga tushirishda ham yuradi.</b> Aks holda yangi deploy'dan keyin
/// tizim ertalabki 03:00 gacha ko'r bo'lardi, va aynan deploy kunlari
/// nomuvofiqlik ko'proq bo'ladi. Tekshiruv idempotent, ya'ni ortiqcha yurish
/// zarar qilmaydi.
/// </para>
/// </summary>
public class AnomalyScanService(IServiceProvider services, ILogger<AnomalyScanService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // Xato butun xizmatni O'LDIRMASIN: keyingi tunda qayta urinadi.
                // Tekshiruv idempotent, shuning uchun qayta urinish xavfsiz.
                logger.LogError(ex, "Moliya tekshiruvi (anomaly scan) bajarilmadi");
            }

            try { await Task.Delay(UntilNextRun(), stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    /// <summary>
    /// Bitta yurish. Testdan va admin tugmasidan
    /// (<c>POST /api/admin/finance/flags/scan</c>) ham shu yo'l chaqiriladi —
    /// ikkita hisoblash yo'li bo'lmasligi uchun.
    /// </summary>
    public async Task<AnomalyScanResult> RunOnceAsync(CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var anomalies = scope.ServiceProvider.GetRequiredService<IAnomalyService>();

        var result = await anomalies.ScanAsync(ct: ct);

        if (result.Created == 0)
        {
            logger.LogDebug("Moliya tekshiruvi: yangi bayroq yo'q ({Since} dan beri).", result.Scanned);
            return result;
        }

        logger.LogWarning(
            "Moliya tekshiruvi: {Count} ta YANGI bayroq — {ByKind}.",
            result.Created,
            string.Join(", ", result.CreatedByKind.Select(p => $"{p.Key}: {p.Value}")));
        return result;
    }

    /// <summary>
    /// Keyingi tungi yurishgacha qolgan vaqt (maktab mintaqasi bo'yicha).
    /// <c>AppClock.Now</c> — Toshkent devor soati, ya'ni yozgi vaqt yoki
    /// serverning UTC'da turishi hisobni buzmaydi.
    /// </summary>
    private static TimeSpan UntilNextRun()
    {
        var now = AppClock.Now;
        var next = now.Date.Add(AnomalySettings.NightlyRunAt.ToTimeSpan());
        if (next <= now) next = next.AddDays(1);
        return next - now;
    }
}

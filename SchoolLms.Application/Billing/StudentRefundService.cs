using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  O'quvchiga pul qaytarish — F1.05 (docs/modules/finance-parity.md §2.1.3,
//  §3.1 A3, §4 — slice S3). Vazifa: gap F1.05.
// ===========================================================================
//
//  NIMA UCHUN KERAK
//  -----------------
//  Bugun faqat TO'LIQ storno bor, u esa taqsimlangan pulni ham qaytarib
//  yuboradi. O'quvchi (yoki uning oilasi) ketganda, faqat hali HECH QANDAY
//  hisob-fakturaga taqsimlanmagan AVANS qaytariladi — allaqachon "ishlatilgan"
//  pul (masalan, o'tgan oy o'qishi uchun) qaytarim emas.
//
//  IKKI QAVATLI NAZORAT — PUL SHU YERDAN CHIQADI (SPEC §4.5)
//  -----------------------------------------------------------
//  Admin SO'RAYDI (`RequestAsync`), direktor TASDIQLAYDI (`ApproveAsync`) —
//  ikkalasi turli shaxs, bazada ham (`ck_student_refunds_approver_differs`),
//  ilovada ham. Tasdiqlanmaguncha `student_refunds` qatori hech qanday pul
//  harakati EMAS: jurnalga faqat `ApproveAsync` yozadi, xuddi `ExpenseService`
//  dagi "pending → posted" bosqichi kabi.
//
//  QATOR — QAYTARILMAYDI, YANGI QATOR — TUZATADI (SPEC §4.1)
//  ------------------------------------------------------------
//  `student_refunds` FAQAT INSERT (`app_rw` da UPDATE jadval darajasida yo'q);
//  yagona ruxsat etilgan UPDATE — to'rtta "qaror" ustuni, va ular ham FAQAT
//  BIR MARTA (`student_refunds_locked` trigger). Xato qaytarimni "yopish" —
//  YANGI qator: `RequestReversalAsync` asl qaytarimga ishora qiluvchi
//  (`ReversalOf`) yangi, PENDING qator yaratadi, u o'z navbatida
//  `ApproveAsync` orqali TASDIQLANADI — ya'ni storno ham xuddi shu ikki
//  qavatli yo'ldan o'tadi (baza `reversal_of` ustunida shartli unikal indeks
//  bilan "bir marta" ni kafolatlaydi). Tasdiqlanganda storno qatorining o'zi
//  yangi jurnal partiyasi YOZMAYDI — u asl qaytarimning partiyasini
//  `ILedgerService.ReverseAsync` bilan teskari qiladi (xuddi `ExpenseService`
//  dagi kabi): ikkita "haqiqat" (storno qatori va jurnal) bir-biridan
//  uzoqlashmasin.
//
//  NAQD QAYTARIM — SMENADAN CHIQADI (F1.03/F1.04 bilan bir xil qoida)
//  ---------------------------------------------------------------------
//  Naqd qaytarim TASDIQLOVCHINING ochiq smenasidan chiqadi (usulni ham,
//  pulni ham AYNAN tasdiqlovchi beradi) — ochiq smenasiz 409 `no_open_shift`.
//  `student_refunds.cash_shift_id` shu smenaga yoziladi va
//  `CashShiftService.CashOutflowAsync` uni `cash_handovers` bilan bir xil
//  sodda usulda o'qiydi (ustun to'g'ridan-to'g'ri smenaga ishora qiladi,
//  jurnal `created_by` + vaqt oralig'ini "taxmin qilish" shart emas).
//
//  IKKI MARTA TASDIQLASHDAN HIMOYA
//  --------------------------------
//  `ApproveAsync` chiqim tasdig'idagi bilan AYNAN bir xil naqsh:
//  `pg_advisory_xact_lock` qaytarim id'si bo'yicha, ochiq tranzaksiya ichida.
//  Ikki BIR VAQTDAGI tasdiq bitta navbatga tushadi — birinchisi o'tadi,
//  qolganlari qulf ostida "qaror allaqachon bor" deb ko'radi va 409
//  `already_decided` bilan qaytadi. Bitta jurnal partiyasi — hech qachon
//  ikkitasi. Muvofiqlik testi: `StudentRefundConcurrencyTests`.
//
//  AVANS — IKKI MARTA TEKSHIRILADI
//  ---------------------------------
//  So'rov vaqtida (tezkor javob uchun) va TASDIQ vaqtida (haqiqiy pul
//  chiqishidan oldin, `pg_advisory_xact_lock` o'quvchi id'si bo'yicha) — ikki
//  turli qaytarim so'rovi bitta o'quvchining avansini BIRGALIKDA ortiqcha
//  sarflab qo'ymasligi uchun (SPEC §4: "check-then-write on money takes
//  pg_advisory_xact_lock in the same transaction").

/// <summary>Qaytarimning HISOBLANGAN holati — ustun sifatida saqlanmaydi.</summary>
public static class StudentRefundStatus
{
    /// <summary>So'ralgan, hali qaror yo'q.</summary>
    public const string Pending = "pending";

    /// <summary>Tasdiqlangan va jurnalga tushgan — hali storno qilinmagan.</summary>
    public const string Approved = "approved";

    /// <summary>Rad etilgan — pul harakati bo'lmadi.</summary>
    public const string Rejected = "rejected";

    /// <summary>Bu QATORNING O'ZI — boshqa qaytarimning tasdiqlangan stornosi.</summary>
    public const string Reversal = "reversal";

    /// <summary>Tasdiqlangan edi, lekin keyinchalik STORNO qilindi.</summary>
    public const string Reversed = "reversed";

    public static readonly IReadOnlyList<string> All =
        [Pending, Approved, Rejected, Reversal, Reversed];
}

/// <summary>
/// O'quvchiga pul qaytarish — o'qish uchun. Pul bilan bog'liq hamma qiymat
/// SERVERDA hisoblanadi.
/// </summary>
/// <param name="CashShiftId">Naqd qaytarim qaysi smenadan chiqdi/qaytdi. Naqd
/// bo'lmasa yoki hali tasdiqlanmagan bo'lsa — <c>null</c>.</param>
/// <param name="ReversalOf">Storno bo'lsa — qaysi qaytarimni bekor qilyapti.</param>
/// <param name="Reversed">Shu (oddiy) qaytarim uchun TASDIQLANGAN storno
/// so'rovi bormi.</param>
/// <param name="Status"><see cref="StudentRefundStatus"/>.</param>
public record StudentRefundDto(
    Guid Id,
    string StudentId,
    string StudentName,
    decimal Amount,
    string Method,
    string Reason,
    string RequestedBy,
    string RequestedByName,
    DateTimeOffset RequestedAt,
    string? ApprovedBy,
    string? ApprovedByName,
    DateTimeOffset? ApprovedAt,
    Guid? CashShiftId,
    string? RejectedReason,
    Guid? ReversalOf,
    bool Reversed,
    string Status);

/// <summary>
/// Yangi qaytarim so'rovi. <c>requestedBy</c> tanada YO'Q — u JWT'dan
/// olinadi (SPEC §4.4).
/// </summary>
public record RequestStudentRefundRequest(string StudentId, decimal Amount, string Method, string Reason);

/// <summary>Asl (tasdiqlangan) qaytarimni bekor qilish so'rovi. Sabab majburiy.</summary>
public record RequestRefundReversalRequest(string Reason);

/// <summary>Qaytarim so'rovini rad etish. Sabab majburiy.</summary>
public record RejectStudentRefundRequest(string Reason);

/// <summary>Ro'yxat filtri.</summary>
public record StudentRefundQuery(string? StudentId = null, string? Status = null);

/// <summary>
/// O'quvchiga pul qaytarish (F1.05). Batafsil: fayl boshidagi izoh.
///
/// <para>
/// Bu interfeysda <c>Update</c> ham, <c>Delete</c> ham YO'Q va bo'lmaydi
/// (SPEC §4.1). Xato qaytarim <see cref="RequestReversalAsync"/> +
/// <see cref="ApproveAsync"/> bilan tuzatiladi.
/// </para>
/// </summary>
public interface IStudentRefundService
{
    /// <summary>Yangi qaytarim so'rovi — har doim <c>pending</c> holatda tug'iladi.</summary>
    Task<StudentRefundDto> RequestAsync(
        RequestStudentRefundRequest request, string actorId, CancellationToken ct = default);

    /// <summary>
    /// Allaqachon TASDIQLANGAN qaytarimni bekor qilishni so'raydi — yangi,
    /// <c>pending</c> qator (<see cref="StudentRefundDto.ReversalOf"/> bilan).
    /// </summary>
    Task<StudentRefundDto> RequestReversalAsync(
        Guid originalRefundId, string reason, string actorId, CancellationToken ct = default);

    /// <summary>
    /// Tasdiqlaydi va AYNAN shu lahzada jurnalga qo'yadi (oddiy qaytarim)
    /// yoki asl qaytarimning partiyasini teskari qiladi (storno so'rovi).
    /// So'ragan shaxsdan BOSHQA kishi bo'lishi SHART (SPEC §4.5). Bitta
    /// qaytarim bo'yicha tasdiqlar KETMA-KET bajariladi (advisory lock).
    /// </summary>
    Task<StudentRefundDto> ApproveAsync(Guid id, string actorId, CancellationToken ct = default);

    /// <summary>Qaytarim (yoki storno) so'rovini rad etadi — pul harakati bo'lmaydi.</summary>
    Task<StudentRefundDto> RejectAsync(
        Guid id, string reason, string actorId, CancellationToken ct = default);

    /// <summary>Bitta qaytarim; topilmasa <c>null</c>.</summary>
    Task<StudentRefundDto?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Ro'yxat (yangisidan eskisiga).</summary>
    Task<IReadOnlyList<StudentRefundDto>> ListAsync(
        StudentRefundQuery query, CancellationToken ct = default);

    /// <summary>
    /// O'quvchining JORIY avansi (qaytarimlar ayirilgan) — so'rov formasida
    /// "eng ko'pi shuncha" chegarasini ko'rsatish uchun.
    /// </summary>
    Task<decimal> AdvanceAsync(string studentId, CancellationToken ct = default);
}

/// <inheritdoc cref="IStudentRefundService"/>
public sealed class StudentRefundService(
    IAppDbContext db, ILedgerService ledger, ICashShiftService shifts) : IStudentRefundService
{
    /// <summary>Baza ustuni <c>numeric(14,2)</c> — arifmetika ham shu aniqlikda.</summary>
    private const int MoneyScale = 2;

    /// <summary>Ro'yxatning yuqori chegarasi — qolgani P&amp;L/hisobot so'rovlariga tushadi.</summary>
    private const int MaxRows = 2000;

    /// <summary><c>audit_log.entity_type</c> — qaytarim yozuvlari shu bo'yicha topiladi.</summary>
    public const string AuditEntityStudentRefund = AuditService.EntityStudentRefund;

    /// <summary>
    /// <b>QAYTARIM BO'YICHA TASDIQ QULFI.</b> Kalit
    /// <c>ExpenseService</c>/<c>CashShiftService</c> dagi bilan bir xil
    /// naqshda: nomlangan satr (<c>refund_approval:{id}</c>), advisory
    /// lock'ning 64-bitli fazosi butun bazada YAGONA bo'lgani uchun.
    /// </summary>
    private const string LockSql = "SELECT pg_advisory_xact_lock(hashtextextended({0}::text, 0))";

    private static string ApprovalLockKey(Guid refundId) => $"refund_approval:{refundId:D}";

    /// <summary>
    /// O'QUVCHI bo'yicha qulf — "so'ralgan summa ≤ joriy avans" tekshiruvi
    /// TASDIQ paytida shu qulf OSTIDA qaytadan bajariladi. Busiz ikki turli
    /// qaytarim (bir xil o'quvchiga) BIR VAQTDA tasdiqlansa, ikkalasi ham
    /// alohida-alohida "avans yetarli" deb ko'rishi mumkin edi va birgalikda
    /// avansdan OShirib yuborardi (SPEC §4: check-then-write qulf talab qiladi).
    /// </summary>
    private static string StudentLockKey(string studentId) => $"refund_student_balance:{studentId}";

    private readonly ActorNames actors = new(db);

    /// <summary>
    /// Xom SQL uchun kontekstning o'zi — <see cref="IAppDbContext"/> da
    /// <c>Database</c> yo'q (ataylab tor interfeys), advisory lock esa EF
    /// LINQ bilan ifodalab bo'lmaydigan yagona narsa.
    /// </summary>
    private readonly DbContext ef = db as DbContext ?? throw new ArgumentException(
        $"{nameof(StudentRefundService)} EF kontekstini talab qiladi: tasdiq qulfi xom SQL "
        + "orqali qo'yiladi. Berilgan implementatsiya DbContext emas.", nameof(db));

    // -----------------------------------------------------------------
    //  So'rash
    // -----------------------------------------------------------------

    /// <inheritdoc />
    public async Task<StudentRefundDto> RequestAsync(
        RequestStudentRefundRequest request, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireActor(actorId);

        var studentId = Trim(request.StudentId)
            ?? throw BillingRuleException.Invalid("invalid_student", "O'quvchi ko'rsatilmagan.");
        if (!await db.Students.AsNoTracking().AnyAsync(s => s.Id == studentId, ct))
            throw BillingRuleException.NotFound("student_not_found", "O'quvchi topilmadi.");

        var amount = Money(request.Amount);
        var method = RequireMethod(request.Method);
        var reason = Trim(request.Reason)
            ?? throw BillingRuleException.Invalid("reason_required", "Sabab majburiy.");

        // Tezkor tekshiruv — ODATIY holatda tushunarli xabar berish uchun.
        // HAQIQIY kafolat esa `ApproveAsync` da, o'quvchi qulfi ostida:
        // so'rov va tasdiq orasida boshqa to'lov/qaytarim bo'lishi mumkin.
        var advance = await new StudentBalanceQuery(db).AdvanceForAsync(studentId, ct);
        if (amount > advance)
            throw BillingRuleException.Invalid("insufficient_advance",
                $"So'ralgan summa ({AuditService.Money(amount)} so'm) o'quvchining joriy "
                + $"avansidan ({AuditService.Money(advance)} so'm) katta.");

        var refund = new StudentRefund
        {
            StudentId = studentId,
            Amount = amount,
            Method = method,
            Reason = reason,
            RequestedBy = actorId,
            RequestedAt = AppClock.NowInstant,
        };

        db.StudentRefunds.Add(refund);
        db.AuditLogs.Add(AuditService.Entry(
            AuditEntityStudentRefund, refund.Id.ToString("D"), "request",
            $"O'quvchiga pul qaytarish so'raldi: {AuditService.Money(amount)} so'm ({method}). "
            + $"Sabab: {reason}",
            actorId: actorId,
            actorName: await actors.OfAsync(actorId, ct),
            after: Snapshot(refund),
            studentId: studentId));

        await db.SaveChangesAsync(ct);

        return (await ToDtosAsync([refund], ct))[0];
    }

    /// <inheritdoc />
    public async Task<StudentRefundDto> RequestReversalAsync(
        Guid originalRefundId, string reason, string actorId, CancellationToken ct = default)
    {
        RequireActor(actorId);
        var cleanReason = Trim(reason)
            ?? throw BillingRuleException.Invalid("reason_required",
                "Storno so'rovining sababi majburiy — u jurnal yozuvida qoladi.");

        var original = await db.StudentRefunds.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == originalRefundId, ct)
            ?? throw BillingRuleException.NotFound("refund_not_found", "Qaytarim topilmadi.");

        if (original.ReversalOf is not null)
            throw BillingRuleException.Conflict("already_reversal",
                "Bu qatorning O'ZI boshqa qaytarimning stornosi — uni yana storno qilib bo'lmaydi.");

        if (original.ApprovedBy is null || original.RejectedReason is not null)
            throw BillingRuleException.Conflict("not_posted",
                "Bu qaytarim hali jurnalga tushmagan (tasdiqlanmagan yoki rad etilgan) — "
                + "storno qiladigan pul harakati yo'q.");

        // Baza `reversal_of` ustunida shartli UNIKAL indeks bilan "bitta
        // qaytarim — bittadan ortiq storno so'rovi bo'lmaydi" ni kafolatlaydi
        // (rad etilgan urinish ham hisobga kiradi); bu yerdagi tekshiruv
        // faqat sababni aniq aytish uchun.
        if (await db.StudentRefunds.AsNoTracking().AnyAsync(r => r.ReversalOf == originalRefundId, ct))
            throw BillingRuleException.Conflict("already_has_reversal_request",
                "Bu qaytarim uchun storno so'rovi allaqachon yuborilgan (tasdiqlangan, "
                + "kutilayotgan yoki rad etilgan).");

        var reversal = new StudentRefund
        {
            StudentId = original.StudentId,
            Amount = original.Amount,
            Method = original.Method,
            Reason = cleanReason,
            RequestedBy = actorId,
            RequestedAt = AppClock.NowInstant,
            ReversalOf = original.Id,
        };

        db.StudentRefunds.Add(reversal);
        db.AuditLogs.Add(AuditService.Entry(
            AuditEntityStudentRefund, reversal.Id.ToString("D"), "request-reversal",
            $"Qaytarim STORNOSI so'raldi ({original.Id:D}): {AuditService.Money(reversal.Amount)} so'm. "
            + $"Sabab: {cleanReason}",
            actorId: actorId,
            actorName: await actors.OfAsync(actorId, ct),
            after: Snapshot(reversal),
            studentId: reversal.StudentId));

        await db.SaveChangesAsync(ct);

        return (await ToDtosAsync([reversal], ct))[0];
    }

    // -----------------------------------------------------------------
    //  Tasdiqlash (SPEC §4.5) — oddiy qaytarim VA storno so'rovi bir yo'ldan
    // -----------------------------------------------------------------

    /// <inheritdoc />
    public async Task<StudentRefundDto> ApproveAsync(
        Guid id, string actorId, CancellationToken ct = default)
    {
        RequireActor(actorId);

        // ---- TASDIQ KETMA-KET BAJARILADI (advisory lock) ----
        // `ExpenseService.ApproveAsync` dagi bilan AYNAN bir xil sabab: ikki
        // bir vaqtdagi so'rov qulfsiz ikkalasi ham "qaror yo'q" deb ko'radi
        // (READ COMMITTED) va ikkalasi ham jurnalga partiya qo'yardi — pul
        // ikki marta chiqib ketardi. Muvofiqlik testi buni isbotlaydi.
        await using var tx = await db.BeginTransactionAsync(ct);

        await ef.Database.ExecuteSqlRawAsync(LockSql, [ApprovalLockKey(id)], ct);

        var refund = await db.StudentRefunds.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw BillingRuleException.NotFound("refund_not_found", "Qaytarim topilmadi.");

        if (refund.ApprovedBy is not null || refund.RejectedReason is not null)
            throw BillingRuleException.Conflict("already_decided",
                "Bu qaytarim bo'yicha qaror allaqachon qabul qilingan.");

        // SPEC §4.5 — bazada ham (`ck_student_refunds_approver_differs`);
        // bu yerda tekshirish sababni aniq aytish uchun.
        if (string.Equals(refund.RequestedBy, actorId, StringComparison.Ordinal))
            throw BillingRuleException.Forbidden("self_approval",
                "So'ragan shaxs o'zi tasdiqlay olmaydi (SPEC §4.5) — ikkinchi shaxs kerak.");

        // F1.03/F1.04 bilan bir xil qoida: naqd pul TASDIQLOVCHINING ochiq
        // smenasidan chiqadi/qaytadi. Smena qulfi shu tranzaksiyada olinadi
        // (qaytarim qulfi allaqachon olingan — ikkalasi har xil nomlangan
        // kalitlar, ya'ni to'qnashmaydi).
        Guid? cashShiftId = null;
        if (PaymentMethod.CountsAsCash(refund.Method))
        {
            var current = await shifts.CurrentAsync(actorId, ct) ?? throw NoOpenShift();

            await ef.Database.ExecuteSqlRawAsync(
                LockSql, [CashShiftService.ShiftLockKey(current.Id)], ct);

            var status = await db.CashShifts.AsNoTracking()
                .Where(s => s.Id == current.Id)
                .Select(s => s.Status)
                .FirstOrDefaultAsync(ct);

            if (!string.Equals(status, CashShiftStatus.Open, StringComparison.Ordinal))
                throw NoOpenShift();

            cashShiftId = current.Id;
        }

        var before = Snapshot(refund);

        if (refund.ReversalOf is null)
        {
            await ApprovePlainRefundAsync(refund, cashShiftId, actorId, before, ct);
        }
        else
        {
            await ApproveReversalAsync(refund, cashShiftId, actorId, before, ct);
        }

        await tx.CommitAsync(ct);

        return (await ToDtosAsync([refund], ct))[0];
    }

    /// <summary>
    /// Oddiy qaytarimni tasdiqlaydi: <c>debit receivable / credit cash|bank</c>
    /// (finance-parity §2.1, F1.05).
    /// </summary>
    private async Task ApprovePlainRefundAsync(
        StudentRefund refund, Guid? cashShiftId, string actorId, object before, CancellationToken ct)
    {
        // HAQIQIY kafolat: o'quvchi qulfi ostida avansni QAYTA tekshiramiz —
        // so'rov va tasdiq orasida boshqa to'lov yoki qaytarim bo'lishi
        // mumkin edi (fayl boshidagi izoh).
        await ef.Database.ExecuteSqlRawAsync(LockSql, [StudentLockKey(refund.StudentId)], ct);

        var advance = await new StudentBalanceQuery(db).AdvanceForAsync(refund.StudentId, ct);
        if (refund.Amount > advance)
            throw BillingRuleException.Conflict("insufficient_advance",
                $"So'ralgan summa ({AuditService.Money(refund.Amount)} so'm) o'quvchining joriy "
                + $"avansidan ({AuditService.Money(advance)} so'm) katta — tasdiqlash orasida "
                + "avans kamaygan.");

        refund.ApprovedBy = actorId;
        refund.ApprovedAt = AppClock.NowInstant;
        refund.CashShiftId = cashShiftId;

        db.AuditLogs.Add(AuditService.Entry(
            AuditEntityStudentRefund, refund.Id.ToString("D"), "approve",
            $"Qaytarim TASDIQLANDI va jurnalga tushdi: {AuditService.Money(refund.Amount)} so'm "
            + $"({Accounts.SettlementFor(refund.Method)}).",
            actorId: actorId,
            actorName: await actors.OfAsync(actorId, ct),
            before: before,
            after: Snapshot(refund),
            studentId: refund.StudentId));

        await db.SaveChangesAsync(ct);

        var memo = $"O'quvchiga pul qaytarish: {refund.Reason}";
        await ledger.PostAsync(
        [
            // Debet: debitorlik OSHADI — pul chiqdi, lekin bu hisob-faktura
            // yoki obuna emas, "hisob-kitobsiz chiqim" (finance-parity §2.1).
            new LedgerPosting(
                Accounts.Receivable, LedgerDirection.Debit, refund.Amount,
                LedgerRefType.Refund, refund.Id, AppClock.Today, memo),
            // Kredit: pul QAYERDAN chiqdi — naqd kassadan, qolgani bankdan.
            new LedgerPosting(
                Accounts.SettlementFor(refund.Method), LedgerDirection.Credit, refund.Amount,
                LedgerRefType.Refund, refund.Id, AppClock.Today, memo),
        ], actorId, ct);
    }

    /// <summary>
    /// Storno so'rovini tasdiqlaydi: bu qatorning O'ZI yangi jurnal partiyasi
    /// YOZMAYDI — u ASL qaytarimning partiyasini <see cref="ILedgerService.ReverseAsync"/>
    /// bilan teskari qiladi (xuddi <c>ExpenseService.ReverseAsync</c> kabi).
    /// </summary>
    private async Task ApproveReversalAsync(
        StudentRefund refund, Guid? cashShiftId, string actorId, object before, CancellationToken ct)
    {
        var anchor = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.RefType == LedgerRefType.Refund
                        && e.RefId == refund.ReversalOf
                        && e.ReversalOf == null)
            .OrderBy(e => e.Id)
            .Select(e => new { e.Id })
            .FirstOrDefaultAsync(ct)
            ?? throw BillingRuleException.Conflict("original_not_posted",
                "Asl qaytarim jurnalga tushmagan — storno qiladigan pul harakati yo'q.");

        refund.ApprovedBy = actorId;
        refund.ApprovedAt = AppClock.NowInstant;
        refund.CashShiftId = cashShiftId;

        db.AuditLogs.Add(AuditService.Entry(
            AuditEntityStudentRefund, refund.Id.ToString("D"), "approve-reversal",
            $"Qaytarim STORNOSI tasdiqlandi ({refund.ReversalOf:D}): "
            + $"{AuditService.Money(refund.Amount)} so'm. Sabab: {refund.Reason}",
            actorId: actorId,
            actorName: await actors.OfAsync(actorId, ct),
            before: before,
            after: Snapshot(refund),
            studentId: refund.StudentId));

        await db.SaveChangesAsync(ct);

        try
        {
            await ledger.ReverseAsync(anchor.Id, refund.Reason, actorId, ct);
        }
        // DIQQAT: `BillingRuleException` `InvalidOperationException` dan
        // meros oladi, shuning uchun u avval tutilib o'zgarishsiz
        // o'tkaziladi (`ExpenseService.ReverseAsync` dagi bilan bir xil naqsh).
        catch (BillingRuleException)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            throw BillingRuleException.Conflict("ledger_reversal_refused", ex.Message);
        }
    }

    // -----------------------------------------------------------------
    //  Rad etish
    // -----------------------------------------------------------------

    /// <inheritdoc />
    public async Task<StudentRefundDto> RejectAsync(
        Guid id, string reason, string actorId, CancellationToken ct = default)
    {
        RequireActor(actorId);
        var cleanReason = Trim(reason)
            ?? throw BillingRuleException.Invalid("reason_required", "Rad etish sababi majburiy.");

        // Rad etishda ham qaytarim qulfi olinadi: tasdiqlash bilan poyga
        // bo'lmasin — ikkalasi ham "qaror yo'q" holatini tekshirib yozadi.
        await using var tx = await db.BeginTransactionAsync(ct);

        await ef.Database.ExecuteSqlRawAsync(LockSql, [ApprovalLockKey(id)], ct);

        var refund = await db.StudentRefunds.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw BillingRuleException.NotFound("refund_not_found", "Qaytarim topilmadi.");

        if (refund.ApprovedBy is not null || refund.RejectedReason is not null)
            throw BillingRuleException.Conflict("already_decided",
                "Bu qaytarim bo'yicha qaror allaqachon qabul qilingan.");

        // DIQQAT: rad etish pul harakati EMAS (jurnalga hech narsa
        // yozilmaydi), shuning uchun "so'ragan ≠ rad etuvchi" talab
        // QILINMAYDI — tasdiqlashdagi kabi ikki qavatli nazorat kerak emas.
        // (Baza ham buni cheklamaydi: `ck_student_refunds_approver_differs`
        // faqat `approved_by` ni tekshiradi, `rejected_reason` ni emas.)
        var before = Snapshot(refund);
        refund.RejectedReason = cleanReason;

        db.AuditLogs.Add(AuditService.Entry(
            AuditEntityStudentRefund, refund.Id.ToString("D"), "reject",
            $"Qaytarim so'rovi RAD ETILDI: {AuditService.Money(refund.Amount)} so'm. "
            + $"Sabab: {cleanReason}",
            actorId: actorId,
            actorName: await actors.OfAsync(actorId, ct),
            before: before,
            after: Snapshot(refund),
            studentId: refund.StudentId));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return (await ToDtosAsync([refund], ct))[0];
    }

    // -----------------------------------------------------------------
    //  O'qish
    // -----------------------------------------------------------------

    /// <inheritdoc />
    public async Task<StudentRefundDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var refund = await db.StudentRefunds.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        return refund is null ? null : (await ToDtosAsync([refund], ct))[0];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StudentRefundDto>> ListAsync(
        StudentRefundQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var q = db.StudentRefunds.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.StudentId))
        {
            var studentId = query.StudentId.Trim();
            q = q.Where(r => r.StudentId == studentId);
        }

        var rows = await q
            .OrderByDescending(r => r.RequestedAt)
            .Take(MaxRows)
            .ToListAsync(ct);

        var dtos = await ToDtosAsync(rows, ct);

        // Holat — HISOBLANGAN maydon (jadval ustuni emas), shuning uchun
        // filtri DTO bosqichida: `RequireStatus` chiqim xizmatidagi bilan
        // bir xil naqsh.
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = RequireStatus(query.Status);
            dtos = [.. dtos.Where(d => d.Status == status)];
        }

        return dtos;
    }

    /// <inheritdoc />
    public Task<decimal> AdvanceAsync(string studentId, CancellationToken ct = default) =>
        new StudentBalanceQuery(db).AdvanceForAsync(studentId, ct);

    // -----------------------------------------------------------------
    //  Ichki yordamchilar
    // -----------------------------------------------------------------

    private async Task<List<StudentRefundDto>> ToDtosAsync(
        IReadOnlyList<StudentRefund> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];

        var ids = rows.Select(r => r.Id).ToList();
        var studentIds = rows.Select(r => r.StudentId).Distinct(StringComparer.Ordinal).ToList();
        var userIds = rows.Select(r => r.RequestedBy)
            .Concat(rows.Select(r => r.ApprovedBy).Where(x => x is not null).Select(x => x!))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var studentNames = await db.Students.AsNoTracking()
            .Where(s => studentIds.Contains(s.Id))
            .Select(s => new { s.Id, s.FullName })
            .ToDictionaryAsync(s => s.Id, s => s.FullName, ct);

        var userNames = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName })
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        // Shu qatorlardan (ASL sifatida) qaysilari uchun TASDIQLANGAN storno
        // so'rovi bor — bitta guruhlangan so'rov, sikl ichida emas.
        var reversedOriginals = await db.StudentRefunds.AsNoTracking()
            .Where(r => r.ReversalOf != null && ids.Contains(r.ReversalOf.Value) && r.ApprovedBy != null)
            .Select(r => r.ReversalOf!.Value)
            .ToListAsync(ct);

        return [.. rows.Select(r =>
        {
            var hasApprovedReversal = reversedOriginals.Contains(r.Id);
            return new StudentRefundDto(
                r.Id,
                r.StudentId,
                studentNames.GetValueOrDefault(r.StudentId, "—"),
                r.Amount,
                r.Method,
                r.Reason,
                r.RequestedBy,
                userNames.GetValueOrDefault(r.RequestedBy, "—"),
                r.RequestedAt,
                r.ApprovedBy,
                r.ApprovedBy is null ? null : userNames.GetValueOrDefault(r.ApprovedBy, "—"),
                r.ApprovedAt,
                r.CashShiftId,
                r.RejectedReason,
                r.ReversalOf,
                hasApprovedReversal,
                StatusOf(r, hasApprovedReversal));
        })];
    }

    private static string StatusOf(StudentRefund r, bool hasApprovedReversal)
    {
        if (r.RejectedReason is not null) return StudentRefundStatus.Rejected;
        if (r.ApprovedBy is null) return StudentRefundStatus.Pending;
        if (r.ReversalOf is not null) return StudentRefundStatus.Reversal;
        return hasApprovedReversal ? StudentRefundStatus.Reversed : StudentRefundStatus.Approved;
    }

    /// <summary>Audit uchun snapshot (<c>before</c>/<c>after</c>) — SPEC §4.6.</summary>
    private static object Snapshot(StudentRefund r) => new
    {
        r.Id,
        r.StudentId,
        r.Amount,
        r.Method,
        r.Reason,
        r.RequestedBy,
        r.ApprovedBy,
        r.CashShiftId,
        r.RejectedReason,
        r.ReversalOf,
    };

    /// <summary>
    /// SPEC §4.2 — naqd pul ochiq smenasiz javondan chiqmaydi/qaytmaydi.
    /// <c>ExpenseService</c>/<c>PaymentService</c>/<c>CashHandoverService</c>
    /// dagi kod bilan AYNAN bir xil kod (<c>no_open_shift</c>).
    /// </summary>
    private static BillingRuleException NoOpenShift() =>
        BillingRuleException.Conflict("no_open_shift",
            "Ochiq kassa smenasi yo'q. Naqd qaytarim smena ichida chiqadi/qaytadi — "
            + "avval smenani oching yoki naqd bo'lmagan usulni tanlang.");

    private static string RequireMethod(string? method) =>
        method is not null && PaymentMethod.All.Contains(method, StringComparer.Ordinal)
            ? method
            : throw BillingRuleException.Invalid("invalid_method",
                $"Noma'lum to'lov usuli: '{method}'. "
                + $"Ruxsat etilganlar: {string.Join(", ", PaymentMethod.All)}.");

    private static string RequireStatus(string? status) =>
        status is not null && StudentRefundStatus.All.Contains(status, StringComparer.Ordinal)
            ? status
            : throw BillingRuleException.Invalid("invalid_status",
                $"Noma'lum holat: '{status}'. "
                + $"Ruxsat etilganlar: {string.Join(", ", StudentRefundStatus.All)}.");

    /// <summary>
    /// Summa musbat va baza aniqligiga (2 kasr) MOS bo'lishi shart. Yaxlitlab
    /// yubormaymiz: 1000.005 kabi qiymat "qabul qilindi" deb ko'rinib,
    /// bazada boshqa raqam bo'lib qolardi.
    /// </summary>
    private static decimal Money(decimal value)
    {
        if (decimal.Round(value, MoneyScale) != value)
            throw BillingRuleException.Invalid("invalid_amount",
                $"Summa tiyin aniqligida (2 kasr) bo'lishi shart: {value}.");
        if (value <= 0m)
            throw BillingRuleException.Invalid("invalid_amount",
                "Qaytarim summasi musbat bo'lishi shart.");
        return value;
    }

    private static void RequireActor(string? actorId)
    {
        if (string.IsNullOrWhiteSpace(actorId))
            throw new ArgumentException(
                "Shaxs noma'lum. U JWT claim'idan olinadi — SPEC §4.4.", nameof(actorId));
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

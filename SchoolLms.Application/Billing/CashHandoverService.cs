using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  KASSADAN PUL TOPSHIRISH — F1.04 (docs/modules/finance-parity.md §2.1.3, §5 Q1).
// ===========================================================================
//
//  QANDAY SAVOLGA JAVOB BERADI
//  ---------------------------
//  "Kassadagi naqd pul qanday chiqadi?" Chiqim (F1.03) — bitta javob, lekin
//  yagonasi emas: pulning kattaroq qismi bankka topshiriladi yoki direktorning
//  seyfiga beriladi. Bu yozuv bo'lmasa `cash` hisobi FAQAT o'sadi va Kassa
//  kunining yopilish qoldig'i har kuni haqiqatdan uzoqlashadi — bir oydan
//  keyin "nomuvofiqlik" hisobotining o'zi ma'nosini yo'qotadi.
//
//  BU EDUSCHOOL'NING "KASSALAR ORASIDA KO'CHIRISH" I EMAS
//  ------------------------------------------------------
//  §2.0 da bir nechta kassa ham, ular orasidagi `waiting → accepted` ko'chirish
//  ham RAD ETILGAN (bitta maktab, bitta stol). Bu yerda `cashbox_id` yo'q va
//  bo'lmaydi: bizda ikkita hisob bor — `cash` va `bank`.
//
//  IKKI MANZIL, IKKI XIL JURNAL TA'SIRI
//  ------------------------------------
//    bank → `debit bank / credit cash`. Pul haqiqatan boshqa hisobga ko'chdi.
//    safe → JURNALGA HECH NARSA YOZILMAYDI. Direktorning seyfidagi pul ham
//           maktabniki va u ham `cash` hisobida turadi — `debit cash /
//           credit cash` esa nolga teng yozuv bo'lardi. O'zgaradigan narsa
//           bitta: shu SMENANING javonida qancha pul qolishi kerakligi.
//  Aynan shuning uchun `CashShiftService.CashOutflowAsync` topshiriqni
//  jurnaldan emas, `cash_handovers` JADVALIDAN o'qiydi — ikkala manzil ham
//  bitta qoidaga bo'ysunishi uchun.
//
//  FAQAT QO'SHILADI (SPEC §4.1)
//  ----------------------------
//  `app_rw` da bu jadvalga UPDATE ham, DELETE ham yo'q (finance-parity §3.1
//  A2). Xato topshiriq QARSHI QATOR bilan tuzatiladi: `reversal_of` originalga
//  ishora qiladi, `cash_shift_id` esa STORNO QILAYOTGAN odamning smenasini
//  ko'rsatadi — pul aynan uning javoniga qaytadi.
//
//  NEGA STORNO'DA "IKKINCHI SHAXS" TALAB QILINMAYDI
//  ------------------------------------------------
//  To'lov stornosi kassirga taqiqlangan (SPEC §4.3), chunki u OLINGAN pulni
//  "qaytarilgan" qilib ko'rsatishga yo'l ochardi. Topshiriq stornosi teskari
//  tomonga ishlaydi: u smenaning kutilgan naqdini OSHIRADI, ya'ni kassirdan
//  KO'PROQ pul talab qiladi. Uni o'z foydasiga ishlatib bo'lmaydi. Shuning
//  uchun bu yerda `ILedgerService.ReverseAsync` (unda ikki qavatli nazorat
//  qat'iy) o'rniga QARSHI PARTIYA qo'yiladi: `debit cash / credit bank`,
//  `ref_id` — storno QATORINING id'si. Jurnal balanslashgan bo'lib qoladi,
//  hech bir satr tahrirlanmaydi.
//
//  HAVFNING QOLGAN QISMI — OCHIQ AYTILGAN
//  --------------------------------------
//  Bo'lmagan topshiriqni yozish (ayniqsa `safe`) kutilgan naqdni PASAYTIRADI,
//  ya'ni kamomadni yashirish uchun ishlatilishi mumkin. Bunga qarshi bu yerda
//  ikkita qavat bor: har qator `audit_log` ga tushadi (SPEC §4.6) va
//  topshiriqlar registri admin/direktorga ochiq. Uchinchi qavat — tasdiq —
//  jadvalda `approved_by` ustuni yo'qligi sababli QURILMADI; u kerak bo'lsa
//  alohida migratsiya talab qiladi (S2 hisobotida qayd etilgan).
// ===========================================================================

/// <summary>
/// Kassadan topshirilgan pul — o'qish uchun.
/// </summary>
/// <param name="Destination"><see cref="CashHandoverDestination"/>: bank | safe.</param>
/// <param name="ReversalOf">Storno bo'lsa — qaysi topshiriqni bekor qilyapti.</param>
/// <param name="Reversed">Shu topshiriq keyinchalik storno qilinganmi.</param>
public record CashHandoverDto(
    Guid Id,
    Guid CashShiftId,
    string CashierId,
    string CashierName,
    decimal Amount,
    string Destination,
    string? Note,
    string CreatedBy,
    string CreatedByName,
    DateTimeOffset CreatedAt,
    Guid? ReversalOf,
    bool Reversed);

/// <summary>
/// Yangi topshiriq. <c>cashShiftId</c> ham, <c>createdBy</c> ham so'rovda
/// YO'Q: smena serverda kassirning ochiq smenasidan, shaxs esa JWT'dan
/// olinadi (SPEC §4.4).
/// </summary>
public record RecordCashHandoverRequest(decimal Amount, string Destination, string? Note);

/// <summary>Topshiriqni storno qilish. Sabab majburiy — u qarshi qatorning izohiga tushadi.</summary>
public record ReverseCashHandoverRequest(string Reason);

/// <summary>Topshiriqlar registri uchun filtr.</summary>
/// <param name="CashShiftId">Bitta smena; <c>null</c> — hammasi.</param>
/// <param name="CashierId">Bitta kassir (controller uni rolga qarab toraytiradi).</param>
/// <param name="From">Shu sanadan (Toshkent kuni, <c>created_at</c> bo'yicha).</param>
/// <param name="To">Shu sanagacha, shu kun ham kiradi.</param>
/// <param name="Destination">bank | safe; <c>null</c> — ikkalasi.</param>
public record CashHandoverQuery(
    Guid? CashShiftId = null,
    string? CashierId = null,
    DateOnly? From = null,
    DateOnly? To = null,
    string? Destination = null);

/// <summary>
/// Kassadan pul topshirish (F1.04). Batafsil: fayl boshidagi izoh.
///
/// <para>
/// Bu interfeysda <c>Update</c> ham, <c>Delete</c> ham YO'Q — va bo'lmaydi
/// (SPEC §4.1). Xato topshiriq <see cref="ReverseAsync"/> bilan tuzatiladi.
/// </para>
/// </summary>
public interface ICashHandoverService
{
    /// <summary>
    /// Topshiriqni yozadi va (manzil <c>bank</c> bo'lsa) jurnalga qo'yadi.
    /// Ochiq smenasiz — <c>409 no_open_shift</c>.
    /// </summary>
    Task<CashHandoverDto> RecordAsync(
        RecordCashHandoverRequest request, string actorId, CancellationToken ct = default);

    /// <summary>
    /// Qarshi qator qo'shadi: pul storno qiluvchining OCHIQ smenasiga
    /// qaytadi. Original qator tegilmaydi.
    /// </summary>
    Task<CashHandoverDto> ReverseAsync(
        Guid id, string reason, string actorId, CancellationToken ct = default);

    /// <summary>Registr (yangisidan eskisiga).</summary>
    Task<IReadOnlyList<CashHandoverDto>> ListAsync(
        CashHandoverQuery query, CancellationToken ct = default);

    /// <summary>Bitta topshiriq; topilmasa <c>null</c>.</summary>
    Task<CashHandoverDto?> GetAsync(Guid id, CancellationToken ct = default);
}

/// <inheritdoc cref="ICashHandoverService"/>
public sealed class CashHandoverService(
    IAppDbContext db, ILedgerService ledger, ICashShiftService shifts) : ICashHandoverService
{
    /// <summary>Baza ustuni <c>numeric(14,2)</c> — arifmetika ham shu aniqlikda.</summary>
    private const int MoneyScale = 2;

    /// <summary>
    /// Registrning yuqori chegarasi. Topshiriq kuniga bir-ikkita, ya'ni 2000
    /// qator ≈ bir necha yillik tarix; filtrsiz so'rov ham serverni yiqitmaydi.
    /// </summary>
    private const int MaxRows = 2000;

    /// <summary><c>audit_log.entity_type</c> — topshiriqlar shu bo'yicha topiladi.</summary>
    public const string AuditEntityCashHandover = AuditService.EntityCashHandover;

    /// <summary>
    /// Smena qulfi. Kalit <see cref="CashShiftService.ShiftLockKey"/> dan
    /// olinadi — nusxa yozilmaydi, aks holda ikki amal bir-birini kutmay
    /// qo'yardi (o'sha metodning izohi).
    /// </summary>
    private const string LockSql = "SELECT pg_advisory_xact_lock(hashtextextended({0}::text, 0))";

    private readonly ActorNames actors = new(db);

    /// <summary>
    /// Xom SQL uchun kontekstning o'zi — <see cref="IAppDbContext"/> da
    /// <c>Database</c> yo'q (u ataylab tor interfeys).
    /// </summary>
    private readonly DbContext ef = db as DbContext ?? throw new ArgumentException(
        $"{nameof(CashHandoverService)} EF kontekstini talab qiladi: smena qulfi xom SQL "
        + "orqali qo'yiladi. Berilgan implementatsiya DbContext emas.", nameof(db));

    // -----------------------------------------------------------------
    //  Yozish
    // -----------------------------------------------------------------

    /// <inheritdoc />
    public async Task<CashHandoverDto> RecordAsync(
        RecordCashHandoverRequest request, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireActor(actorId);

        var amount = Money(request.Amount);
        var destination = RequireDestination(request.Destination);
        var note = Trim(request.Note);

        await using var tx = await db.BeginTransactionAsync(ct);

        var shift = await RequireOpenShiftAsync(actorId, ct);

        var handover = new CashHandover
        {
            CashShiftId = shift,
            Amount = amount,
            Destination = destination,
            Note = note,
            CreatedBy = actorId,
            CreatedAt = AppClock.NowInstant,
            ReversalOf = null,
        };

        db.CashHandovers.Add(handover);
        db.AuditLogs.Add(AuditService.Entry(
            AuditEntityCashHandover, handover.Id.ToString("D"), "create",
            $"Kassadan topshirildi ({Label(destination)}): {AuditService.Money(amount)} so'm"
            + (note is null ? "" : $". Izoh: {note}"),
            actorId: actorId,
            actorName: await actors.OfAsync(actorId, ct),
            after: Snapshot(handover)));

        await db.SaveChangesAsync(ct);

        await PostToLedgerAsync(handover, ToBank: true, actorId, ct);

        await tx.CommitAsync(ct);

        return (await ToDtosAsync([handover], ct))[0];
    }

    /// <inheritdoc />
    public async Task<CashHandoverDto> ReverseAsync(
        Guid id, string reason, string actorId, CancellationToken ct = default)
    {
        RequireActor(actorId);

        var cleanReason = Trim(reason)
            ?? throw BillingRuleException.Invalid("reason_required",
                "Storno sababi majburiy (SPEC §4.3) — u yozuvda qoladi.");

        await using var tx = await db.BeginTransactionAsync(ct);

        var original = await db.CashHandovers.AsNoTracking().FirstOrDefaultAsync(h => h.Id == id, ct)
            ?? throw BillingRuleException.NotFound("handover_not_found", "Topshiriq topilmadi.");

        if (original.ReversalOf is not null)
            throw BillingRuleException.Conflict("already_reversal",
                "Bu qatorning O'ZI storno. Storno'ni storno qilib bo'lmaydi — "
                + "kerak bo'lsa yangi topshiriq yozing.");

        // Ikki marta storno = pulni ikki marta "qaytarish". Bazada ham shunday
        // (`reversal_of` unikal), bu yerdagi tekshiruv sababni aniq aytish uchun.
        if (await db.CashHandovers.AsNoTracking().AnyAsync(h => h.ReversalOf == id, ct))
            throw BillingRuleException.Conflict("already_reversed",
                "Bu topshiriq allaqachon storno qilingan.");

        // Kassir FAQAT o'z smenasidan chiqqan topshiriqni qaytara oladi;
        // admin va direktor — istalganini (SPEC §4.2 dagi "o'zganing smenasi"
        // qoidasining ko'zgusi, `CashShiftService.RequireMayCloseAsync`).
        await RequireMayReverseAsync(original, actorId, ct);

        var shift = await RequireOpenShiftAsync(actorId, ct);

        var mirror = new CashHandover
        {
            CashShiftId = shift,
            // Summa MUSBAT bo'lib qoladi (`ck_cash_handovers_amount`);
            // "bu qarshi qator" degan ma'noni `reversal_of` beradi.
            Amount = original.Amount,
            Destination = original.Destination,
            Note = cleanReason,
            CreatedBy = actorId,
            CreatedAt = AppClock.NowInstant,
            ReversalOf = original.Id,
        };

        db.CashHandovers.Add(mirror);
        db.AuditLogs.Add(AuditService.Entry(
            AuditEntityCashHandover, original.Id.ToString("D"), "reverse",
            $"Topshiriq STORNO qilindi ({Label(original.Destination)}): "
            + $"{AuditService.Money(original.Amount)} so'm. Sabab: {cleanReason}",
            actorId: actorId,
            actorName: await actors.OfAsync(actorId, ct),
            before: Snapshot(original),
            after: Snapshot(mirror)));

        await db.SaveChangesAsync(ct);

        await PostToLedgerAsync(mirror, ToBank: false, actorId, ct);

        await tx.CommitAsync(ct);

        return (await ToDtosAsync([mirror], ct))[0];
    }

    // -----------------------------------------------------------------
    //  O'qish
    // -----------------------------------------------------------------

    /// <inheritdoc />
    public async Task<CashHandoverDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var row = await db.CashHandovers.AsNoTracking().FirstOrDefaultAsync(h => h.Id == id, ct);
        return row is null ? null : (await ToDtosAsync([row], ct))[0];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CashHandoverDto>> ListAsync(
        CashHandoverQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var q = db.CashHandovers.AsNoTracking();

        if (query.CashShiftId is { } shiftId)
            q = q.Where(h => h.CashShiftId == shiftId);

        if (!string.IsNullOrWhiteSpace(query.Destination))
        {
            var destination = RequireDestination(query.Destination);
            q = q.Where(h => h.Destination == destination);
        }

        // Kassir bo'yicha filtr smenaning EGASI bo'yicha ishlaydi, `created_by`
        // bo'yicha emas: admin boshqa kassirning smenasidan topshiriq yozgan
        // holat ham shu kassirning registrida ko'rinishi kerak — pul aynan
        // o'sha javondan chiqadi.
        if (!string.IsNullOrWhiteSpace(query.CashierId))
        {
            var cashierId = query.CashierId.Trim();
            var own = db.CashShifts.AsNoTracking()
                .Where(s => s.CashierId == cashierId)
                .Select(s => s.Id);
            q = q.Where(h => own.Contains(h.CashShiftId));
        }

        // Sana chegaralari: bazada bir kun zaxira bilan (indeksdan foydalanadi),
        // xotirada Toshkent kuni bo'yicha aniq kesish. Sabab `CashShiftService.ListAsync`
        // dagi bilan bir xil: `timestamptz` da "qaysi mahalliy kun" degan savolni
        // LINQ provayderi xom SQL castisiz ifodalay olmaydi.
        if (query.From is { } from)
        {
            var lower = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(-1);
            q = q.Where(h => h.CreatedAt >= lower);
        }
        if (query.To is { } to)
        {
            var upper = new DateTimeOffset(to.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero).AddDays(1);
            q = q.Where(h => h.CreatedAt <= upper);
        }

        var rows = await q
            .OrderByDescending(h => h.CreatedAt)
            .Take(MaxRows)
            .ToListAsync(ct);

        if (query.From is { } exactFrom)
            rows = [.. rows.Where(h => AppClock.LocalDateOf(h.CreatedAt) >= exactFrom)];
        if (query.To is { } exactTo)
            rows = [.. rows.Where(h => AppClock.LocalDateOf(h.CreatedAt) <= exactTo)];

        return await ToDtosAsync(rows, ct);
    }

    // -----------------------------------------------------------------
    //  Ichki yordamchilar
    // -----------------------------------------------------------------

    /// <summary>
    /// Jurnal partiyasi. FAQAT <c>bank</c> manzili yozadi — <c>safe</c> da
    /// pul <see cref="Accounts.Cash"/> hisobida qolaveradi (fayl boshidagi izoh).
    ///
    /// <para>
    /// <paramref name="ToBank"/> yo'nalishni belgilaydi: oddiy topshiriqda
    /// pul bankka KETADI (<c>debit bank / credit cash</c>), storno qatorida
    /// esa QAYTADI (<c>debit cash / credit bank</c>). Ikkala holatda ham bu
    /// ODDIY partiya (<c>ref_type = cash_handover</c>), <c>reversal</c> emas:
    /// sababi fayl boshida.
    /// </para>
    /// </summary>
    private async Task PostToLedgerAsync(
        CashHandover handover, bool ToBank, string actorId, CancellationToken ct)
    {
        if (!string.Equals(handover.Destination, CashHandoverDestination.Bank, StringComparison.Ordinal))
            return;

        var memo = ToBank
            ? $"Kassadan bankka topshirildi{Suffix(handover.Note)}"
            : $"Bankka topshiriq STORNO qilindi{Suffix(handover.Note)}";

        var entryDate = AppClock.Today;

        await ledger.PostAsync(
        [
            new LedgerPosting(
                ToBank ? Accounts.Bank : Accounts.Cash,
                LedgerDirection.Debit, handover.Amount,
                LedgerRefType.CashHandover, handover.Id, entryDate, memo),
            new LedgerPosting(
                ToBank ? Accounts.Cash : Accounts.Bank,
                LedgerDirection.Credit, handover.Amount,
                LedgerRefType.CashHandover, handover.Id, entryDate, memo),
        ], actorId, ct);

        static string Suffix(string? note) => note is null ? "" : $" — {note}";
    }

    /// <summary>
    /// Kassirning ochiq smenasi, QULF ostida tasdiqlangan holda.
    ///
    /// <para>
    /// Qulf <see cref="CashShiftService.CloseAsync"/> bilan bir xil kalitni
    /// oladi: qulfsiz topshiriq smena yopilgandan KEYIN unga biriktirilib
    /// qolardi va kutilgan naqd uni hech qachon ko'rmasdi. Chaqiruvchi ochiq
    /// tranzaksiya ichida bo'lishi shart — advisory lock tranzaksiya oxirida
    /// bo'shaydi.
    /// </para>
    /// </summary>
    private async Task<Guid> RequireOpenShiftAsync(string userId, CancellationToken ct)
    {
        var current = await shifts.CurrentAsync(userId, ct) ?? throw NoOpenShift();

        await ef.Database.ExecuteSqlRawAsync(
            LockSql, [CashShiftService.ShiftLockKey(current.Id)], ct);

        var status = await db.CashShifts.AsNoTracking()
            .Where(s => s.Id == current.Id)
            .Select(s => s.Status)
            .FirstOrDefaultAsync(ct);

        if (!string.Equals(status, CashShiftStatus.Open, StringComparison.Ordinal))
            throw NoOpenShift();

        return current.Id;
    }

    /// <summary>
    /// Kassir o'zganing smenasidan chiqqan topshiriqni storno qila olmaydi;
    /// admin va direktor — qila oladi. Rol FOYDALANUVCHI QATORIDAN o'qiladi,
    /// ya'ni endpoint chetlab o'tilsa ham qoida kuchda qoladi
    /// (<c>CashShiftService.RequireMayCloseAsync</c> dagi naqsh).
    /// </summary>
    private async Task RequireMayReverseAsync(CashHandover original, string actorId, CancellationToken ct)
    {
        var role = await db.Users.AsNoTracking()
            .Where(u => u.Id == actorId)
            .Select(u => u.Role)
            .FirstOrDefaultAsync(ct);

        if (CashShiftService.IsSupervisorRole(role)) return;

        var ownerId = await db.CashShifts.AsNoTracking()
            .Where(s => s.Id == original.CashShiftId)
            .Select(s => s.CashierId)
            .FirstOrDefaultAsync(ct);

        if (!string.Equals(ownerId, actorId, StringComparison.Ordinal))
            throw BillingRuleException.Forbidden("not_your_shift",
                "Bu topshiriq boshqa kassirning smenasidan chiqqan. Uni faqat o'sha kassir, "
                + "admin yoki direktor storno qiladi (SPEC §4.2).");
    }

    private async Task<List<CashHandoverDto>> ToDtosAsync(
        IReadOnlyList<CashHandover> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];

        var ids = rows.Select(h => h.Id).ToList();
        var shiftIds = rows.Select(h => h.CashShiftId).Distinct().ToList();

        var shiftOwners = await db.CashShifts.AsNoTracking()
            .Where(s => shiftIds.Contains(s.Id))
            .Select(s => new { s.Id, s.CashierId })
            .ToDictionaryAsync(s => s.Id, s => s.CashierId, ct);

        var userIds = rows.Select(h => h.CreatedBy)
            .Concat(shiftOwners.Values)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var names = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName })
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        // "Storno qilinganmi" — bitta so'rov, sikl ichida emas.
        var reversed = await db.CashHandovers.AsNoTracking()
            .Where(h => h.ReversalOf != null && ids.Contains(h.ReversalOf.Value))
            .Select(h => h.ReversalOf!.Value)
            .ToListAsync(ct);

        return [.. rows.Select(h =>
        {
            var cashierId = shiftOwners.GetValueOrDefault(h.CashShiftId, h.CreatedBy);
            return new CashHandoverDto(
                h.Id,
                h.CashShiftId,
                cashierId,
                names.GetValueOrDefault(cashierId, "Noma'lum"),
                h.Amount,
                h.Destination,
                h.Note,
                h.CreatedBy,
                names.GetValueOrDefault(h.CreatedBy, "Noma'lum"),
                h.CreatedAt,
                h.ReversalOf,
                reversed.Contains(h.Id));
        })];
    }

    /// <summary>Audit uchun snapshot (SPEC §4.6).</summary>
    private static object Snapshot(CashHandover h) => new
    {
        h.Id,
        h.CashShiftId,
        h.Amount,
        h.Destination,
        h.Note,
        h.CreatedBy,
        h.ReversalOf,
    };

    private static string Label(string destination) =>
        string.Equals(destination, CashHandoverDestination.Bank, StringComparison.Ordinal)
            ? "bank"
            : "seyf";

    /// <summary>
    /// SPEC §4.2 — naqd pul ochiq smenasiz javondan chiqmaydi.
    /// <c>ExpenseService</c> va <c>PaymentService</c> dagi kod bilan AYNAN bir xil.
    /// </summary>
    private static BillingRuleException NoOpenShift() =>
        BillingRuleException.Conflict("no_open_shift",
            "Ochiq kassa smenasi yo'q. Pul javondan smena ichida chiqadi va smena ichida "
            + "qaytadi — avval smenani oching.");

    private static string RequireDestination(string? destination)
    {
        var clean = destination?.Trim().ToLowerInvariant();
        return clean is not null && CashHandoverDestination.All.Contains(clean, StringComparer.Ordinal)
            ? clean
            : throw BillingRuleException.Invalid("invalid_destination",
                $"Noma'lum manzil: '{destination}'. "
                + $"Ruxsat etilganlar: {string.Join(", ", CashHandoverDestination.All)}.");
    }

    /// <summary>
    /// Summa musbat va baza aniqligiga (2 kasr) MOS bo'lishi shart.
    /// Yaxlitlab yubormaymiz: 1000.005 "qabul qilindi" bo'lib ko'rinib,
    /// bazada boshqa raqam bo'lib qolardi.
    /// </summary>
    private static decimal Money(decimal value)
    {
        if (decimal.Round(value, MoneyScale) != value)
            throw BillingRuleException.Invalid("invalid_amount",
                $"Summa tiyin aniqligida (2 kasr) bo'lishi shart: {value}.");
        if (value <= 0m)
            throw BillingRuleException.Invalid("invalid_amount",
                "Topshiriq summasi musbat bo'lishi shart.");
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

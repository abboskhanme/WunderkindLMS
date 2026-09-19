using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  KASSALAR (cash boxes) — "smena" tushunchasini almashtiradi. Mijoz javobi
//  (2026-09): "bizni tizimda smena degan tushuncha umuman bo'lmasin butunlay
//  olib tashla, shunchaki kassa degan narsa bo'lsin xolos, bizda bir nechta
//  kassa bo'lishi mumkin, ular har bir alohida pul kirim chiqim qilishi va
//  o'zaro o'tkazma qilishi mumkin."
// ===========================================================================
//
//  TO'RTTA AMAL, EduSchool ekranidagi AYNAN o'sha to'rtta tugma:
//  Kirim (PayIn) · Chiqim (PayOut) · Ko'chirish (Transfer) · Ayirboshlash
//  (Exchange). Har biri BITTA `cash_box_transactions` qatori — sxema izohi
//  (Domain/CashBoxes.cs) da "bitta qator, ikkita qulf" mantiqi tushuntirilgan.
//
//  BALANS HECH QACHON SAQLANMAYDI (SPEC §4.1) — <see cref="BalanceAsync"/>
//  har safar `cash_box_transactions` dan hisoblaydi.
//
//  QULFLAR — "har qanday tekshir-va-yoz uchun pg_advisory_xact_lock":
//    * bitta kassaga yozish (kirim/chiqim/ayirboshlash) — o'sha kassaning qulfi;
//    * ko'chirish — IKKALA kassaning qulfi, HAR DOIM Guid bo'yicha O'SUVCHI
//      tartibda (A->B va B->A bir vaqtda kelsa ham deadlock bo'lmasin —
//      <see cref="TransferAsync"/> izohi);
//    * sukut (default) belgisini almashtirish — GLOBAL qulf
//      (<see cref="DefaultLockKey"/>), aks holda ikki parallel "meni sukut
//      qil" so'rovi ikkalasi ham "hozir sukut yo'q edi" deb ko'rib, ikkita
//      kassa sukut bo'lib qolishi mumkin edi (bazadagi qisman unikal indeks
//      buni oxir-oqibat ushlaydi, lekin usiz bittasi 23505 bilan tushunarsiz
//      yiqilardi).
//
//  DI'SIZ QURILADI — "CashHandoversController" dagi naqsh
//  ---------------------------------------------------------------------
//  Bu klass Program.cs da RO'YXATDAN O'TKAZILMAGAN (hujjat: report). U faqat
//  `IAppDbContext` talab qiladi — allaqachon DI'da bor — shuning uchun
//  `PaymentService`/`ExpenseService`/`CashBoxesController` uni to'g'ridan-to'g'ri
//  `new CashBoxService(db)` bilan quradi. Bu ATAYLAB: "Do not edit Program.cs"
//  cheklovi ostida yangi funksiya BIRINCHI kundanoq ishlashi kerak edi.

/// <summary>Kassa — ro'yxat va kartochka uchun (balans HAR SAFAR hisoblanadi).</summary>
public record CashBoxDto(
    Guid Id, string Name, string? ResponsibleUserId, string? ResponsibleName,
    decimal Balance, IReadOnlyDictionary<string, decimal> ByMethod,
    bool IsDefault, bool IsActive);

/// <summary>Yangi kassa ochish.</summary>
/// <param name="IsDefault">true — shu kassa SUKUT bo'ladi (boshqasiniki avtomatik yechiladi).</param>
public record CreateCashBoxRequest(string Name, string? ResponsibleUserId = null, bool? IsDefault = null);

/// <summary>Kassani yangilash — nomi, mas'uli, sukut belgisi, faolligi. Hamma maydon ixtiyoriy.</summary>
public record UpdateCashBoxRequest(
    string? Name = null, string? ResponsibleUserId = null, bool? IsDefault = null, bool? IsActive = null);

/// <summary>
/// Kirim (Kirim tugmasi). <c>studentId</c> ixtiyoriy — pul kimdan kelganini
/// bildiradi. <c>transactionTypeId</c> ixtiyoriy (backend darajasida — mijoz
/// yuborgan EduSchool shaklida MAJBURIY, lekin bu talab `PlainIncomeForm.tsx`
/// darajasida: shu maydonni butun tizimda majburiy qilish o'nlab mavjud
/// testni (`CashBoxTests.cs`) va `CashBoxActionModal.tsx` ning eski "in"
/// yo'lini buzardi — <see cref="CashBoxService"/> boshidagi izoh).
/// Berilsa — <see cref="TransactionType.Kind"/> <c>'in'</c> va faol bo'lishi
/// tekshiriladi (<c>RequireTransactionTypeAsync</c>).
/// </summary>
/// <param name="Date">
/// Kirim QAYSI KUN bilan yozilishi (ixtiyoriy; <c>null</c> — bugun). Mijoz
/// so'radi (2026-09-18): "oldingi sana uchun tanlash mumkin bo'lsin" — kassir
/// kechagi yoki o'tgan haftadagi pulni keyin kiritsa, u o'sha kunning
/// hisobotiga tushishi kerak. Ikki chegara:
/// KELAJAK — YO'Q (<c>future_date</c>), va eng ko'pi bilan
/// <see cref="CashBoxService.MaxBackdateDays"/> kun orqaga
/// (<c>date_too_old</c> — "2026" o'rniga "2025" terib yuborishdan himoya).
/// Orqaga yozilgan qator auditda alohida belgilanadi.
/// </param>
public record CashBoxPayInRequest(
    decimal Amount, string Method, string? Note = null, string? StudentId = null,
    Guid? TransactionTypeId = null, DateOnly? Date = null);

/// <summary>Chiqim (Chiqim tugmasi).</summary>
/// <param name="TransactionTypeId">
/// Chiqim turi (ixtiyoriy backend'da, MAJBURIY ekranda — kirim bilan bir xil
/// sabab: <see cref="CashBoxPayInRequest"/> izohi). Berilsa
/// <see cref="TransactionType.Kind"/> <c>'out'</c> va faol bo'lishi
/// tekshiriladi — kirim turini chiqimga yopishtirib bo'lmaydi.
/// </param>
/// <param name="Date">Chiqim qaysi kun bilan yozilishi — <see cref="CashBoxPayInRequest.Date"/> bilan bir xil qoida.</param>
public record CashBoxPayOutRequest(
    decimal Amount, string Method, string? Note = null,
    Guid? TransactionTypeId = null, DateOnly? Date = null);

/// <summary>Ko'chirish (Ko'chirish tugmasi) — bitta kassadan ikkinchisiga.</summary>
/// <param name="Date">Qaysi kun bilan yozilishi — <see cref="CashBoxPayInRequest.Date"/> bilan bir xil qoida.</param>
public record CashBoxTransferRequest(
    Guid ToBoxId, decimal Amount, string Method, string? Note = null, DateOnly? Date = null);

/// <summary>Ayirboshlash (Ayirboshlash tugmasi) — bitta kassa ichida usuldan usulga.</summary>
/// <param name="Date">Qaysi kun bilan yozilishi — <see cref="CashBoxPayInRequest.Date"/> bilan bir xil qoida.</param>
public record CashBoxExchangeRequest(
    decimal Amount, string FromMethod, string ToMethod, string? Note = null, DateOnly? Date = null);

/// <summary>Amalni bekor qilish. Sabab majburiy — u qarshi qatorning izohiga tushadi.</summary>
public record CancelCashBoxTransactionRequest(string Reason);

/// <summary>Kassa harakatlari ro'yxatining bitta qatori.</summary>
/// <param name="No">Filtrlangan ro'yxat ichidagi tartib raqami (1-based; SAQLANMAYDI — faqat ko'rsatish uchun).</param>
/// <param name="Who">Kim yozgan (users.id emas, ism).</param>
/// <param name="ContractNo">Bog'liq o'quvchining shartnoma raqami (bo'lsa) — faqat <c>pay_in</c> + <c>studentId</c>.</param>
/// <param name="Status">posted | cancelled | reversal — <see cref="CashBoxTransactionStatus"/> dan HISOBLANGAN.</param>
/// <param name="TransactionTypeName">
/// Tanlangan tranzaksiya turining nomi (bo'lsa) — <see cref="CashBoxTransaction.TransactionTypeId"/>
/// dan HAL QILINGAN, id emas: jadvaldagi boshqa "kim"/"shartnoma raqami"
/// ustunlari kabi ko'rsatiladigan qiymat. <c>null</c> = tur ko'rsatilmagan.
/// </param>
/// <param name="Note">
/// Kassir yozgan izoh. Jadvalda ALOHIDA ustun (EduSchool kassa ro'yxatida ham
/// "IZOH" ustuni bor, 2026-09-18 da o'qildi): izoh yozilsa-yu ko'rinmasa, uni
/// faqat audit jurnalidan topish mumkin bo'lardi.
/// </param>
/// <param name="CancelReason">
/// Bekor qilish SABABI (EduSchool'dagi "SABAB" ustuni). Bekor qilingan
/// qatorda — uni bekor qilgan storno qatorining izohi; stornoning O'ZIDA —
/// o'z izohi (u yerda izoh AYNAN sabab: <c>CancelTransactionAsync</c>).
/// Oddiy qatorda <c>null</c>.
/// </param>
/// <param name="CreatedAt">
/// Yozuv LAHZASI — jadvalda sana yonida SOAT ko'rsatiladi ("18.09.2026 |
/// 09:00", EduSchool kassa ro'yxatidagi kabi) va chekka ham shu tushadi.
/// <see cref="Date"/> shu lahzaning maktab mintaqasidagi kuni, ya'ni ikkalasi
/// bir manbadan — kunlik filtr bilan chek ustidagi vaqt hech qachon
/// ajralmaydi.
/// </param>
public record CashBoxTransactionRowDto(
    Guid Id, int No, DateOnly Date, string Who, string? ContractNo,
    decimal Amount, string Kind, string Method, string Status,
    string? TransactionTypeName, string? Note = null, string? CancelReason = null,
    DateTimeOffset CreatedAt = default);

/// <summary>Kassa harakatlari ro'yxati uchun filtr.</summary>
public record CashBoxTransactionsQuery(
    DateOnly? From = null, DateOnly? To = null, Guid? BoxId = null, string? Q = null);

/// <summary>Ro'yxat sahifasi + usul kesimidagi va umumiy yakunlar.</summary>
public record CashBoxTransactionsPageDto(
    IReadOnlyList<CashBoxTransactionRowDto> Rows,
    IReadOnlyDictionary<string, decimal> TotalsByMethod,
    decimal InTotal, decimal OutTotal);

/// <summary>
/// Kassalar: kataloq (ochish/o'zgartirish) va to'rtta pul amali (kirim,
/// chiqim, ko'chirish, ayirboshlash). "Smena" o'rnini bosadi. Batafsil: fayl
/// boshidagi izoh.
/// </summary>
public interface ICashBoxService
{
    Task<IReadOnlyList<CashBoxDto>> ListAsync(CancellationToken ct = default);

    Task<CashBoxDto> CreateAsync(
        CreateCashBoxRequest request, string actorId, CancellationToken ct = default);

    Task<CashBoxDto> UpdateAsync(
        Guid id, UpdateCashBoxRequest request, string actorId, CancellationToken ct = default);

    Task<CashBoxTransactionRowDto> PayInAsync(
        Guid boxId, CashBoxPayInRequest request, string actorId, CancellationToken ct = default);

    Task<CashBoxTransactionRowDto> PayOutAsync(
        Guid boxId, CashBoxPayOutRequest request, string actorId, CancellationToken ct = default);

    /// <summary>Atomar juft: pul YO IKKALA kassada ham o'zgaradi, YO hech birida (SPEC talabi).</summary>
    Task<CashBoxTransactionRowDto> TransferAsync(
        Guid fromBoxId, CashBoxTransferRequest request, string actorId, CancellationToken ct = default);

    Task<CashBoxTransactionRowDto> ExchangeAsync(
        Guid boxId, CashBoxExchangeRequest request, string actorId, CancellationToken ct = default);

    /// <summary>Storno: qarshi qator qo'shiladi. Original TEGILMAYDI (SPEC §4.1).</summary>
    Task<CashBoxTransactionRowDto> CancelTransactionAsync(
        Guid transactionId, string reason, string actorId, CancellationToken ct = default);

    Task<CashBoxTransactionsPageDto> TransactionsAsync(
        CashBoxTransactionsQuery query, CancellationToken ct = default);
}

/// <inheritdoc cref="ICashBoxService"/>
public sealed class CashBoxService(IAppDbContext db) : ICashBoxService
{
    /// <summary>Baza ustuni <c>numeric(14,2)</c> — arifmetika ham shu aniqlikda.</summary>
    private const int MoneyScale = 2;

    private const int MaxRows = 2000;

    /// <summary>
    /// Kirimni eng ko'pi bilan shuncha kun ORQAGA yozish mumkin (bir yil).
    /// Chegara TERISH XATOSIGA qarshi: "18.09.2026" o'rniga "18.09.2025"
    /// terilsa, pul yopilgan yilning hisobotiga tushib ketardi va buni faqat
    /// yillik solishtiruvda sezish mumkin bo'lardi. Mijozning haqiqiy ehtiyoji
    /// — kecha/o'tgan hafta unutilgan kirimni kiritish, shuning uchun bir yil
    /// ortig'i bilan yetadi.
    /// </summary>
    public const int MaxBackdateDays = 366;

    /// <summary><c>audit_log.entity_type</c> — kassa kataloq yozuvlari.</summary>
    public const string AuditEntityCashBox = "CashBox";

    /// <summary><c>audit_log.entity_type</c> — kassa harakati yozuvlari.</summary>
    public const string AuditEntityCashBoxTransaction = "CashBoxTransaction";

    private const string LockSql = "SELECT pg_advisory_xact_lock(hashtextextended({0}::text, 0))";

    /// <summary>Sukut (default) belgisini almashtirish uchun GLOBAL qulf kaliti.</summary>
    private const string DefaultLockKey = "cash_box_default";

    private readonly ActorNames actors = new(db);

    /// <summary>
    /// Xom SQL uchun kontekstning o'zi — <see cref="IAppDbContext"/> da
    /// <c>Database</c> yo'q (u ataylab tor interfeys), advisory lock esa EF
    /// LINQ bilan ifodalab bo'lmaydigan yagona narsa (<c>CashShiftService</c>
    /// dagi bilan bir xil yechim).
    /// </summary>
    private readonly DbContext ef = db as DbContext ?? throw new ArgumentException(
        $"{nameof(CashBoxService)} EF kontekstini talab qiladi: qulf xom SQL orqali qo'yiladi. "
        + "Berilgan implementatsiya DbContext emas.", nameof(db));

    /// <summary>
    /// Bitta kassaning qulf kaliti — <b>ochiq</b>, chunki uni
    /// <c>PaymentService</c>/<c>ExpenseService</c> ham (o'z advisory lock
    /// chaqiruvlarida) ishlatadi, nusxa yozish o'rniga.
    /// </summary>
    public static string BoxLockKey(Guid boxId) => $"cash_box:{boxId:D}";

    // =====================================================================
    //  Boshqa xizmatlar (PaymentService, ExpenseService) uchun — DI'siz
    // =====================================================================

    /// <summary>
    /// Joriy SUKUT (default) kassaning id'si. <c>cashBoxId</c> ko'rsatilmagan
    /// to'lov/chiqim shu yerga tushadi. Migratsiya kamida bittasini seed
    /// qiladi va bazadagi qisman unikal indeks "aynan bittasi" ni kafolatlaydi
    /// — shuning uchun bu yerda "topilmadi" holati amalda faqat buzilgan
    /// ma'lumotda yuz beradi.
    /// </summary>
    public static async Task<Guid> DefaultBoxIdAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var id = await db.CashBoxes.AsNoTracking()
            .Where(b => b.IsDefault)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync(ct);

        return id ?? throw BillingRuleException.Conflict("no_default_cash_box",
            "Sukut (default) kassa topilmadi. Kamida bitta kassa sukut bo'lishi shart — "
            + "moliya mas'uliga murojaat qiling.");
    }

    /// <summary>
    /// Kassa BO'YICHA uzluksiz chek raqami (<c>PaymentService</c> chaqiradi —
    /// "smena" o'rnini bosadi, SPEC §4.2 talabi o'zgarmagan: bo'shliq YO'Q).
    ///
    /// <para>
    /// Naqshi <c>CashShiftService.NextReceiptNoAsync</c> bilan AYNAN bir xil:
    /// advisory lock (nega — o'sha metod izohi), ochiq tranzaksiya SHART,
    /// raqam ajratish va yozish bitta tranzaksiyada (bekor qilingan so'rov
    /// teshik qoldirmasin).
    /// </para>
    /// </summary>
    public async Task<long> NextReceiptNoAsync(Guid boxId, CancellationToken ct = default)
    {
        if (ef.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                $"{nameof(NextReceiptNoAsync)} ochiq tranzaksiya ichida chaqirilishi SHART. Qulf "
                + "tranzaksiya oxirida bo'shaydi; tranzaksiyasiz chaqiriq chek raqamini uzluksiz QILMAYDI.");

        await LockBoxAsync(boxId, ct);

        var last = await db.Payments.AsNoTracking()
            .Where(p => p.CashBoxId == boxId)
            .MaxAsync(p => (long?)p.ReceiptNo, ct) ?? 0L;

        return last + 1;
    }

    private Task LockBoxAsync(Guid boxId, CancellationToken ct) =>
        ef.Database.ExecuteSqlRawAsync(LockSql, [BoxLockKey(boxId)], ct);

    // =====================================================================
    //  Kataloq: ochish / yangilash
    // =====================================================================

    /// <inheritdoc />
    public async Task<IReadOnlyList<CashBoxDto>> ListAsync(CancellationToken ct = default)
    {
        var list = await db.CashBoxes.AsNoTracking().OrderBy(b => b.Name).ToListAsync(ct);
        return await ToDtosAsync(list, ct);
    }

    /// <inheritdoc />
    public async Task<CashBoxDto> CreateAsync(
        CreateCashBoxRequest request, string actorId, CancellationToken ct = default)
    {
        RequireActor(actorId);
        var name = RequireName(request.Name);
        var responsibleId = await RequireResponsibleAsync(request.ResponsibleUserId, ct);

        await using var tx = await db.BeginTransactionAsync(ct);
        await ef.Database.ExecuteSqlRawAsync(LockSql, [DefaultLockKey], ct);

        // Birinchi kassa MAJBURAN sukut — aks holda hech qanday sukutsiz
        // holat paydo bo'lardi (SPEC: "exactly one box must be the default").
        var anyExists = await db.CashBoxes.AsNoTracking().AnyAsync(ct);
        var makeDefault = request.IsDefault == true || !anyExists;

        if (makeDefault) await ClearOtherDefaultsAsync(null, ct);

        var box = new CashBox
        {
            Name = name,
            ResponsibleUserId = responsibleId,
            IsDefault = makeDefault,
            IsActive = true,
            CreatedAt = AppClock.NowInstant,
        };
        db.CashBoxes.Add(box);

        db.AuditLogs.Add(AuditService.Entry(
            AuditEntityCashBox, box.Id.ToString("D"), "create",
            $"Kassa ochildi: {name}" + (makeDefault ? " (sukut)" : ""),
            actorId: actorId, actorName: await actors.OfAsync(actorId, ct),
            after: Snapshot(box)));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return (await ToDtosAsync([box], ct))[0];
    }

    /// <inheritdoc />
    public async Task<CashBoxDto> UpdateAsync(
        Guid id, UpdateCashBoxRequest request, string actorId, CancellationToken ct = default)
    {
        RequireActor(actorId);

        await using var tx = await db.BeginTransactionAsync(ct);
        await ef.Database.ExecuteSqlRawAsync(LockSql, [DefaultLockKey], ct);

        var box = await db.CashBoxes.FirstOrDefaultAsync(b => b.Id == id, ct)
            ?? throw BillingRuleException.NotFound("cash_box_not_found", "Kassa topilmadi.");

        var before = Snapshot(box);

        if (request.Name is { } name) box.Name = RequireName(name);

        if (request.ResponsibleUserId is not null)
            box.ResponsibleUserId = await RequireResponsibleAsync(request.ResponsibleUserId, ct);

        if (request.IsActive is { } active)
        {
            if (!active && box.IsDefault)
                throw BillingRuleException.Invalid("cannot_deactivate_default",
                    "Sukut (default) kassani o'chirib bo'lmaydi — avval boshqa kassani sukut qiling.");
            box.IsActive = active;
        }

        if (request.IsDefault is { } wantsDefault)
        {
            if (wantsDefault && !box.IsDefault)
            {
                if (!box.IsActive)
                    throw BillingRuleException.Invalid("inactive_cannot_be_default",
                        "Faol bo'lmagan kassa sukut bo'la olmaydi.");
                await ClearOtherDefaultsAsync(box.Id, ct);
                box.IsDefault = true;
            }
            else if (!wantsDefault && box.IsDefault)
            {
                throw BillingRuleException.Invalid("cannot_unset_default",
                    "Sukut belgisini shu yerda o'chirib bo'lmaydi — boshqa kassani sukut qiling, "
                    + "bu kassaniki avtomatik yechiladi.");
            }
        }

        db.AuditLogs.Add(AuditService.Entry(
            AuditEntityCashBox, box.Id.ToString("D"), "update", $"Kassa yangilandi: {box.Name}",
            actorId: actorId, actorName: await actors.OfAsync(actorId, ct),
            before: before, after: Snapshot(box)));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return (await ToDtosAsync([box], ct))[0];
    }

    /// <summary>
    /// <paramref name="exceptBoxId"/> dan BOSHQA hamma kassaning sukut
    /// belgisini o'chiradi. Chaqiruvchi <see cref="DefaultLockKey"/> qulfini
    /// OLGAN bo'lishi shart — aks holda ikki parallel so'rov ikkalasi ham
    /// "hozir sukut yo'q" deb ko'rib, ikkita kassa sukut bo'lib qolardi.
    /// </summary>
    private async Task ClearOtherDefaultsAsync(Guid? exceptBoxId, CancellationToken ct)
    {
        var others = await db.CashBoxes
            .Where(b => b.IsDefault && (exceptBoxId == null || b.Id != exceptBoxId))
            .ToListAsync(ct);
        if (others.Count == 0) return;
        foreach (var other in others) other.IsDefault = false;
        await db.SaveChangesAsync(ct);
    }

    // =====================================================================
    //  Pul amallari — Kirim / Chiqim / Ko'chirish / Ayirboshlash
    // =====================================================================

    /// <inheritdoc />
    public async Task<CashBoxTransactionRowDto> PayInAsync(
        Guid boxId, CashBoxPayInRequest request, string actorId, CancellationToken ct = default)
    {
        RequireActor(actorId);
        var amount = Money(request.Amount);
        var method = RequireMethod(request.Method);
        var note = Trim(request.Note);
        var studentId = Trim(request.StudentId);

        if (studentId is not null
            && !await db.Students.AsNoTracking().AnyAsync(s => s.Id == studentId, ct))
            throw BillingRuleException.NotFound("student_not_found", "O'quvchi topilmadi.");

        // Ixtiyoriy — izoh: `CashBoxPayInRequest.TransactionTypeId` boshidagi izoh.
        TransactionType? transactionType = request.TransactionTypeId is { } typeId
            ? await RequireTransactionTypeAsync(typeId, TransactionTypeKind.In, ct)
            : null;

        // Sana — ixtiyoriy, `null` bo'lsa hozir (izoh: `CashBoxPayInRequest.Date`).
        var createdAt = ResolveEntryInstant(request.Date);

        await using var tx = await db.BeginTransactionAsync(ct);
        await LockBoxAsync(boxId, ct);
        await RequireActiveBoxAsync(boxId, ct);

        var row = new CashBoxTransaction
        {
            CashBoxId = boxId,
            Kind = CashBoxTransactionKind.PayIn,
            Amount = amount,
            Method = method,
            StudentId = studentId,
            Note = note,
            Status = CashBoxTransactionStatus.Posted,
            CreatedBy = actorId,
            CreatedAt = createdAt,
            TransactionTypeId = transactionType?.Id,
        };
        db.CashBoxTransactions.Add(row);

        db.AuditLogs.Add(AuditService.Entry(
            AuditEntityCashBoxTransaction, row.Id.ToString("D"), "create",
            $"Kassaga kirim: {AuditService.Money(amount)} so'm ({method})"
            + (transactionType is null ? "" : $" — {transactionType.Name}")
            + BackdateNote(createdAt)
            + (note is null ? "" : $" — {note}"),
            actorId: actorId, actorName: await actors.OfAsync(actorId, ct), after: Snapshot(row)));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return (await ToRowDtosAsync([row], ct))[0];
    }

    /// <summary>
    /// <paramref name="id"/> — mavjud, <paramref name="expectedKind"/> ga mos
    /// (<see cref="TransactionTypeKind"/>) va faol ekanini tekshiradi.
    /// Tur — YORLIQ (<c>TransactionTypes.cs</c> boshidagi izoh), shuning uchun
    /// bu yerda qulf KERAK EMAS: parallel yaratish/tahrirlash bilan poyga
    /// yo'q — o'qish-tekshirish, keyin FK o'zi (<c>ON DELETE RESTRICT</c>)
    /// yakuniy himoya.
    /// </summary>
    private async Task<TransactionType> RequireTransactionTypeAsync(
        Guid id, string expectedKind, CancellationToken ct)
    {
        var type = await db.TransactionTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw BillingRuleException.NotFound("transaction_type_not_found", "Tranzaksiya turi topilmadi.");

        if (type.Kind != expectedKind)
            throw BillingRuleException.Invalid("transaction_type_kind_mismatch",
                $"Bu tranzaksiya turi '{expectedKind}' uchun emas.");

        if (!type.IsActive)
            throw BillingRuleException.Conflict("transaction_type_inactive", "Bu tranzaksiya turi faol emas.");

        return type;
    }

    /// <inheritdoc />
    public async Task<CashBoxTransactionRowDto> PayOutAsync(
        Guid boxId, CashBoxPayOutRequest request, string actorId, CancellationToken ct = default)
    {
        RequireActor(actorId);
        var amount = Money(request.Amount);
        var method = RequireMethod(request.Method);
        var note = Trim(request.Note);

        // Chiqim turi — kirimdagidek ixtiyoriy, lekin berilsa `out` bo'lishi shart.
        TransactionType? transactionType = request.TransactionTypeId is { } typeId
            ? await RequireTransactionTypeAsync(typeId, TransactionTypeKind.Out, ct)
            : null;

        var createdAt = ResolveEntryInstant(request.Date);

        await using var tx = await db.BeginTransactionAsync(ct);
        await LockBoxAsync(boxId, ct);
        await RequireActiveBoxAsync(boxId, ct);

        var row = new CashBoxTransaction
        {
            CashBoxId = boxId,
            Kind = CashBoxTransactionKind.PayOut,
            Amount = amount,
            Method = method,
            Note = note,
            Status = CashBoxTransactionStatus.Posted,
            CreatedBy = actorId,
            CreatedAt = createdAt,
            TransactionTypeId = transactionType?.Id,
        };
        db.CashBoxTransactions.Add(row);

        db.AuditLogs.Add(AuditService.Entry(
            AuditEntityCashBoxTransaction, row.Id.ToString("D"), "create",
            $"Kassadan chiqim: {AuditService.Money(amount)} so'm ({method})"
            + (transactionType is null ? "" : $" — {transactionType.Name}")
            + BackdateNote(createdAt)
            + (note is null ? "" : $" — {note}"),
            actorId: actorId, actorName: await actors.OfAsync(actorId, ct), after: Snapshot(row)));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return (await ToRowDtosAsync([row], ct))[0];
    }

    /// <inheritdoc />
    /// <remarks>
    /// <b>QAT'IY QULF TARTIBI</b> (SPEC talabi: "takes both locks in a fixed
    /// order"). Ikkala kassaning qulfi ham GUID qiymati bo'yicha O'SUVCHI
    /// tartibda olinadi — YO'NALISHIDAN qat'i nazar. Agar A→B va B→A
    /// o'tkazmalari bir vaqtda kelsa-yu, har biri "o'z" tartibida (avval
    /// manba, keyin manzil) qulflasa, biri A ni ushlab B ni kutadi, ikkinchisi
    /// B ni ushlab A ni kutadi — KLASSIK DEADLOCK. Ikkalasi ham bir xil GLOBAL
    /// tartibda (masalan kichik GUID birinchi) qulflasa, bu holat FIZIK
    /// jihatdan yuzaga kela olmaydi: ikkinchi so'rov birinchisi ikkala
    /// qulfni ham bo'shatguncha birinchi qulfning o'zida kutadi.
    /// </remarks>
    public async Task<CashBoxTransactionRowDto> TransferAsync(
        Guid fromBoxId, CashBoxTransferRequest request, string actorId, CancellationToken ct = default)
    {
        RequireActor(actorId);
        var amount = Money(request.Amount);
        var method = RequireMethod(request.Method);
        var note = Trim(request.Note);
        var toBoxId = request.ToBoxId;

        if (toBoxId == fromBoxId)
            throw BillingRuleException.Invalid("transfer_same_box", "Kassa o'ziga o'tkazma qila olmaydi.");

        var transferAt = ResolveEntryInstant(request.Date);

        await using var tx = await db.BeginTransactionAsync(ct);

        var (first, second) = fromBoxId.CompareTo(toBoxId) <= 0
            ? (fromBoxId, toBoxId) : (toBoxId, fromBoxId);
        await LockBoxAsync(first, ct);
        await LockBoxAsync(second, ct);

        await RequireActiveBoxAsync(fromBoxId, ct);
        await RequireActiveBoxAsync(toBoxId, ct);

        // BITTA qator — ikkala tomonni ham qamrab oladi (Domain/CashBoxes.cs
        // izohi): manba `CashBoxId`, manzil `TransferToBoxId`. "Ikkalasi ham
        // bo'ladi yoki hech biri bo'lmaydi" shu bitta INSERT bilan avtomatik —
        // ikkinchi qator umuman yo'q, ya'ni yarim bajarilish holati FIZIK
        // jihatdan mumkin emas.
        var row = new CashBoxTransaction
        {
            CashBoxId = fromBoxId,
            Kind = CashBoxTransactionKind.Transfer,
            Amount = amount,
            Method = method,
            Note = note,
            Status = CashBoxTransactionStatus.Posted,
            CreatedBy = actorId,
            CreatedAt = transferAt,
            TransferToBoxId = toBoxId,
        };
        db.CashBoxTransactions.Add(row);

        db.AuditLogs.Add(AuditService.Entry(
            AuditEntityCashBoxTransaction, row.Id.ToString("D"), "create",
            $"Kassalar orasida ko'chirish: {AuditService.Money(amount)} so'm ({method})"
            + (note is null ? "" : $" — {note}"),
            actorId: actorId, actorName: await actors.OfAsync(actorId, ct), after: Snapshot(row)));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return (await ToRowDtosAsync([row], ct))[0];
    }

    /// <inheritdoc />
    public async Task<CashBoxTransactionRowDto> ExchangeAsync(
        Guid boxId, CashBoxExchangeRequest request, string actorId, CancellationToken ct = default)
    {
        RequireActor(actorId);
        var amount = Money(request.Amount);
        var fromMethod = RequireMethod(request.FromMethod);
        var toMethod = RequireMethod(request.ToMethod);
        var note = Trim(request.Note);

        if (fromMethod == toMethod)
            throw BillingRuleException.Invalid("exchange_same_method",
                "Ayirboshlash ikkita HAR XIL usul talab qiladi.");

        var exchangeAt = ResolveEntryInstant(request.Date);

        await using var tx = await db.BeginTransactionAsync(ct);
        await LockBoxAsync(boxId, ct);
        await RequireActiveBoxAsync(boxId, ct);

        var row = new CashBoxTransaction
        {
            CashBoxId = boxId,
            Kind = CashBoxTransactionKind.Exchange,
            Amount = amount,
            Method = fromMethod,
            ToMethod = toMethod,
            Note = note,
            Status = CashBoxTransactionStatus.Posted,
            CreatedBy = actorId,
            CreatedAt = exchangeAt,
        };
        db.CashBoxTransactions.Add(row);

        db.AuditLogs.Add(AuditService.Entry(
            AuditEntityCashBoxTransaction, row.Id.ToString("D"), "create",
            $"Ayirboshlash: {AuditService.Money(amount)} so'm ({fromMethod} -> {toMethod})"
            + (note is null ? "" : $" — {note}"),
            actorId: actorId, actorName: await actors.OfAsync(actorId, ct), after: Snapshot(row)));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return (await ToRowDtosAsync([row], ct))[0];
    }

    /// <inheritdoc />
    public async Task<CashBoxTransactionRowDto> CancelTransactionAsync(
        Guid transactionId, string reason, string actorId, CancellationToken ct = default)
    {
        RequireActor(actorId);
        var cleanReason = Trim(reason)
            ?? throw BillingRuleException.Invalid("reason_required", "Bekor qilish sababi majburiy.");

        var original = await db.CashBoxTransactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == transactionId, ct)
            ?? throw BillingRuleException.NotFound("transaction_not_found", "Amal topilmadi.");

        if (original.ReversalOf is not null)
            throw BillingRuleException.Conflict("already_reversal",
                "Bu qatorning O'ZI bekor qilish yozuvi. Uni bekor qilib bo'lmaydi — "
                + "kerak bo'lsa yangi amal yozing.");

        if (await db.CashBoxTransactions.AsNoTracking().AnyAsync(t => t.ReversalOf == transactionId, ct))
            throw BillingRuleException.Conflict("already_cancelled", "Bu amal allaqachon bekor qilingan.");

        await using var tx = await db.BeginTransactionAsync(ct);

        // Ko'chirish bo'lsa — ikkala kassaning qulfi ham, xuddi
        // `TransferAsync` dagi bilan bir xil qat'iy tartibda.
        if (original.Kind == CashBoxTransactionKind.Transfer && original.TransferToBoxId is { } toBox)
        {
            var (first, second) = original.CashBoxId.CompareTo(toBox) <= 0
                ? (original.CashBoxId, toBox) : (toBox, original.CashBoxId);
            await LockBoxAsync(first, ct);
            await LockBoxAsync(second, ct);
        }
        else
        {
            await LockBoxAsync(original.CashBoxId, ct);
        }

        // Qarshi qator: AYNAN o'sha shakl (kind/method/toMethod/transferToBoxId),
        // lekin `Status = Reversal` va `ReversalOf` originalga ishora qiladi.
        // Balans hisobi (`Contributions`) bu qatorni ISHORASI TESKARI qo'shadi —
        // ya'ni pul avtomatik "qaytadi", ikkinchi mantiq YOZILMAYDI.
        var mirror = new CashBoxTransaction
        {
            CashBoxId = original.CashBoxId,
            Kind = original.Kind,
            Amount = original.Amount,
            Method = original.Method,
            ToMethod = original.ToMethod,
            StudentId = original.StudentId,
            Note = cleanReason,
            Status = CashBoxTransactionStatus.Reversal,
            CreatedBy = actorId,
            CreatedAt = AppClock.NowInstant,
            TransferToBoxId = original.TransferToBoxId,
            ReversalOf = original.Id,
            TransactionTypeId = original.TransactionTypeId,
        };
        db.CashBoxTransactions.Add(mirror);

        db.AuditLogs.Add(AuditService.Entry(
            AuditEntityCashBoxTransaction, original.Id.ToString("D"), "cancel",
            $"Amal BEKOR QILINDI: {AuditService.Money(original.Amount)} so'm ({original.Kind}). "
            + $"Sabab: {cleanReason}",
            actorId: actorId, actorName: await actors.OfAsync(actorId, ct),
            before: Snapshot(original), after: Snapshot(mirror)));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return (await ToRowDtosAsync([mirror], ct))[0];
    }

    private async Task RequireActiveBoxAsync(Guid boxId, CancellationToken ct)
    {
        var box = await db.CashBoxes.AsNoTracking()
            .Where(b => b.Id == boxId)
            .Select(b => new { b.IsActive })
            .FirstOrDefaultAsync(ct)
            ?? throw BillingRuleException.NotFound("cash_box_not_found", "Kassa topilmadi.");

        if (!box.IsActive)
            throw BillingRuleException.Conflict("cash_box_inactive", "Bu kassa faol emas.");
    }

    // =====================================================================
    //  O'qish: ro'yxat va harakatlar jurnali
    // =====================================================================

    /// <inheritdoc />
    public async Task<CashBoxTransactionsPageDto> TransactionsAsync(
        CashBoxTransactionsQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var q = db.CashBoxTransactions.AsNoTracking();

        // Bitta kassa so'ralsa — MANBA sifatida HAM, MANZIL sifatida HAM
        // (ko'chirishning ikkinchi tomoni) ko'rinadigan qatorlar.
        if (query.BoxId is { } boxId)
            q = q.Where(t => t.CashBoxId == boxId || t.TransferToBoxId == boxId);

        if (query.From is { } from)
        {
            var lower = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(-1);
            q = q.Where(t => t.CreatedAt >= lower);
        }
        if (query.To is { } to)
        {
            var upper = new DateTimeOffset(to.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero).AddDays(1);
            q = q.Where(t => t.CreatedAt <= upper);
        }

        var rows = await q.OrderByDescending(t => t.CreatedAt).Take(MaxRows).ToListAsync(ct);

        if (query.From is { } exactFrom)
            rows = [.. rows.Where(t => AppClock.LocalDateOf(t.CreatedAt) >= exactFrom)];
        if (query.To is { } exactTo)
            rows = [.. rows.Where(t => AppClock.LocalDateOf(t.CreatedAt) <= exactTo)];

        var dtos = await ToRowDtosAsync(rows, ct);

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var needle = query.Q.Trim();
            dtos = [.. dtos.Where(r =>
                r.Who.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || (r.ContractNo is not null && r.ContractNo.Contains(needle, StringComparison.OrdinalIgnoreCase)))];
        }

        // Yakunlar — RO'YXATDAGI qatorlar (rows, filtr bilan kesilgan) ustidan,
        // xom `CashBoxTransaction` yozuvlaridan hisoblanadi: `inTotal`/`outTotal`
        // TURGA (kind) qarab (faqat pay_in/pay_out — ko'chirish va ayirboshlash
        // maktab ichida neytral), `totalsByMethod` esa EKRANDA turgan
        // qatorlarning o'z manba tomonidagi ta'siri (ko'rinadigan qatorlarning
        // o'zi — ikkinchi tomon alohida kassaning o'z ro'yxatida ko'rinadi).
        var totalsByMethod = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var m in PaymentMethod.All) totalsByMethod[m] = 0m;
        decimal inTotal = 0m, outTotal = 0m;

        foreach (var row in rows)
        {
            foreach (var (method, delta) in Contributions(row))
                totalsByMethod[method] = decimal.Round(totalsByMethod[method] + delta, MoneyScale);

            var sign = row.ReversalOf is null ? 1m : -1m;
            if (row.Kind == CashBoxTransactionKind.PayIn) inTotal += sign * row.Amount;
            else if (row.Kind == CashBoxTransactionKind.PayOut) outTotal += sign * row.Amount;
        }

        return new CashBoxTransactionsPageDto(
            dtos, totalsByMethod, decimal.Round(inTotal, MoneyScale), decimal.Round(outTotal, MoneyScale));
    }

    // =====================================================================
    //  Balans — HECH QACHON SAQLANMAYDI (SPEC §4.1), HAR SAFAR hisoblanadi
    // =====================================================================

    /// <summary>
    /// Bitta qatorning "manba" kassasiga (<c>CashBoxId</c>) ta'siri, usul
    /// bo'yicha. <c>transfer</c> ning MANZIL tomoni bu yerda YO'Q — uni
    /// <see cref="ToDtosAsync"/> alohida (<c>TransferToBoxId</c> orqali)
    /// qo'shadi, chunki u boshqa kassaga tegishli.
    ///
    /// <para>
    /// Storno (<c>ReversalOf != null</c>) qatori AYNAN shu shaklda, lekin
    /// ISHORASI teskari — ya'ni original qanday qo'shsa, storno SHUNCHA
    /// ayiradi. Ikkinchi, alohida "qaytarish" mantig'i YO'Q: bitta formula
    /// ikkalasini ham qamrab oladi.
    /// </para>
    /// </summary>
    private static IEnumerable<(string Method, decimal Amount)> Contributions(CashBoxTransaction row)
    {
        var sign = row.ReversalOf is null ? 1m : -1m;
        switch (row.Kind)
        {
            case CashBoxTransactionKind.PayIn:
                yield return (row.Method, sign * row.Amount);
                break;
            case CashBoxTransactionKind.PayOut:
                yield return (row.Method, -sign * row.Amount);
                break;
            case CashBoxTransactionKind.Transfer:
                // Manba tomoni — pul CHIQADI.
                yield return (row.Method, -sign * row.Amount);
                break;
            case CashBoxTransactionKind.Exchange:
                yield return (row.Method, -sign * row.Amount);
                yield return (row.ToMethod!, sign * row.Amount);
                break;
        }
    }

    private async Task<List<CashBoxDto>> ToDtosAsync(IReadOnlyList<CashBox> boxes, CancellationToken ct)
    {
        if (boxes.Count == 0) return [];

        var ids = boxes.Select(b => b.Id).ToList();

        var responsibleIds = boxes.Select(b => b.ResponsibleUserId).Where(x => x is not null)
            .Select(x => x!).Distinct().ToList();
        var names = responsibleIds.Count == 0
            ? []
            : await db.Users.AsNoTracking()
                .Where(u => responsibleIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        // Manba tomoni: shu kassalarga tegishli BARCHA qatorlar.
        var outgoing = await db.CashBoxTransactions.AsNoTracking()
            .Where(t => ids.Contains(t.CashBoxId))
            .Select(t => new { t.CashBoxId, t.Kind, t.Amount, t.Method, t.ToMethod, t.ReversalOf })
            .ToListAsync(ct);

        // Manzil tomoni: shu kassalarga KO'CHIRILGAN qatorlar (faqat transfer).
        var incoming = await db.CashBoxTransactions.AsNoTracking()
            .Where(t => t.Kind == CashBoxTransactionKind.Transfer
                        && t.TransferToBoxId != null && ids.Contains(t.TransferToBoxId.Value))
            .Select(t => new { CashBoxId = t.TransferToBoxId!.Value, t.Amount, t.Method, t.ReversalOf })
            .ToListAsync(ct);

        return [.. boxes.Select(box =>
        {
            var byMethod = new Dictionary<string, decimal>(StringComparer.Ordinal);
            foreach (var m in PaymentMethod.All) byMethod[m] = 0m;

            foreach (var t in outgoing.Where(t => t.CashBoxId == box.Id))
                foreach (var (method, delta) in Contributions(new CashBoxTransaction
                {
                    CashBoxId = t.CashBoxId, Kind = t.Kind, Amount = t.Amount,
                    Method = t.Method, ToMethod = t.ToMethod, ReversalOf = t.ReversalOf,
                }))
                    byMethod[method] = decimal.Round(byMethod[method] + delta, MoneyScale);

            foreach (var t in incoming.Where(t => t.CashBoxId == box.Id))
            {
                var sign = t.ReversalOf is null ? 1m : -1m;
                byMethod[t.Method] = decimal.Round(byMethod[t.Method] + sign * t.Amount, MoneyScale);
            }

            return new CashBoxDto(
                box.Id, box.Name, box.ResponsibleUserId,
                box.ResponsibleUserId is null ? null : names.GetValueOrDefault(box.ResponsibleUserId, "—"),
                decimal.Round(byMethod.Values.Sum(), MoneyScale),
                byMethod,
                box.IsDefault, box.IsActive);
        })];
    }

    private async Task<List<CashBoxTransactionRowDto>> ToRowDtosAsync(
        IReadOnlyList<CashBoxTransaction> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];

        var ids = rows.Select(r => r.Id).ToList();

        var userIds = rows.Select(r => r.CreatedBy).Distinct().ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        var studentIds = rows.Select(r => r.StudentId).Where(x => x is not null).Select(x => x!)
            .Distinct().ToList();
        var contractNos = studentIds.Count == 0
            ? new Dictionary<string, string?>(StringComparer.Ordinal)
            : await LatestContractNumbersAsync(studentIds, ct);

        // Tranzaksiya turi nomi — `Who`/`ContractNo` kabi, id emas, HAL
        // QILINGAN qiymat ko'rsatiladi (fayl boshidagi `CashBoxTransactionRowDto` izohi).
        var typeIds = rows.Select(r => r.TransactionTypeId).Where(x => x is not null).Select(x => x!.Value)
            .Distinct().ToList();
        var typeNames = typeIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.TransactionTypes.AsNoTracking()
                .Where(t => typeIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        // "Bekor qilindi" — shu qatorlardan qay biri boshqasining stornosi
        // (`reversal_of`) ekanligi. Storno qatorining IZOHI — bekor qilish
        // sababi (`CancelTransactionAsync` uni shu yerga yozadi), shuning
        // uchun id bilan birga izoh ham olinadi: jadvaldagi "Sabab" ustuni.
        var reversals = await db.CashBoxTransactions.AsNoTracking()
            .Where(t => t.ReversalOf != null && ids.Contains(t.ReversalOf.Value))
            .Select(t => new { Original = t.ReversalOf!.Value, t.Note })
            .ToListAsync(ct);

        var reversedIds = reversals.Select(x => x.Original).ToList();
        var cancelReasons = reversals
            .GroupBy(x => x.Original)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Note).FirstOrDefault(n => n is not null));

        return [.. rows.Select((r, idx) => new CashBoxTransactionRowDto(
            r.Id,
            idx + 1,
            AppClock.LocalDateOf(r.CreatedAt),
            names.GetValueOrDefault(r.CreatedBy, "—"),
            r.StudentId is null ? null : contractNos.GetValueOrDefault(r.StudentId),
            r.Amount,
            r.Kind,
            r.Method,
            DisplayStatus(r, reversedIds.Contains(r.Id)),
            r.TransactionTypeId is null ? null : typeNames.GetValueOrDefault(r.TransactionTypeId.Value),
            // Stornoning o'z izohi AYNAN bekor qilish sababi, shuning uchun u
            // faqat "Sabab" ustunida chiqadi — ikkala ustunda bir xil matn
            // turmasin.
            r.Status == CashBoxTransactionStatus.Reversal ? null : r.Note,
            r.Status == CashBoxTransactionStatus.Reversal
                ? r.Note
                : cancelReasons.GetValueOrDefault(r.Id),
            r.CreatedAt))];
    }

    /// <summary>
    /// O'quvchining ENG SO'NGGI shartnoma raqami (bo'lsa). Ko'p bo'lsa —
    /// imzolangan sanasi bo'yicha eng yangisi, so'ng yaratilgan vaqti bo'yicha.
    /// </summary>
    private async Task<Dictionary<string, string?>> LatestContractNumbersAsync(
        List<string> studentIds, CancellationToken ct)
    {
        var contracts = await db.StudentContracts.AsNoTracking()
            .Where(c => studentIds.Contains(c.StudentId))
            .Select(c => new { c.StudentId, c.Number, c.SignedOn })
            .ToListAsync(ct);

        return contracts
            .GroupBy(c => c.StudentId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(c => c.SignedOn).Select(c => c.Number).FirstOrDefault(),
                StringComparer.Ordinal);
    }

    /// <summary>
    /// Ko'rsatiladigan holat — bazadagi <c>status</c> ustunidan FARQLI:
    /// u faqat qator O'ZI storno EKANLIGINI (<c>reversal</c>) bildiradi;
    /// "bekor qilindi" (<c>cancelled</c>) esa boshqa qatordan (uni bekor
    /// qilgan storno bor-yo'qligidan) hisoblanadi — <c>ExpenseStatus</c> dagi
    /// bilan bir xil naqsh.
    /// </summary>
    private static string DisplayStatus(CashBoxTransaction row, bool hasReversal) =>
        row.Status == CashBoxTransactionStatus.Reversal ? "reversal"
        : hasReversal ? "cancelled"
        : "posted";

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private async Task<string?> RequireResponsibleAsync(string? responsibleUserId, CancellationToken ct)
    {
        var id = Trim(responsibleUserId);
        if (id is null) return null;

        if (!await db.Users.AsNoTracking().AnyAsync(u => u.Id == id, ct))
            throw BillingRuleException.NotFound("user_not_found", "Mas'ul xodim topilmadi.");

        return id;
    }

    private static object Snapshot(CashBox box) => new
    {
        box.Id, box.Name, box.ResponsibleUserId, box.IsDefault, box.IsActive,
    };

    private static object Snapshot(CashBoxTransaction t) => new
    {
        t.Id, t.CashBoxId, t.Kind, t.Amount, t.Method, t.ToMethod, t.StudentId,
        t.Note, t.Status, t.CreatedBy, t.TransferToBoxId, t.ReversalOf, t.TransactionTypeId,
    };

    private static string RequireName(string? name)
    {
        var clean = name?.Trim();
        return !string.IsNullOrWhiteSpace(clean)
            ? clean
            : throw BillingRuleException.Invalid("invalid_name", "Kassa nomi bo'sh bo'lishi mumkin emas.");
    }

    private static string RequireMethod(string? method) =>
        method is not null && PaymentMethod.All.Contains(method, StringComparer.Ordinal)
            ? method
            : throw BillingRuleException.Invalid("invalid_method",
                $"Noma'lum to'lov usuli: '{method}'. Ruxsat etilganlar: {string.Join(", ", PaymentMethod.All)}.");

    /// <summary>
    /// Summa musbat va baza aniqligiga (2 kasr) MOS bo'lishi shart. Yaxlitlab
    /// yubormaymiz: 1000.005 "qabul qilindi" bo'lib ko'rinib, bazada boshqa
    /// raqam bo'lib qolardi.
    /// </summary>
    private static decimal Money(decimal value)
    {
        if (decimal.Round(value, MoneyScale) != value)
            throw BillingRuleException.Invalid("invalid_amount",
                $"Summa tiyin aniqligida (2 kasr) bo'lishi shart: {value}.");
        if (value <= 0m)
            throw BillingRuleException.Invalid("invalid_amount", "Summa musbat bo'lishi shart.");
        return value;
    }

    /// <summary>
    /// Tanlangan sanani yozuv lahzasiga o'giradi (<c>null</c> — hozir).
    /// Chegaralar: kelajak YO'Q va <see cref="MaxBackdateDays"/> kundan uzoq orqaga
    /// ham yo'q — izoh: <see cref="CashBoxPayInRequest.Date"/>.
    /// </summary>
    private static DateTimeOffset ResolveEntryInstant(DateOnly? date)
    {
        if (date is not { } picked) return AppClock.NowInstant;

        var today = AppClock.Today;
        if (picked > today)
            throw BillingRuleException.Invalid("future_date",
                "Kelajakdagi sana bilan yozib bo'lmaydi — eng kechi bugun.");

        var daysBack = today.DayNumber - picked.DayNumber;
        if (daysBack > MaxBackdateDays)
            throw BillingRuleException.Invalid("date_too_old",
                $"Sana juda eski: eng ko'pi bilan {MaxBackdateDays} kun orqaga yozish mumkin.");

        return AppClock.InstantOn(picked);
    }

    /// <summary>
    /// Orqadagi sana AUDITDA ko'rinadi: qator qaysi kunga tushgani hisobotdan
    /// o'qiladi, lekin uni KIM va QACHON o'sha kun bilan yozgani faqat audit
    /// yozuvidan bilinadi. Bugungi yozuvga hech narsa qo'shilmaydi.
    /// </summary>
    private static string BackdateNote(DateTimeOffset createdAt)
    {
        var day = AppClock.LocalDateOf(createdAt);
        return day == AppClock.Today ? string.Empty : $" — {day:dd.MM.yyyy} sanasi bilan";
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

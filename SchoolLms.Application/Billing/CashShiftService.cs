using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

/// <summary>
/// Kassa smenasi, uzluksiz chek raqami va Z-hisobot (SPEC §4.2, §4.6). Vazifa: P1-10.
///
/// <para>
/// <b>Smenaning ma'nosi.</b> Kassir ishni smena ochish bilan boshlaydi va uni
/// sanalgan naqd bilan yopadi. Har to'lov <c>cash_shift_id</c> ni olib yuradi,
/// ya'ni kun oxirida "kassada qancha pul bo'lishi kerak edi" degan savolga
/// javob bitta jadvaldan chiqadi, kassirning xotirasidan emas.
/// </para>
///
/// <para>
/// <b>UCH QAT'IY QOIDA — bularsiz butun modul ma'nosini yo'qotadi.</b>
/// </para>
/// <list type="number">
///   <item>
///     <b><c>expected_cash</c> HECH QACHON mijozdan kelmaydi</b> — u LEDGER'dan
///     hisoblanadi (<see cref="ExpectedCashAsync"/>). Mijoz faqat
///     <c>counted_cash</c> ni (qo'lda sanagan pulni) beradi. Aks holda kassir
///     kutilgan summani o'zi yozib, nomuvofiqlikni nolga tenglashtira olardi.
///   </item>
///   <item>
///     <b>FAQAT <c>cash</c> sanaladi</b> (mijoz javobi, SPEC §8.1 Q13). Karta,
///     o'tkazma va onlayn to'lovlar to'g'ridan-to'g'ri bank hisobiga tushadi —
///     ularni kassirdan naqd sifatida talab qilish har smenada SOXTA kamomad
///     berardi. Qoida bitta joyda: <see cref="PaymentMethod.CountsAsCash"/>.
///   </item>
///   <item>
///     <b><c>variance</c> ni ilova YOZMAYDI</b> — u bazada
///     <c>generated always as (counted_cash - expected_cash) stored</c>
///     (BillingModel.cs). Shuning uchun <see cref="CashShift.Variance"/> da
///     setter yopiq va bu faylda unga birorta ham o'zlashtirish yo'q. Smena
///     yopilgandan keyin nomuvofiqlikni "to'g'rilab" qo'yish imkonsiz.
///   </item>
/// </list>
///
/// <para>
/// <b>SMENA CHEGARASI QANDAY ANIQLANADI.</b> Smenaga tegishli to'lovlar —
/// <c>payments.cash_shift_id</c> bo'yicha (ustunning ma'nosi aynan shu, va uni
/// FK kafolatlaydi), vaqt oralig'i bo'yicha EMAS. Ledger tomonida ham xuddi
/// shu to'plam ishlatiladi: <c>account = 'cash'</c> bo'lgan va <c>ref_id</c> i
/// shu smenaning to'lovlaridan biri bo'lgan yozuvlar. STORNO bundan mustasno
/// emas, faqat u bir qadam orqali topiladi: ko'zgu satr originalning
/// <c>ref_id</c> sini saqlaydi, storno <c>payments</c> qatori esa
/// tasdiqlovchining smenasida turadi — shuning uchun
/// <see cref="ExpectedCashAsync"/> qaytarilgan to'lovlarni originalning id'si
/// orqali oladi. Ikkala tomon BIR XIL to'plamdan kelib chiqqani uchun
/// <c>opening_float + (Z-hisobotdagi cash qatori)
///  − (Z-hisobotdagi "Chiqimlar") − (Z-hisobotdagi "Topshirilgan")
///  == expected_cash</c>
/// invarianti har doim bajariladi — aks holda ikkita "haqiqat" paydo bo'lardi
/// va qaysi biri to'g'riligini hech kim ayta olmasdi. Oxirgi ikki had F1.03 va
/// F1.04 bilan qo'shildi va ikkalasini ham AYNAN bitta metod hisoblaydi
/// (<see cref="CashOutflowAsync"/>), ya'ni invariant kod tuzilishidan kelib
/// chiqadi, kelishuvdan emas. Uni <c>CashDeskOutflowTests</c> har yurishda
/// tekshiradi.
/// </para>
/// <para>
/// Vaqt oralig'i bo'yicha filtrlash ATAYLAB ishlatilmadi: <c>ledger_entries</c>
/// da smena ustuni yo'q, shuning uchun "smena vaqtida yozilgan cash yozuvlari"
/// ikki kassir bir vaqtda ishlaganda BIR-BIRINIKINI ham qamrab olardi.
/// </para>
/// </summary>
public sealed class CashShiftService(IAppDbContext db) : ICashShiftService
{
    /// <summary>Baza ustuni <c>numeric(14,2)</c> — hisob-kitob ham shu aniqlikda.</summary>
    private const int MoneyScale = 2;

    /// <summary>
    /// Ro'yxat so'rovining yuqori chegarasi (eng yangisidan). Smena kuniga 1–2 ta
    /// ochiladi, ya'ni 500 ta ≈ bir yildan ko'proq tarix. Chegara bor, chunki
    /// ro'yxatning har qatori uchun to'lovlar yig'indisi ham o'qiladi: filtrsiz
    /// so'rov bir kun kelib butun tarixni bitta <c>IN (...)</c> ga solib qo'yardi.
    /// Direktor paneli baribir sana bo'yicha filtrlaydi.
    /// </summary>
    private const int MaxListRows = 500;

    /// <summary>
    /// <b>SMENA BO'YICHA MAXSUS QULF (advisory lock)</b> — chek raqamining
    /// uzluksizligi shunga tayanadi. Batafsil: <see cref="NextReceiptNoAsync"/>.
    ///
    /// <para>
    /// Kalit satr bilan nomlangan (<c>cash_shift_receipt:{id}</c>), chunki
    /// advisory lock'ning 64-bitli fazosi butun bazada YAGONA: agar shu yerda
    /// shunchaki <c>shiftId</c> hash'i ishlatilsa, u <c>billing_guards.sql</c>
    /// dagi taqsimot qulfi (<c>payment_id</c> hash'i) bilan tasodifan to'qnash
    /// kelishi mumkin edi. To'qnashuv xatoga olib kelmaydi, lekin ikki mutlaqo
    /// bog'liq bo'lmagan amalni bir-birini kutishga majbur qilardi — va buni
    /// keyinchalik tushuntirish juda qiyin bo'lardi.
    /// </para>
    /// </summary>
    private const string LockSql = "SELECT pg_advisory_xact_lock(hashtextextended({0}::text, 0))";

    /// <summary>
    /// Smena qulfining KALITI — <b>ochiq</b>, chunki uni shu fayldan tashqarida
    /// ham olish kerak (F1.03).
    ///
    /// <para>
    /// <b>Nega ochiq.</b> Naqd chiqim va kassadan topshiriq smenaga
    /// biriktiriladi, ya'ni ikkalasi ham "smena hali ochiqmi?" deb tekshirib,
    /// so'ng yozadi. Bu tekshir-va-yoz esa <see cref="CloseAsync"/> bilan
    /// poyga: qulfsiz chiqim smena yopilgandan KEYIN unga biriktirilib
    /// qolardi va <c>expected_cash</c> uni hech qachon ko'rmasdi — ya'ni
    /// F1.03 ning o'zi, faqat kamroq uchraydigan ko'rinishda. Kalitni
    /// nusxalash o'rniga shu yerdan berish kerak: ikkita "bir xil" satr bir
    /// kun albatta bir-biridan uzoqlashadi va o'shanda ikki amal BIR-BIRINI
    /// KUTMAY qo'yardi — xato esa faqat yuk ostida ko'rinardi.
    /// </para>
    /// </summary>
    public static string ShiftLockKey(Guid shiftId) => $"cash_shift_receipt:{shiftId:D}";

    private static string LockKey(Guid shiftId) => ShiftLockKey(shiftId);

    /// <summary>
    /// Xom SQL uchun kontekstning o'zi. <see cref="IAppDbContext"/> da
    /// <c>Database</c> yo'q (u ataylab tor interfeys), advisory lock esa
    /// EF LINQ bilan ifodalab bo'lmaydigan yagona narsa.
    /// </summary>
    private readonly DbContext ef = db as DbContext ?? throw new ArgumentException(
        $"{nameof(CashShiftService)} EF kontekstini talab qiladi: chek raqami qulfi xom SQL "
        + "orqali qo'yiladi. Berilgan implementatsiya DbContext emas.", nameof(db));

    // =====================================================================
    //  Smena ochish / joriy smena
    // =====================================================================

    /// <inheritdoc />
    public async Task<CashShiftDto> OpenAsync(
        string cashierId, decimal openingFloat, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cashierId))
            throw new ArgumentException("Kassir id'si bo'sh (SPEC §4.4 — u JWT'dan keladi).", nameof(cashierId));

        // Ochilish qoldig'i: sukut 0, kassir kiritishi mumkin (docs/ASSUMPTIONS.md, Q11).
        var floatAmount = decimal.Round(openingFloat, MoneyScale);
        if (floatAmount < 0m)
            throw new CashShiftException(CashShiftError.InvalidOpeningFloat,
                "Ochilish qoldig'i manfiy bo'lishi mumkin emas.");

        // Oldindan tekshiruv — ODATIY holatda tushunarli xabar berish uchun.
        // HAQIQIY kafolat esa pastda: bazadagi qisman unikal indeks.
        var existing = await db.CashShifts.AsNoTracking()
            .FirstOrDefaultAsync(s => s.CashierId == cashierId && s.Status == CashShiftStatus.Open, ct);
        if (existing is not null)
            throw new CashShiftException(CashShiftError.AlreadyOpen,
                "Sizda allaqachon ochiq smena bor. Avval uni yoping (SPEC §4.2).");

        var shift = new CashShift
        {
            CashierId = cashierId,
            OpenedAt = AppClock.NowInstant,
            OpeningFloat = floatAmount,
            Status = CashShiftStatus.Open,
        };
        db.CashShifts.Add(shift);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, OneOpenShiftIndex))
        {
            // Ikki so'rov bir vaqtda kelgan holat. Yuqoridagi tekshiruv buni
            // ushlay olmaydi (ikkalasi ham "ochiq smena yo'q" deb ko'radi) —
            // shuning uchun oxirgi so'z bazaniki.
            throw new CashShiftException(CashShiftError.AlreadyOpen,
                "Sizda allaqachon ochiq smena bor. Avval uni yoping (SPEC §4.2).");
        }

        return await SingleDtoAsync(shift, ct);
    }

    /// <inheritdoc />
    public async Task<CashShiftDto?> CurrentAsync(string cashierId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cashierId))
            throw new ArgumentException("Kassir id'si bo'sh.", nameof(cashierId));

        var shift = await db.CashShifts.AsNoTracking()
            .FirstOrDefaultAsync(s => s.CashierId == cashierId && s.Status == CashShiftStatus.Open, ct);

        return shift is null ? null : await SingleDtoAsync(shift, ct);
    }

    // =====================================================================
    //  Smena yopish
    // =====================================================================

    /// <inheritdoc />
    public async Task<CashShiftDto> CloseAsync(
        Guid shiftId, string closedByUserId, decimal countedCash, string? note,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(closedByUserId))
            throw new ArgumentException("Yopayotgan foydalanuvchi id'si bo'sh (SPEC §4.4).", nameof(closedByUserId));

        var counted = decimal.Round(countedCash, MoneyScale);
        if (counted < 0m)
            throw new CashShiftException(CashShiftError.InvalidCountedCash,
                "Sanalgan naqd manfiy bo'lishi mumkin emas.");

        // Yopish IKKI narsani bitta atomar amalga birlashtiradi: kutilgan naqdni
        // hisoblash va smenani yopiq deb belgilash. Orasida yangi to'lov tushsa,
        // `expected_cash` o'sha to'lovni hisobga olmagan bo'lardi va kassir
        // ayblanardi. Shuning uchun shu yerda ham AYNAN chek raqami qulfi
        // olinadi — ya'ni yopish va yangi chek berish bir-birini istisno qiladi.
        var owned = ef.Database.CurrentTransaction is null
            ? await db.BeginTransactionAsync(ct)
            : null;
        try
        {
            await LockShiftAsync(shiftId, ct);

            var shift = await db.CashShifts.FirstOrDefaultAsync(s => s.Id == shiftId, ct)
                ?? throw new CashShiftException(CashShiftError.NotFound, $"Smena topilmadi: {shiftId}.");

            if (shift.Status != CashShiftStatus.Open)
                throw new CashShiftException(CashShiftError.AlreadyClosed,
                    "Bu smena allaqachon yopilgan. Yopilgan smenani qayta yopib bo'lmaydi.");

            var closedByName = await RequireMayCloseAsync(shift, closedByUserId, ct);

            var expected = await ExpectedCashAsync(shift, ct);

            shift.ExpectedCash = expected;
            shift.CountedCash = counted;
            shift.ClosedAt = AppClock.NowInstant;
            shift.ClosedBy = closedByUserId;
            shift.Status = CashShiftStatus.Closed;
            // DIQQAT: `shift.Variance` ga BU YERDA HAM, BOSHQA JOYDA HAM
            // o'zlashtirish yo'q — uni baza hisoblaydi (SPEC §4.2).

            WriteAuditTrail(shift, closedByName, counted, expected, note);

            await db.SaveChangesAsync(ct);
            if (owned is not null) await owned.CommitAsync(ct);
        }
        finally
        {
            if (owned is not null) await owned.DisposeAsync();
        }

        // `variance` — bazada hisoblanadigan ustun, shuning uchun uning qiymati
        // faqat SaveChanges'dan KEYIN bazadan o'qilganda ishonchli bo'ladi.
        var saved = await db.CashShifts.AsNoTracking().FirstAsync(s => s.Id == shiftId, ct);
        return await SingleDtoAsync(saved, ct);
    }

    /// <summary>
    /// SPEC §4.2: kassir o'zganing smenasini yopa olmaydi; admin va direktor —
    /// yopa oladi va <c>closed_by</c> da AYNAN kim yopgani qoladi.
    ///
    /// <para>
    /// Tekshiruv controller'da emas, SHU YERDA: rol darvozasi (<c>FinanceRole</c>)
    /// "kassaga kira oladimi" degan savolga javob beradi, "AYNAN SHU smena
    /// seniki mi" degan savolga emas. Xizmat rolni foydalanuvchi qatoridan
    /// o'qiydi — ya'ni endpoint chetlab o'tilsa ham qoida kuchda qoladi.
    /// </para>
    /// </summary>
    /// <returns>Yopayotgan foydalanuvchining ismi — audit yozuvi uchun.</returns>
    private async Task<string> RequireMayCloseAsync(
        CashShift shift, string closedByUserId, CancellationToken ct)
    {
        // Bitta so'rov ikki savolga javob beradi: "kim bu?" (audit uchun ism) va
        // "roli nima?" (o'zganing smenasini yopishga haqlimi).
        var actor = await db.Users.AsNoTracking()
            .Where(u => u.Id == closedByUserId)
            .Select(u => new { u.Role, u.FullName })
            .FirstOrDefaultAsync(ct);

        var isOwner = string.Equals(shift.CashierId, closedByUserId, StringComparison.Ordinal);
        if (!isOwner && !IsSupervisorRole(actor?.Role))
            throw new CashShiftException(CashShiftError.NotYourShift,
                "Bu smena boshqa kassirniki. O'zganing smenasini faqat admin yoki direktor yopadi (SPEC §4.2).");

        return actor?.FullName ?? "Noma'lum";
    }

    /// <summary>
    /// SPEC §4.6 — har moliyaviy amal <c>audit_log</c> ga ham tushadi.
    ///
    /// <para>
    /// Bu yerda audit yozuvining IKKINCHI vazifasi ham bor: kassirning izohiga
    /// (<c>note</c>) uy topish. <c>cash_shifts</c> da izoh ustuni yo'q va uni
    /// qo'shish yangi migratsiya degani; izohni jimgina yo'qotish esa eng yomon
    /// variant — "50 ming kam chiqdi, ertaga qaytaraman" yozuvi aynan
    /// nomuvofiqlik tekshirilayotganda kerak bo'ladi.
    /// </para>
    /// </summary>
    private void WriteAuditTrail(
        CashShift shift, string closedByName, decimal counted, decimal expected, string? note)
    {
        var variance = counted - expected;
        var summary = variance == 0m
            ? $"Smena yopildi: kutilgan {Money(expected)}, sanalgan {Money(counted)} so'm — nomuvofiqlik yo'q"
            : $"Smena yopildi: kutilgan {Money(expected)}, sanalgan {Money(counted)} so'm — "
              + $"nomuvofiqlik {Money(variance)} so'm";
        if (!string.IsNullOrWhiteSpace(note)) summary += $". Izoh: {note.Trim()}";

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = AuditEntityCashShift,
            EntityId = shift.Id.ToString(),
            Action = "close",
            Timestamp = AppClock.Iso(),
            ActorId = shift.ClosedBy,
            ActorName = closedByName,
            Summary = summary,
            After = System.Text.Json.JsonSerializer.Serialize(new
            {
                shift.CashierId,
                shift.OpeningFloat,
                ExpectedCash = expected,
                CountedCash = counted,
                Variance = variance,
                Note = note,
            }),
        });
    }

    // =====================================================================
    //  Chek raqami — UZLUKSIZ (SPEC §4.2)
    // =====================================================================

    /// <inheritdoc />
    /// <remarks>
    /// <b>NEGA ADVISORY LOCK, "unique violation'da qayta urinish" EMAS.</b>
    ///
    /// <para>
    /// Uchta sabab, har biri alohida yetarli.
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     <b>Qayta urinishni bu metod bajara olmaydi.</b> U raqamni QAYTARADI,
    ///     <c>payments</c> qatorini esa chaqiruvchi (P1-11) yozadi. Ya'ni
    ///     23505 xatosi bu metoddan ANCHA KEYIN, boshqa kodda chiqadi.
    ///     PostgreSQL'da esa xato bo'lgan statement butun tranzaksiyani
    ///     yaroqsiz qiladi: qayta urinish uchun har to'lovni <c>SAVEPOINT</c>
    ///     bilan o'rash yoki butun tranzaksiyani (to'lov + taqsimotlar +
    ///     ledger) qaytadan yurgizish kerak bo'lardi. Bu "chek raqami" mantig'ini
    ///     ikkita fayl orasida bo'lib yuboradi va imzo shuni ko'rsatmaydi ham.
    ///   </item>
    ///   <item>
    ///     <b>Qayta urinish 50 ta parallel chaqiruvda yiqiladi.</b> Qulfsiz
    ///     <c>max + 1</c> ni ellikta tranzaksiya BIR XIL o'qiydi (READ COMMITTED
    ///     boshqasining commit qilinmagan qatorini ko'rmaydi), ya'ni 49 tasi
    ///     yiqilib qayta uradi, keyin 48 tasi... Bu O(n²) urinish va jonli
    ///     kassada vaqt bo'yicha ochiq oxiri bo'lgan tsikl.
    ///   </item>
    ///   <item>
    ///     <b>Repozitoriyada allaqachon shu yechim ishlatilgan.</b>
    ///     <c>Migrations/Sql/billing_guards.sql</c> dagi taqsimot trigger'i
    ///     ham aynan <c>pg_advisory_xact_lock</c> ni ishlatadi va sababi o'sha
    ///     yerda yozilgan: <c>SELECT ... FOR UPDATE</c> <c>payments</c> jadvalida
    ///     UPDATE huquqini talab qiladi, <c>app_rw</c> da esa u ATAYLAB yo'q
    ///     (SPEC §4.1) — ya'ni qator qulfi kerak bo'lgan rolda 42501 beradi.
    ///     Advisory lock hech qanday jadval huquqini talab qilmaydi va
    ///     tranzaksiya tugashi bilan O'ZI bo'shaydi.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// <b>NEGA TRANZAKSIYA MAJBURIY.</b> <c>pg_advisory_xact_lock</c> tranzaksiya
    /// oxirida bo'shaydi. Tranzaksiyasiz chaqirilsa har statement o'zining
    /// mayda tranzaksiyasida ketadi va qulf shu zahoti bo'shaydi — ya'ni himoya
    /// BO'LMAYDI, lekin kod ishlayotgandek ko'rinadi. Bu jimgina buziladigan
    /// holat, shuning uchun metod ochiq tranzaksiyasiz chaqirilsa YIQILADI.
    /// </para>
    ///
    /// <para>
    /// <b>BEKOR QILINGAN TO'LOV TESHIK QOLDIRMAYDI.</b> Raqam ajratish va
    /// <c>payments</c> ga yozish bitta tranzaksiyada bo'lgani uchun, tranzaksiya
    /// qaytarilsa raqam ham "ishlatilmagan" bo'lib qoladi va keyingi chaqiruv
    /// AYNAN o'sha raqamni oladi. Auditor uchun bo'shliq = o'chirilgan chek
    /// degani, shuning uchun bu farq hal qiluvchi.
    /// </para>
    /// </remarks>
    public async Task<long> NextReceiptNoAsync(Guid shiftId, CancellationToken ct = default)
    {
        if (ef.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                $"{nameof(NextReceiptNoAsync)} ochiq tranzaksiya ichida chaqirilishi SHART "
                + $"({nameof(IAppDbContext)}.{nameof(IAppDbContext.BeginTransactionAsync)}). Qulf tranzaksiya "
                + "oxirida bo'shaydi; tranzaksiyasiz chaqiriq chek raqamini uzluksiz QILMAYDI.");

        // 1. Smena bo'yicha qulf. Shu smenaga raqam so'ragan qolgan hamma
        //    tranzaksiya shu yerda navbatda turadi (boshqa smenalarga ta'sir yo'q).
        await LockShiftAsync(shiftId, ct);

        // 2. Qulf ostida holatni tekshiramiz. Yopilgan smenaga chek berish
        //    Z-hisobotni orqadan buzardi: `expected_cash` allaqachon hisoblangan.
        var status = await db.CashShifts.AsNoTracking()
            .Where(s => s.Id == shiftId)
            .Select(s => s.Status)
            .FirstOrDefaultAsync(ct)
            ?? throw new CashShiftException(CashShiftError.NotFound, $"Smena topilmadi: {shiftId}.");

        if (status != CashShiftStatus.Open)
            throw new CashShiftException(CashShiftError.NotOpen,
                "Smena yopilgan — unga yangi chek berib bo'lmaydi.");

        // 3. Keyingi raqam. `unique (cash_shift_id, receipt_no)` indeksi bu
        //    so'rovni ham tez, ham ikkinchi qavat himoya qiladi.
        var last = await db.Payments.AsNoTracking()
            .Where(p => p.CashShiftId == shiftId)
            .MaxAsync(p => (long?)p.ReceiptNo, ct) ?? 0L;

        return last + 1;
    }

    private Task LockShiftAsync(Guid shiftId, CancellationToken ct) =>
        ef.Database.ExecuteSqlRawAsync(LockSql, [LockKey(shiftId)], ct);

    // =====================================================================
    //  Z-hisobot va ro'yxat
    // =====================================================================

    /// <inheritdoc />
    public async Task<ZReportDto> ZReportAsync(Guid shiftId, CancellationToken ct = default)
    {
        var shift = await db.CashShifts.AsNoTracking().FirstOrDefaultAsync(s => s.Id == shiftId, ct)
            ?? throw new CashShiftException(CashShiftError.NotFound, $"Smena topilmadi: {shiftId}.");

        var totals = await MethodTotalsAsync([shiftId], ct);

        // Usullar kesimi: TO'RTTASI HAM chiqadi, nol bo'lsa ham. Yo'q qator —
        // yo'qolgan qator: "karta bo'yicha nechta?" degan savolga jadvalda
        // javob turishi kerak, uni topolmaganidan ko'ra.
        var byMethod = PaymentMethod.All
            .Select(method =>
            {
                var rows = totals.Where(t => t.Method == method).ToList();
                return new ZReportMethodRowDto(
                    method,
                    rows.Sum(r => r.Count),
                    decimal.Round(rows.Sum(r => r.Signed), MoneyScale));
            })
            .ToList();

        // Toifalar kesimi — bitta so'rov (allocations → invoices → categories).
        // Storno qatorlari bu yerda KO'RINMAYDI: `payment_allocations.amount > 0`
        // check constraint'i manfiy taqsimotni umuman mumkin qilmaydi, ya'ni
        // storno taqsimot yozmaydi. Shuning uchun "nimaga to'landi" kesimi
        // to'lovlar bo'yicha, "qancha qoldi" esa usullar kesimi bo'yicha o'qiladi.
        var byCategory = await (
            from allocation in db.PaymentAllocations.AsNoTracking()
            join payment in db.Payments.AsNoTracking() on allocation.PaymentId equals payment.Id
            join invoice in db.Invoices.AsNoTracking() on allocation.InvoiceId equals invoice.Id
            join category in db.FeeCategories.AsNoTracking() on invoice.CategoryId equals category.Id
            where payment.CashShiftId == shiftId
            group allocation.Amount by new { category.Id, category.Code, category.Name } into g
            orderby g.Key.Code
            select new ZReportCategoryRowDto(g.Key.Id, g.Key.Code, g.Key.Name, g.Sum()))
            .ToListAsync(ct);

        var receipts = db.Payments.AsNoTracking().Where(p => p.CashShiftId == shiftId);
        var receiptFrom = await receipts.MinAsync(p => (long?)p.ReceiptNo, ct);
        var receiptTo = await receipts.MaxAsync(p => (long?)p.ReceiptNo, ct);

        // "Chiqimlar" va "Topshirilgan" qatorlari (F1.03, F1.04) — AYNAN
        // `ExpectedCashAsync` ishlatadigan metoddan. Bu Z-hisobotning eng
        // muhim qo'shimchasi: usullar kesimidagi naqd tushum bilan
        // `expected_cash` orasidagi FARQNI tushuntiradigan yagona qator
        // shu edi, va u yo'q edi.
        var outflow = await CashOutflowAsync(shift, ct);

        return new ZReportDto(
            Shift: await SingleDtoAsync(shift, ct),
            ByMethod: byMethod,
            ByCategory: byCategory,
            ReceiptFrom: receiptFrom,
            ReceiptTo: receiptTo,
            ReversalsCount: totals.Where(t => t.IsReversal).Sum(t => t.Count),
            CashExpensesTotal: outflow.Expenses,
            CashExpensesCount: outflow.ExpensesCount,
            CashHandoversTotal: outflow.Handovers,
            CashHandoversCount: outflow.HandoversCount);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CashShiftDto>> ListAsync(
        CashShiftQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var q = db.CashShifts.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.CashierId))
            q = q.Where(s => s.CashierId == query.CashierId);

        if (!string.IsNullOrWhiteSpace(query.Status))
            q = q.Where(s => s.Status == query.Status);

        // Nomuvofiqlik NOLDAN farqli bo'lganlari (direktor paneli, SPEC §4.6).
        // Ochiq smenada `variance` null, ya'ni ular o'z-o'zidan tushib qoladi.
        if (query.OnlyWithVariance)
            q = q.Where(s => s.Variance != null && s.Variance != 0m);

        // Sana chegaralari IKKI BOSQICHDA. Bazada — bir kun zaxira bilan keng
        // oraliq (indeksdan foydalanadi), xotirada — Toshkent kunini AYNAN
        // hisoblab aniq kesish. Sababi: `opened_at` — `timestamptz`, "Toshkent
        // taqvimidagi qaysi kun" degan savolni esa LINQ provayderi xom SQL
        // castisiz ifodalay olmaydi, xato esa yarim tunda chiqadigan turdan
        // bo'lardi (UTC+5 da kun 19:00 da almashadi).
        if (query.From is { } from)
        {
            var lower = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(-1);
            q = q.Where(s => s.OpenedAt >= lower);
        }
        if (query.To is { } to)
        {
            var upper = new DateTimeOffset(to.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero).AddDays(1);
            q = q.Where(s => s.OpenedAt <= upper);
        }

        var shifts = await q
            .OrderByDescending(s => s.OpenedAt)
            .Take(MaxListRows)
            .ToListAsync(ct);

        if (query.From is { } exactFrom)
            shifts = [.. shifts.Where(s => AppClock.LocalDateOf(s.OpenedAt) >= exactFrom)];
        if (query.To is { } exactTo)
            shifts = [.. shifts.Where(s => AppClock.LocalDateOf(s.OpenedAt) <= exactTo)];

        return await ToDtosAsync(shifts, ct);
    }

    // =====================================================================
    //  Hisob-kitob
    // =====================================================================

    /// <summary>
    /// Kutilgan naqd = ochilish qoldig'i + shu smena to'lovlari keltirgan
    /// <c>cash</c> harakati (debet − kredit) LEDGER bo'yicha − shu smenadan
    /// chiqqan naqd (<see cref="CashOutflowAsync"/>: chiqimlar va
    /// topshiriqlar).
    ///
    /// <para>
    /// <b>F1.03 dan OLDIN</b> formulada oxirgi had YO'Q edi: naqd chiqim
    /// jurnalga <c>credit cash</c> bo'lib tushardi, lekin smenaga umuman
    /// bog'lanmagani uchun bu yerdagi so'rov uni KO'RMASDI. Natija: kassadan
    /// pul chiqadi, kutilgan naqd esa o'zgarmaydi — smena AYNAN o'sha summaga
    /// kam pul bilan yopiladi va <c>shift_variance</c> bayrog'i aybsiz
    /// kassirning ustiga tushadi. Har naqd chiqim uchun, har kuni.
    /// </para>
    ///
    /// <para>
    /// Nega ledger'dan, <c>payments</c> dan emas — SPEC §4.2 shuni talab qiladi:
    /// moliyaviy haqiqatning yagona manbai jurnal. Karta/o'tkazma/onlayn
    /// to'lovlar bu yerga TUSHMAYDI, chunki <c>LedgerService</c> ularni
    /// <c>Accounts.Bank</c> ga yozadi (<see cref="Accounts.SettlementFor"/>) —
    /// ya'ni "faqat naqd sanaladi" qoidasi shu yerda qayta yozilmaydi, jurnalning
    /// o'zidan kelib chiqadi.
    /// </para>
    /// <para>
    /// Kredit ayriladi, chunki storno (<c>ref_type = 'reversal'</c>) kassadan
    /// pul chiqishini AYNAN shunday yozadi.
    /// </para>
    /// <para>
    /// <b>Bu qiymat smena yopilganda BIR MARTA yoziladi va keyin o'zgarmaydi.</b>
    /// Agar to'lov keyinroq (boshqa kunda) storno qilinsa, Z-hisobotning jonli
    /// kesimi o'zgaradi, lekin yopilgan smenadagi <c>expected_cash</c> /
    /// <c>counted_cash</c> / <c>variance</c> TEGILMAYDI — ular "o'sha oqshom
    /// kassada nima bo'lgani" ning yozuvi, keyingi tuzatishlarning emas.
    /// </para>
    /// </summary>
    private async Task<decimal> ExpectedCashAsync(CashShift shift, CancellationToken ct)
    {
        // Shu smenaning ODDIY to'lovlari. Jurnalda ular `ref_id = payments.id`
        // bilan yotadi.
        var shiftPaymentIds = db.Payments.AsNoTracking()
            .Where(p => p.CashShiftId == shift.Id && p.ReversalOf == null)
            .Select(p => (Guid?)p.Id);

        // Shu smenada QAYTARILGAN to'lovlar — ular ORIGINALNING id'si orqali
        // qidiriladi, chunki `LedgerService.ReverseAsync` ko'zgu satrga
        // originalning `ref_id` sini beradi (`reversal_of` esa qaysi satrni
        // teskari qilayotganini ko'rsatadi).
        //
        // NEGA SHUNDAY. Storno `payments` qatori TASDIQLOVCHINING smenasiga
        // yoziladi — pul aynan uning javonidan chiqadi. `ref_id` bo'yicha
        // to'g'ridan-to'g'ri qidirilsa esa ko'zgu satr ORIGINAL to'lov egasining
        // to'plamiga tushib qolardi: aybsiz kassir o'zi qaytarmagan pul uchun
        // ortiqcha qoldiq bilan, tasdiqlovchining kassasi esa nol farq bilan
        // yopilardi. `MethodTotalsAsync` allaqachon `payments.cash_shift_id`
        // bo'yicha guruhlaydi, ya'ni bu yerdagi to'plam bilan MOS bo'lishi shart —
        // aks holda Z-hisobot va `expected_cash` ikki xil javob berardi.
        var reversedOriginalIds = db.Payments.AsNoTracking()
            .Where(p => p.CashShiftId == shift.Id && p.ReversalOf != null)
            .Select(p => p.ReversalOf);

        var rows = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.Account == Accounts.Cash
                        && ((e.ReversalOf == null && shiftPaymentIds.Contains(e.RefId))
                            || (e.ReversalOf != null && reversedOriginalIds.Contains(e.RefId))))
            .GroupBy(e => e.Direction)
            .Select(g => new { Direction = g.Key, Total = g.Sum(x => x.Amount) })
            .ToListAsync(ct);

        var debit = rows.FirstOrDefault(r => r.Direction == LedgerDirection.Debit)?.Total ?? 0m;
        var credit = rows.FirstOrDefault(r => r.Direction == LedgerDirection.Credit)?.Total ?? 0m;

        // Javondan CHIQQAN naqd (F1.03, F1.04). AYNAN shu metod Z-hisobotni
        // ham to'ldiradi — ikkita ta'rif bo'lmasligi uchun (fayl boshidagi
        // invariant).
        var outflow = await CashOutflowAsync(shift, ct);

        return decimal.Round(
            shift.OpeningFloat + debit - credit - outflow.Expenses - outflow.Handovers,
            MoneyScale);
    }

    /// <summary>
    /// Shu smenaning javonidan CHIQQAN naqd: chiqimlar (F1.03) va bankka /
    /// seyfga topshirilgan pul (F1.04). Musbat son = kassadan chiqqan pul.
    ///
    /// <para>
    /// <b>Nega bitta metod.</b> <see cref="ExpectedCashAsync"/> ham,
    /// <see cref="ZReportAsync"/> ham shu yerdan o'qiydi. Ikkalasi o'z
    /// so'rovini yozsa, ular bir kun albatta bir-biridan farq qilardi va
    /// "kutilgan naqd" bilan "Z-hisobotdagi chiqimlar" qatori bir-birini rad
    /// etardi — tekshiruvchi qaysi biriga ishonishni bilmasdi.
    /// </para>
    ///
    /// <para>
    /// <b>CHIQIM — JURNALDAN, TOPSHIRIQ — JADVALDAN. Nega har xil.</b>
    /// Chiqim jurnalda <c>credit cash</c> bo'lib yotadi, ya'ni javobni
    /// jurnalning o'zi beradi. Topshiriqda esa <c>safe</c> manzili jurnalga
    /// UMUMAN yozilmaydi (pul maktabniki bo'lib qolaveradi, faqat javondan
    /// direktorning seyfiga ko'chadi — hisoblar rejasida ikkalasi ham
    /// <see cref="Accounts.Cash"/>). Shuning uchun topshiriq
    /// <c>cash_handovers</c> dan o'qiladi; <c>bank</c> manzilining jurnal
    /// satrlari esa bu yerda ATAYLAB hisobga olinmaydi — aks holda bitta
    /// topshiriq ikki marta ayirilardi.
    /// </para>
    ///
    /// <para>
    /// <b>Storno qaysi smenaga tushadi.</b> Topshiriqning qarshi qatori o'z
    /// <c>cash_shift_id</c> siga ega (pul AYNAN storno qilayotgan odamning
    /// javoniga qaytadi), ya'ni u shu yerda oddiy ayirma bo'lib chiqadi.
    /// Chiqim stornosida esa bunday ustun YO'Q va qo'shib bo'lmaydi
    /// (<c>ledger_entries</c> o'zgarmas, <c>expenses</c> ga ikkinchi qator
    /// yozilmaydi — <c>ExpenseService</c> fayl boshidagi izoh). Shuning uchun
    /// ko'zgu satr storno qiluvchining ISMI (<c>created_by</c>) va
    /// smenasining VAQT ORALIG'I bo'yicha biriktiriladi. Bu yerda vaqt
    /// oralig'i xavfsiz — klass izohidagi ogohlantirish "hamma cash satri"
    /// haqida edi; bu yerda to'plam avval <c>created_by</c> bilan BITTA
    /// kassirga toraytirilgan, bitta kassirda esa bir vaqtning o'zida ikkita
    /// smena bo'lishi mumkin emas (<c>ux_cash_shifts_one_open_per_cashier</c>)
    /// va smenasiz storno umuman qabul qilinmaydi (<c>no_open_shift</c>).
    /// </para>
    ///
    /// <para>
    /// <b>Yopilgan smena qayta hisoblanmaydi.</b> Oraliqning yuqori chegarasi —
    /// <c>closed_at</c>, ya'ni smena yopilgandan KEYIN qilingan storno unga
    /// tushmaydi. Ochiq smenada chegara yo'q (hozirgacha hamma narsa kiradi),
    /// va yopish ayni o'sha tranzaksiyada bo'lgani uchun oraliq bir lahzada
    /// muzlaydi.
    /// </para>
    /// </summary>
    private async Task<CashOutflow> CashOutflowAsync(CashShift shift, CancellationToken ct)
    {
        // ---- 1. Naqd chiqimlar (F1.03) ----

        // Shu smenaga biriktirilgan chiqimlar. Ustunni `ExpenseService`
        // to'ldiradi: chiqim naqd bo'lsa va jurnalga tushsa.
        var shiftExpenseIds = db.Expenses.AsNoTracking()
            .Where(e => e.CashShiftId == shift.Id)
            .Select(e => (Guid?)e.Id);

        // Ko'zgu satr qaysi chiqimniki ekanini `ref_id` aytadi. To'lov
        // stornosining ko'zgusi ham `cash` va `reversal` bo'ladi, shuning
        // uchun to'plam chiqim id'lari bilan kesiladi — id fazolari
        // kesishmaydi, ya'ni bu aniq ajratish.
        var expenseIds = db.Expenses.AsNoTracking().Select(e => (Guid?)e.Id);

        var windowTo = shift.ClosedAt ?? DateTimeOffset.MaxValue;

        var expenseRows = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.Account == Accounts.Cash
                        && ((e.RefType == LedgerRefType.Expense
                             && shiftExpenseIds.Contains(e.RefId))
                            || (e.RefType == LedgerRefType.Reversal
                                && expenseIds.Contains(e.RefId)
                                && e.CreatedBy == shift.CashierId
                                && e.CreatedAt >= shift.OpenedAt
                                && e.CreatedAt <= windowTo)))
            .GroupBy(e => e.Direction)
            .Select(g => new { Direction = g.Key, Total = g.Sum(x => x.Amount), Count = g.Count() })
            .ToListAsync(ct);

        // Kredit — pul javondan chiqdi, debet — storno uni qaytardi.
        var spent = expenseRows.FirstOrDefault(r => r.Direction == LedgerDirection.Credit);
        var refunded = expenseRows.FirstOrDefault(r => r.Direction == LedgerDirection.Debit);

        // ---- 2. Topshiriqlar (F1.04) ----
        var handoverRows = await db.CashHandovers.AsNoTracking()
            .Where(h => h.CashShiftId == shift.Id)
            .GroupBy(h => h.ReversalOf != null)
            .Select(g => new { IsReversal = g.Key, Total = g.Sum(x => x.Amount), Count = g.Count() })
            .ToListAsync(ct);

        var handedOver = handoverRows.FirstOrDefault(r => !r.IsReversal);
        var returned = handoverRows.FirstOrDefault(r => r.IsReversal);

        return new CashOutflow(
            Expenses: decimal.Round((spent?.Total ?? 0m) - (refunded?.Total ?? 0m), MoneyScale),
            ExpensesCount: (spent?.Count ?? 0) + (refunded?.Count ?? 0),
            Handovers: decimal.Round((handedOver?.Total ?? 0m) - (returned?.Total ?? 0m), MoneyScale),
            HandoversCount: (handedOver?.Count ?? 0) + (returned?.Count ?? 0));
    }

    /// <summary>
    /// Javondan chiqqan naqd, ikki sabab bo'yicha. Musbat = kassadan chiqdi,
    /// manfiy = storno chiqqandan ko'proq qaytargan (nazariy holat).
    /// </summary>
    private sealed record CashOutflow(
        decimal Expenses, int ExpensesCount, decimal Handovers, int HandoversCount);

    /// <summary>
    /// Smena × usul × (storno mi) kesimidagi yig'indilar — BITTA so'rov.
    /// Ro'yxatda 500 ta smena bo'lsa ham so'rov soni o'zgarmaydi (N+1 yo'q):
    /// natija qatorlari soni ko'pi bilan smenalar × 4 usul × 2.
    /// </summary>
    private async Task<List<MethodTotal>> MethodTotalsAsync(
        IReadOnlyList<Guid> shiftIds, CancellationToken ct)
    {
        if (shiftIds.Count == 0) return [];

        // `CashShiftId` "smena" modeli olib tashlangач `Guid?` bo'ldi (kassalar,
        // 2026-09): yangi to'lovlarda odatda `null`. Shu yerda FAQAT haqiqatan
        // shu smenalarga tegishli (`.Value`) qatorlar kerak — filtr shart.
        var rows = await db.Payments.AsNoTracking()
            .Where(p => p.CashShiftId != null && shiftIds.Contains(p.CashShiftId.Value))
            .GroupBy(p => new { CashShiftId = p.CashShiftId!.Value, p.Method, IsReversal = p.ReversalOf != null })
            .Select(g => new MethodTotal(
                g.Key.CashShiftId, g.Key.Method, g.Key.IsReversal, g.Count(), g.Sum(p => p.Amount)))
            .ToListAsync(ct);

        return rows;
    }

    /// <summary>
    /// Bitta smena yig'indisi. Storno qatori MANFIY hisoblanadi: uning summasi
    /// bazada musbat (<c>ck_payments_amount</c>), ma'nosi esa "pul qaytdi".
    /// </summary>
    private sealed record MethodTotal(Guid ShiftId, string Method, bool IsReversal, int Count, decimal Total)
    {
        public decimal Signed => IsReversal ? -Total : Total;
    }

    // =====================================================================
    //  DTO
    // =====================================================================

    private async Task<CashShiftDto> SingleDtoAsync(CashShift shift, CancellationToken ct) =>
        (await ToDtosAsync([shift], ct))[0];

    /// <summary>
    /// Entity → DTO. Ismlar va yig'indilar smenalar soniga qaramay IKKITA
    /// so'rovda olinadi — ro'yxat sahifasi N+1 ga aylanmasligi uchun.
    /// </summary>
    private async Task<IReadOnlyList<CashShiftDto>> ToDtosAsync(
        IReadOnlyList<CashShift> shifts, CancellationToken ct)
    {
        if (shifts.Count == 0) return [];

        var shiftIds = shifts.Select(s => s.Id).ToList();

        var userIds = shifts.Select(s => s.CashierId)
            .Concat(shifts.Select(s => s.ClosedBy).Where(id => id is not null).Select(id => id!))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var names = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        var totals = await MethodTotalsAsync(shiftIds, ct);

        return [.. shifts.Select(shift =>
        {
            var mine = totals.Where(t => t.ShiftId == shift.Id).ToList();
            return new CashShiftDto(
                Id: shift.Id,
                CashierId: shift.CashierId,
                CashierName: Name(names, shift.CashierId),
                OpenedAt: shift.OpenedAt,
                ClosedAt: shift.ClosedAt,
                OpeningFloat: shift.OpeningFloat,
                ExpectedCash: shift.ExpectedCash,
                CountedCash: shift.CountedCash,
                Variance: shift.Variance,
                Status: shift.Status,
                ClosedByName: shift.ClosedBy is null ? null : Name(names, shift.ClosedBy),
                PaymentsCount: mine.Sum(t => t.Count),
                CashTotal: decimal.Round(
                    mine.Where(t => PaymentMethod.CountsAsCash(t.Method)).Sum(t => t.Signed), MoneyScale),
                NonCashTotal: decimal.Round(
                    mine.Where(t => !PaymentMethod.CountsAsCash(t.Method)).Sum(t => t.Signed), MoneyScale));
        })];
    }

    private static string Name(IReadOnlyDictionary<string, string> names, string userId) =>
        names.TryGetValue(userId, out var name) ? name : "Noma'lum";

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    /// <summary>SPEC §4.2 — "boshqa kassirning smenasi" qoidasidagi nazoratchi rollar.</summary>
    public static readonly IReadOnlyList<string> SupervisorRoles = [Roles.Admin, Roles.SuperAdmin];

    /// <summary>Shu rol o'zganing smenasini yopa/ko'ra oladimi (SPEC §4.3)?</summary>
    public static bool IsSupervisorRole(string? role) =>
        role is not null && SupervisorRoles.Contains(role, StringComparer.Ordinal);

    /// <summary><c>audit_log.entity_type</c> qiymati — smena yozuvlarini shu bo'yicha topish mumkin.</summary>
    public const string AuditEntityCashShift = "CashShift";

    /// <summary>BillingModel.cs dagi qisman unikal indeks nomi (bir kassirda bitta ochiq smena).</summary>
    private const string OneOpenShiftIndex = "ux_cash_shifts_one_open_per_cashier";

    /// <summary>
    /// Unikal indeks buzilishini indeks NOMI bo'yicha aniqlaydi.
    ///
    /// <para>
    /// <c>PostgresException.SqlState</c> (23505) aniqroq bo'lardi, lekin u
    /// Npgsql tipidir — Application qatlami esa provayderni BILMAYDI va
    /// bilmasligi kerak (SPEC §2.2). Npgsql xato matniga indeks nomini AYNAN
    /// qo'yadi, shuning uchun nom bo'yicha tekshiruv yagona bog'liqliksiz yo'l.
    /// Indeks nomi o'zgarsa, bu yerda ham o'zgaradi — shuning uchun nom
    /// konstanta.
    /// </para>
    /// </summary>
    private static bool IsUniqueViolation(DbUpdateException ex, string indexName)
    {
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
            if (inner.Message.Contains(indexName, StringComparison.Ordinal))
                return true;
        return false;
    }

    /// <summary>Pulni "850 000" ko'rinishida — audit matni uchun.</summary>
    private static string Money(decimal value) =>
        value.ToString("#,##0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ");
}

/// <summary>
/// Kassa smenasidagi KUTILGAN biznes xatolari. Kod (<see cref="Code"/>) — mashina
/// uchun, xabar — kassir uchun (o'zbekcha).
///
/// <para>
/// Nega alohida tip: controller HTTP holatini AYNAN shu kod bo'yicha tanlaydi
/// (<c>shift_already_open</c> → 409, <c>not_your_shift</c> → 403). Xabar matnini
/// tahlil qilish yoki hamma xatoni 400 qilib yuborish frontendni taxminlar
/// ustiga qurardi.
/// </para>
/// </summary>
public sealed class CashShiftException(string code, string message) : Exception(message)
{
    /// <summary><see cref="CashShiftError"/> dagi kodlardan biri.</summary>
    public string Code { get; } = code;
}

/// <summary>Kassa smenasi xato kodlari — frontend va controller shu ro'yxatga tayanadi.</summary>
public static class CashShiftError
{
    /// <summary>Smena topilmadi (404).</summary>
    public const string NotFound = "shift_not_found";

    /// <summary>Kassirda allaqachon ochiq smena bor (409) — SPEC §4.2.</summary>
    public const string AlreadyOpen = "shift_already_open";

    /// <summary>Smena allaqachon yopilgan (409).</summary>
    public const string AlreadyClosed = "shift_already_closed";

    /// <summary>Smena ochiq emas — chek raqami berilmaydi (409).</summary>
    public const string NotOpen = "shift_not_open";

    /// <summary>Boshqa kassirning smenasi (403) — SPEC §4.2.</summary>
    public const string NotYourShift = "not_your_shift";

    /// <summary>Ochilish qoldig'i manfiy (400).</summary>
    public const string InvalidOpeningFloat = "invalid_opening_float";

    /// <summary>Sanalgan naqd yo'q yoki manfiy (400) — SPEC §4.2.</summary>
    public const string InvalidCountedCash = "invalid_counted_cash";
}

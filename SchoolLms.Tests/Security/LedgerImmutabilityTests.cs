using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Billing;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Security;

// ===========================================================================
//  SPEC §4.1 — MOLIYAVIY TARIXNI QAYTA YOZIB BO'LMAYDI. Vazifa: P1-22.
// ===========================================================================
//
//  MIJOZ AYTGAN FIRIBGARLIK AYNAN SHU: kassir pulni oladi va yozuvni
//  o'chiradi. Ilova darajasidagi tekshiruv bunga qarshi tura olmaydi —
//  xato, o'g'irlangan token yoki noto'g'ri sozlangan rol uni yengib o'tadi.
//  Yagona ishonchli qulf — bazaning o'zi.
//
//  BU FAYLDA NIMA ISBOTLANADI
//  --------------------------
//  1. `app_rw` roli (ilova AYNAN shu rol bilan ulanadi) `payments`,
//     `payment_allocations` va `ledger_entries` jadvallarida UPDATE va
//     DELETE qila OLMAYDI — SQLSTATE 42501. Oltita amal, OLTITA ALOHIDA
//     test: ro'yxat bo'ylab aylanadigan bitta test bo'sh ro'yxat bilan
//     ham jimgina yashil bo'lardi.
//  2. NAZORAT GURUHI: owner roli AYNAN o'sha oltita amalni BAJARA OLADI.
//     Busiz test hech narsani isbotlamasdi — buzilgan ulanish satri ham,
//     mavjud bo'lmagan jadval ham "42501 emas" degan natija berardi va
//     biz uni "himoya ishlayapti" deb o'qirdik. Nazorat testlari
//     o'zgarishni ROLLBACK qiladi.
//  3. Ilova qatlamini (PaymentService) BUTUNLAY chetlab o'tib, xom SQL
//     bilan ortiqcha taqsimot yozishga urinish trigger'da yiqiladi.
//
//  NEGA `app_rw` SHART
//  -------------------
//  PostgreSQL'da jadval EGASI ham, superuser ham `REVOKE` ni chetlab
//  o'tadi (SPEC §4.1 dagi "Correction" izohi — jonli serverda tekshirilgan).
//  Shuning uchun har bir test `RequireRealAppRw()` bilan boshlanadi: agar
//  ulanish satri aslida owner'niki bo'lsa, test SOXTA YASHIL bermasdan
//  aniq xabar bilan yiqiladi.
//
//  GRANTLAR QAYERDAN KELADI
//  ------------------------
//  Fixture rolga faqat BAZAVIY huquqlarni beradi (`init-roles.sql` 4-qadami:
//  hamma jadvalga to'liq CRUD). Moliyaviy `REVOKE` ni MIGRATSIYA qo'yadi
//  (`Migrations/Sql/billing_guards.sql`). Ya'ni bu fayl fixture'ning emas,
//  migratsiyaning ishini tekshiradi: `REVOKE` migratsiyadan yo'qolsa,
//  quyidagi oltita test darhol qizil bo'ladi.
// ===========================================================================

/// <summary>
/// Moliyaviy jadvallarning o'zgarmasligi — baza darajasida (SPEC §4.1, P1-22).
/// Batafsil: fayl boshidagi izoh.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class LedgerImmutabilityTests(ApiFixture fixture)
{
    /// <summary>Huquq rad etilganda PostgreSQL qaytaradigan kod: <c>insufficient_privilege</c>.</summary>
    private const string PermissionDenied = "42501";

    /// <summary>Trigger va CHECK constraint qaytaradigan kod: <c>check_violation</c>.</summary>
    private const string CheckViolation = "23514";

    /// <summary>Migratsiyada seed qilingan barqaror toifa id'si (billing_seed.sql).</summary>
    private static readonly Guid TuitionCategoryId = new("00000000-0000-0000-0000-0000000000c1");

    private AppDbContext NewDb() => PostgresFixture.NewContext(fixture.Database.OwnerConnectionString);

    // =====================================================================
    //  1. `app_rw` — OLTITA ALOHIDA RAD ETISH (SPEC §4.1)
    // =====================================================================

    /// <summary>
    /// "Kassir pulni oldi, keyin summani o'zgartirdi" — shu test bunga
    /// qarshi turadi. Faqat 42501 emas, qatorning O'ZGARMAGANI ham
    /// tekshiriladi: xato qaytib, o'zgarish baribir o'tib ketishi mumkin
    /// bo'lgan holat (autocommit ichidagi qisman bajarilish) e'tiborsiz
    /// qolmasin.
    /// </summary>
    [Fact]
    public async Task App_rw_payments_jadvalini_UPDATE_qila_olmaydi_42501()
    {
        var world = await SeedAsync();

        var error = await ExpectSqlStateAsync(
            fixture.Database.AppRwConnectionString,
            $"UPDATE payments SET amount = 1 WHERE id = '{world.PaymentId}'");

        Assert.Equal(PermissionDenied, error.SqlState);
        Assert.Contains("payments", error.Message, StringComparison.OrdinalIgnoreCase);

        await using var db = NewDb();
        Assert.Equal(world.Amount, await db.Payments.AsNoTracking()
            .Where(p => p.Id == world.PaymentId).Select(p => p.Amount).SingleAsync());
    }

    /// <summary>
    /// Tavsifdagi firibgarlikning to'g'ridan-to'g'ri ko'rinishi: chekni
    /// o'chirib tashlash. Qator o'chmasligi SHART.
    /// </summary>
    [Fact]
    public async Task App_rw_payments_jadvalidan_DELETE_qila_olmaydi_42501()
    {
        var world = await SeedAsync();

        var error = await ExpectSqlStateAsync(
            fixture.Database.AppRwConnectionString,
            $"DELETE FROM payments WHERE id = '{world.PaymentId}'");

        Assert.Equal(PermissionDenied, error.SqlState);
        Assert.Contains("payments", error.Message, StringComparison.OrdinalIgnoreCase);

        await using var db = NewDb();
        Assert.True(await db.Payments.AsNoTracking().AnyAsync(p => p.Id == world.PaymentId),
            "To'lov qatori o'chib ketdi — SPEC §4.1 buzilgan.");
    }

    /// <summary>
    /// Taqsimotni tahrirlash — pulni bir hisob-fakturadan boshqasiga
    /// jimgina ko'chirish yo'li. Yopiq.
    /// </summary>
    [Fact]
    public async Task App_rw_payment_allocations_jadvalini_UPDATE_qila_olmaydi_42501()
    {
        var world = await SeedAsync();

        var error = await ExpectSqlStateAsync(
            fixture.Database.AppRwConnectionString,
            $"UPDATE payment_allocations SET amount = 1 WHERE id = '{world.AllocationId}'");

        Assert.Equal(PermissionDenied, error.SqlState);
        Assert.Contains("payment_allocations", error.Message, StringComparison.OrdinalIgnoreCase);

        await using var db = NewDb();
        Assert.Equal(world.Amount, await db.PaymentAllocations.AsNoTracking()
            .Where(a => a.Id == world.AllocationId).Select(a => a.Amount).SingleAsync());
    }

    /// <summary>
    /// Taqsimotni o'chirish hisob-fakturani "to'lanmagan" holatiga
    /// qaytarardi, pul esa allaqachon olingan bo'lardi. Yopiq.
    /// </summary>
    [Fact]
    public async Task App_rw_payment_allocations_jadvalidan_DELETE_qila_olmaydi_42501()
    {
        var world = await SeedAsync();

        var error = await ExpectSqlStateAsync(
            fixture.Database.AppRwConnectionString,
            $"DELETE FROM payment_allocations WHERE id = '{world.AllocationId}'");

        Assert.Equal(PermissionDenied, error.SqlState);
        Assert.Contains("payment_allocations", error.Message, StringComparison.OrdinalIgnoreCase);

        await using var db = NewDb();
        Assert.True(await db.PaymentAllocations.AsNoTracking().AnyAsync(a => a.Id == world.AllocationId),
            "Taqsimot qatori o'chib ketdi — SPEC §4.1 buzilgan.");
    }

    /// <summary>
    /// Jurnal (ledger) — moliyaviy haqiqatning yagona manbai. Uni
    /// tahrirlash butun hisobotni yolg'onga aylantirardi.
    /// </summary>
    [Fact]
    public async Task App_rw_ledger_entries_jadvalini_UPDATE_qila_olmaydi_42501()
    {
        var world = await SeedAsync();

        var error = await ExpectSqlStateAsync(
            fixture.Database.AppRwConnectionString,
            $"UPDATE ledger_entries SET amount = 1 WHERE id = {world.LedgerEntryId}");

        Assert.Equal(PermissionDenied, error.SqlState);
        Assert.Contains("ledger_entries", error.Message, StringComparison.OrdinalIgnoreCase);

        await using var db = NewDb();
        Assert.Equal(world.Amount, await db.LedgerEntries.AsNoTracking()
            .Where(e => e.Id == world.LedgerEntryId).Select(e => e.Amount).SingleAsync());
    }

    /// <summary>Jurnal yozuvini o'chirish — auditning izini yo'qotish. Yopiq.</summary>
    [Fact]
    public async Task App_rw_ledger_entries_jadvalidan_DELETE_qila_olmaydi_42501()
    {
        var world = await SeedAsync();

        var error = await ExpectSqlStateAsync(
            fixture.Database.AppRwConnectionString,
            $"DELETE FROM ledger_entries WHERE id = {world.LedgerEntryId}");

        Assert.Equal(PermissionDenied, error.SqlState);
        Assert.Contains("ledger_entries", error.Message, StringComparison.OrdinalIgnoreCase);

        await using var db = NewDb();
        Assert.True(await db.LedgerEntries.AsNoTracking().AnyAsync(e => e.Id == world.LedgerEntryId),
            "Jurnal yozuvi o'chib ketdi — SPEC §4.1 buzilgan.");
    }

    // =====================================================================
    //  2. NAZORAT GURUHI — owner AYNAN o'sha oltita amalni bajara oladi
    // =====================================================================
    //
    //  Bularsiz yuqoridagi oltita test ma'nosiz. "42501 keldi" degani
    //  o'z-o'zidan "himoya ishlayapti" degani emas: jadval nomi noto'g'ri
    //  bo'lsa 42P01, baza bo'sh bo'lsa 0 qator — ya'ni testni ALDASH oson.
    //  Owner esa `REVOKE` ni chetlab o'tadi (SPEC §4.1), demak u MUVAFFAQIYAT
    //  qaytarishi shart. Ikkalasi birga bo'lgandagina test "grant AYNAN shu
    //  rolga qo'yilgan" degan xulosani beradi.
    //
    //  Har bir nazorat testi tranzaksiya ichida ishlaydi va ROLLBACK bilan
    //  tugaydi — nazorat guruhi test ma'lumotini buzmasin.

    [Fact]
    public async Task Nazorat_owner_payments_jadvalini_UPDATE_qila_oladi()
    {
        var world = await SeedAsync();

        var affected = await ExpectSuccessThenRollbackAsync(
            $"UPDATE payments SET amount = 1 WHERE id = '{world.PaymentId}'");

        Assert.Equal(1, affected);
    }

    [Fact]
    public async Task Nazorat_owner_payments_jadvalidan_DELETE_qila_oladi()
    {
        var world = await SeedAsync();

        // Bola qatorlar FK bilan bog'langan — ular ham shu tranzaksiyada olib
        // tashlanadi, keyin hammasi ROLLBACK bo'ladi.
        var affected = await ExpectSuccessThenRollbackAsync(
            $"DELETE FROM payment_allocations WHERE payment_id = '{world.PaymentId}'",
            $"DELETE FROM ledger_entries WHERE ref_id = '{world.PaymentId}'",
            $"DELETE FROM payments WHERE id = '{world.PaymentId}'");

        Assert.Equal(1, affected);

        // ROLLBACK haqiqatan ishlagan: qator joyida.
        await using var db = NewDb();
        Assert.True(await db.Payments.AsNoTracking().AnyAsync(p => p.Id == world.PaymentId));
    }

    [Fact]
    public async Task Nazorat_owner_payment_allocations_jadvalini_UPDATE_qila_oladi()
    {
        var world = await SeedAsync();

        var affected = await ExpectSuccessThenRollbackAsync(
            $"UPDATE payment_allocations SET amount = 1 WHERE id = '{world.AllocationId}'");

        Assert.Equal(1, affected);
    }

    [Fact]
    public async Task Nazorat_owner_payment_allocations_jadvalidan_DELETE_qila_oladi()
    {
        var world = await SeedAsync();

        var affected = await ExpectSuccessThenRollbackAsync(
            $"DELETE FROM payment_allocations WHERE id = '{world.AllocationId}'");

        Assert.Equal(1, affected);

        await using var db = NewDb();
        Assert.True(await db.PaymentAllocations.AsNoTracking().AnyAsync(a => a.Id == world.AllocationId));
    }

    [Fact]
    public async Task Nazorat_owner_ledger_entries_jadvalini_UPDATE_qila_oladi()
    {
        var world = await SeedAsync();

        var affected = await ExpectSuccessThenRollbackAsync(
            $"UPDATE ledger_entries SET amount = 1 WHERE id = {world.LedgerEntryId}");

        Assert.Equal(1, affected);
    }

    [Fact]
    public async Task Nazorat_owner_ledger_entries_jadvalidan_DELETE_qila_oladi()
    {
        var world = await SeedAsync();

        var affected = await ExpectSuccessThenRollbackAsync(
            $"DELETE FROM ledger_entries WHERE id = {world.LedgerEntryId}");

        Assert.Equal(1, affected);

        await using var db = NewDb();
        Assert.True(await db.LedgerEntries.AsNoTracking().AnyAsync(e => e.Id == world.LedgerEntryId));
    }

    // =====================================================================
    //  3. Qulf SURGIK: INSERT ochiq qoladi
    // =====================================================================

    /// <summary>
    /// <c>REVOKE</c> "hammasini yop" degani EMAS. Ilova to'lov qabul qila
    /// olishi shart — aks holda kassa umuman ishlamasdi, va kimdir muammoni
    /// "tez yechish" uchun `GRANT ALL` yozib qo'yardi. INSERT ochiq,
    /// UPDATE/DELETE yopiq: xato to'lov faqat storno bilan tuzatiladi.
    /// </summary>
    [Fact]
    public async Task App_rw_uch_jadvalga_ham_INSERT_qila_oladi_qulf_faqat_UPDATE_va_DELETE()
    {
        fixture.Database.RequireRealAppRw();
        var world = await SeedAsync();
        var newPaymentId = Guid.NewGuid();

        await using (var conn = new NpgsqlConnection(fixture.Database.AppRwConnectionString))
        {
            await conn.OpenAsync();

            await ExecuteAsync(conn,
                $"""
                 INSERT INTO payments (id, receipt_no, student_id, amount, method,
                                       cash_shift_id, cashier_id, note, received_at, reversal_of)
                 VALUES ('{newPaymentId}', {world.ReceiptNo + 1}, '{world.StudentId}', 1000, 'cash',
                         '{world.ShiftId}', '{world.CashierId}', 'app_rw insert', now(), NULL)
                 """);

            await ExecuteAsync(conn,
                $"""
                 INSERT INTO payment_allocations (id, payment_id, invoice_id, amount)
                 VALUES ('{Guid.NewGuid()}', '{newPaymentId}', '{world.InvoiceId}', 1000)
                 """);

            await ExecuteAsync(conn,
                $"""
                 INSERT INTO ledger_entries (entry_date, account, direction, amount,
                                             ref_type, ref_id, memo, created_by, created_at)
                 VALUES (current_date, 'cash', 'debit', 1000, 'payment', '{newPaymentId}',
                         'app_rw insert', '{world.CashierId}', now()),
                        (current_date, 'receivable', 'credit', 1000, 'payment', '{newPaymentId}',
                         'app_rw insert', '{world.CashierId}', now())
                 """);
        }

        await using var db = NewDb();
        var inserted = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == newPaymentId);
        Assert.Equal(1000m, inserted.Amount);
        Assert.Equal(world.CashierId, inserted.CashierId);
        Assert.Single(await db.PaymentAllocations.AsNoTracking()
            .Where(a => a.PaymentId == newPaymentId).ToListAsync());
        // Juftlik: debet `cash` + kredit `receivable`. INSERT ochiqligini isbotlash
        // uchun bitta qator ham yetardi, ammo yolg'iz qator umumiy jurnalni
        // nomutanosib qoldirardi (yuqoridagi `SeedAsync` izohiga qarang).
        Assert.Equal(2, (await db.LedgerEntries.AsNoTracking()
            .Where(e => e.RefId == newPaymentId).ToListAsync()).Count);
    }

    /// <summary>
    /// <c>TRUNCATE</c> — "hamma qatorni o'chirish" ning DELETE'siz yo'li.
    /// <c>billing_guards.sql</c> uni ham qaytarib oladi; agar olmasa, butun
    /// yuqoridagi himoya bitta buyruq bilan chetlab o'tilardi.
    /// </summary>
    [Fact]
    public async Task App_rw_payments_jadvalini_TRUNCATE_qila_olmaydi_42501()
    {
        var world = await SeedAsync();

        var error = await ExpectSqlStateAsync(
            fixture.Database.AppRwConnectionString, "TRUNCATE payments CASCADE");

        Assert.Equal(PermissionDenied, error.SqlState);

        await using var db = NewDb();
        Assert.True(await db.Payments.AsNoTracking().AnyAsync(p => p.Id == world.PaymentId));
    }

    // =====================================================================
    //  4. Trigger — ilovani CHETLAB O'TIB ham ortiqcha taqsimot yozib bo'lmaydi
    // =====================================================================

    /// <summary>
    /// SPEC §3.7 invarianti: taqsimotlar yig'indisi to'lov summasidan
    /// oshmasin. <c>PaymentService</c> buni tekshiradi — lekin bu test
    /// xizmatni BUTUNLAY chetlab o'tadi va to'g'ridan-to'g'ri xom SQL
    /// yozadi (aynan chetlab o'tishga uringan odam shunday qiladi).
    /// Trigger uni to'xtatishi SHART.
    /// </summary>
    [Fact]
    public async Task Xom_SQL_bilan_ortiqcha_taqsimot_yozib_bolmaydi_trigger_toxtatadi()
    {
        fixture.Database.RequireRealAppRw();
        var world = await SeedAsync();     // to'lov 500 000, allaqachon 500 000 taqsimlangan

        // Yana bittasi — yig'indi 1 000 000 bo'lardi, to'lov esa 500 000.
        var error = await ExpectSqlStateAsync(
            fixture.Database.AppRwConnectionString,
            $"""
             INSERT INTO payment_allocations (id, payment_id, invoice_id, amount)
             VALUES ('{Guid.NewGuid()}', '{world.PaymentId}', '{world.InvoiceId}', {world.Amount})
             """);

        Assert.Equal(CheckViolation, error.SqlState);
        // Xabar matni `billing_guards.sql` dan AYNAN olingan — SPEC §3.7.
        Assert.Contains("Allocation exceeds payment amount", error.Message, StringComparison.Ordinal);

        await using var db = NewDb();
        var total = await db.PaymentAllocations.AsNoTracking()
            .Where(a => a.PaymentId == world.PaymentId).SumAsync(a => a.Amount);
        Assert.Equal(world.Amount, total);
    }

    /// <summary>
    /// Trigger huquqqa BOG'LIQ EMAS: sxema egasi ham (u <c>REVOKE</c> ni
    /// chetlab o'tadi) ortiqcha taqsimot yoza olmaydi. Ya'ni bu invariantni
    /// buzishning umuman yo'li yo'q — hatto migratsiya roli bilan ham.
    /// </summary>
    [Fact]
    public async Task Owner_ham_ortiqcha_taqsimot_yoza_olmaydi_trigger_rolga_qaramaydi()
    {
        var world = await SeedAsync();

        var error = await ExpectSqlStateAsync(
            fixture.Database.OwnerConnectionString,
            $"""
             INSERT INTO payment_allocations (id, payment_id, invoice_id, amount)
             VALUES ('{Guid.NewGuid()}', '{world.PaymentId}', '{world.InvoiceId}', 1)
             """);

        Assert.Equal(CheckViolation, error.SqlState);
        Assert.Contains("Allocation exceeds payment amount", error.Message, StringComparison.Ordinal);
    }

    // =====================================================================
    //  5. Rolning O'ZI to'g'ri sozlanganmi
    // =====================================================================

    /// <summary>
    /// <c>app_rw</c> HECH NARSAGA EGA BO'LMASLIGI SHART. Egalik bo'lsa
    /// <c>REVOKE</c> unga umuman ta'sir qilmaydi va yuqoridagi hamma test
    /// bir kunda jimgina ma'nosiz bo'lib qolardi (SPEC §4.1 "Correction").
    /// <c>init-roles.sql</c> ning 6-qadamidagi <c>owns_in_public = 0</c>
    /// tekshiruvining test ko'rinishi.
    /// </summary>
    [Fact]
    public async Task App_rw_public_sxemada_hech_narsaga_ega_emas_va_superuser_emas()
    {
        fixture.Database.RequireRealAppRw();

        await using var conn = new NpgsqlConnection(fixture.Database.AppRwConnectionString);
        await conn.OpenAsync();

        var owned = (long)(await ScalarAsync(conn,
            $"""
             SELECT count(*) FROM pg_class c
               JOIN pg_namespace n ON n.oid = c.relnamespace
               JOIN pg_roles r ON r.oid = c.relowner
              WHERE n.nspname = 'public'
                AND r.rolname = '{PostgresFixture.AppRwRole}'
                AND c.relkind IN ('r','p','S','v','m')
             """))!;
        Assert.Equal(0L, owned);

        var isSuperuser = (bool)(await ScalarAsync(conn,
            $"SELECT rolsuper FROM pg_roles WHERE rolname = '{PostgresFixture.AppRwRole}'"))!;
        Assert.False(isSuperuser, "`app_rw` superuser — REVOKE unga ta'sir qilmaydi.");

        // Va bu haqiqatan ilova ulanadigan rol.
        Assert.Equal(PostgresFixture.AppRwRole, (string?)await ScalarAsync(conn, "SELECT current_user"));
    }

    /// <summary>
    /// Huquqlar jadvalining O'ZI (SPEC §4.1). Yuqoridagi testlar xatti-harakatni
    /// tekshiradi, bu esa e'lon qilingan holatni: uch jadvalda <c>app_rw</c> uchun
    /// FAQAT <c>SELECT</c> va <c>INSERT</c> qolgan bo'lishi kerak.
    /// </summary>
    [Theory]
    [InlineData("payments")]
    [InlineData("payment_allocations")]
    [InlineData("ledger_entries")]
    public async Task Uch_jadvalda_app_rw_uchun_faqat_SELECT_va_INSERT_qolgan(string table)
    {
        fixture.Database.RequireRealAppRw();

        await using var conn = new NpgsqlConnection(fixture.Database.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            """
            SELECT privilege_type FROM information_schema.role_table_grants
             WHERE grantee = @role AND table_schema = 'public' AND table_name = @t
             ORDER BY privilege_type
            """, conn);
        cmd.Parameters.AddWithValue("role", PostgresFixture.AppRwRole);
        cmd.Parameters.AddWithValue("t", table);

        var granted = new List<string>();
        await using (var reader = await cmd.ExecuteReaderAsync())
            while (await reader.ReadAsync()) granted.Add(reader.GetString(0));

        Assert.Equal(new[] { "INSERT", "SELECT" }, granted);
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    /// <summary>Bitta test uchun to'liq, haqiqiy pul zanjiri.</summary>
    private sealed record World(
        string CashierId, string StudentId, Guid ShiftId, Guid InvoiceId,
        Guid PaymentId, Guid AllocationId, long LedgerEntryId,
        long ReceiptNo, decimal Amount);

    /// <summary>
    /// Har test O'Z ma'lumotini yaratadi (testlar bitta bazani bo'lishadi).
    /// Yozuv OWNER ulanishi bilan — bu tayyorgarlik, sinov emas.
    /// </summary>
    private async Task<World> SeedAsync(decimal amount = 500_000m)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        await using var db = NewDb();

        var cashier = new AppUser
        {
            FullName = $"Immutability kassir {suffix}",
            Role = Roles.Cashier,
            Email = $"immutability.{suffix}",
        };
        var student = new Student { FullName = $"Immutability o'quvchi {suffix}", ClassName = "1-A" };
        db.Users.Add(cashier);
        db.Students.Add(student);
        await db.SaveChangesAsync();

        var shift = new CashShift
        {
            CashierId = cashier.Id,
            OpenedAt = AppClock.NowInstant,
            OpeningFloat = 0m,
            Status = CashShiftStatus.Open,
        };
        var invoice = new Invoice
        {
            StudentId = student.Id,
            CategoryId = TuitionCategoryId,
            PeriodMonth = new DateOnly(2026, 9, 1),
            Amount = amount,
            Discount = 0m,
            DueOn = new DateOnly(2026, 9, 10),
            Status = InvoiceStatus.Paid,
            CreatedAt = AppClock.NowInstant,
        };
        db.CashShifts.Add(shift);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var payment = new Payment
        {
            ReceiptNo = 1,
            StudentId = student.Id,
            Amount = amount,
            Method = PaymentMethod.Cash,
            CashShiftId = shift.Id,
            CashierId = cashier.Id,
            ReceivedAt = AppClock.NowInstant,
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var allocation = new PaymentAllocation
        {
            PaymentId = payment.Id,
            InvoiceId = invoice.Id,
            Amount = amount,
        };
        var entry = new LedgerEntry
        {
            EntryDate = AppClock.Today,
            Account = Accounts.Cash,
            Direction = LedgerDirection.Debit,
            Amount = amount,
            RefType = LedgerRefType.Payment,
            RefId = payment.Id,
            CreatedBy = cashier.Id,
            CreatedAt = AppClock.NowInstant,
        };
        // QARSHI YOZUV — shartsiz kerak. Testlar bitta bazani bo'lishadi, va
        // `ExpensesTests.Pul_aylanmasi_halqasi_balansda_qoladi` BUTUN jurnal
        // balansda ekanini tekshiradi. Yolg'iz debet yozuv o'sha o'zgarmasni
        // buzardi — va bu bu yerdagi sinovga hech narsa qo'shmasdi: quyidagi
        // testlar `World.LedgerEntryId` (aynan shu debet qator) ustida ishlaydi.
        // To'lovning tabiiy qarshi yozuvi — qarzning kamayishi.
        var counterEntry = new LedgerEntry
        {
            EntryDate = AppClock.Today,
            Account = Accounts.Receivable,
            Direction = LedgerDirection.Credit,
            Amount = amount,
            RefType = LedgerRefType.Payment,
            RefId = payment.Id,
            CreatedBy = cashier.Id,
            CreatedAt = AppClock.NowInstant,
        };
        db.PaymentAllocations.Add(allocation);
        db.LedgerEntries.Add(entry);
        db.LedgerEntries.Add(counterEntry);
        await db.SaveChangesAsync();

        return new World(
            cashier.Id, student.Id, shift.Id, invoice.Id,
            payment.Id, allocation.Id, entry.Id, payment.ReceiptNo, amount);
    }

    /// <summary>
    /// Buyruqni bajaradi va <see cref="PostgresException"/> KUTADI. Xato
    /// bo'lmasa test yiqiladi — "urinib ko'rdik, o'tib ketdi" jimgina
    /// yashil bo'lib qolmasin.
    /// </summary>
    private async Task<PostgresException> ExpectSqlStateAsync(string connectionString, string sql)
    {
        // Grantga tayanadigan har bir test SHU YERDAN o'tadi: ulanish satri aslida
        // owner'niki bo'lsa, natija ma'nosiz bo'lardi.
        if (connectionString == fixture.Database.AppRwConnectionString)
            fixture.Database.RequireRealAppRw();

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);

        return await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
    }

    /// <summary>
    /// Owner bilan bajaradi (muvaffaqiyat KUTILADI) va <b>ROLLBACK</b> qiladi.
    /// Oxirgi buyruq ta'sir qilgan qatorlar sonini qaytaradi.
    /// </summary>
    private async Task<int> ExpectSuccessThenRollbackAsync(params string[] statements)
    {
        await using var conn = new NpgsqlConnection(fixture.Database.OwnerConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var affected = 0;
        foreach (var sql in statements)
        {
            await using var cmd = new NpgsqlCommand(sql, conn, tx);
            affected = await cmd.ExecuteNonQueryAsync();
        }

        await tx.RollbackAsync();
        return affected;
    }

    private static async Task ExecuteAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        var value = await cmd.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }
}

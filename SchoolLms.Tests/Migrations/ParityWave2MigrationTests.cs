using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Migrations;

// ===========================================================================
//  `ParityWave2Schema` migratsiyasi — docs/modules/existing-module-gaps.md
//  §2.2, §2.3, §3.5, §5.5, §6.3.
// ===========================================================================
//
//  NEGA MIGRATSIYAGA ALOHIDA TEST YOZILADI
//  ---------------------------------------
//  Loyiha qoidasi: model o'zgarsa — migratsiya testi MAJBURIY. Sabab oddiy:
//  migratsiya jonli maktab bazasida bir marta yuradi va u yerda "orqaga
//  qaytarish" degan tugma yo'q. Ilova testlari migratsiyani KO'RMAYDI —
//  ular baza allaqachon to'g'ri degan taxmin ustida ishlaydi.
//
//  BU FAYLDA NIMA ISBOTLANADI
//  --------------------------
//  1. `Up()` qo'llanadi va beshta yangi jadval, sakkizta yangi ustun hamda
//     yangi indekslar paydo bo'ladi — turi, nullability va DEFAULT'i bilan.
//  2. MAVJUD qatorlar to'g'ri DEFAULT oladi. Bu eng muhim tasdiq: migratsiya
//     `notify_parent` ni mavjud har bir sababda `false` qilishi SHART (§6.3),
//     va ota-ona kabinetidagi o'zlashtirish YOQILGAN bo'lib qolishi kerak.
//     Shuning uchun test migratsiyani ORQAGA qaytaradi, eski qator qo'yadi va
//     yana oldinga yuradi — ya'ni haqiqiy "eski baza" holatini takrorlaydi.
//  3. `Down()` AYNAN `Up()` yaratganini qaytaradi, ortiqchasini emas.
//  4. `app_rw` beshta jadvalda to'liq CRUD qila oladi — jumladan
//     `debtor_actions` da DELETE, chunki §3.5 uni ataylab moliyaviy
//     REVOKE ro'yxatiga qo'shmaydi.
//  5. Birorta MAVJUD jadval birorta ustunini yo'qotmaydi. EF'ning
//     `--autogenerate` i model bilan snapshot kelishmaganda "ortiqcha" DROP
//     chiqaradi va jonli bazada yo'qolgan ustunni qaytarib bo'lmaydi.
//     Bu yerda butun sxemaning (jadval, ustun) to'plami migratsiyadan oldin
//     va keyin solishtiriladi.
//
//  HAR TEST O'Z BAZASIDA
//  ---------------------
//  Quyidagi testlarning uchtasi migratsiyani orqaga qaytaradi — ya'ni
//  jadvallarni O'CHIRADI. Umumiy bazada bu boshqa test klassini o'rtasida
//  yiqitardi. xUnit har test uchun klassning yangi nusxasini yaratadi, ya'ni
//  `InitializeAsync` ham har test uchun yuradi va har biri shablondan o'z
//  nusxasini oladi (~100 ms).
// ===========================================================================

/// <summary>
/// `ParityWave2Schema` migratsiyasining o'zi — qo'llanishi, orqaga qaytishi,
/// DEFAULT'lari va grantlari. Batafsil: fayl boshidagi izoh.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class ParityWave2MigrationTests(ApiFixture fixture) : IAsyncLifetime
{
    /// <summary>Tekshirilayotgan migratsiya.</summary>
    private const string ThisMigration = "20260916114934_ParityWave2Schema";

    /// <summary>Undan oldingisi — `Down()` shu nuqtaga qaytaradi.</summary>
    private const string PreviousMigration = "20260912102427_GuardiansAndTelegramLink";

    /// <summary>Migratsiya yaratadigan beshta jadval.</summary>
    private static readonly string[] NewTables =
    [
        "debtor_statuses", "debtor_actions",
        "certificate_types", "certificates",
        "student_archive_reasons",
    ];

    /// <summary>Migratsiya MAVJUD jadvallarga qo'shadigan sakkizta ustun.</summary>
    private static readonly (string Table, string Column)[] NewColumns =
    [
        ("students", "archive_reason_id"),
        ("discipline_reasons", "notify_parent"),
        ("discipline_reasons", "description"),
        ("discipline_reasons", "is_active"),
        ("school_meta", "archive_only_non_debtor_students"),
        ("school_meta", "make_attendance_reason_required"),
        ("school_meta", "is_student_grade_required"),
        ("school_meta", "show_learning_progress_in_parent_dashboard"),
    ];

    private TestDatabase _database = default!;

    public async Task InitializeAsync() =>
        _database = await fixture.Postgres.CreateDatabaseAsync("parity2");

    /// <summary>
    /// Har test O'Z bazasini oladi, ya'ni O'Z ulanish hovuzini ham. Hovuz
    /// tozalanmasa, tugagan testning ulanishlari ochiq qolib konteynerdagi
    /// <c>max_connections</c> ni yeb qo'yadi va KEYINGI test klasslari
    /// <c>53300</c> bilan yiqiladi (<c>AllocationTests</c> dagi bir xil izoh).
    /// Bu yerda IKKITA satr bor — owner va <c>app_rw</c> — ikkalasi ham
    /// yopiladi.
    /// </summary>
    public Task DisposeAsync()
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.OwnerConnectionString));
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.AppRwConnectionString));
        return Task.CompletedTask;
    }

    // =====================================================================
    //  1. `Up()` — jadvallar, ustunlar, indekslar
    // =====================================================================

    /// <summary>Migratsiya zanjiri oxirigacha yuradi va beshta jadval paydo bo'ladi.</summary>
    [Fact]
    public async Task Up_beshta_yangi_jadvalni_yaratadi()
    {
        await using var db = NewDb();
        Assert.Contains(ThisMigration, await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());

        foreach (var table in NewTables)
            Assert.True(await TableExistsAsync(table), $"`{table}` jadvali yaratilmagan.");
    }

    /// <summary>
    /// Yangi jadvallardagi ustunlar — turi va nullability bilan. §2.3 va §3.5
    /// dagi sxema bloklaridan AYNAN ko'chirilgan; bu yerdagi har bir qator
    /// o'sha hujjatning bitta satriga to'g'ri keladi.
    /// </summary>
    [Fact]
    public async Task Yangi_jadval_ustunlari_spec_dagidek()
    {
        // (jadval, ustun, tur, null bo'la oladimi)
        (string Table, string Column, string Type, bool Nullable)[] expected =
        [
            // §3.5 — debtor_statuses
            ("debtor_statuses", "id", "uuid", false),
            ("debtor_statuses", "name", "text", false),
            ("debtor_statuses", "color", "text", false),
            ("debtor_statuses", "hint", "text", true),
            ("debtor_statuses", "position", "integer", false),
            ("debtor_statuses", "is_active", "boolean", false),

            // §3.5 — debtor_actions. `comment` MAJBURIY; `promised_on` va
            // `deleted_at` null bo'la oladi: va'da ham, o'chirish ham ixtiyoriy.
            ("debtor_actions", "id", "uuid", false),
            ("debtor_actions", "student_id", "text", false),
            ("debtor_actions", "status_id", "uuid", true),
            ("debtor_actions", "comment", "text", false),
            ("debtor_actions", "promised_on", "date", true),
            ("debtor_actions", "created_by", "text", false),
            ("debtor_actions", "created_at", "timestamp with time zone", false),
            ("debtor_actions", "deleted_at", "timestamp with time zone", true),

            // §2.3 — certificate_types
            ("certificate_types", "id", "uuid", false),
            ("certificate_types", "name", "text", false),
            ("certificate_types", "is_scored", "boolean", false),
            ("certificate_types", "is_active", "boolean", false),
            ("certificate_types", "created_at", "timestamp with time zone", false),

            // §2.3 — certificates
            ("certificates", "id", "uuid", false),
            ("certificates", "student_id", "text", false),
            ("certificates", "type_id", "uuid", false),
            ("certificates", "subject_id", "text", true),
            ("certificates", "teacher_id", "text", true),
            ("certificates", "number", "text", true),
            ("certificates", "score", "numeric", true),
            ("certificates", "issued_on", "date", false),
            ("certificates", "expires_on", "date", true),
            ("certificates", "file_url", "text", true),
            ("certificates", "comment", "text", true),
            ("certificates", "created_by", "text", false),
            ("certificates", "created_at", "timestamp with time zone", false),

            // §2.2 — student_archive_reasons
            ("student_archive_reasons", "id", "uuid", false),
            ("student_archive_reasons", "name", "text", false),
            ("student_archive_reasons", "is_active", "boolean", false),
            ("student_archive_reasons", "position", "integer", false),
        ];

        foreach (var (table, column, type, nullable) in expected)
        {
            var info = await ColumnAsync(table, column);
            Assert.True(info is not null, $"`{table}.{column}` ustuni yo'q.");
            Assert.Equal(type, info!.Value.Type);
            Assert.Equal(nullable, info.Value.Nullable);
        }

        // §2.3: `score numeric(6,2)` — aniqlik ham tekshiriladi, chunki
        // `numeric` ning o'zi cheksiz aniqlikni ham anglatishi mumkin va
        // IELTS 7.5 bilan SAT 1600 ikkalasi ham sig'ishi kerak.
        Assert.Equal(6, await IntAsync(
            "select numeric_precision from information_schema.columns "
            + "where table_name = 'certificates' and column_name = 'score'"));
        Assert.Equal(2, await IntAsync(
            "select numeric_scale from information_schema.columns "
            + "where table_name = 'certificates' and column_name = 'score'"));
    }

    /// <summary>
    /// Mavjud jadvallarga qo'shilgan sakkizta ustun va ularning DEFAULT'i.
    /// DEFAULT bu yerda BEZAK EMAS: `Up()` aynan shu qiymat bilan eski
    /// qatorlarni to'ldiradi (buni <see cref="Eski_qatorlar_togri_DEFAULT_qiymatni_oladi"/>
    /// alohida isbotlaydi).
    /// </summary>
    [Fact]
    public async Task Mavjud_jadvallarga_qoshilgan_ustunlar_togri_DEFAULT_bilan()
    {
        // `students.archive_reason_id` — null bo'la oladi va DEFAULT'i yo'q:
        // eski arxivlangan o'quvchilarga sabab TAXMIN QILINMAYDI. Ularning
        // tafsiloti eski `archive_reason` matnida qoladi.
        var archiveReasonId = await ColumnAsync("students", "archive_reason_id");
        Assert.True(archiveReasonId is not null);
        Assert.Equal("uuid", archiveReasonId!.Value.Type);
        Assert.True(archiveReasonId.Value.Nullable);
        Assert.Null(archiveReasonId.Value.Default);

        // Eski erkin matn ustuni JOYIDA — §2.2 "Boshqa" tanlovi uni talab qiladi.
        Assert.True(await ColumnAsync("students", "archive_reason") is not null,
            "`students.archive_reason` yo'qolgan — bu qo'shimcha migratsiya edi.");

        // §6.3 — ota-onaga xabar SUKUT BO'YICHA O'CHIQ.
        await AssertBoolDefaultAsync("discipline_reasons", "notify_parent", expected: false);
        // Mavjud sabablarning hammasi faol bo'lib qoladi.
        await AssertBoolDefaultAsync("discipline_reasons", "is_active", expected: true);

        var description = await ColumnAsync("discipline_reasons", "description");
        Assert.True(description is not null);
        Assert.Equal("text", description!.Value.Type);
        Assert.True(description.Value.Nullable);

        // §5.5 — FAQAT TO'RTTA bayroq. Q4: qarzdorni arxivlash bloklangan.
        await AssertBoolDefaultAsync("school_meta", "archive_only_non_debtor_students", expected: true);
        // Ma'lumot sifati darvozalari — bugungi xatti-harakat saqlanadi.
        await AssertBoolDefaultAsync("school_meta", "make_attendance_reason_required", expected: false);
        await AssertBoolDefaultAsync("school_meta", "is_student_grade_required", expected: false);
        // Ota-onalar bugun o'zlashtirishni ko'rishadi — ko'rishaveradi.
        await AssertBoolDefaultAsync("school_meta", "show_learning_progress_in_parent_dashboard", expected: true);

        // §5.5 modul o'chirgichlarini ATAYLAB rad etadi — ular sxemaga
        // kirmaganini ham tekshiramiz, aks holda keyingi to'lqin ularni
        // "allaqachon bor ekan" deb qabul qilib qo'yardi.
        foreach (var declined in new[]
                 { "enable_behavior_system", "gamification", "warehouse", "reception_attendance_enabled" })
            Assert.Null(await ColumnAsync("school_meta", declined));
    }

    /// <summary>
    /// Check constraint'lar va indekslar. Ular EF modelida (ParityModel.cs)
    /// yozilgan, ya'ni snapshot'ga tushadi va keyingi `--autogenerate` ularni
    /// "ortiqcha" deb DROP qilmaydi — lekin ularning BAZADA borligini faqat
    /// shu test tekshiradi.
    /// </summary>
    [Fact]
    public async Task Constraintlar_va_indekslar_bazada_bor()
    {
        foreach (var name in new[]
                 {
                     "ck_debtor_statuses_name", "ck_debtor_statuses_position",
                     "ck_debtor_actions_comment",
                     "ck_certificate_types_name",
                     "ck_certificates_score", "ck_certificates_period",
                     "ck_student_archive_reasons_name", "ck_student_archive_reasons_position",
                 })
            Assert.True(await ConstraintExistsAsync(name), $"`{name}` check constraint'i yo'q.");

        foreach (var name in new[]
                 {
                     // §3.5 — "shu o'quvchining oxirgi amali" (created_at teskari).
                     "ix_debtor_actions_student_id_created_at",
                     // §3.5 — buzilgan va'dalar uchun qisman indeks.
                     "ix_debtor_actions_open_promises",
                     // §2.3 dagi ikkala indeks.
                     "ix_certificates_student_id_issued_on",
                     "ix_certificates_type_id",
                     // Kataloglarning unikal nomlari (seed ham shularga tayanadi).
                     "ix_debtor_statuses_name",
                     "ix_certificate_types_name",
                     "ix_student_archive_reasons_name",
                 })
            Assert.True(await IndexExistsAsync(name), $"`{name}` indeksi yo'q.");

        // Qisman indeks HAQIQATAN qisman bo'lishi kerak — `WHERE` bandi
        // yo'qolsa indeks ishlayveradi, lekin "faqat tirik va'dalar" ma'nosi
        // yo'qoladi va u jadval o'sishi bilan birga o'sib ketadi.
        var partial = await TextAsync(
            "select indexdef from pg_indexes where indexname = 'ix_debtor_actions_open_promises'") ?? "";
        Assert.Contains("WHERE", partial, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deleted_at IS NULL", partial, StringComparison.OrdinalIgnoreCase);

        // Check constraint HAQIQATAN ishlaydi — izohsiz amal yozib bo'lmaydi.
        var error = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var conn = new NpgsqlConnection(_database.OwnerConnectionString);
            await conn.OpenAsync();
            await ExecAsync(conn,
                "insert into debtor_actions (id, student_id, comment, created_by, created_at) "
                + "values (gen_random_uuid(), 'yoq', '   ', 'yoq', now())");
        });
        // FK ham, CHECK ham qaytarilishi mumkin — ikkalasi ham "bu qator
        // kirmaydi" degani; muhimi, bo'sh izoh JIMGINA yozilib qolmasligi.
        Assert.Contains(error.SqlState, new[] { "23514", "23503" });
    }

    /// <summary>
    /// `turnstile_events (device_user_id, event_at)` — spec'da yo'q, lekin
    /// turniket hisobotlarining hammasi `event_at` bo'yicha filtrlaydi va
    /// `TurnstileService.IngestAsync` dedup uchun aynan shu ikki ustunni
    /// o'qiydi. Indekssiz ikkalasi ham to'liq skan edi.
    /// </summary>
    [Fact]
    public async Task Turniket_hodisalarida_yangi_indeks_bor()
    {
        var def = await TextAsync(
            "select indexdef from pg_indexes "
            + "where indexname = 'ix_turnstile_events_device_user_id_event_at'");

        Assert.False(string.IsNullOrEmpty(def), "Turniket indeksi yaratilmagan.");
        Assert.Contains("device_user_id", def!, StringComparison.Ordinal);
        Assert.Contains("event_at", def, StringComparison.Ordinal);
    }

    // =====================================================================
    //  2. Seed
    // =====================================================================

    /// <summary>
    /// Katalog qatorlari. Id'lar BARQAROR — ular hujjatda va nosozlikni
    /// tekshirishda uchraydi, ya'ni har muhitda bir xil bo'lishi kerak.
    /// </summary>
    [Fact]
    public async Task Kataloglar_seed_qilingan()
    {
        // §3.5 — to'rtta boshlang'ich holat.
        Assert.Equal(4L, await CountAsync("select count(*) from debtor_statuses"));
        Assert.Equal("To'lash va'da qilindi", await TextAsync(
            "select name from debtor_statuses where id = '00000000-0000-0000-0000-0000000000d2'"));

        // §2.2 — "Boshqa" ATAYLAB ro'yxatda va oxirida: u erkin matn bilan
        // birga ishlaydi, ya'ni usiz administrator eng yaqin, lekin noto'g'ri
        // sababni tanlab qo'yardi va hisobot jimgina buzilardi.
        Assert.Equal(7L, await CountAsync("select count(*) from student_archive_reasons"));
        Assert.Equal(99, await IntAsync(
            "select \"position\" from student_archive_reasons where name = 'Boshqa'"));

        // §2.3 — turlar ATAYLAB seed qilinmaydi: maktab qaysi imtihonlarni
        // o'tkazishi bizning taxminimiz emas.
        Assert.Equal(0L, await CountAsync("select count(*) from certificate_types"));
    }

    // =====================================================================
    //  3. Grantlar — §3.5 ning qarori
    // =====================================================================

    /// <summary>
    /// `app_rw` beshta yangi jadvalning HAMMASIDA to'liq CRUD qila oladi.
    ///
    /// <para>
    /// Harness rolga eng yomon holatni (hamma joyda to'liq CRUD) beradi, ya'ni
    /// bu test o'z-o'zicha kam narsani isbotlardi — SHUNING UCHUN pastda
    /// NAZORAT tasdig'i bor: `payments` da DELETE hali ham YO'Q. Ikkalasi
    /// birga "REVOKE mexanizmi ishlayapti, lekin bu jadvallarga TEGMAYDI"
    /// degani.
    /// </para>
    /// <para>
    /// Nega REVOKE yo'q: §3.5 `debtor_actions` ni "append-only in behaviour
    /// but not in grants" deb belgilaydi. Tarix `deleted_at` bilan
    /// himoyalanadi; xato yozilgan izohni tuzatish esa oddiy `UPDATE` bo'lib
    /// qolishi kerak.
    /// </para>
    /// </summary>
    [Fact]
    public async Task App_rw_yangi_jadvallarda_toliq_CRUD_qila_oladi()
    {
        _database.RequireRealAppRw();

        foreach (var table in NewTables)
            foreach (var privilege in new[] { "SELECT", "INSERT", "UPDATE", "DELETE" })
                Assert.True(
                    await BoolAsync(
                        $"select has_table_privilege('app_rw', 'public.{table}', '{privilege}')"),
                    $"`app_rw` da `{table}` uchun {privilege} huquqi yo'q — "
                    + "Migrations/Sql/parity_wave2_guards.sql ni tekshiring.");

        // NAZORAT: moliyaviy REVOKE hali ham kuchda. Busiz yuqoridagi tsikl
        // "hamma narsaga ruxsat berilgan" bazada ham yashil bo'lardi.
        Assert.False(
            await BoolAsync("select has_table_privilege('app_rw', 'public.payments', 'DELETE')"),
            "`payments` da DELETE paydo bo'lgan — SPEC §4.1 buzilgan.");
    }

    /// <summary>
    /// Grant HAQIQATDA ishlayotganini `has_table_privilege` emas, amalning
    /// o'zi ko'rsatadi: `app_rw` bilan qator qo'shiladi, tahrirlanadi va
    /// o'chiriladi. Aynan §3.5 talab qilgan xatti-harakat.
    /// </summary>
    [Fact]
    public async Task App_rw_debtor_action_qatorini_qosha_tahrirlay_va_ochira_oladi()
    {
        _database.RequireRealAppRw();

        string studentId, userId;
        await using (var db = NewDb())
        {
            var user = new AppUser
            {
                FullName = "Kassir", Role = "admin", Email = $"kassir.{Guid.NewGuid():N}",
            };
            var student = new Student
            {
                FullName = "Qarzdor o'quvchi", ClassName = "1-A", EnrollmentDate = "2026-01-01",
            };
            db.Users.Add(user);
            db.Students.Add(student);
            await db.SaveChangesAsync();
            (studentId, userId) = (student.Id, user.Id);
        }

        var id = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(_database.AppRwConnectionString);
        await conn.OpenAsync();

        await ExecAsync(conn,
            "insert into debtor_actions (id, student_id, status_id, comment, promised_on, created_by, created_at) "
            + $"values ('{id}', '{studentId}', "
            + "'00000000-0000-0000-0000-0000000000d2', 'Ota-ona bilan gaplashildi', "
            + $"current_date + 7, '{userId}', now())");

        // Tahrirlash — izohdagi xato tuzatiladi (moliyaviy jadval emas).
        await ExecAsync(conn, $"update debtor_actions set comment = 'Tuzatildi' where id = '{id}'");
        Assert.Equal("Tuzatildi", await TextAsync($"select comment from debtor_actions where id = '{id}'"));

        // O'chirish — GRANT darajasida mumkin. Ilova buni qilmaydi
        // (`deleted_at` qo'yadi), lekin bu kod qoidasi, grant qoidasi emas.
        await ExecAsync(conn, $"delete from debtor_actions where id = '{id}'");
        Assert.Equal(0L, await CountAsync($"select count(*) from debtor_actions where id = '{id}'"));
    }

    // =====================================================================
    //  4. Eski qatorlar va DEFAULT'lar — eng muhim tasdiq
    // =====================================================================

    /// <summary>
    /// MIGRATSIYADAN OLDIN mavjud bo'lgan qator yangi ustunlarda qanday
    /// qiymat oladi.
    ///
    /// <para>
    /// Test migratsiyani ORQAGA qaytaradi, o'sha "eski" holatda intizom sababi
    /// va maktab sozlamasi qatorini yozadi, so'ng yana OLDINGA yuradi. Shundan
    /// keyingina savol haqiqiy bo'ladi: jonli maktab bazasidagi o'nlab intizom
    /// sababi migratsiyadan keyin ota-onaga xabar yubora boshlaydimi?
    /// Javob YO'Q bo'lishi shart (§6.3 ogohlantirishi).
    /// </para>
    /// </summary>
    [Fact]
    public async Task Eski_qatorlar_togri_DEFAULT_qiymatni_oladi()
    {
        await MigrateToAsync(PreviousMigration);

        // "Eski" qatorlar — yangi ustunlar hali yo'q paytda yoziladi.
        var reasonId = Guid.NewGuid().ToString();
        var metaId = Guid.NewGuid().ToString();
        await using (var conn = new NpgsqlConnection(_database.OwnerConnectionString))
        {
            await conn.OpenAsync();
            await InsertMinimalRowAsync(conn, "discipline_reasons", new()
            {
                ["id"] = $"'{reasonId}'",
                ["name"] = "'Kechikdi'",
                ["points"] = "-5",
            });
            await InsertMinimalRowAsync(conn, "school_meta", new()
            {
                ["id"] = $"'{metaId}'",
                ["current_year"] = "'2025/2026'",
                ["name"] = "'Wunderkind'",
            });
        }

        await MigrateToAsync(null);

        // §6.3: mavjud HAR BIR sabab ota-onaga xabar YUBORMAYDI.
        Assert.False(
            await BoolAsync($"select notify_parent from discipline_reasons where id = '{reasonId}'"),
            "Eski intizom sababi `notify_parent = true` bo'lib qoldi — §6.3 buzilgan.");
        // Va faol bo'lib qoladi (aks holda butun modul jimgina o'chib qolardi).
        Assert.True(await BoolAsync($"select is_active from discipline_reasons where id = '{reasonId}'"));
        Assert.Null(await TextAsync($"select description from discipline_reasons where id = '{reasonId}'"));

        // §5.5 / Q4 — qarzdorni arxivlash bloklangan.
        Assert.True(await BoolAsync(
            $"select archive_only_non_debtor_students from school_meta where id = '{metaId}'"));
        // Bugungi xatti-harakat: majburiy emas.
        Assert.False(await BoolAsync(
            $"select make_attendance_reason_required from school_meta where id = '{metaId}'"));
        Assert.False(await BoolAsync(
            $"select is_student_grade_required from school_meta where id = '{metaId}'"));
        // Ota-onalar o'zlashtirishni KO'RISHDA DAVOM ETADI.
        Assert.True(
            await BoolAsync(
                $"select show_learning_progress_in_parent_dashboard from school_meta where id = '{metaId}'"),
            "Ota-ona kabinetidagi o'zlashtirish migratsiyadan keyin jimgina o'chib qoldi.");

        // Eski ma'lumot joyida: nom ham, ball ham o'zgarmagan.
        Assert.Equal("Kechikdi", await TextAsync(
            $"select name from discipline_reasons where id = '{reasonId}'"));
        Assert.Equal(-5, await IntAsync($"select points from discipline_reasons where id = '{reasonId}'"));
    }

    // =====================================================================
    //  5. `Down()` va ustun yo'qotmaslik
    // =====================================================================

    /// <summary>
    /// `Down()` AYNAN `Up()` yaratganini qaytaradi: beshta jadval va sakkizta
    /// ustun ketadi. So'ng migratsiya QAYTA qo'llanadi va hammasi joyiga
    /// qaytadi — ya'ni orqaga qaytish bir tomonlama emas va seed idempotent.
    /// </summary>
    [Fact]
    public async Task Down_yaratilgan_hamma_narsani_orqaga_qaytaradi()
    {
        await MigrateToAsync(PreviousMigration);

        foreach (var table in NewTables)
            Assert.False(await TableExistsAsync(table), $"`Down()` dan keyin `{table}` qolib ketdi.");

        foreach (var (table, column) in NewColumns)
            Assert.Null(await ColumnAsync(table, column));

        Assert.False(await IndexExistsAsync("ix_turnstile_events_device_user_id_event_at"));

        await using (var db = NewDb())
            Assert.DoesNotContain(ThisMigration, await db.Database.GetAppliedMigrationsAsync());

        // ---- va yana oldinga ----
        await MigrateToAsync(null);

        foreach (var table in NewTables)
            Assert.True(await TableExistsAsync(table), $"Qayta qo'llashdan keyin `{table}` yo'q.");
        foreach (var (table, column) in NewColumns)
            Assert.True(await ColumnAsync(table, column) is not null, $"`{table}.{column}` qaytmadi.");
        Assert.True(await IndexExistsAsync("ix_turnstile_events_device_user_id_event_at"));

        // Seed IDEMPOTENT: ikkinchi yurishda qatorlar IKKILANMAYDI.
        Assert.Equal(4L, await CountAsync("select count(*) from debtor_statuses"));
        Assert.Equal(7L, await CountAsync("select count(*) from student_archive_reasons"));
    }

    /// <summary>
    /// ENG QIMMAT XATO SINFI: EF'ning `--autogenerate` i model bilan snapshot
    /// kelishmaganda "ortiqcha" <c>DROP COLUMN</c> chiqaradi va jonli maktab
    /// bazasida yo'qolgan ustunni qaytarib bo'lmaydi.
    ///
    /// <para>
    /// Shuning uchun bu test bitta-ikkita ustunni emas, BUTUN SXEMANI
    /// solishtiradi: migratsiyadan oldingi (jadval, ustun) to'plami keyingi
    /// to'plamning QISMI bo'lishi shart. Bitta ustun yo'qolsa ham test uning
    /// aniq nomini aytib yiqiladi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Migratsiya_birorta_mavjud_ustunni_yoqotmaydi()
    {
        await MigrateToAsync(PreviousMigration);
        var before = await AllColumnsAsync();

        await MigrateToAsync(null);
        var after = await AllColumnsAsync();

        var lost = before.Except(after).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.True(lost.Count == 0,
            "Migratsiya quyidagi ustun(lar)ni yo'qotdi: " + string.Join(", ", lost));

        // NAZORAT: to'plam haqiqatan o'sgan bo'lishi kerak, aks holda yuqoridagi
        // tasdiq hech narsa qilmagan migratsiyada ham yashil bo'lardi.
        var added = after.Except(before).ToList();
        Assert.NotEmpty(added);
        foreach (var table in NewTables)
            Assert.Contains(added, c => c.StartsWith(table + ".", StringComparison.Ordinal));
        foreach (var (table, column) in NewColumns)
            Assert.Contains($"{table}.{column}", added);
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private AppDbContext NewDb() => PostgresFixture.NewContext(_database.OwnerConnectionString);

    /// <summary>
    /// Migratsiyani berilgan nuqtaga olib boradi. <c>null</c> = oxirigacha
    /// (<c>Up</c>), nom berilsa — o'sha migratsiyagacha ORQAGA (<c>Down</c>).
    /// </summary>
    private async Task MigrateToAsync(string? target)
    {
        await using var db = NewDb();
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    /// <summary>
    /// Jadvalga MINIMAL qator qo'yadi: NOT NULL va DEFAULT'siz har bir ustunga
    /// turiga mos "bo'sh" qiymat, qolganiga tegilmaydi.
    ///
    /// <para>
    /// Nega qo'lda yozilgan <c>INSERT</c> emas: <c>school_meta</c> da 30 dan
    /// ortiq NOT NULL ustun bor va ular vaqt o'tishi bilan o'zgaradi. Qo'lda
    /// yozilgan ro'yxat birinchi yangi ustunda testni yiqitardi — va bu
    /// migratsiyaning emas, testning nosozligi bo'lardi.
    /// </para>
    /// </summary>
    private static async Task InsertMinimalRowAsync(
        NpgsqlConnection conn, string table, Dictionary<string, string> values)
    {
        await using (var cmd = new NpgsqlCommand(
                         "select column_name, data_type from information_schema.columns "
                         + "where table_schema = 'public' and table_name = @t "
                         + "and is_nullable = 'NO' and column_default is null", conn))
        {
            cmd.Parameters.AddWithValue("t", table);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var column = reader.GetString(0);
                if (values.ContainsKey(column)) continue;
                values[column] = reader.GetString(1) switch
                {
                    "text" or "character varying" or "character" => "''",
                    "boolean" => "false",
                    "integer" or "bigint" or "smallint" => "0",
                    "numeric" or "double precision" or "real" => "0",
                    "uuid" => "gen_random_uuid()",
                    "date" => "current_date",
                    "jsonb" or "json" => "'{}'",
                    _ => "now()",
                };
            }
        }

        var columns = string.Join(", ", values.Keys.Select(c => $"\"{c}\""));
        await ExecAsync(conn, $"insert into {table} ({columns}) values ({string.Join(", ", values.Values)})");
    }

    private readonly record struct ColumnInfo(string Type, bool Nullable, string? Default);

    private async Task<ColumnInfo?> ColumnAsync(string table, string column)
    {
        await using var conn = new NpgsqlConnection(_database.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "select data_type, is_nullable, column_default from information_schema.columns "
            + "where table_schema = 'public' and table_name = @t and column_name = @c", conn);
        cmd.Parameters.AddWithValue("t", table);
        cmd.Parameters.AddWithValue("c", column);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new ColumnInfo(
            reader.GetString(0),
            reader.GetString(1) == "YES",
            reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    /// <summary>`boolean not null default &lt;expected&gt;` ekanini tekshiradi.</summary>
    private async Task AssertBoolDefaultAsync(string table, string column, bool expected)
    {
        var info = await ColumnAsync(table, column);
        Assert.True(info is not null, $"`{table}.{column}` ustuni yo'q.");
        Assert.Equal("boolean", info!.Value.Type);
        Assert.False(info.Value.Nullable, $"`{table}.{column}` null bo'la olmasligi kerak.");
        Assert.Equal(expected ? "true" : "false", info.Value.Default);
    }

    /// <summary>Butun sxemaning "jadval.ustun" to'plami.</summary>
    private async Task<HashSet<string>> AllColumnsAsync()
    {
        var rows = await ListAsync(
            "select table_name || '.' || column_name from information_schema.columns "
            + "where table_schema = 'public' "
            // Migratsiya tarixi jadvali hisobga olinmaydi — u har qadamda o'zgaradi.
            + "and table_name <> '__EFMigrationsHistory'");
        return rows.ToHashSet(StringComparer.Ordinal);
    }

    private Task<bool> TableExistsAsync(string table) => BoolAsync(
        "select exists (select 1 from information_schema.tables "
        + $"where table_schema = 'public' and table_name = '{table}')");

    private Task<bool> IndexExistsAsync(string name) => BoolAsync(
        $"select exists (select 1 from pg_indexes where schemaname = 'public' and indexname = '{name}')");

    private Task<bool> ConstraintExistsAsync(string name) => BoolAsync(
        $"select exists (select 1 from pg_constraint where conname = '{name}' and contype = 'c')");

    private async Task<object?> RawScalarAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(_database.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        var value = await cmd.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    private async Task<bool> BoolAsync(string sql) => (bool)(await RawScalarAsync(sql))!;

    private async Task<long> CountAsync(string sql) => (long)(await RawScalarAsync(sql))!;

    private async Task<int> IntAsync(string sql) => (int)(await RawScalarAsync(sql))!;

    private async Task<string?> TextAsync(string sql) => (string?)await RawScalarAsync(sql);

    private async Task<List<string>> ListAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(_database.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        var result = new List<string>();
        while (await reader.ReadAsync()) result.Add(reader.GetString(0));
        return result;
    }

    private static async Task ExecAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}

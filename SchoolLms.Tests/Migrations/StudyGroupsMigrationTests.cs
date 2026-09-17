using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Migrations;

// ===========================================================================
//  `StudyGroupsAndMemberships` — docs/modules/students-parity.md §2.1, §3.1.
// ===========================================================================
//
//  NIMA UCHUN BU MIGRATSIYA BOSHQALARIDAN XAVFLIROQ
//  ------------------------------------------------
//  U maktabning ENG KATTA va eng ishlatiladigan jadvallariga tegadi:
//  `journal_entries`, `lesson_notes`, `quarter_grades`, `schedule_templates`,
//  `week_assignments`. Ularning har biriga `owner_kind` ustuni qo'shiladi va
//  MAVJUD har bir qator `'class'` bo'lishi SHART — aks holda darsning kimga
//  tegishli ekani noma'lum bo'lib qoladi va jurnal jimgina bo'shab ko'rinadi.
//
//  Ikkinchi xavf — BACKFILL. `class_memberships` `students.class_name` dan
//  to'ldiriladi. Bu yerdagi xato "bola sinfsiz qoldi" degani, ya'ni aynan
//  bugun tuzatilgan G-1 xatosining takrori.
//
//  Shuning uchun bu testlar ikki yo'nalishda yuradi: migratsiyadan OLDIN
//  qator qo'yib, keyin oldinga surib natijani tekshiradi; va `Down()` dan
//  keyin hech qanday iz qolmasligini talab qiladi.

[Collection(SchoolLmsCollection.Name)]
public class StudyGroupsMigrationTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string ThisMigration = "20260917063212_StudyGroupsAndMemberships";
    private const string PreviousMigration = "20260916114934_ParityWave2Schema";

    /// <summary>Guruh dunyosining beshta yangi jadvali.</summary>
    private static readonly string[] NewTables =
    [
        "study_groups",
        "study_group_classes",
        "study_group_teachers",
        "study_group_members",
        "class_memberships",
    ];

    /// <summary>`owner_kind` qo'shiladigan jadvallar — hammasi maktabning o'zak jadvali.</summary>
    private static readonly string[] OwnerKindTables =
    [
        "journal_entries",
        "lesson_notes",
        "quarter_grades",
        "schedule_templates",
        "week_assignments",
    ];

    private TestDatabase _database = default!;

    public async Task InitializeAsync() =>
        _database = await fixture.Postgres.CreateDatabaseAsync("studygroups");

    /// <summary>
    /// Har test o'z bazasini oladi, ya'ni o'z ulanish hovuzini ham. Tozalanmasa
    /// konteynerdagi <c>max_connections</c> tugaydi va KEYINGI klasslar
    /// <c>53300</c> bilan yiqiladi (<c>AllocationTests</c> dagi bir xil sabab).
    /// </summary>
    public Task DisposeAsync()
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.OwnerConnectionString));
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.AppRwConnectionString));
        return Task.CompletedTask;
    }

    // =====================================================================
    //  1. Up — jadvallar va ustunlar
    // =====================================================================

    [Fact]
    public async Task Up_beshta_yangi_jadvalni_yaratadi()
    {
        await using var conn = await OpenOwnerAsync();

        foreach (var table in NewTables)
            Assert.True(await TableExistsAsync(conn, table), $"`{table}` jadvali yo'q");
    }

    /// <summary>
    /// <b>Eng muhim test.</b> Migratsiyadan OLDIN yozilgan qatorlar
    /// <c>owner_kind = 'class'</c> bo'lib qoladi: ya'ni maktabning butun
    /// tarixi "sinf darsi" bo'lib saqlanadi va hech narsa o'zgarmaydi.
    /// Shu bilan birga guruh darslari bayrog'i O'CHIQ keladi — yangi
    /// xatti-harakat maktab o'zi yoqmaguncha boshlanmaydi.
    /// </summary>
    [Fact]
    public async Task Eski_qatorlar_class_bolib_qoladi_va_guruh_bayrogi_ochiq()
    {
        await MigrateToAsync(PreviousMigration);

        await using (var conn = await OpenOwnerAsync())
        {
            foreach (var table in OwnerKindTables)
                Assert.False(await ColumnExistsAsync(conn, table, "owner_kind"),
                    $"`{table}.owner_kind` migratsiyadan OLDIN bo'lmasligi kerak");

            await InsertMinimalRowAsync(conn, "journal_entries", []);
            await InsertMinimalRowAsync(conn, "lesson_notes", []);
            await InsertMinimalRowAsync(conn, "quarter_grades", []);
            await InsertMinimalRowAsync(conn, "schedule_templates", []);
            await InsertMinimalRowAsync(conn, "week_assignments", []);
            await InsertMinimalRowAsync(conn, "subjects", []);
            await InsertMinimalRowAsync(conn, "school_meta", []);
        }

        await MigrateToAsync(null);

        await using (var conn = await OpenOwnerAsync())
        {
            foreach (var table in OwnerKindTables)
            {
                var distinct = await ScalarAsync(conn,
                    $"select string_agg(distinct owner_kind, ',') from {table}");
                Assert.Equal("class", distinct);
            }

            Assert.Equal(false, await ScalarAsync(conn, "select bool_or(is_groupable) from subjects"));
            Assert.Equal(false, await ScalarAsync(conn,
                "select bool_or(group_lessons_enabled) from school_meta"));
        }
    }

    // =====================================================================
    //  2. Backfill — `students.class_name` dan sinf a'zoligi
    // =====================================================================

    /// <summary>
    /// Sinfi bor o'quvchiga AYNAN BITTA faol a'zolik yoziladi; sinfsizga
    /// hech narsa; bazada mavjud bo'lmagan sinf nomi bo'lsa ham hech narsa
    /// (bu yerda taxmin qilish — bolani boshqa sinfga qo'shib yuborish demak).
    /// </summary>
    [Fact]
    public async Task Backfill_sinfi_bor_oquvchiga_bitta_faol_azolik_yozadi()
    {
        await MigrateToAsync(PreviousMigration);

        const string withClass = "st-backfill-1";
        const string noClass = "st-backfill-2";
        const string unknownClass = "st-backfill-3";

        await using (var conn = await OpenOwnerAsync())
        {
            await InsertMinimalRowAsync(conn, "classes", new()
            {
                ["id"] = "'cls-backfill'",
                ["name"] = "'11-Z'",
            });

            await InsertMinimalRowAsync(conn, "students", new()
            {
                ["id"] = $"'{withClass}'", ["class_name"] = "'11-Z'",
            });
            await InsertMinimalRowAsync(conn, "students", new()
            {
                ["id"] = $"'{noClass}'", ["class_name"] = "''",
            });
            await InsertMinimalRowAsync(conn, "students", new()
            {
                ["id"] = $"'{unknownClass}'", ["class_name"] = "'99-Q'",
            });
        }

        await MigrateToAsync(null);

        await using (var conn = await OpenOwnerAsync())
        {
            Assert.Equal(1L, await ScalarAsync(conn,
                $"select count(*) from class_memberships where student_id = '{withClass}' and left_on is null"));
            Assert.Equal("cls-backfill", await ScalarAsync(conn,
                $"select class_id from class_memberships where student_id = '{withClass}'"));

            Assert.Equal(0L, await ScalarAsync(conn,
                $"select count(*) from class_memberships where student_id = '{noClass}'"));
            Assert.Equal(0L, await ScalarAsync(conn,
                $"select count(*) from class_memberships where student_id = '{unknownClass}'"));
        }
    }

    // =====================================================================
    //  3. Baza darajasidagi qoidalar
    // =====================================================================

    /// <summary>
    /// Bitta o'quvchi bitta fanda ikkita FAOL guruhda tura olmaydi — buni
    /// ilova emas, BAZA rad etadi (students-parity.md §5 Q1 sukut qarori).
    /// Tark etilgan a'zolik (<c>left_on</c>) esa to'sqinlik qilmaydi.
    /// </summary>
    [Fact]
    public async Task Bir_fanda_ikkinchi_faol_guruh_azoligi_rad_etiladi()
    {
        await using var conn = await OpenOwnerAsync();

        await InsertActorAsync(conn, "u-grp");
        await InsertMinimalRowAsync(conn, "subjects", new() { ["id"] = "'sub-eng'", ["name"] = "'Ingliz tili'" });
        await InsertMinimalRowAsync(conn, "students", new() { ["id"] = "'st-grp-1'" });
        await ExecAsync(conn,
            "insert into study_groups (id, name, subject_id, is_archived, created_by, created_at) values "
            + "('11111111-1111-1111-1111-111111111111', 'Ingliz A', 'sub-eng', false, 'u-grp', now()), "
            + "('22222222-2222-2222-2222-222222222222', 'Ingliz B', 'sub-eng', false, 'u-grp', now())");

        await ExecAsync(conn, Member("11111111-1111-1111-1111-111111111111", leftOn: null));

        var duplicate = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecAsync(conn, Member("22222222-2222-2222-2222-222222222222", leftOn: null)));
        Assert.Equal("23505", duplicate.SqlState);

        // Guruhni tark etgan bo'lsa — ikkinchisiga qo'shilishi mumkin.
        await ExecAsync(conn, "update study_group_members set left_on = current_date");
        await ExecAsync(conn, Member("22222222-2222-2222-2222-222222222222", leftOn: null));

        Assert.Equal(1L, await ScalarAsync(conn,
            "select count(*) from study_group_members where left_on is null"));

        static string Member(string groupId, string? leftOn) =>
            "insert into study_group_members "
            + "(id, group_id, subject_id, student_id, joined_on, left_on, created_by, created_at) values "
            + $"(gen_random_uuid(), '{groupId}', 'sub-eng', 'st-grp-1', current_date, "
            + $"{(leftOn is null ? "null" : $"'{leftOn}'")}, 'u-grp', now())";
    }

    /// <summary>Bir o'quvchiga ikkita faol SINF a'zoligi ham bazada mumkin emas.</summary>
    [Fact]
    public async Task Ikkinchi_faol_sinf_azoligi_rad_etiladi()
    {
        await using var conn = await OpenOwnerAsync();

        await InsertMinimalRowAsync(conn, "classes", new() { ["id"] = "'cls-a'", ["name"] = "'1-A'" });
        await InsertMinimalRowAsync(conn, "classes", new() { ["id"] = "'cls-b'", ["name"] = "'1-B'" });
        await InsertMinimalRowAsync(conn, "students", new() { ["id"] = "'st-cls-1'", ["class_name"] = "''" });

        await ExecAsync(conn, Membership("cls-a"));

        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(conn, Membership("cls-b")));
        Assert.Equal("23505", duplicate.SqlState);

        static string Membership(string classId) =>
            "insert into class_memberships (id, student_id, class_id, joined_on, created_at) values "
            + $"(gen_random_uuid(), 'st-cls-1', '{classId}', current_date, now())";
    }

    /// <summary>
    /// Ilova roli (<c>app_rw</c>) yangi jadvallarda ishlay olishi kerak —
    /// bularning birortasi moliyaviy jadval emas, shuning uchun to'liq CRUD.
    /// </summary>
    [Fact]
    public async Task App_rw_yangi_jadvallarda_ishlay_oladi()
    {
        _database.RequireRealAppRw();

        await using var owner = await OpenOwnerAsync();
        await InsertActorAsync(owner, "u-rw");
        await InsertMinimalRowAsync(owner, "subjects", new() { ["id"] = "'sub-rw'", ["name"] = "'Fizika'" });

        await using var app = new NpgsqlConnection(_database.AppRwConnectionString);
        await app.OpenAsync();

        await ExecAsync(app,
            "insert into study_groups (id, name, subject_id, is_archived, created_by, created_at) values "
            + "('33333333-3333-3333-3333-333333333333', 'Fizika A', 'sub-rw', false, 'u-rw', now())");
        await ExecAsync(app, "update study_groups set name = 'Fizika B' where subject_id = 'sub-rw'");
        Assert.Equal("Fizika B", await ScalarAsync(app, "select name from study_groups where subject_id = 'sub-rw'"));
        await ExecAsync(app, "delete from study_groups where subject_id = 'sub-rw'");
    }

    // =====================================================================
    //  4. Down — iz qoldirmaydi
    // =====================================================================

    [Fact]
    public async Task Down_yaratilgan_hamma_narsani_orqaga_qaytaradi()
    {
        await MigrateToAsync(PreviousMigration);

        await using (var conn = await OpenOwnerAsync())
        {
            foreach (var table in NewTables)
                Assert.False(await TableExistsAsync(conn, table), $"`{table}` `Down()` dan keyin qolib ketdi");

            foreach (var table in OwnerKindTables)
                Assert.False(await ColumnExistsAsync(conn, table, "owner_kind"),
                    $"`{table}.owner_kind` `Down()` dan keyin qolib ketdi");

            Assert.False(await ColumnExistsAsync(conn, "subjects", "is_groupable"));
            Assert.False(await ColumnExistsAsync(conn, "school_meta", "group_lessons_enabled"));
        }

        // Va qaytadan oldinga — migratsiya ikkinchi marta ham toza yuradi.
        await MigrateToAsync(null);

        await using (var conn = await OpenOwnerAsync())
            foreach (var table in NewTables)
                Assert.True(await TableExistsAsync(conn, table));
    }

    /// <summary>
    /// Migratsiya birorta MAVJUD ustunni yo'qotmasligi kerak: oldingi holatdagi
    /// (jadval, ustun) to'plami keyingisining QISM to'plami bo'lishi shart.
    /// Avtogeneratsiya so'ralmagan <c>DROP</c> yozganda aynan shu test qizil bo'ladi.
    /// </summary>
    [Fact]
    public async Task Migratsiya_birorta_mavjud_ustunni_yoqotmaydi()
    {
        await MigrateToAsync(PreviousMigration);
        HashSet<string> before;
        await using (var conn = await OpenOwnerAsync()) before = await AllColumnsAsync(conn);

        await MigrateToAsync(null);
        await using (var conn = await OpenOwnerAsync())
        {
            var after = await AllColumnsAsync(conn);
            var lost = before.Except(after).ToList();
            Assert.True(lost.Count == 0, "Migratsiya ustun(lar)ni yo'qotdi: " + string.Join(", ", lost));
        }
    }

    // =====================================================================
    //  Yordamchi qism
    // =====================================================================

    private AppDbContext NewDb() => PostgresFixture.NewContext(_database.OwnerConnectionString);

    /// <summary>null = oxirigacha (Up), nom = o'sha migratsiyagacha orqaga (Down).</summary>
    private async Task MigrateToAsync(string? target)
    {
        await using var db = NewDb();
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    private async Task<NpgsqlConnection> OpenOwnerAsync()
    {
        var conn = new NpgsqlConnection(_database.OwnerConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    private static async Task ExecAsync(NpgsqlConnection conn, string sql)
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

    private static async Task<bool> TableExistsAsync(NpgsqlConnection conn, string table) =>
        await ScalarAsync(conn,
            "select count(*) from information_schema.tables "
            + $"where table_schema = 'public' and table_name = '{table}'") is 1L;

    private static async Task<bool> ColumnExistsAsync(NpgsqlConnection conn, string table, string column) =>
        await ScalarAsync(conn,
            "select count(*) from information_schema.columns "
            + $"where table_schema = 'public' and table_name = '{table}' and column_name = '{column}'") is 1L;

    private static async Task<HashSet<string>> AllColumnsAsync(NpgsqlConnection conn)
    {
        var columns = new HashSet<string>(StringComparer.Ordinal);
        await using var cmd = new NpgsqlCommand(
            "select table_name, column_name from information_schema.columns where table_schema = 'public'", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) columns.Add($"{reader.GetString(0)}.{reader.GetString(1)}");
        return columns;
    }

    /// <summary>
    /// `created_by` foydalanuvchiga FK bilan bog'langan, shuning uchun har bir
    /// yozuv uchun aktyor kerak.
    /// </summary>
    private static Task InsertActorAsync(NpgsqlConnection conn, string id) =>
        InsertMinimalRowAsync(conn, "users", new()
        {
            ["id"] = $"'{id}'",
            ["email"] = $"'{id}@test.local'",
            ["full_name"] = "'Test aktyor'",
        });

    /// <summary>
    /// Jadvalga minimal qator qo'yadi: DEFAULT'siz har bir NOT NULL ustunga
    /// turiga mos "bo'sh" qiymat. Qo'lda yozilgan ustun ro'yxati birinchi yangi
    /// ustunda testni yiqitardi — va bu migratsiyaning emas, testning nosozligi
    /// bo'lardi (<c>ParityWave2MigrationTests</c> dagi bir xil sabab).
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
                    // `users.permissions` — text[]; information_schema uni "ARRAY" deb ataydi.
                    "ARRAY" => "'{}'",
                    _ => "now()",
                };
            }
        }

        var columns = string.Join(", ", values.Keys.Select(c => $"\"{c}\""));
        await ExecAsync(conn, $"insert into {table} ({columns}) values ({string.Join(", ", values.Values)})");
    }
}

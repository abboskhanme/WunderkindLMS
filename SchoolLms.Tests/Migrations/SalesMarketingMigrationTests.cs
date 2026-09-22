using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Migrations;

// ===========================================================================
//  `SalesAndMarketing` — ommaviy ariza formasi (surveys, survey_submissions),
//  yangiliklar (news) va `leads` ga uchta qo'shimcha ustun.
//  docs/modules/sales-marketing.md §4 (sxema), §8.3 (bu testning qamrovi).
//  Shakli `TransactionTypesMigrationTests` dan.
// ===========================================================================
//
//  QAMROV
//  ------
//  1) Uchta jadval — ustunlar AYNAN deklaratsiya qilingan to'plam (tip va
//     null bo'la olishi bilan). Ortiqcha ustun ham xato: D3/D4/D6 `language`,
//     `custom_fields`, `branch_id` ni ataylab rad etgan.
//  2) `leads` — `source` (default 'manual'), `survey_id`, `created_at`.
//  3) HAR BIR CHECK haqiqatan rad etadi (mavjudligi kifoya emas) — va aynan
//     O'SHA cheklov nomi bilan. `ck_leads_source_survey` ikkala yo'nalishda.
//  4) FK xulqi: restrict (D7) va set null (§4.1, §4.2).
//  5) Migratsiyadan OLDIN bo'lgan lid: `created_at = NULL`, `source = 'manual'` (Q7).
//  6) `app_rw` — uchala jadvalda to'liq CRUD, va bu huquqni MIGRATSIYA
//     beradi (fixture'ning sukut huquqi emas — pastdagi testga qarang).
//  7) `Down()` — AYNAN `Up()` qo'shganini olib tashlaydi, ortiq ham, kam ham emas.

[Collection(SchoolLmsCollection.Name)]
public class SalesMarketingMigrationTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string PreviousMigration = "20260918151831_AttendanceMarkPerLesson";
    private const string ThisMigration = "20260921150559_SalesAndMarketing";

    private const string UserId = "u-sm-mig";
    private const string SurveyId = "5a1e5000-0000-0000-0000-000000000001";
    private const string StageId = "st-sm-mig";

    /// <summary>Uchta yangi jadvalning AYNAN deklaratsiya qilingan ustunlari (§4.1–§4.3).</summary>
    private static readonly (string Table, string Column, string DataType, string Nullable)[] Columns =
    [
        ("surveys", "id", "uuid", "NO"),
        ("surveys", "slug", "text", "NO"),
        ("surveys", "name", "text", "NO"),
        ("surveys", "subtitle", "text", "YES"),
        ("surveys", "image_url", "text", "YES"),
        ("surveys", "offer_url", "text", "YES"),
        ("surveys", "thank_you_text", "text", "YES"),
        ("surveys", "stage_id", "text", "YES"),
        ("surveys", "show_student_first_name_input", "boolean", "NO"),
        ("surveys", "show_student_last_name_input", "boolean", "NO"),
        ("surveys", "show_student_phone_number_input", "boolean", "NO"),
        ("surveys", "show_student_grade_input", "boolean", "NO"),
        ("surveys", "show_student_gender_input", "boolean", "NO"),
        ("surveys", "is_active", "boolean", "NO"),
        ("surveys", "created_by", "text", "NO"),
        ("surveys", "created_at", "timestamp with time zone", "NO"),
        ("surveys", "updated_at", "timestamp with time zone", "YES"),

        ("survey_submissions", "id", "uuid", "NO"),
        ("survey_submissions", "survey_id", "uuid", "NO"),
        ("survey_submissions", "lead_id", "text", "YES"),
        ("survey_submissions", "status", "text", "NO"),
        ("survey_submissions", "parent_first_name", "text", "NO"),
        ("survey_submissions", "parent_last_name", "text", "YES"),
        ("survey_submissions", "parent_phone", "text", "NO"),
        ("survey_submissions", "parent_phone_key", "text", "NO"),
        ("survey_submissions", "student_first_name", "text", "YES"),
        ("survey_submissions", "student_last_name", "text", "YES"),
        ("survey_submissions", "student_phone", "text", "YES"),
        ("survey_submissions", "student_grade", "smallint", "YES"),
        ("survey_submissions", "student_gender", "text", "YES"),
        ("survey_submissions", "ip", "text", "YES"),
        ("survey_submissions", "user_agent", "text", "YES"),
        ("survey_submissions", "created_at", "timestamp with time zone", "NO"),

        ("news", "id", "uuid", "NO"),
        ("news", "title", "text", "NO"),
        ("news", "body", "text", "NO"),
        ("news", "image_url", "text", "YES"),
        ("news", "for_employee", "boolean", "NO"),
        ("news", "for_parent", "boolean", "NO"),
        ("news", "for_student", "boolean", "NO"),
        ("news", "published_at", "timestamp with time zone", "YES"),
        ("news", "author_id", "text", "NO"),
        ("news", "author_name", "text", "NO"),
        ("news", "telegram_sent_at", "timestamp with time zone", "YES"),
        ("news", "telegram_recipient_count", "integer", "NO"),
        ("news", "telegram_sent_count", "integer", "NO"),
        ("news", "created_at", "timestamp with time zone", "NO"),
        ("news", "updated_at", "timestamp with time zone", "YES"),
        ("news", "deleted_at", "timestamp with time zone", "YES"),
    ];

    /// <summary>`leads` ga shu migratsiya qo'shgan uchta ustun (§4.4).</summary>
    private static readonly (string Table, string Column, string DataType, string Nullable)[] LeadColumns =
    [
        ("leads", "source", "text", "NO"),
        ("leads", "survey_id", "uuid", "YES"),
        ("leads", "created_at", "timestamp without time zone", "YES"),
    ];

    private static readonly string[] NewTables = ["surveys", "survey_submissions", "news"];

    private TestDatabase _database = default!;

    public async Task InitializeAsync() =>
        _database = await fixture.Postgres.CreateDatabaseAsync("salesmktmig");

    public Task DisposeAsync()
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.OwnerConnectionString));
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.AppRwConnectionString));
        return Task.CompletedTask;
    }

    // =====================================================================
    //  1) Sxema
    // =====================================================================

    [Fact]
    public async Task Up_uchta_jadvalni_aynan_deklaratsiya_qilingan_ustunlar_bilan_qoshadi()
    {
        await using var conn = await OpenOwnerAsync();

        foreach (var table in NewTables)
        {
            var expected = Columns.Where(c => c.Table == table)
                .Select(c => $"{c.Column}:{c.DataType}:{c.Nullable}").OrderBy(x => x, StringComparer.Ordinal).ToList();
            var actual = (await ColumnsOfAsync(conn, table))
                .Select(c => $"{c.Column}:{c.DataType}:{c.Nullable}").OrderBy(x => x, StringComparer.Ordinal).ToList();
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public async Task Up_leadsga_uchta_ustun_qoshadi_source_sukuti_manual()
    {
        await using var conn = await OpenOwnerAsync();
        var actual = await ColumnsOfAsync(conn, "leads");

        foreach (var (_, column, dataType, nullable) in LeadColumns)
        {
            var row = Assert.Single(actual, c => c.Column == column);
            Assert.Equal(dataType, row.DataType);
            Assert.Equal(nullable, row.Nullable);
        }

        var sourceDefault = (string?)await ScalarAsync(conn,
            "select column_default from information_schema.columns "
            + "where table_schema = 'public' and table_name = 'leads' and column_name = 'source'");
        Assert.Equal("'manual'::text", sourceDefault);
        // `created_at` — ATAYLAB sukutsiz: `default now()` tarixni migratsiya vaqti bilan tamg'alardi.
        Assert.Null(await ScalarAsync(conn,
            "select column_default from information_schema.columns "
            + "where table_schema = 'public' and table_name = 'leads' and column_name = 'created_at'"));
    }

    /// <summary>§4.1–§4.3 dagi sukut qiymatlari — minimal qatorlar ular bilan to'ladi.</summary>
    [Fact]
    public async Task Sukut_qiymatlari_spetsifikatsiyadagidek()
    {
        await using var conn = await OpenOwnerAsync();
        await SeedBaseAsync(conn);

        await ExecAsync(conn, $"insert into surveys (slug, name, created_by) values ('sukut-1', 'Sukut', '{UserId}')");
        Assert.Equal(1L, await ScalarAsync(conn,
            "select count(*) from surveys where slug = 'sukut-1' and id is not null "
            + "and show_student_first_name_input and show_student_last_name_input "
            + "and not show_student_phone_number_input and show_student_grade_input and show_student_gender_input "
            + "and is_active and created_at is not null and updated_at is null and stage_id is null"));

        await ExecAsync(conn,
            "insert into survey_submissions (survey_id, parent_first_name, parent_phone, parent_phone_key) "
            + $"values ('{SurveyId}', 'Sukut', '+998 90 000 00 00', '900000000')");
        Assert.Equal(1L, await ScalarAsync(conn,
            "select count(*) from survey_submissions where parent_first_name = 'Sukut' "
            + "and status = 'lead' and created_at is not null and lead_id is null"));

        await ExecAsync(conn,
            $"insert into news (title, body, for_parent, author_id, author_name) values ('Sukut', 'Matn', true, '{UserId}', 'A')");
        Assert.Equal(1L, await ScalarAsync(conn,
            "select count(*) from news where title = 'Sukut' and not for_employee and not for_student "
            + "and telegram_recipient_count = 0 and telegram_sent_count = 0 and created_at is not null "
            + "and published_at is null and deleted_at is null and telegram_sent_at is null"));
    }

    /// <summary>Indekslar mavjud VA to'g'ri qurilgan (unikal, qisman, kamayuvchi).</summary>
    [Fact]
    public async Task Indekslar_spetsifikatsiyadagidek_qurilgan()
    {
        await using var conn = await OpenOwnerAsync();

        var slug = await IndexDefAsync(conn, "surveys", "ux_surveys_slug");
        Assert.Contains("CREATE UNIQUE INDEX", slug, StringComparison.Ordinal);
        Assert.Contains("lower(slug)", slug, StringComparison.Ordinal);

        var feed = await IndexDefAsync(conn, "news", "ix_news_feed");
        Assert.Contains("published_at DESC", feed, StringComparison.Ordinal);
        Assert.Contains("WHERE (deleted_at IS NULL)", feed, StringComparison.Ordinal);

        var dedupe = await IndexDefAsync(conn, "survey_submissions", "ix_survey_submissions_dedupe");
        Assert.Contains("(survey_id, parent_phone_key, created_at DESC)", dedupe, StringComparison.Ordinal);

        Assert.Contains("(survey_id, created_at DESC)",
            await IndexDefAsync(conn, "survey_submissions", "ix_survey_submissions_survey"), StringComparison.Ordinal);
        Assert.Contains("(lead_id)",
            await IndexDefAsync(conn, "survey_submissions", "ix_survey_submissions_lead"), StringComparison.Ordinal);
        Assert.Contains("(is_active, created_at DESC)",
            await IndexDefAsync(conn, "surveys", "ix_surveys_active"), StringComparison.Ordinal);
        Assert.Contains("(source, created_at DESC)",
            await IndexDefAsync(conn, "leads", "ix_leads_source"), StringComparison.Ordinal);

        var leadSurvey = await IndexDefAsync(conn, "leads", "ix_leads_survey");
        Assert.Contains("(survey_id)", leadSurvey, StringComparison.Ordinal);
        Assert.Contains("WHERE (survey_id IS NOT NULL)", leadSurvey, StringComparison.Ordinal);
    }

    // =====================================================================
    //  2) CHECK'lar haqiqatan rad etadi — aynan o'z nomi bilan
    // =====================================================================

    /// <summary>
    /// Har bir CHECK uchun yomon qiymat — SQLSTATE 23514 va AYNAN o'sha
    /// cheklov nomi (boshqa cheklov tasodifan ushlab qolgani "o'tdi" deb
    /// hisoblanmasin). <c>{user}</c>, <c>{survey}</c> — <see cref="SeedBaseAsync"/>.
    /// </summary>
    [Theory]
    [InlineData("insert into surveys (slug, name, created_by) values ('nom-yoq', '   ', '{user}')", "ck_surveys_name")]
    [InlineData("insert into surveys (slug, name, created_by) values ('Katta-harf', 'N', '{user}')", "ck_surveys_slug")]
    [InlineData("insert into surveys (slug, name, created_by) values ('ab', 'N', '{user}')", "ck_surveys_slug")]
    [InlineData("insert into surveys (slug, name, created_by) values ('-boshida', 'N', '{user}')", "ck_surveys_slug")]
    [InlineData("insert into surveys (slug, name, created_by) values ('oxirida-', 'N', '{user}')", "ck_surveys_slug")]
    [InlineData("insert into surveys (slug, name, created_by) values ('ikki--chiziq', 'N', '{user}')", "ck_surveys_slug")]
    [InlineData("insert into surveys (slug, name, created_by) values ('bo sh-joy', 'N', '{user}')", "ck_surveys_slug")]
    [InlineData("insert into surveys (slug, name, created_by) values (repeat('a', 61), 'N', '{user}')", "ck_surveys_slug")]
    [InlineData("insert into surveys (slug, name, created_by, show_student_gender_input) values ('jins-yoq', 'N', '{user}', false)", "ck_surveys_required_toggles")]
    [InlineData("insert into surveys (slug, name, created_by, show_student_grade_input) values ('sinf-yoq', 'N', '{user}', false)", "ck_surveys_required_toggles")]
    [InlineData("insert into survey_submissions (survey_id, parent_first_name, parent_phone, parent_phone_key, status) values ('{survey}', 'Ota', '+998', '1', 'spam')", "ck_survey_submissions_status")]
    [InlineData("insert into survey_submissions (survey_id, parent_first_name, parent_phone, parent_phone_key, student_gender) values ('{survey}', 'Ota', '+998', '1', 'boshqa')", "ck_survey_submissions_gender")]
    [InlineData("insert into survey_submissions (survey_id, parent_first_name, parent_phone, parent_phone_key, student_grade) values ('{survey}', 'Ota', '+998', '1', 12)", "ck_survey_submissions_grade")]
    [InlineData("insert into survey_submissions (survey_id, parent_first_name, parent_phone, parent_phone_key, student_grade) values ('{survey}', 'Ota', '+998', '1', -1)", "ck_survey_submissions_grade")]
    [InlineData("insert into survey_submissions (survey_id, parent_first_name, parent_phone, parent_phone_key) values ('{survey}', '  ', '+998', '1')", "ck_survey_submissions_parent")]
    [InlineData("insert into survey_submissions (survey_id, parent_first_name, parent_phone, parent_phone_key) values ('{survey}', 'Ota', '   ', '1')", "ck_survey_submissions_parent")]
    [InlineData("insert into news (title, body, for_parent, author_id, author_name) values ('  ', 'Matn', true, '{user}', 'A')", "ck_news_title")]
    [InlineData("insert into news (title, body, for_parent, author_id, author_name) values ('Sarlavha', '  ', true, '{user}', 'A')", "ck_news_body")]
    [InlineData("insert into news (title, body, author_id, author_name) values ('Sarlavha', 'Matn', '{user}', 'A')", "ck_news_audience")]
    [InlineData("insert into leads (id, full_name, gender, birth_date, parent_full_name, parent_phone, target_grade, stage, source) values ('l-bad-1', 'L', 'male', '', 'P', '+998', 1, 's', 'telegram')", "ck_leads_source")]
    [InlineData("insert into leads (id, full_name, gender, birth_date, parent_full_name, parent_phone, target_grade, stage, source, survey_id) values ('l-bad-2', 'L', 'male', '', 'P', '+998', 1, 's', 'survey', null)", "ck_leads_source_survey")]
    [InlineData("insert into leads (id, full_name, gender, birth_date, parent_full_name, parent_phone, target_grade, stage, source, survey_id) values ('l-bad-3', 'L', 'male', '', 'P', '+998', 1, 's', 'manual', '{survey}')", "ck_leads_source_survey")]
    public async Task Check_cheklovi_yomon_qiymatni_rad_etadi(string sql, string constraint)
    {
        await using var conn = await OpenOwnerAsync();
        await SeedBaseAsync(conn);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(conn, Fill(sql)));
        Assert.Equal("23514", ex.SqlState);
        Assert.Equal(constraint, ex.ConstraintName);
    }

    /// <summary>Chegaradagi TO'G'RI qiymatlar o'tadi — CHECK'lar ortiqcha qattiq emas.</summary>
    [Fact]
    public async Task Chegaradagi_togri_qiymatlar_otadi()
    {
        await using var conn = await OpenOwnerAsync();
        await SeedBaseAsync(conn);

        await ExecAsync(conn, $"insert into surveys (slug, name, created_by) values ('abc', 'Uch', '{UserId}')");
        await ExecAsync(conn, $"insert into surveys (slug, name, created_by) values (repeat('b', 60), 'Oltmish', '{UserId}')");
        await ExecAsync(conn, $"insert into surveys (slug, name, created_by) values ('qabul-2027-a1', 'Qabul', '{UserId}')");

        foreach (var (grade, gender) in new[] { ("0", "'female'"), ("11", "'male'"), ("null", "null") })
        {
            await ExecAsync(conn,
                "insert into survey_submissions (survey_id, parent_first_name, parent_phone, parent_phone_key, status, student_grade, student_gender) "
                + $"values ('{SurveyId}', 'Ota', '+998', '1', 'duplicate', {grade}, {gender})");
        }

        await ExecAsync(conn,
            $"insert into news (title, body, for_student, author_id, author_name) values ('Faqat oquvchi', 'Matn', true, '{UserId}', 'A')");

        await ExecAsync(conn,
            "insert into leads (id, full_name, gender, birth_date, parent_full_name, parent_phone, target_grade, stage, source, survey_id) "
            + $"values ('l-ok-1', 'L', 'male', '', 'P', '+998', 0, 's', 'survey', '{SurveyId}')");
        await ExecAsync(conn,
            "insert into leads (id, full_name, gender, birth_date, parent_full_name, parent_phone, target_grade, stage) "
            + "values ('l-ok-2', 'L', 'female', '', 'P', '+998', 0, 's')");

        Assert.Equal(1L, await ScalarAsync(conn, "select count(*) from leads where id = 'l-ok-2' and source = 'manual' and survey_id is null"));
    }

    /// <summary><c>ux_surveys_slug</c> — ikkinchi bir xil slug 23505.</summary>
    [Fact]
    public async Task Slug_bazada_yagona()
    {
        await using var conn = await OpenOwnerAsync();
        await SeedBaseAsync(conn);
        await ExecAsync(conn, $"insert into surveys (slug, name, created_by) values ('yagona-slug', 'A', '{UserId}')");

        var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(conn,
            $"insert into surveys (slug, name, created_by) values ('yagona-slug', 'B', '{UserId}')"));
        Assert.Equal("23505", ex.SqlState);
        Assert.Equal("ux_surveys_slug", ex.ConstraintName);
    }

    // =====================================================================
    //  3) FK xulqi — restrict va set null
    // =====================================================================

    /// <summary>
    /// D7: topshirig'i yoki lidi bor ariza o'chmaydi (restrict). Muallif xodim
    /// ham o'chmaydi. Lid o'chsa topshiriq QOLADI (<c>lead_id → NULL</c>),
    /// kanban ustuni o'chsa ariza qoladi (<c>stage_id → NULL</c>).
    /// </summary>
    [Fact]
    public async Task FK_restrict_va_set_null_spetsifikatsiyadagidek()
    {
        await using var conn = await OpenOwnerAsync();
        await SeedBaseAsync(conn);

        await ExecAsync(conn,
            "insert into leads (id, full_name, gender, birth_date, parent_full_name, parent_phone, target_grade, stage, source, survey_id) "
            + $"values ('l-fk', 'L', 'male', '', 'P', '+998', 1, '{StageId}', 'survey', '{SurveyId}')");
        await ExecAsync(conn,
            "insert into survey_submissions (id, survey_id, lead_id, parent_first_name, parent_phone, parent_phone_key) "
            + $"values ('5a1e5000-0000-0000-0000-0000000000f1', '{SurveyId}', 'l-fk', 'Ota', '+998', '1')");
        await ExecAsync(conn,
            $"insert into news (title, body, for_parent, author_id, author_name) values ('FK', 'Matn', true, '{UserId}', 'A')");

        var surveyInUse = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecAsync(conn, $"delete from surveys where id = '{SurveyId}'"));
        Assert.Equal("23503", surveyInUse.SqlState);

        var author = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecAsync(conn, $"delete from users where id = '{UserId}'"));
        Assert.Equal("23503", author.SqlState);

        await ExecAsync(conn, "delete from leads where id = 'l-fk'");
        Assert.Equal(1L, await ScalarAsync(conn,
            "select count(*) from survey_submissions where id = '5a1e5000-0000-0000-0000-0000000000f1' and lead_id is null"));

        // Topshiriq qoldi — ariza hali ham o'chmaydi, endi FAQAT submissions tufayli.
        var stillInUse = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecAsync(conn, $"delete from surveys where id = '{SurveyId}'"));
        Assert.Equal("fk_survey_submissions_surveys_survey_id", stillInUse.ConstraintName);

        await ExecAsync(conn, $"delete from lead_stages where id = '{StageId}'");
        Assert.Equal(1L, await ScalarAsync(conn, $"select count(*) from surveys where id = '{SurveyId}' and stage_id is null"));
    }

    /// <summary>Lid FK'si alohida: topshiriqsiz, lekin lidi bor ariza ham o'chmaydi (<c>fk_leads_surveys_survey_id</c>).</summary>
    [Fact]
    public async Task Lidi_bor_arizani_ochirib_bolmaydi()
    {
        await using var conn = await OpenOwnerAsync();
        await SeedBaseAsync(conn);
        await ExecAsync(conn,
            "insert into leads (id, full_name, gender, birth_date, parent_full_name, parent_phone, target_grade, stage, source, survey_id) "
            + $"values ('l-fk2', 'L', 'male', '', 'P', '+998', 1, 's', 'survey', '{SurveyId}')");

        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecAsync(conn, $"delete from surveys where id = '{SurveyId}'"));
        Assert.Equal("23503", ex.SqlState);
        Assert.Equal("fk_leads_surveys_survey_id", ex.ConstraintName);
    }

    // =====================================================================
    //  4) Migratsiyadan oldingi lid (§4.4, Q7)
    // =====================================================================

    /// <summary>
    /// Migratsiyadan OLDIN yaratilgan lid: <c>source = 'manual'</c> (haqiqat —
    /// uni xodim qo'lda kiritgan), <c>created_at = NULL</c> (taxmin qilingan
    /// sana yozilmaydi), <c>survey_id = NULL</c>.
    /// </summary>
    [Fact]
    public async Task Migratsiyadan_oldingi_lid_created_at_NULL_va_source_manual_oladi()
    {
        await MigrateToAsync(PreviousMigration);
        await using (var conn = await OpenOwnerAsync())
        {
            Assert.False(await ColumnExistsAsync(conn, "leads", "source"), "`leads.source` oldingi migratsiyada bo'lmasligi kerak");
            await ExecAsync(conn,
                "insert into leads (id, full_name, gender, birth_date, parent_full_name, parent_phone, target_grade, note, stage) "
                + "values ('l-eski', 'Eski lid', 'male', '2015-01-01', 'Ota', '+998 90 000 00 01', 3, 'Qo''lda', 's')");
        }

        await MigrateToAsync(ThisMigration);

        await using var after = await OpenOwnerAsync();
        Assert.Equal(1L, await ScalarAsync(after,
            "select count(*) from leads where id = 'l-eski' "
            + "and source = 'manual' and created_at is null and survey_id is null "
            + "and full_name = 'Eski lid' and note = 'Qo''lda'"));
    }

    // =====================================================================
    //  5) `app_rw` — MIGRATSIYA bergan to'liq CRUD
    // =====================================================================

    /// <summary>
    /// Fixture `app_rw` ga owner yaratgan HAR jadvalga sukut huquqi beradi —
    /// ya'ni oddiy tekshiruv migratsiyadagi GRANT yo'qolsa ham yashil qolardi.
    /// Shuning uchun: <c>Down()</c> → owner'ning sukut huquqini BEKOR qilish →
    /// nazorat jadvali (huquqsiz bo'lishi kerak) → <c>Up()</c>. Endi uchala
    /// jadvaldagi huquq faqat <c>sales_marketing_guards.sql</c> dan kelishi mumkin.
    /// </summary>
    [Fact]
    public async Task App_rw_uchala_jadvalda_tolik_crud_qila_oladi_va_buni_migratsiya_beradi()
    {
        _database.RequireRealAppRw();

        await MigrateToAsync(PreviousMigration);
        await using (var owner = await OpenOwnerAsync())
        {
            await ExecAsync(owner,
                "alter default privileges in schema public revoke select, insert, update, delete on tables from app_rw");
            await ExecAsync(owner, "create table zz_grant_control (id int)");
            Assert.False((bool)(await ScalarAsync(owner,
                "select has_table_privilege('app_rw', 'public.zz_grant_control', 'SELECT')"))!,
                "Nazorat: sukut huquqi bekor qilinmadi — test hech narsani isbotlamaydi.");
        }

        await MigrateToAsync(ThisMigration);

        await using (var owner = await OpenOwnerAsync())
        {
            await SeedBaseAsync(owner);
            foreach (var table in NewTables)
            foreach (var privilege in new[] { "SELECT", "INSERT", "UPDATE", "DELETE" })
            {
                Assert.True((bool)(await ScalarAsync(owner,
                        $"select has_table_privilege('app_rw', 'public.{table}', '{privilege}')"))!,
                    $"`app_rw` da `{table}` uchun {privilege} yo'q");
            }
        }

        await using var app = new NpgsqlConnection(_database.AppRwConnectionString);
        await app.OpenAsync();

        await ExecAsync(app, $"insert into surveys (slug, name, created_by) values ('grant-test', 'G', '{UserId}')");
        await ExecAsync(app, "update surveys set name = 'G2' where slug = 'grant-test'");
        await ExecAsync(app,
            "insert into survey_submissions (id, survey_id, parent_first_name, parent_phone, parent_phone_key) "
            + "select '5a1e5000-0000-0000-0000-0000000000a1', id, 'Ota', '+998', '1' from surveys where slug = 'grant-test'");
        await ExecAsync(app, "update survey_submissions set status = 'duplicate' where id = '5a1e5000-0000-0000-0000-0000000000a1'");
        await ExecAsync(app, $"insert into news (title, body, for_parent, author_id, author_name) values ('Grant', 'M', true, '{UserId}', 'A')");
        await ExecAsync(app, "update news set title = 'Grant2' where title = 'Grant'");

        Assert.Equal(1L, await ScalarAsync(app, "select count(*) from surveys where slug = 'grant-test' and name = 'G2'"));
        Assert.Equal(1L, await ScalarAsync(app, "select count(*) from survey_submissions where status = 'duplicate' and id = '5a1e5000-0000-0000-0000-0000000000a1'"));
        Assert.Equal(1L, await ScalarAsync(app, "select count(*) from news where title = 'Grant2'"));

        await ExecAsync(app, "delete from survey_submissions where id = '5a1e5000-0000-0000-0000-0000000000a1'");
        await ExecAsync(app, "delete from surveys where slug = 'grant-test'");
        await ExecAsync(app, "delete from news where title = 'Grant2'");
        Assert.Equal(0L, await ScalarAsync(app, "select count(*) from surveys where slug = 'grant-test'"));
        Assert.Equal(0L, await ScalarAsync(app, "select count(*) from news where title = 'Grant2'"));
    }

    // =====================================================================
    //  6) Down() — AYNAN Up() qo'shganini olib tashlaydi
    // =====================================================================

    /// <summary>
    /// Ikkala o'lchov ham shu migratsiyaning ikki CHEKKASIDA olinadi
    /// (<c>ThisMigration</c> va <c>PreviousMigration</c>), <c>head</c>da emas —
    /// keyingi migratsiya bu testni buzmasin (<c>TransactionTypesMigrationTests</c>
    /// izohidagi tuzoq). Farq — ustunlar, cheklovlar va indekslar bo'yicha —
    /// AYNAN kutilgan to'plam: ortiqcha narsa o'chsa ham, biror narsa qolib
    /// ketsa ham qizil. Keyin qayta <c>Up()</c> sxemani AYNAN tiklaydi.
    /// </summary>
    [Fact]
    public async Task Down_faqat_shu_migratsiya_qoshganini_olib_tashlaydi()
    {
        await MigrateToAsync(ThisMigration);
        var up = await SchemaAsync();

        await MigrateToAsync(PreviousMigration);
        var down = await SchemaAsync();

        await using (var db = NewDb())
            Assert.DoesNotContain(ThisMigration, await db.Database.GetAppliedMigrationsAsync());

        Assert.Empty(down.Columns.Except(up.Columns));
        Assert.Empty(down.Constraints.Except(up.Constraints));
        Assert.Empty(down.Indexes.Except(up.Indexes));

        var expectedColumns = Columns.Concat(LeadColumns).Select(c => $"{c.Table}.{c.Column}").OrderBy(x => x, StringComparer.Ordinal);
        Assert.Equal(expectedColumns, up.Columns.Except(down.Columns).OrderBy(x => x, StringComparer.Ordinal));

        string[] expectedConstraints =
        [
            "leads.ck_leads_source", "leads.ck_leads_source_survey", "leads.fk_leads_surveys_survey_id",
            "news.ck_news_audience", "news.ck_news_body", "news.ck_news_title", "news.fk_news_users_author_id", "news.pk_news",
            "survey_submissions.ck_survey_submissions_gender", "survey_submissions.ck_survey_submissions_grade",
            "survey_submissions.ck_survey_submissions_parent", "survey_submissions.ck_survey_submissions_status",
            "survey_submissions.fk_survey_submissions_leads_lead_id", "survey_submissions.fk_survey_submissions_surveys_survey_id",
            "survey_submissions.pk_survey_submissions",
            "surveys.ck_surveys_name", "surveys.ck_surveys_required_toggles", "surveys.ck_surveys_slug",
            "surveys.fk_surveys_lead_stages_stage_id", "surveys.fk_surveys_users_created_by", "surveys.pk_surveys",
        ];
        Assert.Equal(expectedConstraints.OrderBy(x => x, StringComparer.Ordinal),
            up.Constraints.Except(down.Constraints).OrderBy(x => x, StringComparer.Ordinal));

        string[] expectedIndexes =
        [
            "leads.ix_leads_source", "leads.ix_leads_survey",
            "news.ix_news_author_id", "news.ix_news_feed", "news.pk_news",
            "survey_submissions.ix_survey_submissions_dedupe", "survey_submissions.ix_survey_submissions_lead",
            "survey_submissions.ix_survey_submissions_survey", "survey_submissions.pk_survey_submissions",
            "surveys.ix_surveys_active", "surveys.ix_surveys_created_by", "surveys.ix_surveys_stage_id",
            "surveys.pk_surveys", "surveys.ux_surveys_slug",
        ];
        Assert.Equal(expectedIndexes.OrderBy(x => x, StringComparer.Ordinal),
            up.Indexes.Except(down.Indexes).OrderBy(x => x, StringComparer.Ordinal));

        await MigrateToAsync(ThisMigration);
        var again = await SchemaAsync();
        Assert.Equal(up.Columns.OrderBy(x => x, StringComparer.Ordinal), again.Columns.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(up.Constraints.OrderBy(x => x, StringComparer.Ordinal), again.Constraints.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(up.Indexes.OrderBy(x => x, StringComparer.Ordinal), again.Indexes.OrderBy(x => x, StringComparer.Ordinal));
    }

    // =====================================================================
    //  Yordamchi qism
    // =====================================================================

    private AppDbContext NewDb() => PostgresFixture.NewContext(_database.OwnerConnectionString);

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

    /// <summary>Bitta foydalanuvchi, bitta kanban ustuni va bitta ariza — CHECK/FK testlari uchun.</summary>
    private static async Task SeedBaseAsync(NpgsqlConnection conn)
    {
        await InsertMinimalRowAsync(conn, "users", new() { ["id"] = $"'{UserId}'", ["role"] = "'admin'" });
        await ExecAsync(conn, $"insert into lead_stages (id, title, color, \"order\") values ('{StageId}', 'Yangi', 'blue', 0)");
        await ExecAsync(conn,
            $"insert into surveys (id, slug, name, created_by, stage_id) values ('{SurveyId}', 'asosiy-ariza', 'Asosiy', '{UserId}', '{StageId}')");
    }

    private static string Fill(string sql) => sql.Replace("{user}", UserId).Replace("{survey}", SurveyId);

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

    private static async Task<bool> ColumnExistsAsync(NpgsqlConnection conn, string table, string column) =>
        await ScalarAsync(conn,
            "select count(*) from information_schema.columns "
            + $"where table_schema = 'public' and table_name = '{table}' and column_name = '{column}'") is 1L;

    private static async Task<List<(string Column, string DataType, string Nullable)>> ColumnsOfAsync(
        NpgsqlConnection conn, string table)
    {
        var result = new List<(string, string, string)>();
        await using var cmd = new NpgsqlCommand(
            "select column_name, data_type, is_nullable from information_schema.columns "
            + "where table_schema = 'public' and table_name = @t", conn);
        cmd.Parameters.AddWithValue("t", table);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return result;
    }

    private static async Task<string> IndexDefAsync(NpgsqlConnection conn, string table, string index)
    {
        var def = (string?)await ScalarAsync(conn,
            "select indexdef from pg_indexes "
            + $"where schemaname = 'public' and tablename = '{table}' and indexname = '{index}'");
        Assert.True(def is not null, $"`{index}` indeksi `{table}` da yo'q");
        return def!;
    }

    private sealed record Schema(HashSet<string> Columns, HashSet<string> Constraints, HashSet<string> Indexes);

    /// <summary>
    /// Sxemaning uchta kesimi, `jadval.nom` ko'rinishida. Cheklovlar — faqat
    /// p/f/c/u/x (NOT NULL'ni PostgreSQL 18 alohida cheklov qilib saqlaydi;
    /// uni ustunlar to'plami allaqachon qamraydi).
    /// </summary>
    private async Task<Schema> SchemaAsync()
    {
        await using var conn = await OpenOwnerAsync();
        return new Schema(
            await SetAsync(conn, "select table_name || '.' || column_name from information_schema.columns where table_schema = 'public'"),
            await SetAsync(conn,
                "select rel.relname || '.' || con.conname from pg_constraint con "
                + "join pg_class rel on rel.oid = con.conrelid join pg_namespace ns on ns.oid = rel.relnamespace "
                + "where ns.nspname = 'public' and con.contype in ('p','f','c','u','x')"),
            await SetAsync(conn, "select tablename || '.' || indexname from pg_indexes where schemaname = 'public'"));
    }

    private static async Task<HashSet<string>> SetAsync(NpgsqlConnection conn, string sql)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) set.Add(reader.GetString(0));
        return set;
    }

    /// <summary>
    /// Minimal qator: DEFAULT'siz har bir NOT NULL ustunga turiga mos "bo'sh"
    /// qiymat (<c>TransactionTypesMigrationTests</c> bilan bir xil yordamchi).
    /// </summary>
    private static async Task InsertMinimalRowAsync(
        NpgsqlConnection conn, string table, Dictionary<string, string> values)
    {
        await using (var cmd = new NpgsqlCommand(
                         "select column_name, data_type from information_schema.columns "
                         + "where table_schema = 'public' and table_name = @t "
                         + "and is_nullable = 'NO' and column_default is null "
                         + "and is_generated = 'NEVER' and identity_generation is null", conn))
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
                    "ARRAY" => "'{}'",
                    _ => "now()",
                };
            }
        }

        var columns = string.Join(", ", values.Keys.Select(c => $"\"{c}\""));
        await ExecAsync(conn, $"insert into {table} ({columns}) values ({string.Join(", ", values.Values)})");
    }
}

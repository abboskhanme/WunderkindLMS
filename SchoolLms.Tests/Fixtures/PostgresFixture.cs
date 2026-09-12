using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Infrastructure.Data;
using Testcontainers.PostgreSql;

namespace SchoolLms.Tests.Fixtures;

/// <summary>
/// Bitta test yurishi uchun BIR MARTA ko'tariladigan Postgres 17 konteyneri.
///
/// <para>Nima qiladi:</para>
/// <list type="number">
///   <item>postgres:17-alpine konteynerini ko'taradi (Testcontainers, tasodifiy port);</item>
///   <item><c>schoollms_owner</c> rolini va unga tegishli <c>schoollms_template</c> bazasini yaratadi;</item>
///   <item>migratsiyani AYNAN owner roli bilan qo'llaydi (prodda ham shunday bo'ladi — P1-02);</item>
///   <item>har test klassi uchun shu shablondan arzon nusxa (<c>CREATE DATABASE ... TEMPLATE</c>)
///         beradi — migratsiya qayta-qayta yugurmaydi, testlar bir-birining ma'lumotini ko'rmaydi;</item>
///   <item>har baza uchun IKKITA ulanish satri beradi: owner va <c>app_rw</c> (P1-22 ledger
///         immutability testi ikkalasini ham talab qiladi).</item>
/// </list>
///
/// <para>
/// MUHIM — <c>app_rw</c> ROLI VA GRANTLAR ORASIDAGI CHEGARA (P1-22, docs/TESTING.md §4).
/// Fixture rolning FAQAT MAVJUDLIGINI ta'minlaydi va unga ENG YOMON holatni beradi:
/// <c>init-roles.sql</c> ning 4-qadamidagi bazaviy huquqlar — sxemadagi HAR jadvalga
/// to'liq CRUD (<c>payments</c> ga DELETE ham kiradi). Moliyaviy <c>REVOKE</c> ni fixture
/// HECH QACHON yozmaydi — uni migratsiya (<c>Migrations/Sql/billing_guards.sql</c>) o'zi
/// qaytarib olishi shart. Shuning uchun P1-22 testlari fixture'ning emas, MIGRATSIYANING
/// grantini tekshiradi: <c>REVOKE</c> migratsiyadan yo'qolsa, test darhol qizil bo'ladi.
/// </para>
/// <para>
/// Nega rolni prod migratsiyasi emas, fixture yaratadi: <c>billing_guards.sql</c> dagi
/// GRANT bloki <c>if exists (select 1 from pg_roles where rolname = 'app_rw')</c> bilan
/// o'ralgan va rol yo'q bo'lsa JIM o'tib ketadi; migratsiya rolni o'zi yarata olmaydi,
/// chunki <c>schoollms_owner</c> prodda ham, bu yerda ham <c>NOCREATEROLE</c>
/// (<c>deploy/init-roles.sql</c>). Ya'ni prodda rolni <c>init-roles.sql</c> yaratadi,
/// bu yerda esa fixture — ikkalasi ham AYNAN bir xil bazaviy huquqlarni beradi va
/// ikkalasida ham qulfni migratsiya qo'yadi.
/// </para>
/// <para>
/// Rol baribir topilmasa (masalan tashqi Postgres'da yaratib bo'lmasa) fixture owner
/// satriga QAYTADI va <see cref="TestDatabase.AppRwIsOwnerFallback"/> = true qo'yadi —
/// bunday holatda grantga tayanadigan test <see cref="TestDatabase.RequireRealAppRw"/>
/// orqali O'ZI YIQILISHI kerak, o'tib ketmasligi.
/// </para>
///
/// <para>
/// Ishlab turgan lokal stack'ga (wunderkind-database, 5432) TEGMAYDI: konteyner nomi
/// boshqa, port tasodifiy, baza nomi boshqa. Ilova ulanish satri ham DI'da almashtiriladi
/// (<see cref="ApiFactory"/>), shuning uchun appsettings.json dagi localhost:5432 hech
/// qachon ishlatilmaydi.
/// </para>
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    /// <summary>Sxema egasi — migratsiya shu rol bilan qo'llanadi.</summary>
    public const string OwnerRole = "schoollms_owner";
    public const string OwnerPassword = "owner_test_pwd";

    /// <summary>
    /// Ilova (request-path) roli. Prodda uni <c>deploy/init-roles.sql</c> yaratadi, bu yerda —
    /// fixture (migratsiya <c>NOCREATEROLE</c> egasi bilan yuradi va rol yarata olmaydi).
    /// Fixture faqat BAZAVIY huquqlarni beradi; moliyaviy <c>REVOKE</c> migratsiyaniki.
    /// </summary>
    public const string AppRwRole = "app_rw";
    public const string AppRwPassword = "app_rw_test_pwd";

    private const string TemplateDatabase = "schoollms_template";

    private PostgreSqlContainer? _container;

    /// <summary>Superuser ulanish satri (`postgres` bazasiga) — rol/baza yaratish uchun.</summary>
    private string _adminConnectionString = string.Empty;

    /// <summary>Testda yaratilgan bazalar — tozalash uchun.</summary>
    private readonly List<string> _createdDatabases = new();

    private int _dbCounter;

    /// <summary>
    /// true bo'lsa — <c>app_rw</c> roli migratsiyada topilmadi va owner'ga qaytildi.
    /// P1-02 birlashtirilgach false bo'ladi.
    /// </summary>
    public bool AppRwIsOwnerFallback { get; private set; }

    /// <summary>Shablon (migratsiya qo'llangan) baza — faqat o'qish uchun tekshiruvlarda.</summary>
    public TestDatabase Template { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        // TEST_PG_ADMIN_CONNECTION berilgan bo'lsa — tashqi Postgres (CI xizmati yoki
        // docker-compose.test.yml). Berilmasa — o'zimiz konteyner ko'taramiz.
        var external = Environment.GetEnvironmentVariable("TEST_PG_ADMIN_CONNECTION");
        if (!string.IsNullOrWhiteSpace(external))
        {
            _adminConnectionString = external;
        }
        else
        {
            _container = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("postgres")
                .WithUsername("postgres")
                .WithPassword("postgres")
                // Locale=C — prod (docker-compose.yml) bilan bir xil tartiblash.
                .WithEnvironment("POSTGRES_INITDB_ARGS", "--encoding=UTF8 --locale=C")
                .Build();
            await _container.StartAsync();
            _adminConnectionString = _container.GetConnectionString();
        }

        // HAR BIRI ALOHIDA buyruq: Npgsql bir nechta statement'ni yashirin tranzaksiyaga
        // o'raydi, DROP/CREATE DATABASE esa tranzaksiya blokida ishlamaydi
        // (tashqi Postgres qayta ishlatilganda shu yerda yiqilardi).
        await ExecuteAdminAsync($"""DROP DATABASE IF EXISTS "{TemplateDatabase}" WITH (FORCE);""");
        await ExecuteAdminAsync($"""DROP ROLE IF EXISTS "{AppRwRole}";""");
        await ExecuteAdminAsync($"""DROP ROLE IF EXISTS "{OwnerRole}";""");
        await ExecuteAdminAsync($"""CREATE ROLE "{OwnerRole}" LOGIN PASSWORD '{OwnerPassword}';""");
        await ExecuteAdminAsync($"""CREATE DATABASE "{TemplateDatabase}" OWNER "{OwnerRole}";""");

        // ---- `app_rw`: migratsiyadan OLDIN va FAQAT bazaviy huquqlar bilan ----
        await CreateAppRwWithBaselineGrantsAsync();

        // ---- Migratsiya: AYNAN owner roli bilan ----
        var ownerTemplateConn = BuildConnectionString(TemplateDatabase, OwnerRole, OwnerPassword);
        await using (var db = NewContext(ownerTemplateConn))
            await db.Database.MigrateAsync();

        // Rol haqiqatan bormi? (Yuqoridagi qadam jim yiqilgan bo'lsa — owner'ga qaytamiz va
        // `RequireRealAppRw()` grantga tayanadigan testni aniq xabar bilan yiqitadi.)
        AppRwIsOwnerFallback = !await RoleExistsAsync(AppRwRole);
        if (!AppRwIsOwnerFallback)
        {
            // Parolni qayta tasdiqlaymiz (tashqi Postgres qayta ishlatilganda eskisi
            // qolgan bo'lishi mumkin). GRANT'larga TEGMAYMIZ: migratsiya qo'ygan
            // REVOKE aynan shu yerdan keyin sinovdan o'tadi.
            await ExecuteAdminAsync($"""ALTER ROLE "{AppRwRole}" WITH LOGIN PASSWORD '{AppRwPassword}';""");
        }

        Template = BuildTestDatabase(TemplateDatabase);

        // Shablondan nusxa olish uchun unga ochiq ulanish qolmasligi kerak.
        NpgsqlConnection.ClearAllPools();
    }

    /// <summary>
    /// <c>app_rw</c> rolini yaratadi va unga <c>deploy/init-roles.sql</c> ning
    /// <b>4-qadamidagi</b> bazaviy huquqlarni beradi — <b>boshqa hech narsani emas</b>.
    ///
    /// <para>
    /// Migratsiyadan OLDIN chaqiriladi, ikki sabab bilan:
    /// (1) <c>billing_guards.sql</c> dagi GRANT/REVOKE bloki rol mavjud bo'lmasa jim o'tib
    /// ketadi — ya'ni rol migratsiya paytida BOR bo'lishi shart;
    /// (2) <c>ALTER DEFAULT PRIVILEGES</c> faqat KEYIN yaratilgan jadvallarga ta'sir qiladi,
    /// ya'ni u <c>CREATE TABLE</c> lardan oldin turishi kerak.
    /// </para>
    /// <para>
    /// <b>Bu yerda `payments` ga DELETE ham beriladi — ATAYLAB.</b> Fixture rolga eng yomon
    /// holatni (hamma joyda to'liq CRUD) beradi; SPEC §4.1 qulfini migratsiya qo'yadi.
    /// Agar shu faylga birorta moliyaviy <c>REVOKE</c> yozilsa, P1-22 o'z-o'zini tekshirgan
    /// bo'lardi va migratsiyadan <c>REVOKE</c> yo'qolganini payqamasdi.
    /// </para>
    /// <para>
    /// <c>NOCREATEROLE</c>, <c>NOINHERIT</c>, <c>NOSUPERUSER</c> — <c>init-roles.sql</c>
    /// dagi atributlarning aynan o'zi. <c>NOINHERIT</c> muhim: rol biror guruhga a'zo
    /// bo'lib qolsa ham uning huquqlarini avtomatik olmaydi.
    /// </para>
    /// </summary>
    private async Task CreateAppRwWithBaselineGrantsAsync()
    {
        await ExecuteAdminAsync(
            $"""
             CREATE ROLE "{AppRwRole}" LOGIN PASSWORD '{AppRwPassword}'
                 NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOINHERIT NOBYPASSRLS;
             """);

        // Quyidagilar BAZAGA bog'liq (sxema huquqi va default privileges — per-database
        // kataloglar), shuning uchun shablon bazasiga ulanib bajariladi. `CREATE DATABASE
        // ... TEMPLATE` ularni nusxaga ham ko'chiradi.
        await ExecuteOnDatabaseAsync(TemplateDatabase,
            $"""GRANT CONNECT ON DATABASE "{TemplateDatabase}" TO "{AppRwRole}";""");
        await ExecuteOnDatabaseAsync(TemplateDatabase,
            $"""GRANT USAGE ON SCHEMA public TO "{AppRwRole}";""");
        // Ilova sxemada obyekt yaratmaydi — bu ham `init-roles.sql` dagi qator.
        await ExecuteOnDatabaseAsync(TemplateDatabase,
            $"""REVOKE CREATE ON SCHEMA public FROM "{AppRwRole}";""");

        // `FOR ROLE schoollms_owner` — hayotiy muhim: usiz sukut huquqi buyruqni
        // BAJARGAN rolga (superuser) bog'lanadi va migratsiya yaratgan jadvallar ilovaga
        // umuman ko'rinmay qolardi (init-roles.sql dagi 2-izoh).
        await ExecuteOnDatabaseAsync(TemplateDatabase,
            $"""
             ALTER DEFAULT PRIVILEGES FOR ROLE "{OwnerRole}" IN SCHEMA public
                 GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO "{AppRwRole}";
             """);
        await ExecuteOnDatabaseAsync(TemplateDatabase,
            $"""
             ALTER DEFAULT PRIVILEGES FOR ROLE "{OwnerRole}" IN SCHEMA public
                 GRANT USAGE, SELECT ON SEQUENCES TO "{AppRwRole}";
             """);
    }

    /// <summary>
    /// Test klassi uchun yangi, migratsiya qo'llangan baza. Shablondan nusxalanadi —
    /// migratsiya qayta yugurmaydi (~50 ms o'rniga ~2 s).
    /// </summary>
    public async Task<TestDatabase> CreateDatabaseAsync(string? namePrefix = null)
    {
        // Postgres identifikatori 63 bayt bilan cheklangan — prefiksni qisqartiramiz.
        var prefix = Sanitize(namePrefix ?? "test");
        if (prefix.Length > 20) prefix = prefix[..20];
        var name = $"{prefix}_{Interlocked.Increment(ref _dbCounter)}_{Guid.NewGuid():N}";

        await ExecuteAdminAsync(
            $"""CREATE DATABASE "{name}" TEMPLATE "{TemplateDatabase}" OWNER "{OwnerRole}";""");
        lock (_createdDatabases) _createdDatabases.Add(name);

        return BuildTestDatabase(name);
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();

        if (_container is not null)
        {
            // Konteyner butunlay o'chadi — alohida bazalarni tozalash shart emas.
            await _container.DisposeAsync();
            return;
        }

        // Tashqi Postgres — o'zimizdan keyin tozalaymiz.
        foreach (var name in _createdDatabases)
            await ExecuteAdminAsync($"""DROP DATABASE IF EXISTS "{name}" WITH (FORCE);""");
        await ExecuteAdminAsync($"""DROP DATABASE IF EXISTS "{TemplateDatabase}" WITH (FORCE);""");
        await ExecuteAdminAsync($"""DROP ROLE IF EXISTS "{AppRwRole}";""");
        await ExecuteAdminAsync($"""DROP ROLE IF EXISTS "{OwnerRole}";""");
    }

    /// <summary>Berilgan ulanish satri uchun AppDbContext — prod bilan bir xil sozlamalarda.</summary>
    public static AppDbContext NewContext(string connectionString) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options);

    private TestDatabase BuildTestDatabase(string name) => new(
        Name: name,
        OwnerConnectionString: BuildConnectionString(name, OwnerRole, OwnerPassword),
        AppRwConnectionString: AppRwIsOwnerFallback
            ? BuildConnectionString(name, OwnerRole, OwnerPassword)
            : BuildConnectionString(name, AppRwRole, AppRwPassword),
        AppRwIsOwnerFallback: AppRwIsOwnerFallback);

    private string BuildConnectionString(string database, string user, string password) =>
        new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            Database = database,
            Username = user,
            Password = password,
            IncludeErrorDetail = true,
            // Kichik pul: 50 ta test x 100 ulanish = konteynerning max_connections'ini yeydi.
            MaxPoolSize = 10,
        }.ConnectionString;

    private async Task<bool> RoleExistsAsync(string role)
    {
        await using var conn = new NpgsqlConnection(_adminConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT 1 FROM pg_roles WHERE rolname = @r", conn);
        cmd.Parameters.AddWithValue("r", role);
        return await cmd.ExecuteScalarAsync() is not null;
    }

    private async Task ExecuteAdminAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(_adminConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Superuser sifatida, lekin AYNAN berilgan bazaga ulanib bajaradi. Sxema huquqi va
    /// <c>ALTER DEFAULT PRIVILEGES</c> — baza ichidagi kataloglar, `postgres` bazasidan
    /// turib ularni qo'yib bo'lmaydi.
    /// </summary>
    private async Task ExecuteOnDatabaseAsync(string database, string sql)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            Database = database,
        }.ConnectionString;

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static string Sanitize(string s)
    {
        var chars = s.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '_')
            .ToArray();
        var result = new string(chars).Trim('_');
        return result.Length == 0 ? "test" : result;
    }
}

/// <summary>
/// Bitta throwaway baza va unga ikkita ulanish satri.
/// </summary>
/// <param name="Name">Baza nomi (debug uchun; smoke testda ilova AYNAN shu bazaga
/// ulanganini tekshiramiz).</param>
/// <param name="OwnerConnectionString">Sxema egasi — migratsiya va test ma'lumotini tayyorlash.</param>
/// <param name="AppRwConnectionString">Ilova roli — request-path shu bilan ishlaydi.</param>
/// <param name="AppRwIsOwnerFallback">true = `app_rw` roli hali yo'q (P1-02 birlashtirilmagan),
/// satr aslida owner'niki. Grant'ga tayanadigan test buni ko'rib YIQILISHI kerak.</param>
public sealed record TestDatabase(
    string Name,
    string OwnerConnectionString,
    string AppRwConnectionString,
    bool AppRwIsOwnerFallback)
{
    /// <summary>
    /// GRANT'larga tayanadigan testlar (P1-22 — ledger immutability) SHUNI birinchi qatorda
    /// chaqirsin. Fallback holatida <c>app_rw</c> satri aslida owner'niki bo'ladi va
    /// "UPDATE rad etildi" testi SOXTA YASHIL o'tib ketardi. Bu metod uni aniq xabar bilan
    /// yiqitadi. <c>Skip</c> ATAYLAB ishlatilmagan: o'tkazib yuborilgan test — unutilgan test.
    /// </summary>
    public void RequireRealAppRw()
    {
        Assert.False(AppRwIsOwnerFallback,
            $"`{PostgresFixture.AppRwRole}` roli migratsiyadan keyin topilmadi — ulanish satri "
            + "owner'niki. Bu testning natijasi ma'nosiz. P1-02 (rollar va grantlar) "
            + "birlashtirilishini kuting.");
    }
}

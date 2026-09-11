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
/// MUHIM (P1-02 bilan bog'liqlik): <c>app_rw</c> rolini SHU FIXTURE YARATMAYDI. Uni P1-02
/// migratsiyasi yaratishi kerak. Agar migratsiyadan keyin rol topilmasa, fixture owner
/// satriga QAYTADI va <see cref="TestDatabase.AppRwIsOwnerFallback"/> = true qo'yadi.
/// Buni ataylab shunday qildik: agar fixture rolni o'zi yaratib, grantlarni o'zi qo'ysa,
/// P1-22 "ledger o'zgartirib bo'lmaydi" testi SOXTA YASHIL bo'lardi — u fixture'ning
/// grantini tekshirardi, prod migratsiyasinikini emas. Fallback holatida P1-22 testi
/// shu bayroqni ko'rib O'ZI YIQILISHI kerak, o'tib ketmasligi.
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

    /// <summary>Ilova (request-path) roli. P1-02 migratsiyasi yaratadi.</summary>
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
        await ExecuteAdminAsync($"""DROP ROLE IF EXISTS "{OwnerRole}";""");
        await ExecuteAdminAsync($"""CREATE ROLE "{OwnerRole}" LOGIN PASSWORD '{OwnerPassword}';""");
        await ExecuteAdminAsync($"""CREATE DATABASE "{TemplateDatabase}" OWNER "{OwnerRole}";""");

        // ---- Migratsiya: AYNAN owner roli bilan ----
        var ownerTemplateConn = BuildConnectionString(TemplateDatabase, OwnerRole, OwnerPassword);
        await using (var db = NewContext(ownerTemplateConn))
            await db.Database.MigrateAsync();

        // P1-02 migratsiyasi `app_rw` ni yaratdimi?
        AppRwIsOwnerFallback = !await RoleExistsAsync(AppRwRole);
        if (!AppRwIsOwnerFallback)
        {
            // Rol bor, lekin paroli bizga noma'lum (migratsiya o'zi qo'ygan) — testda
            // ulanish uchun ma'lum parol o'rnatamiz. GRANT'larga TEGMAYMIZ: aynan ular
            // sinovdan o'tishi kerak.
            await ExecuteAdminAsync($"""ALTER ROLE "{AppRwRole}" WITH LOGIN PASSWORD '{AppRwPassword}';""");
        }

        Template = BuildTestDatabase(TemplateDatabase);

        // Shablondan nusxa olish uchun unga ochiq ulanish qolmasligi kerak.
        NpgsqlConnection.ClearAllPools();
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

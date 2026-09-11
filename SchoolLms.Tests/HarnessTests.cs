using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Harness'ning O'ZINI tekshiradi. Bu testlar yiqilsa — quyidagi hamma test natijasi
/// ma'nosiz, chunki ular noto'g'ri bazaga yoki noto'g'ri rol ostida ishlagan bo'ladi.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class HarnessTests(ApiFixture fixture)
{
    /// <summary>Migratsiya haqiqatan qo'llangan: tarix jadvalida yozuv bor va jadvallar mavjud.</summary>
    [Fact]
    public async Task Migratsiya_qollanadi_va_jadvallar_yaratiladi()
    {
        await using var db = PostgresFixture.NewContext(fixture.Database.OwnerConnectionString);

        var applied = await db.Database.GetAppliedMigrationsAsync();
        Assert.Contains("20260910110618_InitialPostgres", applied);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());

        var tables = await ReadTableNamesAsync(fixture.Database.OwnerConnectionString);

        // Nomlar snake_case bo'lishi kerak — UseSnakeCaseNamingConvention ishlayotganini
        // ham shu tekshiradi (aks holda "FinanceTransactions" bo'lardi).
        foreach (var expected in new[]
                 {
                     "users", "students", "teachers", "classes", "subjects",
                     "finance_transactions", "monthly_charges", "audit_logs", "school_meta",
                 })
            Assert.Contains(expected, tables);

        // Sxema egasi haqiqatan owner roli — grant'lar shunga tayanadi (P1-02/P1-22).
        var owner = await ScalarAsync<string>(
            fixture.Database.OwnerConnectionString,
            "SELECT tableowner FROM pg_tables WHERE schemaname = 'public' AND tablename = 'users'");
        Assert.Equal(PostgresFixture.OwnerRole, owner);
    }

    /// <summary>
    /// ENG MUHIM TEST. appsettings.json da <c>Host=localhost;Port=5432;Database=schoollms</c>
    /// yozilgan — bu ishlab turgan DEV BAZASI. Agar DI almashtiruvi buzilsa, testlar
    /// jimgina real ma'lumot ustida ishlab, uni buzib qo'yardi.
    /// </summary>
    [Fact]
    public async Task Ilova_throwaway_bazaga_ulanadi_dev_bazasiga_emas()
    {
        using var scope = fixture.Api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            await using var cmd = new NpgsqlCommand("SELECT current_database(), current_user", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());

            var database = reader.GetString(0);
            var user = reader.GetString(1);

            Assert.Equal(fixture.Database.Name, database);
            Assert.StartsWith("app_", database, StringComparison.Ordinal);
            Assert.NotEqual("schoollms", database);          // prod/dev baza nomi
            Assert.NotEqual("postgres", user);               // superuser bilan ishlamaymiz

            var expectedUser = fixture.Database.AppRwIsOwnerFallback
                ? PostgresFixture.OwnerRole
                : PostgresFixture.AppRwRole;
            Assert.Equal(expectedUser, user);
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    /// <summary>Owner satri haqiqatan sxema egasi — DDL qila oladi.</summary>
    [Fact]
    public async Task Owner_ulanish_satri_DDL_qila_oladi()
    {
        var table = "harness_probe_" + Guid.NewGuid().ToString("N")[..8];

        await using var conn = new NpgsqlConnection(fixture.Database.OwnerConnectionString);
        await conn.OpenAsync();

        await using (var create = new NpgsqlCommand($"CREATE TABLE {table} (id int)", conn))
            await create.ExecuteNonQueryAsync();

        await using (var check = new NpgsqlCommand(
                         "SELECT tableowner FROM pg_tables WHERE schemaname='public' AND tablename=@t", conn))
        {
            check.Parameters.AddWithValue("t", table);
            Assert.Equal(PostgresFixture.OwnerRole, (string?)await check.ExecuteScalarAsync());
        }

        await using (var drop = new NpgsqlCommand($"DROP TABLE {table}", conn))
            await drop.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Fixture IKKITA ulanish satri berishi shart (P1-22 talabi), va ular o'rtasidagi
    /// munosabat <c>AppRwIsOwnerFallback</c> bayrog'iga MOS bo'lishi kerak.
    /// Bayroq yolg'on qo'yilsa yoki fallback noto'g'ri simlansa — shu test yiqiladi.
    /// </summary>
    [Fact]
    public async Task Ikkala_ulanish_satri_beriladi_va_fallback_bayrogi_haqiqatga_mos()
    {
        Assert.NotEmpty(fixture.Database.OwnerConnectionString);
        Assert.NotEmpty(fixture.Database.AppRwConnectionString);

        var appRwUser = new NpgsqlConnectionStringBuilder(fixture.Database.AppRwConnectionString).Username;
        var ownerUser = new NpgsqlConnectionStringBuilder(fixture.Database.OwnerConnectionString).Username;

        Assert.Equal(PostgresFixture.OwnerRole, ownerUser);

        if (fixture.Database.AppRwIsOwnerFallback)
        {
            // P1-02 hali birlashtirilmagan: satr owner'niki bo'lishi SHART, va rol
            // haqiqatan bazada yo'q bo'lishi kerak.
            Assert.Equal(PostgresFixture.OwnerRole, appRwUser);
            Assert.Null(await ScalarAsync<object>(
                fixture.Database.OwnerConnectionString,
                $"SELECT 1 FROM pg_roles WHERE rolname = '{PostgresFixture.AppRwRole}'"));
        }
        else
        {
            Assert.Equal(PostgresFixture.AppRwRole, appRwUser);
            Assert.NotEqual(ownerUser, appRwUser);
        }

        // Ikkala satr ham haqiqatan ulanadi.
        foreach (var (cs, expectedUser) in new[]
                 {
                     (fixture.Database.OwnerConnectionString, ownerUser),
                     (fixture.Database.AppRwConnectionString, appRwUser),
                 })
            Assert.Equal(expectedUser, await ScalarAsync<string>(cs, "SELECT current_user"));
    }

    /// <summary>Shablondan olingan yangi baza ham migratsiyalangan, lekin BO'SH bo'ladi —
    /// testlar bir-birining ma'lumotini ko'rmasligi shunga tayanadi.</summary>
    [Fact]
    public async Task Yangi_baza_shablondan_migratsiyalangan_va_bosh_boladi()
    {
        var fresh = await fixture.Postgres.CreateDatabaseAsync("isolation");
        Assert.NotEqual(fixture.Database.Name, fresh.Name);

        // Umumiy bazaga foydalanuvchi qo'shamiz...
        var (seeded, _) = await fixture.Api.SeedUserAsync("admin");

        await using var freshDb = PostgresFixture.NewContext(fresh.OwnerConnectionString);
        Assert.Empty(await freshDb.Database.GetPendingMigrationsAsync());
        // ...yangi bazada u KO'RINMAYDI.
        Assert.False(await freshDb.Users.AnyAsync(u => u.Id == seeded.Id));
    }

    // ---------- yordamchilar ----------

    private static async Task<HashSet<string>> ReadTableNamesAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT tablename FROM pg_tables WHERE schemaname = 'public'", conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        var names = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync()) names.Add(reader.GetString(0));
        return names;
    }

    private static async Task<T?> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        var value = await cmd.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)value;
    }
}

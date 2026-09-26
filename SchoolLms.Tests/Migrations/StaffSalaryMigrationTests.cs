using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Migrations;

// ===========================================================================
//  `StaffSalary` — docs/modules/employees-unified.md. Purely additive:
//  users.phone / salary / salary_start_date (NOT NULL with defaults, so every
//  existing account stays valid) and a nullable expenses.employee_user_id
//  (FK → users, RESTRICT) with two CHECKs mirroring ck_expenses_teacher_only_salary.
//  COVERAGE: 1) rows written before the migration survive it with the defaults;
//  2) the new CHECKs and FK bind; 3) Down() on a database WITH staff salary data
//  removes only this migration's columns and keeps every row; Up() again works.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class StaffSalaryMigrationTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string PreviousMigration = "20260925124026_StaffAccessRoles";

    /// <summary>This migration itself — Down() is measured FROM here, so columns added by later
    /// migrations (e.g. boarding_attendance.reason_id) are not counted as "removed by Down".</summary>
    private const string ThisMigration = "20260926061213_StaffSalary";

    private static readonly (string Table, string Column)[] NewColumns =
    [
        ("users", "phone"),
        ("users", "salary"),
        ("users", "salary_start_date"),
        ("expenses", "employee_user_id"),
    ];

    private TestDatabase _database = default!;

    public async Task InitializeAsync() =>
        _database = await fixture.Postgres.CreateDatabaseAsync("staffsalarymig");

    public Task DisposeAsync()
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.OwnerConnectionString));
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.AppRwConnectionString));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Up_eski_qatorlarni_sukut_qiymat_bilan_saqlaydi()
    {
        await MigrateToAsync(PreviousMigration);
        await using (var conn = await OpenOwnerAsync())
        {
            await InsertMinimalRowAsync(conn, "users", new()
            {
                ["id"] = "'u-old-staff'", ["email"] = "'old.staff'", ["role"] = "'staff'",
            });
            await InsertMinimalRowAsync(conn, "expenses", new()
            {
                ["id"] = "'44444444-4444-4444-4444-444444444444'", ["category"] = "'utilities'",
                ["amount"] = "1000", ["created_by"] = "'u-old-staff'",
            });
        }

        await MigrateToAsync(null);

        await using var db = await OpenOwnerAsync();
        Assert.Equal("", await ScalarAsync(db, "select phone from users where id = 'u-old-staff'"));
        Assert.Equal(0m, await ScalarAsync(db, "select salary from users where id = 'u-old-staff'"));
        Assert.Equal("", await ScalarAsync(db, "select salary_start_date from users where id = 'u-old-staff'"));
        Assert.Null(await ScalarAsync(db,
            "select employee_user_id from expenses where id = '44444444-4444-4444-4444-444444444444'"));
        Assert.Equal("numeric", await ScalarAsync(db,
            "select data_type from information_schema.columns where table_name = 'users' and column_name = 'salary'"));
        Assert.Equal(2, await ScalarAsync(db,
            "select numeric_scale from information_schema.columns where table_name = 'users' and column_name = 'salary'"));
    }

    [Fact]
    public async Task Cheklovlar_va_fk_ishlaydi()
    {
        await using var conn = await OpenOwnerAsync();
        await InsertMinimalRowAsync(conn, "users", new() { ["id"] = "'u-ck-staff'", ["email"] = "'ck.staff'", ["role"] = "'staff'" });
        await InsertMinimalRowAsync(conn, "teachers", new() { ["id"] = "'t-ck-1'" });

        // Allowed: a salary expense to a staff account.
        await InsertExpenseAsync(conn, "u-ck-staff", "salary", teacherId: null, employeeId: "u-ck-staff");

        // Employee only on salary; never together with a teacher; must reference a real user.
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() =>
            InsertExpenseAsync(conn, "u-ck-staff", "utilities", null, "u-ck-staff"))).SqlState);
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() =>
            InsertExpenseAsync(conn, "u-ck-staff", "salary", "t-ck-1", "u-ck-staff"))).SqlState);
        Assert.Equal("23503", (await Assert.ThrowsAsync<PostgresException>(() =>
            InsertExpenseAsync(conn, "u-ck-staff", "salary", null, "u-missing"))).SqlState);
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() =>
            ExecAsync(conn, "update users set salary = -1 where id = 'u-ck-staff'"))).SqlState);

        // RESTRICT: a paid staff account cannot vanish with its salary history.
        Assert.Equal("23503", (await Assert.ThrowsAsync<PostgresException>(() =>
            ExecAsync(conn, "delete from users where id = 'u-ck-staff'"))).SqlState);
    }

    [Fact]
    public async Task Down_malumotli_bazada_faqat_shu_ustunlarni_olib_tashlaydi()
    {
        HashSet<string> head;
        long users, expenses;
        await MigrateToAsync(ThisMigration);
        await using (var conn = await OpenOwnerAsync())
        {
            await InsertMinimalRowAsync(conn, "users", new()
            {
                ["id"] = "'u-down-staff'", ["email"] = "'down.staff'", ["role"] = "'staff'",
                ["phone"] = "'+998 90 000 00 00'", ["salary"] = "2500000", ["salary_start_date"] = "'2026-09-01'",
            });
            await InsertExpenseAsync(conn, "u-down-staff", "salary", null, "u-down-staff");
            head = await AllColumnsAsync(conn);
            users = (long)(await ScalarAsync(conn, "select count(*) from users"))!;
            expenses = (long)(await ScalarAsync(conn, "select count(*) from expenses"))!;
        }

        await MigrateToAsync(PreviousMigration);

        await using (var conn = await OpenOwnerAsync())
        {
            var gone = head.Except(await AllColumnsAsync(conn)).OrderBy(c => c).ToList();
            Assert.Equal(NewColumns.Select(c => $"{c.Table}.{c.Column}").OrderBy(c => c), gone);
            Assert.Equal(users, await ScalarAsync(conn, "select count(*) from users"));
            Assert.Equal(expenses, await ScalarAsync(conn, "select count(*) from expenses"));
            Assert.Equal("staff", await ScalarAsync(conn, "select role from users where id = 'u-down-staff'"));
        }

        await MigrateToAsync(ThisMigration);

        await using (var conn = await OpenOwnerAsync())
        {
            Assert.True(head.SetEquals(await AllColumnsAsync(conn)), "Up() after Down() did not restore the schema");
            // Re-added with defaults; the salary row itself survived the round trip.
            Assert.Equal(0m, await ScalarAsync(conn, "select salary from users where id = 'u-down-staff'"));
            Assert.Equal(expenses, await ScalarAsync(conn, "select count(*) from expenses"));
        }
    }

    // =====================================================================
    //  Helpers (same pattern as CashBoxesMigrationTests)
    // =====================================================================

    private static Task InsertExpenseAsync(
        NpgsqlConnection conn, string createdBy, string category, string? teacherId, string? employeeId) =>
        InsertMinimalRowAsync(conn, "expenses", new()
        {
            ["category"] = $"'{category}'",
            ["amount"] = "1000",
            ["created_by"] = $"'{createdBy}'",
            ["teacher_id"] = teacherId is null ? "null" : $"'{teacherId}'",
            ["employee_user_id"] = employeeId is null ? "null" : $"'{employeeId}'",
        });

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
    /// Minimal row: every NOT NULL column without a DEFAULT gets an "empty" value of its type,
    /// so a later column does not break the test.
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

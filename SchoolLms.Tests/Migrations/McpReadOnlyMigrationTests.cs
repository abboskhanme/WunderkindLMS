using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Migrations;

// ===========================================================================
//  `McpReadOnly` — OAuth clients, grants, codes, hashed tokens and the MCP audit.
//  COVERS: 1) five tables; 2) app_rw full CRUD on OAuth tables, mcp_audit append-only;
//  3) app_ro cannot read codes/tokens but reads the rest; 4) Down() removes only these.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class McpReadOnlyMigrationTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string PreviousMigration = "20260926113454_TrackMembersFromYearStart";
    private const string ThisMigration = "20260926150755_McpReadOnly";
    private static readonly string[] Tables = ["mcp_clients", "mcp_grants", "mcp_auth_codes", "mcp_tokens", "mcp_audit"];

    private TestDatabase _database = default!;

    public async Task InitializeAsync() => _database = await fixture.Postgres.CreateDatabaseAsync("mcpmig");

    public Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Jadvallar_va_huquqlar()
    {
        _database.RequireRealAppRw();
        await using var owner = await OpenAsync(_database.OwnerConnectionString);
        foreach (var t in Tables)
            Assert.Equal(1L, await ScalarAsync(owner, $"select count(*) from information_schema.tables where table_name = '{t}'"));

        await using var rw = await OpenAsync(_database.AppRwConnectionString);
        await ExecAsync(rw, "insert into mcp_clients (id, name, redirect_uris, created_at) values ('mcp_t', 'T', '{https://x/cb}', now())");
        await ExecAsync(rw, "update mcp_clients set name = 'T2' where id = 'mcp_t'");
        await ExecAsync(rw, "insert into mcp_audit (at, user_id, user_name, client_id, client_name, tool, arguments, outcome, duration_ms) "
                            + "values (now(), 'u', 'U', 'mcp_t', 'T', 'x', '', 'ok', 1)");
        var upd = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(rw, "update mcp_audit set tool = 'y'"));
        Assert.Equal("42501", upd.SqlState);
        var del = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(rw, "delete from mcp_audit"));
        Assert.Equal("42501", del.SqlState);
        await ExecAsync(rw, "delete from mcp_clients where id = 'mcp_t'");

        // app_ro: allow-list only — no mcp_* table is on it (tools never read OAuth state or the audit).
        await using var ro = await OpenAsync(_database.AppRoConnectionString);
        Assert.Equal(0L, Convert.ToInt64(await ScalarAsync(ro, "select count(*) from students where false")));
        foreach (var t in Tables)
            Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => ScalarAsync(ro, $"select count(*) from {t}"))).SqlState);

        // Retention function: app_rw may call it, it never deletes rows younger than 365 days.
        Assert.Equal(0, await ScalarAsync(rw, "select public.mcp_prune_audit(1)"));
        Assert.Equal(1L, await ScalarAsync(owner, "select count(*) from mcp_audit"));
        await ExecAsync(owner, "update mcp_audit set at = now() - interval '400 days'");
        Assert.Equal(1, await ScalarAsync(rw, "select public.mcp_prune_audit(365)"));
        var bad = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecAsync(owner, "insert into mcp_audit (at, user_id, user_name, client_id, client_name, tool, arguments, outcome, duration_ms) "
                             + "values (now(), 'u', 'U', 'c', 'C', 'x', '', 'hacked', 1)"));
        Assert.Equal("23514", bad.SqlState);
    }

    [Fact]
    public async Task Down_faqat_mcp_jadvallarini_olib_tashlaydi()
    {
        await MigrateToAsync(PreviousMigration);
        HashSet<string> before;
        await using (var c = await OpenAsync(_database.OwnerConnectionString)) before = await AllColumnsAsync(c);
        await MigrateToAsync(ThisMigration);
        await using (var c = await OpenAsync(_database.OwnerConnectionString))
        {
            var after = await AllColumnsAsync(c);
            Assert.Empty(before.Except(after));
            Assert.All(after.Except(before), col => Assert.StartsWith("mcp_", col));
        }
        await MigrateToAsync(PreviousMigration);
        await using (var c = await OpenAsync(_database.OwnerConnectionString))
            Assert.True(before.SetEquals(await AllColumnsAsync(c)), "Down() did not restore the schema");
        await MigrateToAsync(null);
    }

    private async Task MigrateToAsync(string? target)
    {
        await using var db = PostgresFixture.NewContext(_database.OwnerConnectionString);
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    private static async Task<NpgsqlConnection> OpenAsync(string cs)
    {
        var c = new NpgsqlConnection(cs);
        await c.OpenAsync();
        return c;
    }

    private static async Task ExecAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        return await cmd.ExecuteScalarAsync();
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
}

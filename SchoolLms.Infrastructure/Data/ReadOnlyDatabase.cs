using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SchoolLms.Infrastructure.Data;

/// <summary>
/// The database the MCP (AI client) tools read through — docs/modules/mcp-readonly.md.
///
/// <para>
/// It hands out ordinary <see cref="AppDbContext"/> instances (so every existing query
/// service can take them as <c>IAppDbContext</c>), but built from different options with
/// three independent locks:
/// </para>
/// <list type="number">
///   <item>the connection logs in as <c>app_ro</c> — SELECT only, owns nothing, and the role
///     has <c>default_transaction_read_only = on</c> (deploy/init-roles.sql);</item>
///   <item>a save-changes interceptor throws before anything reaches the database;</item>
///   <item>change tracking is off, so nothing is even staged;</item>
///   <item>a command interceptor lets only <c>SELECT</c> / <c>WITH</c> statements through.</item>
/// </list>
/// <para>
/// Deliberately NOT a <c>DbContext</c> subclass: a second context type in the assembly makes
/// every <c>dotnet ef migrations …</c> command fail with "More than one DbContext was found".
/// </para>
/// </summary>
public sealed class ReadOnlyDatabase(string readOnlyConnectionString)
{
    public DbContextOptions<AppDbContext> Options { get; } = BuildOptions(readOnlyConnectionString);

    /// <summary>A new context on the <c>app_ro</c> connection. The caller disposes it.</summary>
    public AppDbContext CreateContext() => new(Options);

    public static DbContextOptions<AppDbContext> BuildOptions(string readOnlyConnectionString) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(readOnlyConnectionString,
                npg => npg.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .UseSnakeCaseNamingConvention()
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .AddInterceptors(RefuseWrites.Instance, SelectOnly.Instance)
            .Options;

    /// <summary>Any SaveChanges on a read-only context fails loudly.</summary>
    public sealed class RefuseWrites : SaveChangesInterceptor
    {
        public static readonly RefuseWrites Instance = new();

        public const string Message =
            "Read-only database (MCP tools): a write was attempted and refused.";

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) =>
            throw new InvalidOperationException(Message);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(Message);
    }

    /// <summary>Only SELECT / WITH … SELECT reach the connection. Anything else (raw SQL,
    /// a service's ExecuteUpdate/ExecuteDelete) is refused before it is sent.</summary>
    public sealed class SelectOnly : DbCommandInterceptor
    {
        public static readonly SelectOnly Instance = new();

        public const string Message = "Read-only database (MCP tools): only SELECT statements are allowed.";

        public static bool IsSelect(string sql)
        {
            var s = sql.AsSpan().TrimStart();
            // Skip leading SQL comments EF may emit (tags).
            while (s.StartsWith("--"))
            {
                var nl = s.IndexOf('\n');
                s = nl < 0 ? ReadOnlySpan<char>.Empty : s[(nl + 1)..].TrimStart();
            }
            while (s.StartsWith("/*"))
            {
                var end = s.IndexOf("*/");
                s = end < 0 ? ReadOnlySpan<char>.Empty : s[(end + 2)..].TrimStart();
            }
            if (s.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                || s.StartsWith("(SELECT", StringComparison.OrdinalIgnoreCase)) return true;
            if (!s.StartsWith("WITH", StringComparison.OrdinalIgnoreCase)) return false;
            // A data-modifying CTE (WITH x AS (DELETE …)) is still a write.
            var text = s.ToString().ToUpperInvariant();
            return !(text.Contains("INSERT ") || text.Contains("UPDATE ") || text.Contains("DELETE ")
                     || text.Contains("MERGE ") || text.Contains("TRUNCATE "));
        }

        private static void Check(System.Data.Common.DbCommand command)
        {
            if (!IsSelect(command.CommandText)) throw new InvalidOperationException(Message);
        }

        public override InterceptionResult<System.Data.Common.DbDataReader> ReaderExecuting(
            System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<System.Data.Common.DbDataReader> result)
        { Check(command); return result; }

        public override ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default)
        { Check(command); return ValueTask.FromResult(result); }

        public override InterceptionResult<object> ScalarExecuting(
            System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        { Check(command); return result; }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        { Check(command); return ValueTask.FromResult(result); }

        public override InterceptionResult<int> NonQueryExecuting(
            System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<int> result) =>
            throw new InvalidOperationException(Message);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(Message);
    }
}

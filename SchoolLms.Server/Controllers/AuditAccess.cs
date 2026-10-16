using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Filters of the audit log, shared by the per-record history (<see cref="AuditController"/>),
/// the audit screen (<see cref="AuditLogController"/>) and the pupil card's activity tab.
/// Every field is optional.
/// </summary>
/// <param name="From">Day (<c>yyyy-MM-dd</c>, inclusive) or a full timestamp.</param>
/// <param name="To">Day (<c>yyyy-MM-dd</c>, the whole day included) or a full timestamp (inclusive).</param>
/// <param name="ActorId">users.id; "system" — rows with no person behind them.</param>
/// <param name="Section">Section key, or several separated by commas ("finance,salary").</param>
/// <param name="Kind">create | update | delete.</param>
/// <param name="Q">Text inside the summary.</param>
public sealed record AuditFilter(
    string? EntityType = null, string? EntityId = null, string? StudentId = null, string? TeacherId = null,
    string? Action = null, string? From = null, string? To = null, string? ActorId = null,
    string? Section = null, string? Kind = null, string? Q = null);

/// <summary>Who may see what in the audit log, and the one filter pipeline.</summary>
public static class AuditAccess
{
    /// <summary>Permission key of the audit screen (client: config/constants.ts).</summary>
    public const string Permission = "audit";

    /// <summary><see cref="AuditFilter.ActorId"/> value for rows written by the system.</summary>
    public const string SystemActor = "system";

    /// <summary>
    /// The money right: finance and salary rows are shown, and amounts stay in before/after.
    /// Admin, director, finance staff ("finance") and view-only finance staff ("finance:view") —
    /// everyone who can open the finance screens the rows come from.
    /// </summary>
    public static bool CanSeeMoney(this ClaimsPrincipal user) =>
        FinanceMatrix.IsAllowed(FinanceAction.ViewBillingReports, user) || user.IsInRole(Roles.FinanceViewer);

    /// <summary>Filters plus the money rule (no finance/salary rows without the right).</summary>
    public static IQueryable<AuditLog> Apply(IQueryable<AuditLog> q, AuditFilter f, bool canSeeMoney)
    {
        if (!canSeeMoney) q = AuditCatalog.WithoutMoneyRows(q);

        if (!string.IsNullOrEmpty(f.EntityType)) q = q.Where(a => a.EntityType == f.EntityType);
        if (!string.IsNullOrEmpty(f.EntityId)) q = q.Where(a => a.EntityId == f.EntityId);
        if (!string.IsNullOrEmpty(f.StudentId)) q = q.Where(a => a.StudentId == f.StudentId);
        if (!string.IsNullOrEmpty(f.TeacherId)) q = q.Where(a => a.TeacherId == f.TeacherId);
        if (!string.IsNullOrEmpty(f.Action)) q = q.Where(a => a.Action == f.Action);

        if (!string.IsNullOrEmpty(f.ActorId))
            q = f.ActorId == SystemActor ? q.Where(a => a.ActorId == null) : q.Where(a => a.ActorId == f.ActorId);

        if (!string.IsNullOrWhiteSpace(f.Section))
            q = AuditCatalog.WhereSections(q, f.Section.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

        if (!string.IsNullOrWhiteSpace(f.Kind)) q = AuditCatalog.WhereKind(q, f.Kind.Trim());

        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var term = $"%{f.Q.Trim()}%";
            q = q.Where(a => EF.Functions.ILike(a.Summary, term));
        }

        // `timestamp` is local ISO text ("yyyy-MM-ddTHH:mm:ss"), so text order is time order.
        if (!string.IsNullOrEmpty(f.From)) q = q.Where(a => string.Compare(a.Timestamp, f.From) >= 0);
        if (!string.IsNullOrEmpty(f.To))
        {
            if (DateOnly.TryParseExact(f.To, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            {
                // A bare day means the whole day: everything before the next midnight.
                var next = day.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                q = q.Where(a => string.Compare(a.Timestamp, next) < 0);
            }
            else
            {
                q = q.Where(a => string.Compare(a.Timestamp, f.To) <= 0);
            }
        }
        return q;
    }

    /// <summary>Newest first; the id breaks ties (the keyset order).</summary>
    public static IQueryable<AuditLog> Newest(IQueryable<AuditLog> q) =>
        q.OrderByDescending(a => a.Timestamp).ThenByDescending(a => a.Id);

    /// <summary>Per-record DTO with the money rule applied to before/after.</summary>
    public static AuditLogDto ToLogDto(AuditLog a, bool canSeeMoney) => new(
        a.Id, a.EntityType, a.EntityId, a.Action, a.Timestamp,
        a.ActorName, a.Summary,
        canSeeMoney ? a.Before : AuditCatalog.RedactMoney(a.Before),
        canSeeMoney ? a.After : AuditCatalog.RedactMoney(a.After),
        a.StudentId, a.TeacherId);

    /// <summary>Screen DTO: section, kind and labels resolved, money rule applied.</summary>
    public static AuditEntryDto ToEntryDto(
        AuditLog a, bool canSeeMoney, IReadOnlyDictionary<string, string> students, IReadOnlyDictionary<string, string> teachers)
    {
        var section = AuditCatalog.SectionOf(a.EntityType, a.Summary);
        return new AuditEntryDto(
            a.Id, a.EntityType, a.EntityId, a.Action, AuditCatalog.KindOf(a.Action), AuditCatalog.ActionLabel(a.Action),
            section, AuditCatalog.SectionLabel(section), a.Timestamp, a.ActorId, a.ActorName ?? "Tizim", a.Summary,
            canSeeMoney ? a.Before : AuditCatalog.RedactMoney(a.Before),
            canSeeMoney ? a.After : AuditCatalog.RedactMoney(a.After),
            a.StudentId, a.StudentId is null ? null : students.GetValueOrDefault(a.StudentId),
            a.TeacherId, a.TeacherId is null ? null : teachers.GetValueOrDefault(a.TeacherId));
    }
}

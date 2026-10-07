using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SchoolLms.Application.Services;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// The action is refused (409 <c>student_archived</c>) when it names an ARCHIVED pupil — client,
/// 2026-10-08: an archived pupil is no longer the school's, almost nothing is done with them
/// (<see cref="ArchivedStudents"/> says what stays open). The pupil is read from the route value
/// <see cref="Route"/> and from the body's <c>StudentId</c> / <c>StudentIds</c>, so one attribute covers
/// both shapes. Runs after authorization: a caller without the right still gets 401/403 first.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ActiveStudentAttribute : Attribute, IAsyncActionFilter
{
    /// <summary>Route value holding the pupil's id (<c>id</c>, <c>studentId</c>); null — the body only.</summary>
    public string? Route { get; init; }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (Route is { } key && context.RouteData.Values.TryGetValue(key, out var value)
            && value?.ToString() is { Length: > 0 } routeId)
            ids.Add(routeId);
        foreach (var arg in context.ActionArguments.Values)
        {
            if (arg is null or string || arg.GetType().IsPrimitive) continue;
            var type = arg.GetType();
            if (type.GetProperty("StudentId")?.GetValue(arg) is string one && !string.IsNullOrWhiteSpace(one))
                ids.Add(one.Trim());
            if (type.GetProperty("StudentIds")?.GetValue(arg) is IEnumerable<string> many)
                foreach (var id in many)
                    if (!string.IsNullOrWhiteSpace(id)) ids.Add(id.Trim());
        }

        if (ids.Count > 0)
        {
            var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var archived = await ArchivedStudents.NamesAsync(db, ids, context.HttpContext.RequestAborted);
            if (archived.Count > 0)
            {
                context.Result = new ConflictObjectResult(new { code = ArchivedStudents.Code, message = ArchivedStudents.Message(archived) });
                return;
            }
        }
        await next();
    }
}

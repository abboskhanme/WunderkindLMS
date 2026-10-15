using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Thank-you / warning messages after a test — the best N and the lowest M pupils, N and M typed in by hand
/// (client, 2026-10-15). Logic: <see cref="ExamAwardService"/>; docs/modules/exam-awards.md.
///
/// <para><b>Permission.</b> Reading the lists — the exam section, like the results themselves. Sending reaches
/// families, so it needs both the full <c>exams</c> and the full <c>messages</c> key (admin and superadmin always).</para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("exams")]
[Route("api/admin/exams/{id}/awards")]
public sealed class ExamAwardsController(ExamAwardService service) : ControllerBase
{
    /// <summary><c>GET …/awards/preview?top=3&amp;bottom=3&amp;perClass=false</c> — who would get what, and the default texts.</summary>
    [HttpGet("preview")]
    public async Task<ActionResult<ExamAwardPreviewDto>> Preview(
        string id, [FromQuery] int top = 3, [FromQuery] int bottom = 3, [FromQuery] bool perClass = false,
        CancellationToken ct = default)
    {
        var (value, error) = await service.PreviewAsync(id, top, bottom, perClass, ct);
        return error is null ? value! : Fail(error);
    }

    [HttpPost("send")]
    public async Task<ActionResult<ExamAwardSendResultDto>> Send(string id, ExamAwardSendRequest req, CancellationToken ct = default)
    {
        if (!User.HasPerm("exams") || !User.HasPerm("messages")) return Forbid();
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var (value, error) = await service.SendAsync(id, req, uid, ct);
        return error is null ? value! : Fail(error);
    }

    /// <summary>What was already sent for this exam.</summary>
    [HttpGet]
    public Task<List<ExamAwardNoticeDto>> Sent(string id, CancellationToken ct = default) => service.SentAsync(id, ct);

    private ActionResult Fail(string message) =>
        message == ExamAwardService.NotFound ? NotFound(new { message }) : BadRequest(new { message });
}

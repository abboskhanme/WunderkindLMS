using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Services;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Yuqori paneldagi umumiy qidiruv — o'quvchi, ota-ona, o'qituvchi, sinf, guruh, lid.
///
/// <para>
/// Faqat o'qish. Xodim (staff) uchun <see cref="AdminPermAttribute"/> odatdagidek hamma
/// narsani o'qishga ruxsat BERARDI; qidiruv esa bitta so'rov bilan barcha bo'limning
/// ism va telefonlarini ochib berishi mumkin, shu sabab har bo'lim o'z ruxsat kaliti
/// bilan alohida tekshiriladi (<see cref="PermissionCheck.HasPerm"/>). Ruxsati yo'q
/// bo'limning natijasi javobda umuman bo'lmaydi.
/// </para>
/// </summary>
[ApiController]
[Authorize(Roles = "admin,superadmin,staff")]
[Route("api/admin/search")]
public class GlobalSearchController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<GlobalSearchHitDto>>> Get([FromQuery] string? q, CancellationToken ct) =>
        await GlobalSearchQuery.RunAsync(db, q, perm => User.HasReadPerm(perm), ct);
}

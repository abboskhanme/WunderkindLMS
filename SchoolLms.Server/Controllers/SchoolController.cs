using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;

namespace SchoolLms.Server.Controllers;

/// <summary>Maktab nomi — brending uchun (barcha kirgan foydalanuvchilarga ochiq).</summary>
[ApiController]
[Authorize]
[Route("api/school")]
public class SchoolController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SchoolNameDto>> Get()
    {
        var m = await db.SchoolMeta.FirstOrDefaultAsync();
        return new SchoolNameDto(m?.Name ?? "");
    }
}

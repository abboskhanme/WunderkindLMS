using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// O'qituvchilar faollik hisoboti (admin): dars o'tyaptimi, baho qo'ymoqdami, mavzu/uy vazifa
/// bermoqdami. <c>quarter=0</c> → barcha choraklar; aks holda tanlangan chorak.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("teacherReports")]
[Route("api/admin/teacher-reports")]
public class TeacherReportsController(AppDbContext db) : ControllerBase
{
    /// <summary>Barcha o'qituvchilar bo'yicha umumiy ko'rinish.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TeacherReportRowDto>>> Overview([FromQuery] int quarter = 0)
        => await TeacherActivityReport.BuildOverviewAsync(db, quarter);

    /// <summary>Bitta o'qituvchining batafsil hisoboti (sinf/fan yoyilmasi).</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<TeacherReportDetailDto>> Detail(string id, [FromQuery] int quarter = 0)
    {
        var dto = await TeacherActivityReport.BuildDetailAsync(db, id, quarter);
        return dto is null ? NotFound() : dto;
    }
}

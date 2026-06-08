using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Dtos;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Admin/superadmin uchun umumiy fayl yuklash endpoint'i — formalardagi (metrika, passport,
/// shartnoma fayllari va h.k.) rasm/PDF'larni serverga saqlash. URL qaytariladi va shu URL
/// keyin tegishli entity (Student, Contract, ...) maydonida saqlanadi.
/// </summary>
[ApiController]
[Authorize(Roles = "admin,superadmin,staff")]
[Route("api/admin/uploads")]
public class UploadsController(IWebHostEnvironment env) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<UploadedFileDto>> Upload(IFormFile file)
    {
        if (Application.Services.UploadGuard.Validate(file) is { } error)
            return BadRequest(new { message = error });

        var dir = System.IO.Path.Combine(env.ContentRootPath, "uploads");
        System.IO.Directory.CreateDirectory(dir);
        var stored = Application.Services.UploadGuard.SafeName(file);
        await using (var fs = System.IO.File.Create(System.IO.Path.Combine(dir, stored)))
            await file.CopyToAsync(fs);

        return new UploadedFileDto(file.FileName, $"/uploads/{stored}", file.Length, file.ContentType ?? "");
    }
}

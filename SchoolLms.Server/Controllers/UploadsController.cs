using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Server.Dtos;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Admin/superadmin uchun umumiy fayl yuklash endpoint'i — formalardagi (metrika, passport,
/// shartnoma fayllari va h.k.) rasm/PDF'larni serverga saqlash. URL qaytariladi va shu URL
/// keyin tegishli entity (Student, Contract, ...) maydonida saqlanadi.
/// </summary>
[ApiController]
[Authorize(Roles = "admin,superadmin")]
[Route("api/admin/uploads")]
public class UploadsController(IWebHostEnvironment env) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<UploadedFileDto>> Upload(IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Fayl bo'sh" });
        if (file.Length > 20_000_000)
            return BadRequest(new { message = "Fayl 20 MB dan katta" });

        var dir = System.IO.Path.Combine(env.ContentRootPath, "uploads");
        System.IO.Directory.CreateDirectory(dir);
        var stored = $"{Guid.NewGuid():N}{System.IO.Path.GetExtension(file.FileName)}";
        await using (var fs = System.IO.File.Create(System.IO.Path.Combine(dir, stored)))
            await file.CopyToAsync(fs);

        return new UploadedFileDto(file.FileName, $"/uploads/{stored}", file.Length, file.ContentType ?? "");
    }
}

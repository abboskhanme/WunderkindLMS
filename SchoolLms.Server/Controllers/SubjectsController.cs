using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Server.Data;
using SchoolLms.Server.Dtos;
using SchoolLms.Server.Models;

namespace SchoolLms.Server.Controllers;

[ApiController]
[Authorize(Roles = "admin,superadmin")]
[Route("api/admin/subjects")]
public class SubjectsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Subject>>> GetAll() =>
        await db.Subjects.OrderBy(s => s.Name).ToListAsync();

    [HttpPost]
    public async Task<ActionResult<Subject>> Create(SubjectPayload payload)
    {
        var subject = new Subject { Name = payload.Name };
        db.Subjects.Add(subject);
        await db.SaveChangesAsync();
        return subject;
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<Subject>> Update(string id, SubjectPayload payload)
    {
        var subject = await db.Subjects.FindAsync(id);
        if (subject is null) return NotFound();
        subject.Name = payload.Name;
        await db.SaveChangesAsync();
        return subject;
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var subject = await db.Subjects.FindAsync(id);
        if (subject is null) return NotFound();
        db.Subjects.Remove(subject);
        await db.SaveChangesAsync();
        return NoContent();
    }
}

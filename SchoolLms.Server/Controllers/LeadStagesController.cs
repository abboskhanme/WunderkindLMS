using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Server.Data;
using SchoolLms.Server.Dtos;
using SchoolLms.Server.Models;

namespace SchoolLms.Server.Controllers;

[ApiController]
[Authorize(Roles = "admin,superadmin")]
[Route("api/admin/lead-stages")]
public class LeadStagesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<LeadStage>>> GetAll() =>
        await db.LeadStages.OrderBy(s => s.Order).ToListAsync();

    [HttpPost]
    public async Task<ActionResult<LeadStage>> Create(StagePayload p)
    {
        var maxOrder = await db.LeadStages.AnyAsync() ? await db.LeadStages.MaxAsync(s => s.Order) : -1;
        var stage = new LeadStage { Title = p.Title, Color = p.Color, Order = maxOrder + 1 };
        db.LeadStages.Add(stage);
        await db.SaveChangesAsync();
        return stage;
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<LeadStage>> Update(string id, StagePayload p)
    {
        var stage = await db.LeadStages.FindAsync(id);
        if (stage is null) return NotFound();
        stage.Title = p.Title;
        stage.Color = p.Color;
        await db.SaveChangesAsync();
        return stage;
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var stage = await db.LeadStages.FindAsync(id);
        if (stage is null) return NotFound();
        db.LeadStages.Remove(stage);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Ustunlar tartibini saqlash (kelgan id'lar tartibida).</summary>
    [HttpPatch("reorder")]
    public async Task<IActionResult> Reorder(ReorderRequest req)
    {
        var stages = await db.LeadStages.ToListAsync();
        for (var i = 0; i < req.Ids.Count; i++)
        {
            var stage = stages.FirstOrDefault(s => s.Id == req.Ids[i]);
            if (stage is not null) stage.Order = i;
        }
        await db.SaveChangesAsync();
        return NoContent();
    }
}

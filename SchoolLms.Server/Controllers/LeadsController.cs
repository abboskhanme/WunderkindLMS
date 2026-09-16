using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

[ApiController]
[Authorize]
[AdminPerm("leads")]
[Route("api/admin/leads")]
public class LeadsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Lead>>> GetAll() =>
        await db.Leads.ToListAsync();

    /// <summary>
    /// Buyurtmalar voronkasi (§4, #9) — alohida sahifa uchun. Lidlar DOSKASIGA hech qanday
    /// aloqasi yo'q: bu faqat o'qish.
    ///
    /// <para>
    /// <c>lostStages</c> — vergul bilan ajratilgan ustun id'lari: qaysi ustunlar "yo'qotildi"
    /// deb hisoblansin. Bazada bunday bayroq yo'q (ustunlar mijozniki, u ularni xohlagancha
    /// nomlaydi), shuning uchun tanlovni chaqiruvchi beradi. Berilmasa — barcha ustunlar
    /// voronka bosqichi deb qaraladi.
    /// </para>
    /// </summary>
    [HttpGet("funnel")]
    public async Task<ActionResult<LeadFunnelDto>> Funnel([FromQuery] string? lostStages)
    {
        var lost = string.IsNullOrWhiteSpace(lostStages)
            ? null
            : lostStages.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return await LeadFunnelQuery.BuildAsync(db, lost);
    }

    [HttpPost]
    public async Task<ActionResult<Lead>> Create(LeadCreateRequest p)
    {
        var lead = new Lead
        {
            FullName = p.FullName,
            Gender = p.Gender,
            BirthDate = p.BirthDate,
            ParentFullName = p.ParentFullName,
            ParentPhone = p.ParentPhone,
            TargetGrade = p.TargetGrade,
            Note = p.Note,
            Stage = p.Stage,
        };
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        return lead;
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, LeadUpdateRequest p)
    {
        var lead = await db.Leads.FindAsync(id);
        if (lead is null) return NotFound();
        lead.FullName = p.FullName;
        lead.Gender = p.Gender;
        lead.BirthDate = p.BirthDate;
        lead.ParentFullName = p.ParentFullName;
        lead.ParentPhone = p.ParentPhone;
        lead.TargetGrade = p.TargetGrade;
        lead.Note = p.Note;
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Lidni boshqa bosqichga (ustunga) ko'chirish.</summary>
    [HttpPatch("{id}")]
    public async Task<IActionResult> ChangeStage(string id, LeadStageRequest req)
    {
        var lead = await db.Leads.FindAsync(id);
        if (lead is null) return NotFound();
        lead.Stage = req.Stage;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var lead = await db.Leads.FindAsync(id);
        if (lead is null) return NotFound();
        db.Leads.Remove(lead);
        await db.SaveChangesAsync();
        return NoContent();
    }
}

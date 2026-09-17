using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Fanlar — <c>docs/modules/students-parity.md</c> §2.5 (G-9, F-1).
///
/// <para>
/// <b>Ruxsat <c>schedule</c> bo'lib qoladi</b> — menyu esa "O'quv bo'limi"
/// ostida turibdi. Nomuvofiqlik bor va u F-4 sifatida alohida yozilgan (P2);
/// bu yerda darvozani o'zgartirish bugun ishlayotgan ekranni buzardi.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("schedule")]
[Route("api/admin/subjects")]
public class SubjectsController(AppDbContext db) : ControllerBase
{
    /// <summary>
    /// Fanlar ro'yxati. <paramref name="groupable"/>=true bo'lsa faqat
    /// "guruhlarga bo'linadi" deb belgilanganlari — guruh formasining fan
    /// tanlovi aynan shu ro'yxatni so'raydi (§2.1.1:
    /// <c>subjects/all?isGroupsSubject=true</c>).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Subject>>> GetAll(
        [FromQuery] bool? groupable = null, CancellationToken ct = default)
    {
        var q = db.Subjects.AsNoTracking().AsQueryable();
        if (groupable is true) q = q.Where(s => s.IsGroupable);
        return await q.OrderBy(s => s.Name).ToListAsync(ct);
    }

    [HttpPost]
    public async Task<ActionResult<Subject>> Create(SubjectPayload payload)
    {
        var subject = new Subject { Name = payload.Name, IsGroupable = payload.IsGroupable };
        db.Subjects.Add(subject);
        await db.SaveChangesAsync();
        return subject;
    }

    /// <summary>
    /// Fanni tahrirlash.
    ///
    /// <para>
    /// <b>"Guruhlarga bo'linadi" bayrog'ini O'CHIRISH</b> shu fan bo'yicha
    /// FAOL guruh turgan bo'lsa rad etiladi: bayroq o'chsa, guruh formasi
    /// fanni ko'rsatmay qo'yadi va mavjud guruh tahrirlab bo'lmaydigan
    /// "yetim" bo'lib qolardi. Guruh o'zi arxivlangach bayroqni o'chirish
    /// mumkin.
    /// </para>
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<Subject>> Update(string id, SubjectPayload payload)
    {
        var subject = await db.Subjects.FindAsync(id);
        if (subject is null) return NotFound();

        if (subject.IsGroupable && !payload.IsGroupable)
        {
            var active = await db.StudyGroups.CountAsync(g => g.SubjectId == id && !g.IsArchived);
            if (active > 0)
                return Conflict(new
                {
                    message = $"Bu fan bo'yicha {active} ta faol o'quv guruhi bor — "
                              + "\"guruhlarga bo'linadi\" belgisini olib tashlab bo'lmaydi. "
                              + "Avval guruhlarni arxivlang.",
                });
        }

        subject.Name = payload.Name;
        subject.IsGroupable = payload.IsGroupable;
        await db.SaveChangesAsync();
        return subject;
    }

    /// <summary>
    /// Fanni o'chirish — <b>F-1 tuzatuvi</b>.
    ///
    /// <para>
    /// Ilgari bu yerda hech qanday tekshiruv yo'q edi. Fan o'chirilganda unga
    /// bog'langan jadval katagi, jurnal qatori, chorak bahosi, dars mavzusi va
    /// sertifikat "yetim" qolardi: ekranlar fan nomi o'rniga bo'shliq
    /// ko'rsatardi, chorak bahosi esa qaysi fandan qo'yilgani noma'lum bo'lib
    /// qolardi. Bu xato jim: hech qayerda xatolik chiqmasdi.
    /// </para>
    /// <para>
    /// Endi ishlatilayotgan fan o'chirilmaydi va javob QAYERDA ishlatilganini
    /// aytadi — administrator nima qilishini bilsin.
    /// </para>
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct = default)
    {
        var subject = await db.Subjects.FindAsync([id], ct);
        if (subject is null) return NotFound();

        var used = new List<string>();
        await CountAsync(used, "o'quv guruhi", db.StudyGroups.Where(g => g.SubjectId == id), ct);
        await CountAsync(used, "dars jadvali katagi",
            db.Set<ScheduleLesson>().Where(l => l.SubjectId == id), ct);
        await CountAsync(used, "jurnal yozuvi", db.JournalEntries.Where(e => e.SubjectId == id), ct);
        await CountAsync(used, "chorak bahosi", db.QuarterGrades.Where(g => g.SubjectId == id), ct);
        await CountAsync(used, "dars mavzusi", db.LessonNotes.Where(n => n.SubjectId == id), ct);
        // Z-3: bitta sertifikat bir nechta fanni qamrab olishi mumkin —
        // `certificate_subjects` endi HAR bir fan (asosiysi ham) uchun qator saqlaydi,
        // shuning uchun eski `certificates.subject_id` tekshiruvi shu bilan almashtirildi
        // (docs/modules/students-parity.md §2.7 Z-3; eski ustunning o'zi tegilmagan).
        await CountAsync(used, "sertifikat", db.CertificateSubjects.Where(cs => cs.SubjectId == id), ct);
        await CountAsync(used, "topshiriq", db.Assignments.Where(a => a.SubjectId == id), ct);

        if (used.Count > 0)
            return Conflict(new
            {
                message = $"\"{subject.Name}\" fani ishlatilmoqda ({string.Join(", ", used)}) — "
                          + "uni o'chirib bo'lmaydi. Fan tarixiy yozuvlarning nomi; o'chirilsa "
                          + "ular qaysi fandan ekani noma'lum bo'lib qoladi.",
            });

        db.Subjects.Remove(subject);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Bog'liqlikni sanaydi va topilsa ro'yxatga "N ta &lt;nom&gt;" qo'shadi.</summary>
    private static async Task CountAsync<T>(
        List<string> used, string label, IQueryable<T> q, CancellationToken ct)
    {
        var count = await q.CountAsync(ct);
        if (count > 0) used.Add($"{count} ta {label}");
    }
}

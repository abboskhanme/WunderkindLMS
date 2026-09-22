using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Fanlar — <c>docs/modules/students-parity.md</c> §2.5 (G-9, F-1, F-3, F-4).
///
/// <para>
/// <b>Ruxsat — <c>students</c> (F-4 tuzatuvi).</b> Ilgari bu yerda <c>schedule</c>
/// turardi, menyu esa "O'quv bo'limi" (<c>students</c>) ostida — uch tomonlama
/// nomuvofiqlik (<c>App.tsx</c> marshrut darvozasi ham <c>schedule</c> so'ragan).
/// Natija: <c>students</c> ruxsatli-lekin-<c>schedule</c>siz xodim menyuda
/// "Fanlar"ni ko'radi (nav bolasi alohida <c>perm</c>siz — ota elementning
/// <c>students</c> darvozasidan o'tadi), bosganda esa "ruxsatingiz yo'q" oladi.
/// <b>To'g'ri tomon — <c>students</c>:</b> mijoz 2026-09-17 da Fanlar/Xonalar
/// tartibini AYNAN EduSchool'ning "O'quv bo'limi" menyusidan olishni so'radi
/// (<c>navigation.ts</c> izohi) — bu joylashuv qaror, tasodif emas. Va bu
/// fayldagi naqsh allaqachon bor: <c>StudentStatusesController</c> va
/// <c>CertificateTypesController</c> xuddi shu sababdan <c>students</c>ni
/// tanlagan ("menyu O'quv bo'limi ostida, darvoza ham shunga mos bo'lsin").
/// Fanlar katalogi ham xuddi shunday — dars jadvali UNI ISHLATADI, lekin
/// egasi emas.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("students")]
[Route("api/admin/subjects")]
public partial class SubjectsController(AppDbContext db) : ControllerBase
{
    public const string ColorMessage = "Rang #RRGGBB ko'rinishida bo'lsin (masalan #34C759)";

    /// <summary>Baza CHECK constraint'i bilan AYNAN bir xil shakl (`ck_subjects_color`).</summary>
    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex ColorPattern();

    /// <summary>
    /// Fanlar ro'yxati. <paramref name="groupable"/>=true bo'lsa faqat
    /// "guruhlarga bo'linadi" deb belgilanganlari — guruh formasining fan
    /// tanlovi aynan shu ro'yxatni so'raydi (§2.1.1:
    /// <c>subjects/all?isGroupsSubject=true</c>).
    ///
    /// <para>
    /// <paramref name="isActive"/> berilmasa — HAMMASI qaytadi (faol ham,
    /// faolsiz ham). Bu ATAYLAB: jadval, jurnal, chorak bahosi va sertifikat
    /// kabi ko'plab o'qiydigan ekranlar fan nomini ID bo'yicha shu ro'yxatdan
    /// qidiradi — faolsizlantirilgan fan filtrlanib ketsa, o'sha ESKI
    /// yozuvlar "fansiz" ko'rinib qolardi (F-3 talabi: "must not break
    /// existing... rows"). Faqat FAOLLARNI so'raydigan YANGI tanlov —
    /// <c>isActive=true</c> bilan ochiq qoldirilgan.
    /// </para>
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Subject>>> GetAll(
        [FromQuery] bool? groupable = null, [FromQuery] bool? isActive = null,
        CancellationToken ct = default)
    {
        var q = db.Subjects.AsNoTracking().AsQueryable();
        if (groupable is true) q = q.Where(s => s.IsGroupable);
        if (isActive is { } active) q = q.Where(s => s.IsActive == active);
        return await q.OrderBy(s => s.Name).ToListAsync(ct);
    }

    [HttpPost]
    public async Task<ActionResult<Subject>> Create(SubjectPayload payload)
    {
        var color = NormalizeColor(payload.Color);
        if (color is null && !string.IsNullOrWhiteSpace(payload.Color))
            return BadRequest(new { message = ColorMessage });

        var subject = new Subject
        {
            Name = payload.Name,
            IsGroupable = payload.IsGroupable,
            Color = color,
            IsActive = payload.IsActive,
        };
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
    /// <para>
    /// <b><c>IsActive</c> — bunday qulf YO'Q (F-3).</b> Fanni faolsizlantirish
    /// undan foydalanayotgan hech narsani buzmaydi: jadval katagi, jurnal
    /// qatori, chorak bahosi va sertifikat JOYIDA qoladi, faqat YANGI
    /// tanlovda (jadval yaratish, yangi guruh) ko'rinmay qoladi. Shuning
    /// uchun bu yerda "faol guruhi bor" tekshiruvi YO'Q — u faqat
    /// <c>IsGroupable</c> bayrog'iga tegishli.
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

        var color = NormalizeColor(payload.Color);
        if (color is null && !string.IsNullOrWhiteSpace(payload.Color))
            return BadRequest(new { message = ColorMessage });

        subject.Name = payload.Name;
        subject.IsGroupable = payload.IsGroupable;
        subject.Color = color;
        subject.IsActive = payload.IsActive;
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
    /// aytadi — administrator nima qilishini bilsin. <b>F-3</b>dan keyin
    /// tavsiya endi aniq: o'chirish o'rniga faolsizlantirish (tarixiy
    /// yozuvlar buzilmaydi, yangi tanlovda ko'rinmay qoladi).
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
        // admission-and-testing.md §5.14: these three are the first tables with a
        // REAL foreign key onto `subjects` (ON DELETE RESTRICT) — without the count
        // the delete below would fail as a 500 instead of this 409.
        await CountAsync(used, "test bazasi", db.QuestionBanks.Where(b => b.SubjectId == id), ct);
        await CountAsync(used, "imtihon fani", db.ExamSections.Where(s => s.SubjectId == id), ct);
        await CountAsync(used, "mavsumiy baho", db.SeasonalMarks.Where(m => m.SubjectId == id), ct);

        if (used.Count > 0)
            return Conflict(new
            {
                message = $"\"{subject.Name}\" fani ishlatilmoqda ({string.Join(", ", used)}) — "
                          + "uni o'chirib bo'lmaydi. Fan tarixiy yozuvlarning nomi; o'chirilsa "
                          + "ular qaysi fandan ekani noma'lum bo'lib qoladi. Buning o'rniga fanni "
                          + "faolsizlantiring — u yangi tanlovda ko'rinmay qoladi.",
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

    /// <summary>
    /// <c>#RRGGBB</c> ga keltiradi (katta harfda). Bo'sh — null (rang yo'q,
    /// neytral); shakli noto'g'ri — null va chaqiruvchi 400 qaytaradi
    /// (<see cref="StudentStatusesController"/> dagi bir xil naqsh).
    /// </summary>
    private static string? NormalizeColor(string? raw)
    {
        var v = (raw ?? "").Trim();
        if (v.Length == 0) return null;
        if (!v.StartsWith('#')) v = "#" + v;
        return ColorPattern().IsMatch(v) ? "#" + v[1..].ToUpperInvariant() : null;
    }
}

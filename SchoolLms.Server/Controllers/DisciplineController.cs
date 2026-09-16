using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using System.Security.Claims;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Intizomiy ball — har o'quvchi 100 balldan boshlaydi. Sabablar IKKI manbadan:
/// "other" — mustaqil intizomiy sabablar (shu yerda CRUD); "attendance" — davomat sabablari
/// (<see cref="AbsenceReason"/>, jurnalda ishlatiladi) — ularning balli shu yerda belgilanadi va
/// jurnalda shu sabab bilan davomat qo'yilsa qoldiga avtomatik ta'sir qiladi. Faqat admin.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("discipline")]
[Route("api/admin/discipline")]
public class DisciplineController(AppDbContext db) : ControllerBase
{
    private const int BaseScore = 100;
    private string Uid => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    // ---------- Ball sabablar (birlashgan) ----------

    /// <summary>Barcha sabablar: mustaqil intizomiy ("other") + davomat sabablari ("attendance").</summary>
    [HttpGet("reasons")]
    public async Task<ActionResult<IEnumerable<DisciplineReasonDto>>> GetReasons()
    {
        var other = await db.DisciplineReasons.OrderBy(r => r.Name)
            .Select(r => new DisciplineReasonDto(r.Id, r.Name, r.Points, "other")).ToListAsync();
        var attendance = await db.AbsenceReasons.OrderBy(r => r.Name)
            .Select(r => new DisciplineReasonDto(r.Id, r.Name, r.Points, "attendance")).ToListAsync();
        return other.Concat(attendance).ToList();
    }

    [HttpPost("reasons")]
    public async Task<ActionResult<DisciplineReasonDto>> CreateReason(SaveDisciplineReasonRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { message = "Sabab nomi kerak" });
        var r = new DisciplineReason { Name = req.Name.Trim(), Points = req.Points };
        db.DisciplineReasons.Add(r);
        await db.SaveChangesAsync();
        return new DisciplineReasonDto(r.Id, r.Name, r.Points, "other");
    }

    [HttpPut("reasons/{id}")]
    public async Task<ActionResult<DisciplineReasonDto>> UpdateReason(string id, SaveDisciplineReasonRequest req)
    {
        var r = await db.DisciplineReasons.FindAsync(id);
        if (r is null) return NotFound();
        r.Name = (req.Name ?? "").Trim();
        r.Points = req.Points;
        await db.SaveChangesAsync();
        return new DisciplineReasonDto(r.Id, r.Name, r.Points, "other");
    }

    [HttpDelete("reasons/{id}")]
    public async Task<IActionResult> DeleteReason(string id)
    {
        var r = await db.DisciplineReasons.FindAsync(id);
        if (r is not null) { db.DisciplineReasons.Remove(r); await db.SaveChangesAsync(); }
        return NoContent();
    }

    /// <summary>Davomat sababiga ball belgilash (nomi Sozlamalar/jurnal tarafida boshqariladi).</summary>
    [HttpPut("reasons/attendance/{id}")]
    public async Task<ActionResult<DisciplineReasonDto>> SetAttendancePoints(string id, SetReasonPointsRequest req)
    {
        var r = await db.AbsenceReasons.FindAsync(id);
        if (r is null) return NotFound();
        r.Points = req.Points;
        await db.SaveChangesAsync();
        return new DisciplineReasonDto(r.Id, r.Name, r.Points, "attendance");
    }

    // ---------- Ballar nazorati ----------

    /// <summary>
    /// Faol o'quvchilar jamlamasi: plus/minus/qoldi. Qoldi = 100 + qo'lda kiritilgan ballar
    /// + jurnal davomati ballari (sabab balli != 0 bo'lganlar).
    /// </summary>
    [HttpGet("scores")]
    public async Task<ActionResult<IEnumerable<DisciplineScoreRowDto>>> GetScores()
        => await BuildScoresAsync();

    /// <summary>
    /// Ballar nazorati Excel (.xlsx) ga — ekrandagi FILTRLAR bilan bir xil qatorlar.
    /// Filtrlar sahifada mijoz tarafida qo'llanadi (<c>/scores</c> butun ro'yxatni qaytaradi),
    /// shuning uchun eksport ham xuddi shu filtrlarni qabul qiladi — yuklangan fayl ekranda
    /// ko'rinib turgan narsaga aynan mos bo'lishi uchun.
    /// </summary>
    [HttpGet("scores/export")]
    public async Task<IActionResult> ExportScores(
        [FromQuery] string? className, [FromQuery] string? search,
        [FromQuery] int? minPoints, [FromQuery] int? maxPoints, [FromQuery] string? sort)
    {
        var rows = FilterScores(await BuildScoresAsync(), className, search, minPoints, maxPoints, sort);

        var headers = new[] { "F.I.SH.", "Sinf", "Rag'bat (+)", "Jazo (−)", "Qoldi" };
        var cells = rows.Select(r => (IReadOnlyList<string>)new[]
        {
            r.FullName, r.ClassName, r.Plus.ToString(), r.Minus.ToString(), r.Remaining.ToString(),
        });

        var bytes = ExcelExport.Build("Ballar nazorati", headers, cells);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"ballar_nazorati_{AppClock.Now:yyyy-MM-dd}.xlsx");
    }

    /// <summary>
    /// Ekrandagi filtrlarning server tarafdagi nusxasi (eksport uchun). <c>sort</c> qiymatlari
    /// sahifadagi tanlov bilan bir xil: class | remaining_desc | remaining_asc | plus_desc | minus_desc.
    /// </summary>
    private static List<DisciplineScoreRowDto> FilterScores(
        List<DisciplineScoreRowDto> rows, string? className, string? search,
        int? minPoints, int? maxPoints, string? sort)
    {
        var q = (search ?? "").Trim();
        var list = rows.Where(r =>
                (string.IsNullOrWhiteSpace(className) || className == "all" || r.ClassName == className)
                && (q.Length == 0 || r.FullName.Contains(q, StringComparison.OrdinalIgnoreCase))
                && (minPoints is null || r.Remaining >= minPoints)
                && (maxPoints is null || r.Remaining <= maxPoints))
            .ToList();

        return sort switch
        {
            "remaining_desc" => list.OrderByDescending(r => r.Remaining).ToList(),
            "remaining_asc" => list.OrderBy(r => r.Remaining).ToList(),
            "plus_desc" => list.OrderByDescending(r => r.Plus).ToList(),
            "minus_desc" => list.OrderByDescending(r => r.Minus).ToList(),
            _ => list, // "class" — BuildScoresAsync allaqachon sinf+F.I.SH bo'yicha tartiblagan
        };
    }

    private async Task<List<DisciplineScoreRowDto>> BuildScoresAsync()
    {
        var students = await db.Students.Where(s => !s.IsArchived)
            .Select(s => new { s.Id, s.FullName, s.ClassName }).ToListAsync();
        var manual = await db.DisciplinePoints.Select(p => new { p.StudentId, p.Points }).ToListAsync();
        var absPts = await db.AbsenceReasons.ToDictionaryAsync(r => r.Id, r => r.Points);
        var journal = await db.JournalEntries.Where(e => e.ReasonId != null)
            .Select(e => new { e.StudentId, e.ReasonId }).ToListAsync();

        var plusBy = new Dictionary<string, int>();
        var minusBy = new Dictionary<string, int>();
        void Apply(string sid, int pts)
        {
            if (pts > 0) plusBy[sid] = plusBy.GetValueOrDefault(sid) + pts;
            else if (pts < 0) minusBy[sid] = minusBy.GetValueOrDefault(sid) + (-pts);
        }
        foreach (var m in manual) Apply(m.StudentId, m.Points);
        foreach (var j in journal)
            if (j.ReasonId is not null && absPts.TryGetValue(j.ReasonId, out var p)) Apply(j.StudentId, p);

        return students.Select(s =>
        {
            var plus = plusBy.GetValueOrDefault(s.Id);
            var minus = minusBy.GetValueOrDefault(s.Id);
            return new DisciplineScoreRowDto(s.Id, s.FullName, s.ClassName, plus, minus, BaseScore + plus - minus);
        })
        .OrderBy(r => r.ClassName, StringComparer.OrdinalIgnoreCase)
        .ThenBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
        .ToList();
    }

    /// <summary>O'quvchiga qo'lda ball kiritadi (sabab "other" yoki "attendance" bo'lishi mumkin).</summary>
    [HttpPost("points")]
    public async Task<ActionResult<DisciplinePointDto>> AddPoint(AddDisciplinePointRequest req)
    {
        var student = await db.Students.FindAsync(req.StudentId);
        if (student is null) return NotFound(new { message = "O'quvchi topilmadi" });

        string name;
        int pts;
        var dr = await db.DisciplineReasons.FindAsync(req.ReasonId);
        if (dr is not null) { name = dr.Name; pts = dr.Points; }
        else
        {
            var ar = await db.AbsenceReasons.FindAsync(req.ReasonId);
            if (ar is null) return BadRequest(new { message = "Sabab tanlanmadi" });
            name = ar.Name; pts = ar.Points;
        }

        var user = await db.Users.FindAsync(Uid);
        var p = new DisciplinePoint
        {
            StudentId = student.Id,
            ReasonId = req.ReasonId,
            ReasonName = name,
            Points = pts,
            Note = (req.Note ?? "").Trim(),
            CreatedAt = AppClock.Now.ToString("o"),
            CreatedBy = user?.FullName ?? "Administrator",
        };
        db.DisciplinePoints.Add(p);
        await db.SaveChangesAsync();
        return new DisciplinePointDto(p.Id, p.StudentId, name, pts, p.Note, p.CreatedAt, p.CreatedBy, "manual");
    }

    /// <summary>O'quvchining ball tarixi: qo'lda kiritilgan (o'chirsa bo'ladi) + jurnal davomati (faqat ko'rish).</summary>
    [HttpGet("points")]
    public async Task<ActionResult<IEnumerable<DisciplinePointDto>>> GetPoints([FromQuery] string studentId)
    {
        var manual = await db.DisciplinePoints.Where(p => p.StudentId == studentId).ToListAsync();
        var drNames = await db.DisciplineReasons.ToDictionaryAsync(r => r.Id, r => r.Name);
        var absReasons = await db.AbsenceReasons.ToDictionaryAsync(r => r.Id, r => new { r.Name, r.Points });

        var result = manual.Select(p => new DisciplinePointDto(
            p.Id, p.StudentId,
            string.IsNullOrEmpty(p.ReasonName) ? drNames.GetValueOrDefault(p.ReasonId, "—") : p.ReasonName,
            p.Points, p.Note, p.CreatedAt, p.CreatedBy, "manual")).ToList();

        // Jurnal davomati — sabab balli != 0 bo'lganlari (qoldiga ta'sir qilgani uchun ko'rsatamiz).
        var journal = await db.JournalEntries
            .Where(e => e.StudentId == studentId && e.ReasonId != null).ToListAsync();
        foreach (var e in journal)
        {
            if (e.ReasonId is null || !absReasons.TryGetValue(e.ReasonId, out var r) || r.Points == 0) continue;
            result.Add(new DisciplinePointDto(
                e.Id, studentId, r.Name, r.Points, "Jurnal davomati", e.Date, "", "attendance"));
        }

        return result.OrderByDescending(p => p.CreatedAt, StringComparer.Ordinal).ToList();
    }

    // ---------- Harakatlar (maktab bo'ylab lenta) ----------

    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;
    /// <summary>Davr berilmasa ko'riladigan oxirgi kunlar soni.</summary>
    private const int DefaultWindowDays = 30;

    /// <summary>
    /// Maktab bo'ylab "nima bo'ldi" lentasi — <c>GET points?studentId=</c> dan farqli ravishda
    /// BITTA o'quvchiga bog'lanmagan. Ikki manba birlashtiriladi: qo'lda kiritilgan ballar va
    /// jurnal davomati (sabab balli != 0 bo'lganlari) — ikkinchisi <c>source: "attendance"</c>
    /// bilan belgilanadi.
    ///
    /// <para>Filtrlar (hammasi ixtiyoriy): <c>from</c>/<c>to</c> (YYYY-MM-DD, ikkalasi ham
    /// KIRADI), <c>className</c>, <c>reasonId</c>, <c>author</c> (qo'lda kiritgan xodim),
    /// <c>sign</c> = positive|negative, <c>source</c> = manual|attendance, <c>search</c> (F.I.SH).
    /// Davr BUTUNLAY berilmasa — oxirgi 30 kun (<see cref="DefaultWindowDays"/>), butun tarixni
    /// har so'rovda skanerlamaslik uchun.</para>
    ///
    /// <para>Sabab bo'yicha filtr <c>ReasonId</c> ustidan ishlaydi, ekranga esa yozuv paytidagi
    /// NUSXA (<c>ReasonName</c>/<c>Points</c>) chiqadi — sabab keyin tahrirlansa ham tarix
    /// o'zgarmasligi uchun (<see cref="DisciplinePoint"/>).</para>
    /// </summary>
    [HttpGet("feed")]
    public async Task<ActionResult<DisciplineFeedDto>> GetFeed(
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? className,
        [FromQuery] string? reasonId, [FromQuery] string? author, [FromQuery] string? sign,
        [FromQuery] string? source, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = DefaultPageSize)
    {
        if (!TryDay(from, out var fromDay)) return BadRequest(new { message = "Davr boshi noto'g'ri (YYYY-MM-DD)" });
        if (!TryDay(to, out var toDay)) return BadRequest(new { message = "Davr oxiri noto'g'ri (YYYY-MM-DD)" });
        if (fromDay is not null && toDay is not null && toDay < fromDay)
            return BadRequest(new { message = "Davr oxiri boshidan oldin bo'lishi mumkin emas" });
        if (fromDay is null && toDay is null) fromDay = AppClock.Today.AddDays(-DefaultWindowDays);

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var fromText = fromDay?.ToString("yyyy-MM-dd");
        var toText = toDay?.ToString("yyyy-MM-dd");
        // Qo'lda kiritilgan yozuvda vaqt ham bor ("2026-09-16T10:12:..."), shuning uchun yuqori
        // chegara — ERTANGI kun va qat'iy kichik. Aks holda oxirgi kunning yozuvlari tushib qolardi.
        var toExclusive = toDay?.AddDays(1).ToString("yyyy-MM-dd");

        var students = await db.Students.AsNoTracking()
            .Select(s => new { s.Id, s.FullName, s.ClassName, s.IsArchived }).ToListAsync();
        var byStudent = students.ToDictionary(s => s.Id);

        var wantClass = string.IsNullOrWhiteSpace(className) || className == "all" ? null : className.Trim();
        var term = (search ?? "").Trim();

        // Arxivlangan o'quvchi ham chiqadi: uning yozuvi TARIX, va u sinfdan ketgani bilan
        // o'sha kuni bo'lgan voqea bo'lmagan bo'lib qolmaydi.
        (string FullName, string ClassName)? Student(string id)
        {
            if (!byStudent.TryGetValue(id, out var s)) return null;
            if (wantClass is not null && s.ClassName != wantClass) return null;
            if (term.Length > 0 && !s.FullName.Contains(term, StringComparison.OrdinalIgnoreCase)) return null;
            return (s.FullName, s.ClassName);
        }

        var rows = new List<DisciplineFeedRowDto>();

        if (source != "attendance")
        {
            var q = db.DisciplinePoints.AsNoTracking().AsQueryable();
            if (fromText is { } f) q = q.Where(p => string.Compare(p.CreatedAt, f) >= 0);
            if (toExclusive is { } t) q = q.Where(p => string.Compare(p.CreatedAt, t) < 0);
            if (!string.IsNullOrWhiteSpace(reasonId)) q = q.Where(p => p.ReasonId == reasonId);
            if (!string.IsNullOrWhiteSpace(author)) q = q.Where(p => p.CreatedBy == author);
            if (sign == "positive") q = q.Where(p => p.Points > 0);
            else if (sign == "negative") q = q.Where(p => p.Points < 0);

            var drNames = await db.DisciplineReasons.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Name);
            foreach (var p in await q.ToListAsync())
            {
                if (Student(p.StudentId) is not { } s) continue;
                rows.Add(new DisciplineFeedRowDto(
                    p.Id, p.StudentId, s.FullName, s.ClassName,
                    string.IsNullOrEmpty(p.ReasonName) ? drNames.GetValueOrDefault(p.ReasonId, "—") : p.ReasonName,
                    p.Points, p.Note, p.CreatedAt, p.CreatedBy, "manual"));
            }
        }

        // Jurnal davomatida MUALLIF yo'q (belgini o'qituvchi jurnalda qo'yadi, yozuvda saqlanmaydi) —
        // shuning uchun xodim bo'yicha filtr tanlanganda bu manba umuman qatnashmaydi.
        if (source != "manual" && string.IsNullOrWhiteSpace(author))
        {
            var absReasons = await db.AbsenceReasons.AsNoTracking()
                .ToDictionaryAsync(r => r.Id, r => new { r.Name, r.Points });
            var q = db.JournalEntries.AsNoTracking().Where(e => e.ReasonId != null);
            if (fromText is { } f) q = q.Where(e => string.Compare(e.Date, f) >= 0);
            if (toText is { } t) q = q.Where(e => string.Compare(e.Date, t) <= 0);
            if (!string.IsNullOrWhiteSpace(reasonId)) q = q.Where(e => e.ReasonId == reasonId);

            foreach (var e in await q.ToListAsync())
            {
                if (e.ReasonId is null || !absReasons.TryGetValue(e.ReasonId, out var r) || r.Points == 0) continue;
                if (sign == "positive" && r.Points < 0) continue;
                if (sign == "negative" && r.Points > 0) continue;
                if (Student(e.StudentId) is not { } s) continue;
                rows.Add(new DisciplineFeedRowDto(
                    e.Id, e.StudentId, s.FullName, s.ClassName, r.Name, r.Points,
                    "Jurnal davomati", e.Date, "", "attendance"));
            }
        }

        // Ikki manba bitta kalit bilan tartiblanadi: qo'lda kiritilganda to'liq vaqt bor,
        // jurnalda faqat sana — ISO satr sifatida taqqoslash ikkalasida ham to'g'ri ishlaydi.
        var ordered = rows
            .OrderByDescending(r => r.CreatedAt, StringComparer.Ordinal)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .ToList();

        var authors = (await db.DisciplinePoints.AsNoTracking()
                .Select(p => p.CreatedBy).Distinct().ToListAsync())
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToList();
        var classNames = students
            .Where(s => !s.IsArchived && !string.IsNullOrWhiteSpace(s.ClassName))
            .Select(s => s.ClassName).Distinct()
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();

        return new DisciplineFeedDto(
            ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            ordered.Count, page, pageSize,
            ordered.Count(r => r.Points > 0), ordered.Count(r => r.Points < 0), ordered.Sum(r => r.Points),
            authors, classNames);
    }

    private static bool TryDay(string? value, out DateOnly? day)
    {
        day = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (!DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", out var parsed)) return false;
        day = parsed;
        return true;
    }

    /// <summary>Qo'lda kiritilgan ball yozuvini o'chiradi (jurnal davomatini emas).</summary>
    [HttpDelete("points/{id}")]
    public async Task<IActionResult> DeletePoint(string id)
    {
        var p = await db.DisciplinePoints.FindAsync(id);
        if (p is not null) { db.DisciplinePoints.Remove(p); await db.SaveChangesAsync(); }
        return NoContent();
    }
}

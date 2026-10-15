using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

// ===========================================================================
//  O'QITUVCHI SERTIFIKATLARI VA USTAMALARI — oylikka to'g'ridan-to'g'ri (2026-10-15)
//  docs/modules/teacher-allowances.md
// ===========================================================================
//
//  Mijoz: "Xodimlar bo'limidagi o'qituvchilar ma'lumotlarini ichiga sertifikat va ustama qismlarini
//  qo'shish va buni to'g'ridan to'g'ri moliyaga bog'lash (oylik hisob-kitobi)". Har qator oylikka
//  TeacherPay orqali kiradi — maosh jadvali, tafsilot, daftar va moliya hisoboti bir xil raqam ko'radi.
//
//  RUXSAT — xuddi ustama foizi (SalaryRatesController) va Bonus/Jarima kabi: bu PUL. O'qish —
//  moliya hisobotlari darajasi (admin, direktor, "finance" va "finance:view" xodim); yozish —
//  ManagePayrollAdjustments (admin, direktor, "finance" xodim). Har o'zgarish — audit qatori.
// ===========================================================================

/// <summary>A teacher's certificates and named allowances — CRUD; the salary reads them through <see cref="TeacherPayContext"/>.</summary>
[ApiController]
[Authorize(Roles = Roles.FinanceStaff)]
[AdminPerm("teachers")]
[Route("api/admin/teachers/{teacherId}/allowances")]
public partial class TeacherAllowancesController(AppDbContext db, AuditService audit) : ControllerBase
{
    private const decimal MaxFixed = 1_000_000_000m;

    [HttpGet]
    [FinanceRole(FinanceAction.ViewBillingReports)]
    public async Task<ActionResult<TeacherAllowancesDto>> List(string teacherId, CancellationToken ct)
    {
        var teacher = await db.Teachers.AsNoTracking().FirstOrDefaultAsync(t => t.Id == teacherId, ct);
        if (teacher is null) return NotFound(new { message = "O'qituvchi topilmadi" });
        var month = AppClock.Now.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var pay = await TeacherPayContext.LoadAsync(db, teacherId, ct);
        var rows = await db.TeacherAllowances.AsNoTracking().Where(a => a.TeacherId == teacherId)
            .OrderBy(a => a.Source).ThenByDescending(a => a.CreatedAt).ToListAsync(ct);
        return new TeacherAllowancesDto(teacher.Id, teacher.FullName, teacher.BonusPct, pay.For(teacher, month),
            [.. rows.Select(a => ToDto(a, month))]);
    }

    [HttpPost]
    [FinanceRole(FinanceAction.ManagePayrollAdjustments)]
    public async Task<ActionResult<TeacherAllowanceDto>> Create(string teacherId, TeacherAllowanceRequest req, CancellationToken ct)
    {
        if (!await db.Teachers.AnyAsync(t => t.Id == teacherId, ct)) return NotFound(new { message = "O'qituvchi topilmadi" });
        var row = new TeacherAllowance
        {
            TeacherId = teacherId,
            CreatedBy = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "",
            CreatedAt = AppClock.NowInstant,
        };
        if (Apply(row, req) is { } error) return BadRequest(new { message = error });
        row.UpdatedAt = row.CreatedAt;
        db.TeacherAllowances.Add(row);
        audit.Record(AuditService.EntityTeacherSalary, teacherId, "create",
            $"{SourceLabel(row.Source)} qo'shildi: {row.Title} — {AmountText(row)}, {PeriodText(row)}",
            after: Snapshot(row), teacherId: teacherId);
        await db.SaveChangesAsync(ct);
        return ToDto(row, AppClock.Now.ToString("yyyy-MM", CultureInfo.InvariantCulture));
    }

    [HttpPut("{id:guid}")]
    [FinanceRole(FinanceAction.ManagePayrollAdjustments)]
    public async Task<ActionResult<TeacherAllowanceDto>> Update(string teacherId, Guid id, TeacherAllowanceRequest req, CancellationToken ct)
    {
        var row = await db.TeacherAllowances.FirstOrDefaultAsync(a => a.Id == id && a.TeacherId == teacherId, ct);
        if (row is null) return NotFound(new { message = "Yozuv topilmadi" });
        var before = Snapshot(row);
        if (Apply(row, req) is { } error) return BadRequest(new { message = error });
        row.UpdatedAt = AppClock.NowInstant;
        audit.Record(AuditService.EntityTeacherSalary, teacherId, "update",
            $"{SourceLabel(row.Source)} o'zgardi: {row.Title} — {AmountText(row)}, {PeriodText(row)}",
            before: before, after: Snapshot(row), teacherId: teacherId);
        await db.SaveChangesAsync(ct);
        return ToDto(row, AppClock.Now.ToString("yyyy-MM", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// For a line entered by mistake. A certificate that simply ran out keeps its row (its expiry date stops it),
    /// and an allowance that ends gets a "to" month — both keep past months' pay as it was.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [FinanceRole(FinanceAction.ManagePayrollAdjustments)]
    public async Task<IActionResult> Delete(string teacherId, Guid id, CancellationToken ct)
    {
        var row = await db.TeacherAllowances.FirstOrDefaultAsync(a => a.Id == id && a.TeacherId == teacherId, ct);
        if (row is null) return NotFound(new { message = "Yozuv topilmadi" });
        db.TeacherAllowances.Remove(row);
        audit.Record(AuditService.EntityTeacherSalary, teacherId, "delete",
            $"{SourceLabel(row.Source)} o'chirildi: {row.Title} — {AmountText(row)}, {PeriodText(row)}",
            before: Snapshot(row), teacherId: teacherId);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Validates <paramref name="req"/> and copies it onto <paramref name="row"/>; an Uzbek message on error.</summary>
    private static string? Apply(TeacherAllowance row, TeacherAllowanceRequest req)
    {
        var source = (req.Source ?? "").Trim();
        if (!TeacherAllowanceSources.All.Contains(source)) return "Turi noto'g'ri (certificate yoki ustama)";
        var title = (req.Title ?? "").Trim();
        if (title.Length == 0) return "Nomini kiriting";
        if (title.Length > 200) return "Nomi 200 belgidan oshmasin";
        var kind = (req.AmountKind ?? "").Trim();
        if (!TeacherAllowanceKinds.All.Contains(kind)) return "Summa turi noto'g'ri (percent yoki fixed)";
        if (req.Amount < 0) return "Summa manfiy bo'lmasin";
        if (kind == TeacherAllowanceKinds.Percent && req.Amount > 1000) return "Foiz 0–1000 oralig'ida bo'lsin";
        if (kind == TeacherAllowanceKinds.Fixed && req.Amount > MaxFixed) return "Summa juda katta";
        var from = (req.FromMonth ?? "").Trim();
        if (!MonthRx().IsMatch(from)) return "Qaysi oydan — YYYY-MM ko'rinishida bo'lsin";
        var to = string.IsNullOrWhiteSpace(req.ToMonth) ? null : req.ToMonth.Trim();
        if (to is not null && !MonthRx().IsMatch(to)) return "Qaysi oygacha — YYYY-MM ko'rinishida bo'lsin";
        if (to is not null && string.CompareOrdinal(to, from) < 0) return "Tugash oyi boshlanish oyidan oldin bo'lmasin";
        if (!TryDate(req.IssuedOn, out var issued)) return "Berilgan sana YYYY-MM-DD ko'rinishida bo'lsin";
        if (!TryDate(req.ExpiresOn, out var expires)) return "Amal qilish muddati YYYY-MM-DD ko'rinishida bo'lsin";
        if (issued is { } i && expires is { } e && e < i) return "Amal qilish muddati berilgan sanadan oldin bo'lmasin";
        var file = string.IsNullOrWhiteSpace(req.FileUrl) ? null : req.FileUrl.Trim();
        if (file is not null && !QuestionBankService.IsUploadUrl(file)) return "Fayl manzili noto'g'ri — faylni qayta yuklang";
        var level = Clip(req.Level, 64);
        var number = Clip(req.Number, 64);
        var note = Clip(req.Note, 1000);

        row.Source = source;
        row.Title = title;
        row.AmountKind = kind;
        row.Amount = req.Amount;
        row.FromMonth = from;
        row.ToMonth = to;
        row.IssuedOn = issued;
        row.ExpiresOn = expires;
        row.FileUrl = file;
        row.Level = level;
        row.Number = number;
        row.Note = note;
        return null;
    }

    private static bool TryDate(string? value, out DateOnly? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (!DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return false;
        date = d;
        return true;
    }

    private static string? Clip(string? value, int max)
    {
        var v = value?.Trim();
        return string.IsNullOrEmpty(v) ? null : v.Length > max ? v[..max] : v;
    }

    private static TeacherAllowanceDto ToDto(TeacherAllowance a, string month) => new(
        a.Id, a.TeacherId, a.Source, a.Title, a.AmountKind, a.Amount, a.FromMonth, a.ToMonth, a.Level, a.Number,
        a.IssuedOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        a.ExpiresOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        a.FileUrl, a.Note, a.ActiveIn(month), a.ExpiresOn is { } e && e < AppClock.Today, a.CreatedAt);

    private static object Snapshot(TeacherAllowance a) => new
    {
        a.Source, a.Title, a.AmountKind, a.Amount, a.FromMonth, a.ToMonth, a.Level, a.Number,
        a.IssuedOn, a.ExpiresOn, a.FileUrl, a.Note,
    };

    private static string SourceLabel(string source) => source == TeacherAllowanceSources.Certificate ? "Sertifikat" : "Ustama";

    private static string AmountText(TeacherAllowance a) =>
        a.AmountKind == TeacherAllowanceKinds.Percent
            ? $"{a.Amount.ToString("0.##", CultureInfo.InvariantCulture)}%"
            : $"{AuditService.Money(a.Amount)} so'm/oy";

    private static string PeriodText(TeacherAllowance a) =>
        a.ToMonth is null ? $"{a.FromMonth} dan" : $"{a.FromMonth} — {a.ToMonth}";

    [GeneratedRegex(@"^\d{4}-(0[1-9]|1[0-2])$")]
    private static partial Regex MonthRx();
}

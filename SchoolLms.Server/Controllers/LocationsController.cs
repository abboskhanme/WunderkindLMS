using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Admin uchun o'quvchilar joylashuvi (xarita) ma'lumotlari — §2.8 (L-1, L-2).
///
/// <para>
/// Xodim nuqtani profil kartochkasidan ("Manzil" tab'i, <c>StudentProfileController</c>)
/// qo'yadi: uchtagacha, bittadan turdan (<see cref="StudentLocationKind"/>). Bu yerda
/// FAQAT o'qish — barcha faol o'quvchilarning barcha turdagi pin'lari bitta ro'yxatda.
/// </para>
///
/// <para>
/// <b>ESKI VA YANGI.</b> <c>home</c> turi uchun <c>student_locations</c> qatori mavjud
/// bo'lsa — o'sha ishlatiladi; yo'q bo'lsa (xodim hali yangi ekrandan saqlamagan,
/// lekin ESKI <c>students.latitude/longitude</c> ustunlarida qiymat bor — L-1 yoki eski
/// mobil ilova qoldirgan) — o'shandan pin sintez qilinadi. Ikkalasi ikki marta ko'rsatilmaydi.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("app")]
[Route("api/admin/locations")]
public class LocationsController(AppDbContext db) : ControllerBase
{
    /// <summary>
    /// Joylashuvi bor faol o'quvchilarning barcha pin'lari (kind + `pickup`da vaqt).
    /// Bitta o'quvchida uchtagacha qator bo'lishi mumkin.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<StudentLocationPinDto>>> GetAll(
        [FromQuery] string? className = null)
    {
        var studentsQ = db.Students.AsNoTracking().Where(s => !s.IsArchived);
        if (!string.IsNullOrWhiteSpace(className))
            studentsQ = studentsQ.Where(s => s.ClassName == className);

        var students = await studentsQ
            .Select(s => new
            {
                s.Id, s.FullName, s.ClassName,
                s.Latitude, s.Longitude, s.LocationAddress,
            })
            .ToListAsync();
        var ids = students.Select(s => s.Id).ToList();

        var typed = ids.Count == 0
            ? []
            : await db.StudentLocations.AsNoTracking()
                .Where(l => ids.Contains(l.StudentId)).ToListAsync();
        var byStudent = typed.ToLookup(l => l.StudentId);

        var pins = new List<StudentLocationPinDto>();
        foreach (var s in students)
        {
            var rows = byStudent[s.Id];
            var hasHome = false;
            foreach (var r in rows)
            {
                if (r.Kind == StudentLocationKind.Home) hasHome = true;
                pins.Add(new StudentLocationPinDto(
                    s.Id, s.FullName, s.ClassName, r.Kind,
                    (double)r.Lat, (double)r.Lng, r.Name,
                    r.PickupFrom?.ToString("HH:mm", CultureInfo.InvariantCulture),
                    r.PickupTo?.ToString("HH:mm", CultureInfo.InvariantCulture)));
            }

            // Eski (L-1) ustunlar — faqat `student_locations`da aniq `home` qatori bo'lmasa.
            if (!hasHome && s.Latitude is { } lat && s.Longitude is { } lng)
                pins.Add(new StudentLocationPinDto(
                    s.Id, s.FullName, s.ClassName, StudentLocationKind.Home,
                    lat, lng, s.LocationAddress, null, null));
        }

        return pins
            .OrderBy(p => p.ClassName).ThenBy(p => p.FullName)
            .ThenBy(p => Array.IndexOf(StudentLocationKind.All, p.Kind))
            .ToList();
    }
}

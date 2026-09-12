using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Kassa ish o'rni uchun o'quvchi QIDIRUVI. Vazifa: P1-16.
///
/// <para>
/// <b>Nega alohida endpoint kerak bo'ldi.</b> Kassir to'lovni o'quvchiga
/// yozadi, ya'ni avval o'quvchini topishi shart. Mavjud
/// <c>GET /api/admin/students</c> esa <see cref="AdminPermAttribute"/> darvozasi
/// ostida: u faqat admin / superadmin / staff ni o'tkazadi va <c>cashier</c> ga
/// <b>403</b> beradi (jonli stack'da tekshirilgan). Kassirni o'sha darvozadan
/// o'tkazish — butun admin CRUD'ini (yaratish, tahrirlash, arxivlash,
/// login/parol eksporti) unga ochib qo'yish degani. Shuning uchun bu yerda
/// AYNAN bitta amal bor: ismi bo'yicha qidirish.
/// </para>
///
/// <para>
/// <b>Ruxsat.</b> <c>[FinanceRole(FinanceAction.AcceptPayment)]</c> — SPEC §4.3
/// jadvalining "To'lov qabul qilish va chek berish" qatori (kassir, admin,
/// direktor). Yangi qoida o'ylab topilmadi: o'quvchini qidirish to'lov qabul
/// qilishning ajralmas qismi, ya'ni o'sha huquqning o'zi.
/// </para>
///
/// <para>
/// <b>Bu controller'da PUT, POST va DELETE YO'Q</b> — va bo'lmaydi. Kassir
/// o'quvchi kartochkasini o'zgartira olmaydi; bu ekran faqat o'qiydi.
/// </para>
///
/// <para>
/// <b>Qarz summasi bu yerda hisoblanmaydi.</b> "Qancha qarz" degan savolning
/// yagona javobi <c>PaymentService.SuggestAllocationAsync</c> da
/// (<c>GET /api/cash/payments/suggest-allocation</c>) — storno qilingan
/// to'lovlarni hisobga olmaydigan qoida bilan. Pul formulasining ikkinchi
/// nusxasini yozish — ular yozilgan kuni bir xil bo'ladi va biri tuzatilgan
/// kuni ajralib ketadi.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[Route("api/cash/students")]
[Produces("application/json")]
public sealed class CashierStudentsController(AppDbContext db) : ControllerBase
{
    /// <summary>
    /// Qidiruv satrining eng qisqa uzunligi. Bir harf butun maktab ro'yxatini
    /// qaytarardi — kassirga kerak emas va shaxsiy ma'lumotni keraksiz yoyadi.
    /// </summary>
    private const int MinQueryLength = 2;

    /// <summary>Bir so'rovda qaytadigan eng ko'p qator.</summary>
    private const int MaxRows = 25;

    /// <summary>
    /// Ismi, ota-onasi yoki telefoni bo'yicha o'quvchi qidirish.
    /// Arxivlangan o'quvchilar chiqmaydi — ular to'lov qabul qilmaydi.
    /// </summary>
    /// <param name="q">Qidiruv satri (kamida 2 belgi). Qisqa bo'lsa — bo'sh ro'yxat.</param>
    [HttpGet]
    [FinanceRole(FinanceAction.AcceptPayment)]
    public async Task<ActionResult<IEnumerable<CashierStudentDto>>> Search(
        [FromQuery] string? q, CancellationToken ct)
    {
        var term = (q ?? string.Empty).Trim();
        if (term.Length < MinQueryLength) return Ok(Array.Empty<CashierStudentDto>());

        // `ToLower().Contains(...)` — provayderdan mustaqil (EF uni `lower(x) like '%…%'`
        // ga o'giradi). Npgsql'ning `ILike` i qisqaroq bo'lardi, lekin u paket Server
        // loyihasiga faqat tranzitiv keladi va shu bitta qidiruv uchun bog'liqlikni
        // qattiqlashtirishga arzimaydi.
        var needle = term.ToLowerInvariant();

        var rows = await db.Students.AsNoTracking()
            .Where(s => !s.IsArchived
                        && (s.FullName.ToLower().Contains(needle)
                            || s.ParentFullName.ToLower().Contains(needle)
                            || s.ParentPhone.ToLower().Contains(needle)))
            .OrderBy(s => s.FullName)
            .Take(MaxRows)
            .Select(s => new CashierStudentDto(
                s.Id, s.FullName, s.ClassName, s.ParentFullName, s.ParentPhone))
            .ToListAsync(ct);

        return Ok(rows);
    }
}

/// <summary>
/// Kassa qidiruvi qaytaradigan MINIMAL o'quvchi kartochkasi: kassirga
/// "bu o'sha odammi" degan savolga javob berish uchun yetadigan maydonlar.
/// Balans, chegirma, manzil, login — yo'q.
/// </summary>
/// <param name="Id">O'quvchi id'si — to'lovda shu yuboriladi.</param>
/// <param name="FullName">O'quvchining F.I.SH.</param>
/// <param name="ClassName">Sinfi (bir xil ismlarni ajratish uchun).</param>
/// <param name="ParentFullName">Ota-ona F.I.SH — kassada aynan u turadi.</param>
/// <param name="ParentPhone">Ota-ona telefoni.</param>
public sealed record CashierStudentDto(
    string Id, string FullName, string ClassName, string ParentFullName, string ParentPhone);

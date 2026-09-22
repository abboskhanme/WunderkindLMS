using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Yangiliklar — "Savdo va marketing → Yangiliklar" ning admin API'si
/// (<c>docs/modules/sales-marketing.md</c> §5.4, vazifa SM-5).
///
/// <para>
/// <b>Ruxsat — <c>marketing</c>.</b> <c>leads</c> bu yerga to'g'ri kelmaydi
/// (yangilik lid emas), <c>settings</c> esa e'lon yozadigan odamga berish
/// uchun juda kuchli kalit (§6.4). <see cref="AdminPermAttribute"/> ning
/// odatdagi xulqi: xodim uchun GET har doim ochiq — boshqa bo'lim ekrani
/// yangiliklar ro'yxatini o'qiy olsin — YOZISH esa faqat <c>marketing</c>
/// claim'i bilan. §8.1 ning RBAC jadvali aynan shunga tayanadi.
/// </para>
///
/// <para>
/// <b>Bu controller "Xabarlar → E'lon" ning o'rnini bosmaydi</b> (§3.3 N1).
/// <c>Broadcast</c> — bitta sinfning ota-onalariga mail-merge xabar
/// (qarzdorlik summasi bilan); yangilik esa butun maktabga, auditoriya
/// bo'yicha va DOIMIY. Ikkovi ikki xil vazifa, ikkovi ham qoladi.
/// </para>
///
/// <para>
/// <b>Rasm bu yerdan YUKLANMAYDI.</b> Banner mavjud <c>UploadsController</c>
/// (<c>POST /api/admin/uploads</c>, <c>UploadGuard</c> bilan) orqali
/// yuklanadi va bu yerga faqat qaytgan <c>/uploads/...</c> manzili keladi —
/// <see cref="CertificatesController"/> dagi sabab bilan bir xil.
/// </para>
///
/// <para>
/// <b>O'QISH endpoint'lari (§5.5) bu yerda YO'Q — ular SM-6 niki.</b> Lenta
/// (<c>GET /api/admin/news/feed</c>, portal va Mini App) alohida vazifa.
/// Shu sababdan bu fayldagi id bo'yicha marshrutlar <c>{id:guid}</c> bilan
/// cheklangan: <c>feed</c> so'zi ularga TUSHMAYDI, ya'ni SM-6 o'z
/// action'ini qo'shganda marshrut to'qnashuvi bo'lmaydi.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("marketing")]
[Route("api/admin/news")]
public class NewsController(AppDbContext db, AuditService audit, INewsTelegramNotifier telegram) : ControllerBase
{
    /// <summary>Joriy foydalanuvchi (JWT'dan) — <c>news.author_id</c> uchun.</summary>
    private string Uid =>
        User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value ?? "";

    /* ===================================================================
     *  1. O'qish
     * ================================================================ */

    /// <summary>
    /// Ro'yxat, yangisi tepada (§5.4). <paramref name="state"/> —
    /// <c>all | draft | published | archived</c>; <c>archived</c> yumshoq
    /// o'chirilganlar (§3.3 N8), qolgan uchtasida ular ko'rinmaydi.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<NewsListResultDto>> GetAll(
        [FromQuery] string? state = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = NewsService.DefaultPageSize,
        CancellationToken ct = default)
        => await NewsService.ListAsync(db, state, page, pageSize, ct);

    /// <summary>
    /// Bitta yangilik — tahrirlash formasi uchun.
    ///
    /// <para>
    /// <b>Arxivdagi qator ham qaytadi</b> (o'chirilganlar bu yerda
    /// filtrlanmaydi): ekran "Arxiv" ro'yxatidan ochilgan yozuvni FAQAT
    /// O'QISH uchun ko'rsatadi, va o'chirilgan e'lon nima deganini
    /// ko'rsatmaslik — §3.3 N8 ning butun maqsadini yo'qqa chiqarardi.
    /// Yozish amallari esa arxivdagi qatorda ishlamaydi (404).
    /// </para>
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<NewsAdminDto>> Get(Guid id, CancellationToken ct = default)
    {
        var row = await db.News.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, ct);
        if (row is null) return NotFound(new { message = NewsService.NotFoundMessage });
        return NewsService.ToDto(row);
    }

    /* ===================================================================
     *  2. Yozish
     * ================================================================ */

    /// <summary>
    /// Yangi yangilik — HAR DOIM qoralama (§3.3 N3). E'lon qilish alohida
    /// qadam, chunki u haqiqiy odamlarga haqiqiy xabar yuboradi.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<NewsAdminDto>> Create(
        NewsSaveRequest req, CancellationToken ct = default)
    {
        if (NewsService.Validate(req) is { } error) return ValidationError(error);

        var row = NewsService.Create(db, audit, req, Uid, await AuthorNameAsync(ct));
        await db.SaveChangesAsync(ct);

        return NewsService.ToDto(row);
    }

    /// <summary>
    /// Tahrirlash. E'lon qilingan yangilikda sarlavha va matnni o'zgartirish
    /// mumkin, AUDITORIYANI esa yo'q — 409 <c>news_published</c> (§5.4):
    /// ketgan Telegram xabarini qaytarib bo'lmaydi, ya'ni "endi faqat
    /// xodimlarga" degan o'zgarish ota-onalarning telefonidagi nusxani
    /// joyida qoldirardi.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<NewsAdminDto>> Update(
        Guid id, NewsSaveRequest req, CancellationToken ct = default)
    {
        var row = await LiveAsync(id, ct);
        if (row is null) return NotFound(new { message = NewsService.NotFoundMessage });

        if (NewsService.Validate(req) is { } error) return ValidationError(error);

        if (row.PublishedAt is not null && NewsService.AudienceChanged(row, req))
            return Conflict(new { code = "news_published", message = NewsService.AudienceLockedMessage });

        NewsService.Update(audit, row, req);
        await db.SaveChangesAsync(ct);

        return NewsService.ToDto(row);
    }

    /// <summary>
    /// E'lon qilish: lentaga chiqaradi va (so'ralgan bo'lsa) Telegram
    /// tarqatmasini yuboradi, natijani qatorga yozadi (§3.3 N4).
    ///
    /// <para>
    /// <b>Ikkinchi marta chaqirilsa — 409 <c>news_already_published</c></b>
    /// (§5.4), 200 emas: "qayta e'lon qilish" bitta ota-onaga ikkita bir xil
    /// xabar yuborardi. E'londan qaytarib, keyin qayta e'lon qilish esa —
    /// adminning ATAYLAB qilgan ikki qadami, ekran buni tasdiq oynasida
    /// aytadi.
    /// </para>
    /// <para>
    /// Tana kelmasa yoki <c>sendTelegram</c> berilmasa — sukut bo'yicha
    /// <c>true</c> (§3.3 N4: composer'da belgi yoqiq turadi).
    /// </para>
    /// </summary>
    [HttpPost("{id:guid}/publish")]
    public async Task<ActionResult<NewsAdminDto>> Publish(
        Guid id, [FromBody] NewsPublishRequest? req = null, CancellationToken ct = default)
    {
        var row = await LiveAsync(id, ct);
        if (row is null) return NotFound(new { message = NewsService.NotFoundMessage });

        if (row.PublishedAt is not null)
            return Conflict(new { code = "news_already_published", message = NewsService.AlreadyPublishedMessage });

        // Tekshiruvdan keyin boshqa so'rov ulgurib qolgan bo'lsa ham — bitta tarqatma (atomar egallash).
        var delivery = await NewsService.PublishAsync(db, audit, row, req?.SendTelegram ?? true, telegram, ct);
        if (delivery is null)
            return Conflict(new { code = "news_already_published", message = NewsService.AlreadyPublishedMessage });

        return NewsService.ToDto(row);
    }

    /// <summary>
    /// E'londan qaytarish: lentadan yashiradi. Telegram hisoblagichlari
    /// TEGILMAYDI — ketgan xabarni qaytarib bo'lmaydi va tarix buni
    /// yashirmasligi kerak (§3.3 N3).
    ///
    /// <para>
    /// Qoralamada chaqirilsa — 200 va qator O'ZGARMAYDI: ekranda ikki
    /// odam ishlayotganda "allaqachon qaytarilgan" holati xato emas, va
    /// unga audit qatori yozish jurnalni hech nima demaydigan satrlar
    /// bilan to'ldirardi.
    /// </para>
    /// </summary>
    [HttpPost("{id:guid}/unpublish")]
    public async Task<ActionResult<NewsAdminDto>> Unpublish(Guid id, CancellationToken ct = default)
    {
        var row = await LiveAsync(id, ct);
        if (row is null) return NotFound(new { message = NewsService.NotFoundMessage });

        if (row.PublishedAt is null) return NewsService.ToDto(row);

        NewsService.Unpublish(audit, row);
        await db.SaveChangesAsync(ct);

        return NewsService.ToDto(row);
    }

    /// <summary>
    /// YUMSHOQ o'chirish (§3.3 N8): <c>deleted_at</c> qo'yiladi, qator
    /// qoladi. Yangilik barcha lentalardan yo'qoladi, admin esa uni
    /// <c>state=archived</c> bilan o'qiy oladi.
    ///
    /// <para>
    /// Topilmagan yoki allaqachon arxivlangan qator uchun ham <b>204</b>:
    /// o'chirishni takrorlash xato emas (bir xil naqsh —
    /// <c>ArchiveReasonsController.Delete</c>).
    /// </para>
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        var row = await LiveAsync(id, ct);
        if (row is null) return NoContent();

        NewsService.SoftDelete(audit, row);
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    /* ===================================================================
     *  3. Yordamchilar
     * ================================================================ */

    /// <summary>
    /// Yozish amallari uchun qator: arxivlangani YO'Q. Kuzatuvli
    /// (tracking) — qaytgan obyekt shu yerda o'zgartiriladi.
    /// </summary>
    private Task<NewsItem?> LiveAsync(Guid id, CancellationToken ct) =>
        db.News.FirstOrDefaultAsync(n => n.Id == id && n.DeletedAt == null, ct);

    /// <summary>
    /// §5.6 dagi <c>validation</c> — har doim <c>{ code, message }</c>,
    /// o'zbekcha jumla bilan. Klient shartli mantiqni <c>code</c> ga
    /// qarab quradi (<c>newsErrorCode</c>), odamga esa <c>message</c>
    /// ko'rinadi.
    /// </summary>
    private BadRequestObjectResult ValidationError(string message) =>
        BadRequest(new { code = "validation", message });

    /// <summary>
    /// Muallif ismi — <c>news.author_name</c> uchun NUSXA (§4.3).
    /// Odatda JWT'dagi ism yetadi; claim biror sababga ko'ra bo'lmasa,
    /// bazadan o'qiladi, chunki bu qiymat qatorda MANGU qoladi va
    /// "Noma'lum" deb yozib qo'yish — tarixni buzish.
    /// </summary>
    private async Task<string> AuthorNameAsync(CancellationToken ct)
    {
        var claim = User.FindFirst(ClaimTypes.Name)?.Value;
        if (!string.IsNullOrWhiteSpace(claim)) return claim;

        return await db.Users.AsNoTracking()
            .Where(u => u.Id == Uid)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync(ct) ?? "Noma'lum";
    }

    /// <summary>
    /// Xodimlar lentasi admin panel uchun (sales-marketing.md §5.5). Id marshrutlari
    /// <c>{id:guid}</c> bilan cheklangan, shuning uchun <c>feed</c> ularga tushmaydi.
    /// </summary>
    [HttpGet("feed")]
    public async Task<ActionResult<IReadOnlyList<NewsFeedDto>>> Feed([FromQuery] int? take, CancellationToken ct) =>
        Ok(await NewsFeedQuery.ListAsync(db, NewsFeedAudience.Employee, take, ct));
}

using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

// ===========================================================================
//  Telegram Mini App — kirish va akkaunt bog'lash. SPEC §6 Faza 3.
// ===========================================================================
//
//  UCHTA QOIDA
//  -----------
//  1. `initData` IMZOSI TEKSHIRILMAGUNCHA undagi hech narsaga ishonilmaydi.
//     Foydalanuvchi id'si ham, ismi ham — hammasi imzodan KEYIN o'qiladi
//     (`TelegramInitData.Validate`). Imzo mos kelmasa 401 va tamom.
//
//  2. TOKENNI SHU CONTROLLER YASAMAYDI. Muvaffaqiyatli tekshiruvdan keyin
//     `SessionFactory.IssueAsync` chaqiriladi — `AuthController.Login` ham
//     aynan o'sha metodni chaqiradi. Ya'ni Mini App tokeni oddiy login
//     tokeni bilan BIR XIL (imzo, muddat, da'volar), va mavjud barcha
//     endpointlar hech qanday o'zgarishsiz ishlayveradi.
//
//  3. BOT TOKENI SIZIB CHIQMAYDI. U `SchoolMeta` dan `TelegramService` ga
//     yuklanadi (startupda) va bu yerda faqat HMAC hisoblash uchun
//     ishlatiladi. Javobda ham, logda ham, xato matnida ham ko'rinmaydi.
//
//  NEGA "BOG'LANMAGAN" 409, 401 EMAS
//  ---------------------------------
//  401 — "kimligingni bilmadim". Bu yerda esa kimligi ANIQ (Telegram uni
//  imzolab tasdiqladi), faqat maktab bazasida bog'lanish yo'q. Frontend
//  uchun bu ikki holat butunlay boshqacha ekran: birida "qaytadan urinib
//  ko'ring", ikkinchisida "maktabdan kod oling". Shuning uchun 409 va
//  `{ code: "not_linked", telegram: {...} }` — ishlab turgan Mini App
//  (`schoollms.client/src/pages/miniapp/ui-tg/src/lib/session.jsx`) aynan
//  shu ikki belgidan birini kutadi.
//
//  BOG'LASH CHIPTASI (COOKIE) — NEGA KERAK
//  ---------------------------------------
//  Mini App `POST /api/tg/link` ga FAQAT kodni yuboradi, `initData` ni emas.
//  Kodning o'zi esa kimligini isbotlamaydi: uni eshitib qolgan har kim
//  yuborishi mumkin bo'lardi. Shuning uchun `auth` imzoni tekshirgach,
//  tasdiqlangan Telegram id'ni 15 daqiqalik HttpOnly cookie'ga yozib
//  qo'yadi (`tg_link`), `link` esa o'shani o'qiydi. Cookie JWT kaliti bilan
//  HMAC qilinadi va domen ajratgichga ega, ya'ni uni yasab bo'lmaydi va u
//  sessiya tokeni o'rniga ham ishlamaydi. Yo'li `/api/tg` bilan cheklangan.
//
//  `initData` ni tanada (yoki `X-Telegram-Init-Data` sarlavhasida) yuboradigan
//  mijoz uchun bu yo'l umuman kerak emas — u afzal va birinchi tekshiriladi.
// ===========================================================================

/// <summary>Telegram Mini App kirishi va bog'lanishi (`/api/tg`).</summary>
[ApiController]
[Route("api/tg")]
public sealed class TelegramAuthController(
    AppDbContext db,
    JwtTokenService jwt,
    JwtOptions jwtOptions,
    TelegramService telegram,
    ILogger<TelegramAuthController> logger) : ControllerBase
{
    /// <summary>`initData` ni tanadan tashqari shu sarlavhada ham yuborish mumkin.</summary>
    private const string InitDataHeader = "X-Telegram-Init-Data";

    private TelegramLinkService Links => new(db);

    /// <summary>
    /// Mini App kirishi. Imzo to'g'ri va Telegram akkaunti bog'langan bo'lsa —
    /// oddiy login bilan bir xil JWT.
    /// </summary>
    [HttpPost("auth")]
    [AllowAnonymous]
    [EnableRateLimiting("telegram")]
    public async Task<ActionResult<TgAuthResponse>> Auth(TgAuthRequest req, CancellationToken ct)
    {
        var check = Verify(req?.InitData ?? HeaderInitData());
        if (check.Error is not null) return check.Error;
        var tgUser = check.User!;

        var account = await Links.FindAsync(tgUser.Id, ct);
        if (account is null)
            return NotLinked(tgUser);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == account.UserId, ct);
        if (user is null)
        {
            // Akkaunt o'chirilgan bo'lsa FK cascade bog'lanishni ham olib ketadi,
            // ya'ni bu holat amalda bo'lmasligi kerak. Bo'lsa — bog'lanmagan deb
            // ko'rsatamiz, 500 emas: foydalanuvchi kod bilan qaytadan bog'lay oladi.
            logger.LogWarning("Telegram bog'lanishi egasiz qoldi: account={AccountId}", account.Id);
            return NotLinked(tgUser);
        }

        if (await SessionFactory.IsBlockedAsync(db, user, ct))
            return Unauthorized(new { code = "account_blocked", message = "Akkaunt arxivlangan yoki to'xtatilgan" });

        account.LastSeenAt = AppClock.Now;
        account.DisplayName = tgUser.DisplayName;
        account.Username = tgUser.Username;

        // Bog'landi — endi chipta kerak emas.
        ClearLinkTicket();

        var session = await SessionFactory.IssueAsync(db, jwt, user, ct);
        return Ok(new TgAuthResponse("ok", session.Token, session.User, Profile(tgUser), null));
    }

    /// <summary>
    /// Bir martalik kod bilan bog'lash. Kod maktab admin panelidan chiqariladi
    /// (`POST /api/admin/telegram/link-codes`). Muvaffaqiyatda darrov token beriladi —
    /// foydalanuvchi ikkinchi marta kirishga majbur bo'lmaydi.
    ///
    /// <para>
    /// Kimligi uch manbadan, shu tartibda: so'rov tanasidagi <c>initData</c>,
    /// <c>X-Telegram-Init-Data</c> sarlavhasi, va <c>/api/tg/auth</c> qoldirgan
    /// bog'lash chiptasi (cookie). Hech biri bo'lmasa 401 — kodning o'zi
    /// kimligini isbotlamaydi.
    /// </para>
    /// </summary>
    [HttpPost("link")]
    [AllowAnonymous]
    [EnableRateLimiting("telegram")]
    public async Task<ActionResult<TgAuthResponse>> Link(TgLinkRequest req, CancellationToken ct)
    {
        var check = VerifyForLink(req);
        if (check.Error is not null) return check.Error;
        var tgUser = check.User!;

        var (result, user) = await Links.RedeemAsync(req!.Code, tgUser, ct);
        switch (result)
        {
            case LinkCodeResult.Invalid:
                // Kod noto'g'ri, muddati o'tgan va ishlatilgan holatlari BIR XIL javob
                // beradi: farq qilsak, kod topishga urinayotgan odam qaysi taxmini
                // "yaqin" ekanini bilib olardi.
                logger.LogWarning("Telegram bog'lash kodi rad etildi: tg={TelegramUserId}", tgUser.Id);
                return BadRequest(new { code = "invalid_code", message = "Kod noto'g'ri yoki muddati o'tgan" });

            case LinkCodeResult.TelegramAlreadyLinked:
                return Conflict(new
                {
                    code = "telegram_already_linked",
                    message = "Bu Telegram akkaunti allaqachon boshqa foydalanuvchiga bog'langan",
                });

            case LinkCodeResult.UserAlreadyLinked:
                return Conflict(new
                {
                    code = "user_already_linked",
                    message = "Bu foydalanuvchiga allaqachon boshqa Telegram akkaunti bog'langan",
                });
        }

        if (await SessionFactory.IsBlockedAsync(db, user!, ct))
            return Unauthorized(new { code = "account_blocked", message = "Akkaunt arxivlangan yoki to'xtatilgan" });

        ClearLinkTicket();

        var session = await SessionFactory.IssueAsync(db, jwt, user!, ct);
        return Ok(new TgAuthResponse("ok", session.Token, session.User, Profile(tgUser), null));
    }

    /// <summary>
    /// Mini App qobig'ining birinchi chaqiruvi: men kimman va nimani ko'raman.
    /// Ota-onaga farzandlar ro'yxati (almashtirgich), o'qituvchiga o'z profili.
    /// </summary>
    [HttpGet("me")]
    // `student` ham bor: ishlab turgan Mini App o'quvchi hisobini ham
    // ota-ona paneliga yo'naltiradi (ui-tg/src/App.jsx, PARENT_ROLES).
    [Authorize(Roles = "parent,teacher,student")]
    public async Task<ActionResult<TgProfileDto>> Me(CancellationToken ct)
    {
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (uid is null) return Unauthorized();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == uid, ct);
        if (user is null) return Unauthorized();

        var account = await db.TelegramAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.UserId == uid, ct);
        var schoolName = (await db.SchoolMeta.AsNoTracking().FirstOrDefaultAsync(ct))?.Name ?? "";

        var children = user.Role == "parent"
            ? await new GuardianAccess(db).ChildCardsAsync(uid, ct)
            : [];

        TeacherProfileDto? teacher = null;
        if (user.Role == Roles.Teacher)
        {
            var t = await db.Teachers.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == uid, ct);
            if (t is not null)
            {
                var subjects = (await db.Subjects.AsNoTracking()
                        .Where(s => t.SubjectIds.Contains(s.Id)).ToListAsync(ct))
                    .Select(s => new SubjectDto(s.Id, s.Name))
                    .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
                teacher = new TeacherProfileDto(
                    t.Id, t.FullName, user.Email, t.HomeroomClass, subjects, t.Permissions, t.PhotoUrl);
            }
        }

        // Bog'lanish yo'q bo'lishi mumkin: token web login orqali olingan bo'lsa
        // (`/api/auth/login`), Mini App qobig'i baribir ishlashi kerak.
        var telegramProfile = account is null
            ? new TgTelegramUserDto("", "", null)
            : new TgTelegramUserDto(account.TelegramUserId.ToString(), account.DisplayName, account.Username);

        return new TgProfileDto(user.Id, user.FullName, user.Role, schoolName, telegramProfile, children, teacher);
    }

    /// <summary>
    /// O'z Telegram bog'lanishini uzish. Shundan keyin Mini App yana
    /// <c>status: "unlinked"</c> qaytaradi va yangi kod kerak bo'ladi.
    /// </summary>
    [HttpDelete("link")]
    [Authorize]
    public async Task<IActionResult> Unlink(CancellationToken ct)
    {
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (uid is null) return Unauthorized();

        var account = await db.TelegramAccounts.FirstOrDefaultAsync(a => a.UserId == uid, ct);
        if (account is null) return NotFound(new { message = "Telegram bog'lanishi topilmadi" });

        db.TelegramAccounts.Remove(account);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ------------------------------------------------------------------
    //  Ichki
    // ------------------------------------------------------------------

    /// <summary>Tekshiruv natijasi: foydalanuvchi YOKI tayyor xato javobi (ikkalasidan biri null).</summary>
    private readonly record struct Check(TelegramWebAppUser? User, ActionResult? Error);

    /// <summary>
    /// `initData` ni tekshiradi. Xato bo'lsa mijozga UMUMIY xabar qaytariladi —
    /// qaysi bosqichda yiqilgani (imzo, muddat, foydalanuvchi) aytilmaydi, aks
    /// holda soxta `initData` yig'ayotgan odamga bepul teskari aloqa berilardi.
    /// Aniq sabab faqat serverda logga tushadi (tokensiz, hash'siz).
    /// </summary>
    private Check Verify(string? initData)
    {
        var result = TelegramInitData.Validate(initData, telegram.BotToken, DateTimeOffset.UtcNow);
        if (result.Ok) return new Check(result.User, null);

        if (result.Error == InitDataError.BotNotConfigured)
        {
            logger.LogError("Telegram bot tokeni sozlanmagan — Mini App kirishi ishlamaydi.");
            return new Check(null, StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                code = "telegram_not_configured",
                message = "Telegram bot sozlanmagan. Maktab ma'muriyatiga murojaat qiling.",
            }));
        }

        logger.LogWarning("Telegram initData rad etildi: sabab={Reason}", result.Error);
        return new Check(null, Unauthorized(new
        {
            code = "invalid_init_data",
            message = "Telegram ma'lumoti tasdiqlanmadi. Ilovani qaytadan oching.",
        }));
    }

    private static TgTelegramUserDto Profile(TelegramWebAppUser u) =>
        new(u.Id.ToString(), u.DisplayName, u.Username);

    /// <summary>
    /// 409 + <c>code: "not_linked"</c>, va yonida bog'lash chiptasi (cookie) —
    /// keyingi <c>POST /api/tg/link</c> faqat kod bilan kelishi mumkin.
    /// </summary>
    private ActionResult<TgAuthResponse> NotLinked(TelegramWebAppUser u)
    {
        IssueLinkTicket(u);
        return Conflict(new TgNotLinkedDto(
            "not_linked",
            "Telegram akkauntingiz maktab tizimiga bog'lanmagan. "
            + "Maktab ma'muriyatidan bir martalik kod oling va shu yerga kiriting.",
            Profile(u)));
    }

    /// <summary>Sarlavhadagi `initData` (tanada bo'lmasa).</summary>
    private string? HeaderInitData() =>
        Request.Headers.TryGetValue(InitDataHeader, out var value) ? value.ToString() : null;

    /// <summary>
    /// Bog'lash so'rovi uchun kimlikni aniqlaydi: tana → sarlavha → chipta.
    /// Chipta oxirgi, chunki u eng kam ma'lumot beradi (til, familiya yo'q).
    /// </summary>
    private Check VerifyForLink(TgLinkRequest? req)
    {
        var raw = req?.InitData;
        if (string.IsNullOrWhiteSpace(raw)) raw = HeaderInitData();
        if (!string.IsNullOrWhiteSpace(raw)) return Verify(raw);

        var ticket = ReadLinkTicket();
        if (ticket is not null) return new Check(ticket, null);

        logger.LogWarning("Telegram bog'lash so'rovida kimlik yo'q (initData ham, chipta ham).");
        return new Check(null, Unauthorized(new
        {
            code = "invalid_init_data",
            message = "Telegram ma'lumoti tasdiqlanmadi. Ilovani qaytadan oching.",
        }));
    }

    // ------------------------------------------------------------------
    //  Bog'lash chiptasi (cookie) — fayl boshidagi izohga qarang
    // ------------------------------------------------------------------

    private const string LinkTicketCookie = "tg_link";

    /// <summary>Chipta umri. Kodning umri bilan bir xil (`TelegramLinkService.CodeLifetime`).</summary>
    private static readonly TimeSpan LinkTicketLifetime = TimeSpan.FromMinutes(15);

    /// <summary>
    /// DOMEN AJRATGICHI. HMAC kaliti JWT bilan bir xil, shuning uchun imzolanadigan
    /// matn boshiga shu prefiks qo'yiladi: chipta hech qachon boshqa turdagi
    /// imzolangan qiymat sifatida qabul qilinmasin.
    /// </summary>
    private const string TicketPurpose = "tg-link-ticket\n";

    private void IssueLinkTicket(TelegramWebAppUser user)
    {
        var expires = DateTimeOffset.UtcNow.Add(LinkTicketLifetime);
        // Ajratgich '\n', shuning uchun nomdagi yangi qator bo'sh joyga almashtiriladi.
        var payload = string.Join('\n',
            user.Id.ToString(CultureInfo.InvariantCulture),
            user.DisplayName.Replace('\n', ' '),
            user.Username ?? "",
            expires.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));

        var value = Base64Url(Encoding.UTF8.GetBytes(payload)) + "." + Base64Url(SignTicket(payload));

        Response.Cookies.Append(LinkTicketCookie, value, new CookieOptions
        {
            HttpOnly = true,
            // Dev'da (http://localhost:8080) `Secure` cookie'ni brauzer ham,
            // curl ham yubormaydi — shuning uchun sxemaga qarab qo'yiladi.
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            // Faqat shu controller'ga boradi; boshqa endpointlar uni ko'rmaydi.
            Path = "/api/tg",
            Expires = expires,
            IsEssential = true,
        });
    }

    private void ClearLinkTicket() =>
        Response.Cookies.Delete(LinkTicketCookie, new CookieOptions { Path = "/api/tg" });

    /// <summary>Chipta yaroqli bo'lsa — undagi Telegram foydalanuvchisi, aks holda null.</summary>
    private TelegramWebAppUser? ReadLinkTicket()
    {
        if (!Request.Cookies.TryGetValue(LinkTicketCookie, out var raw) || string.IsNullOrEmpty(raw))
            return null;

        var dot = raw.IndexOf('.');
        if (dot <= 0 || dot == raw.Length - 1) return null;

        byte[] payloadBytes, signature;
        try
        {
            payloadBytes = FromBase64Url(raw[..dot]);
            signature = FromBase64Url(raw[(dot + 1)..]);
        }
        catch (FormatException) { return null; }

        var payload = Encoding.UTF8.GetString(payloadBytes);
        if (!CryptographicOperations.FixedTimeEquals(SignTicket(payload), signature)) return null;

        var parts = payload.Split('\n');
        if (parts.Length != 4) return null;
        if (!long.TryParse(parts[0], CultureInfo.InvariantCulture, out var id) || id <= 0) return null;
        if (!long.TryParse(parts[3], CultureInfo.InvariantCulture, out var expiresUnix)) return null;
        if (DateTimeOffset.FromUnixTimeSeconds(expiresUnix) < DateTimeOffset.UtcNow) return null;

        return new TelegramWebAppUser(
            id, parts[1], null, string.IsNullOrEmpty(parts[2]) ? null : parts[2], null);
    }

    private byte[] SignTicket(string payload) => HMACSHA256.HashData(
        Encoding.UTF8.GetBytes(jwtOptions.Key), Encoding.UTF8.GetBytes(TicketPurpose + payload));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}

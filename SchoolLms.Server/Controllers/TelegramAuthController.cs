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
    IConfiguration config,
    ILogger<TelegramAuthController> logger) : ControllerBase
{
    /// <summary>`initData` ni tanadan tashqari shu sarlavhada ham yuborish mumkin.</summary>
    private const string InitDataHeader = "X-Telegram-Init-Data";

    /// <summary>Logout from a keyboard-launched Mini App names its Telegram by the panel key.</summary>
    private const string PanelKeyHeader = "X-Telegram-Panel-Key";

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

        // An archived/stopped account: it leaves this Telegram (2026-10-07). Another saved account
        // takes over (2026-10-08); none — the link goes and the Telegram signs in to another one.
        if (await SessionFactory.IsBlockedAsync(db, user, ct))
        {
            await TelegramSavedAccounts.ForgetAsync(db, tgUser.Id, user.Id, ct);
            var next = await NextSavedAsync(tgUser.Id, ct);
            if (next is null)
            {
                db.TelegramAccounts.Remove(account);
                await db.SaveChangesAsync(ct);
                return NotLinked(tgUser, BlockedMessage);
            }
            account.UserId = next.Id;
            user = next;
        }

        account.LastSeenAt = AppClock.Now;
        account.DisplayName = tgUser.DisplayName;
        account.Username = tgUser.Username;
        await TelegramSavedAccounts.RememberAsync(db, tgUser.Id, user.Id, ct);

        // Bog'landi — endi chipta kerak emas.
        ClearLinkTicket();

        var session = await SessionFactory.IssueAsync(db, jwt, user, ct, tgUser.Id);
        return Ok(new TgAuthResponse("ok", session.Token, session.User, Profile(tgUser), null));
    }

    /// <summary>
    /// "🏫 Panelni ochish" keyboard button → session (client, 2026-09-28). Telegram gives a
    /// keyboard-launched Mini App no <c>initData</c>; the bot put a personal key in the button URL
    /// instead (<see cref="TelegramPanelKeys"/>). Unknown, replaced or expired key → 401
    /// <c>panel_key_invalid</c> ("/start" in the bot gives a fresh button).
    /// </summary>
    [HttpPost("auth/key")]
    [AllowAnonymous]
    [EnableRateLimiting("telegram")]
    public async Task<ActionResult<TgAuthResponse>> AuthByPanelKey(TgPanelKeyRequest req, CancellationToken ct)
    {
        var account = await TelegramPanelKeys.ResolveAsync(db, req?.Key, ct);
        var user = account is null ? null : await db.Users.FirstOrDefaultAsync(u => u.Id == account.UserId, ct);
        if (account is null || user is null)
            return Unauthorized(new { code = "panel_key_invalid", message = "Tugma eskirgan — botda /start ni bosing" });

        if (await SessionFactory.IsBlockedAsync(db, user, ct))
        {
            // The same as the initData path: another saved account takes over, else unlink with a ticket.
            await TelegramSavedAccounts.ForgetAsync(db, account.TelegramUserId, user.Id, ct);
            var next = await NextSavedAsync(account.TelegramUserId, ct);
            if (next is null)
            {
                var leaving = new TelegramWebAppUser(account.TelegramUserId, account.DisplayName, null, account.Username, null);
                db.TelegramAccounts.Remove(account);
                await db.SaveChangesAsync(ct);
                return NotLinked(leaving, BlockedMessage);
            }
            account.UserId = next.Id;
            user = next;
        }

        account.LastSeenAt = AppClock.Now;
        await TelegramSavedAccounts.RememberAsync(db, account.TelegramUserId, user.Id, ct);
        var session = await SessionFactory.IssueAsync(db, jwt, user, ct, account.TelegramUserId);
        return Ok(new TgAuthResponse("ok", session.Token, session.User,
            new TgTelegramUserDto(account.TelegramUserId.ToString(), account.DisplayName, account.Username), null));
    }

    /// <summary>
    /// Just linked in the Mini App → the bot sends the "🏫 Panelni ochish" keyboard, so from now on
    /// the panel opens from the chat. Never fails the sign-in: a send error is only logged.
    /// </summary>
    /// <returns>The new button's key, or null when the bot could not send it.</returns>
    private async Task<string?> SendPanelKeyboardAsync(long telegramUserId, CancellationToken ct)
    {
        try
        {
            var account = await db.TelegramAccounts.FirstOrDefaultAsync(a => a.TelegramUserId == telegramUserId, ct);
            if (account is not null)
                return await TelegramPanelKeys.SendKeyboardWithKeyAsync(db, telegram, TelegramPanelKeys.MiniAppUrl(config), account,
                    "✅ Hisobingiz ulandi. Endi panelni pastdagi «🏫 Panelni ochish» tugmasi orqali ochasiz.", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Panel tugmasi yuborilmadi: tg={TelegramUserId}", telegramUserId);
        }
        return null;
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
    /// <summary>
    /// Birinchi kirish login va parol bilan (mijoz, 2026-09-25: "telegramdan kirsa ham login paroli saqlanadimi
    /// ... har safar parol termasligi kerak"). Parol to'g'ri bo'lsa, Telegram akkaunti shu foydalanuvchiga
    /// bog'lanadi va token beriladi; keyingi ochilishlarda <c>/api/tg/auth</c> uni imzo bo'yicha o'zi taniydi.
    /// Kimligi — imzolangan <c>initData</c> (yoki bog'lash chiptasi); parol taxminiga qarshi <c>login</c> chegarasi.
    /// </summary>
    [HttpPost("link-login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<TgAuthResponse>> LinkByLogin(TgLoginLinkRequest req, CancellationToken ct)
    {
        var check = VerifyForLink(req is null ? null : new TgLinkRequest("", req.InitData));
        if (check.Error is not null) return check.Error;
        var tgUser = check.User!;

        var login = (req!.Login ?? "").Trim();
        // Case, spaces and apostrophes do not matter; "Familiya Ism" finds the login (LoginLookup).
        var user = await LoginLookup.FindAsync(db, login, ct);
        if (user is null || !PasswordHasher.Verify(req.Password ?? "", user.PasswordHash))
        {
            logger.LogWarning("Telegram orqali muvaffaqiyatsiz login: login={Login}, tg={TelegramUserId}", login, tgUser.Id);
            return Unauthorized(new { code = "invalid_credentials", message = "Login yoki parol noto'g'ri" });
        }

        if (await SessionFactory.IsBlockedAsync(db, user, ct))
            return Unauthorized(new { code = "account_blocked", message = "Akkaunt arxivlangan yoki to'xtatilgan" });

        switch (await Links.LinkDirectAsync(user, tgUser, ct))
        {
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
                    message = "Bu foydalanuvchiga boshqa Telegram akkaunti bog'langan. Almashtirish uchun maktab administratoriga murojaat qiling.",
                });
        }

        ClearLinkTicket();
        await TelegramSavedAccounts.RememberAsync(db, tgUser.Id, user.Id, ct);
        var session = await SessionFactory.IssueAsync(db, jwt, user, ct, tgUser.Id);
        var key = await SendPanelKeyboardAsync(tgUser.Id, ct);
        // Linked from a keyboard-launched app (no initData): hand it the new button's key, so it can
        // sign in again by itself when the token expires (2026-10-07).
        var keyless = string.IsNullOrWhiteSpace(req.InitData) && string.IsNullOrWhiteSpace(HeaderInitData());
        return Ok(new TgAuthResponse("ok", session.Token, session.User, Profile(tgUser), null, keyless ? key : null));
    }

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

        var session = await SessionFactory.IssueAsync(db, jwt, user!, ct, tgUser.Id);
        await SendPanelKeyboardAsync(tgUser.Id, ct);
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
        var meta = await db.SchoolMeta.AsNoTracking().FirstOrDefaultAsync(ct);
        var schoolName = meta?.Name ?? "";
        // §5.5 — bayroq FAQAT ota-onani yopadi; o'quvchi o'z bahosini ko'raveradi.
        var showProgress = user.Role != "parent"
            || (meta?.ShowLearningProgressInParentDashboard ?? true);

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

        return new TgProfileDto(
            user.Id, user.FullName, user.Role, schoolName, telegramProfile, children, teacher, showProgress);
    }

    /// <summary>
    /// O'z Telegram bog'lanishini uzish. Shundan keyin Mini App yana
    /// <c>status: "unlinked"</c> qaytaradi va yangi kod kerak bo'ladi.
    ///
    /// <para>
    /// Mini App'ning "Chiqish" tugmasi <c>X-Telegram-Init-Data</c> sarlavhasini
    /// yuboradi — shunda FAQAT shu Telegram akkaunt uziladi: bitta hisobga ota
    /// ham, ona ham bog'langan bo'lishi mumkin, otaning chiqishi onani chiqarib
    /// yubormasligi kerak. Sarlavhasiz chaqiruv — avvalgidek hammasini uzadi.
    /// </para>
    /// </summary>
    [HttpDelete("link")]
    [Authorize]
    public async Task<IActionResult> Unlink(CancellationToken ct)
    {
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (uid is null) return Unauthorized();

        var query = db.TelegramAccounts.Where(a => a.UserId == uid);
        // Which Telegram is leaving — known for a Mini App session; then a link ticket lets the same
        // Telegram sign in to another account right away (keyboard launches carry no initData).
        long? leaving = null;

        var raw = HeaderInitData();
        if (long.TryParse(User.FindFirst(JwtTokenService.TelegramClaim)?.Value, CultureInfo.InvariantCulture, out var fromSession))
        {
            // The session itself says which Telegram it was opened from (2026-10-07): exact, and it
            // does not fail when the initData is older than 24 h or the bot has replaced the panel key —
            // before, such a "Chiqish" was refused and the next open landed in the old account again.
            leaving = fromSession;
            query = query.Where(a => a.TelegramUserId == fromSession);
        }
        else if (!string.IsNullOrWhiteSpace(raw))
        {
            // Imzo tekshirilmasa — hech narsa uzilmaydi. "Hammasini uzish" ga
            // tushib ketish boshqa oila a'zosini jimgina chiqarib yuborardi.
            // 401 emas, 400: sessiya tokeni to'g'ri, eskirgani — Telegram ma'lumoti.
            var check = TelegramInitData.Validate(raw, telegram.BotToken, DateTimeOffset.UtcNow);
            if (!check.Ok)
                return BadRequest(new
                {
                    code = "invalid_init_data",
                    message = "Telegram ma'lumoti eskirgan. Ilovani yopib, qaytadan oching.",
                });
            var telegramUserId = check.User!.Id;
            leaving = telegramUserId;
            query = query.Where(a => a.TelegramUserId == telegramUserId);
        }
        else if (Request.Headers[PanelKeyHeader].ToString() is { Length: > 0 } panelKey)
        {
            // Opened from the "🏫 Panelni ochish" button (no initData): the key says WHICH Telegram
            // is leaving — the same rule as above, never "unlink them all".
            var keyAccount = await TelegramPanelKeys.ResolveAsync(db, panelKey, ct);
            if (keyAccount is null || keyAccount.UserId != uid)
                return BadRequest(new { code = "panel_key_invalid", message = "Tugma eskirgan — botda /start ni bosing" });
            var keyId = keyAccount.Id;
            leaving = keyAccount.TelegramUserId;
            query = query.Where(a => a.Id == keyId);
        }

        var accounts = await query.ToListAsync(ct);
        foreach (var gone in accounts)
            await TelegramSavedAccounts.ForgetAsync(db, gone.TelegramUserId, uid, ct);

        // Several accounts on this Telegram (2026-10-08): leaving one opens the next saved one — the
        // row (and its panel key) stays, only its account changes. The last one leaves for real.
        if (leaving is { } staying && accounts.Count > 0 && await NextSavedAsync(staying, ct) is { } next)
        {
            var row = accounts[0];
            row.UserId = next.Id;
            row.LastSeenAt = AppClock.Now;
            db.TelegramAccounts.RemoveRange(accounts.Skip(1));
            await TelegramSavedAccounts.RememberAsync(db, staying, next.Id, ct);
            var switched = await SessionFactory.IssueAsync(db, jwt, next, ct, staying);
            return Ok(new { ticket = (string?)null, token = switched.Token, user = switched.User });
        }

        string? ticket = null;
        if (leaving is { } tg)
        {
            var gone = accounts.FirstOrDefault();
            ticket = IssueLinkTicket(new TelegramWebAppUser(tg, gone?.DisplayName ?? "", null, gone?.Username, null));
        }
        if (accounts.Count == 0) return NotFound(new { message = "Telegram bog'lanishi topilmadi", ticket });

        db.TelegramAccounts.RemoveRange(accounts);
        await db.SaveChangesAsync(ct);
        // The ticket is in the body too: inside Telegram Web the app is a cross-site iframe and the
        // cookie may not come back with the next request (2026-10-07).
        return ticket is null ? NoContent() : Ok(new { ticket });
    }

    // ------------------------------------------------------------------
    //  Several accounts on one Telegram (client, 2026-10-08) — TelegramSavedAccounts.cs
    // ------------------------------------------------------------------

    /// <summary>The accounts this Telegram can switch to — the active one first.</summary>
    [HttpGet("accounts")]
    [Authorize]
    public async Task<ActionResult<TgAccountsDto>> Accounts(CancellationToken ct)
    {
        if (SessionTelegram() is not { } tg) return TelegramOnly();
        var link = await db.TelegramAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.TelegramUserId == tg, ct);
        var active = link?.UserId ?? CurrentUserId();
        if (active is not null && !await db.TelegramSavedAccounts.AnyAsync(s => s.TelegramUserId == tg && s.UserId == active, ct))
            await TelegramSavedAccounts.RememberAsync(db, tg, active, ct);

        var saved = await TelegramSavedAccounts.ListAsync(db, tg, ct);
        var ids = saved.Select(s => s.UserId).ToList();
        var users = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToListAsync(ct);
        var usable = new List<AppUser>();
        foreach (var s in saved)
        {
            if (users.FirstOrDefault(u => u.Id == s.UserId) is not { } u) continue;
            if (await SessionFactory.IsBlockedAsync(db, u, ct))
            {
                // Archived since it was added — it drops out of the list for good.
                await TelegramSavedAccounts.ForgetAsync(db, tg, u.Id, ct);
                continue;
            }
            usable.Add(u);
        }
        var subtitles = await TelegramSavedAccounts.SubtitlesAsync(db, usable, ct);
        return new TgAccountsDto([.. usable
            .Select(u => new TgSavedAccountDto(u.Id, u.FullName, u.Role, subtitles[u.Id], u.Id == active))
            .OrderByDescending(a => a.Active)]);
    }

    /// <summary>
    /// Adds an account with ITS OWN login and password and switches to it; the current one stays in the
    /// list. Each account is proven once — switching back and forth needs no password.
    /// </summary>
    [HttpPost("accounts")]
    [Authorize]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<TgAuthResponse>> AddAccount(TgAddAccountRequest req, CancellationToken ct)
    {
        if (SessionTelegram() is not { } tg) return TelegramOnly();
        var login = (req?.Login ?? "").Trim();
        var user = await LoginLookup.FindAsync(db, login, ct);
        if (user is null || !PasswordHasher.Verify(req?.Password ?? "", user.PasswordHash))
        {
            logger.LogWarning("Telegram'ga akkaunt qo'shish rad etildi: login={Login}, tg={TelegramUserId}", login, tg);
            // 400, not 401: the caller IS signed in — a 401 would read as "session expired" and reload the app.
            return BadRequest(new { code = "invalid_credentials", message = "Login yoki parol noto'g'ri" });
        }
        if (await SessionFactory.IsBlockedAsync(db, user, ct))
            return Conflict(new { code = "account_blocked", message = "Akkaunt arxivlangan yoki to'xtatilgan" });

        if (CurrentUserId() is { } current) await TelegramSavedAccounts.RememberAsync(db, tg, current, ct);
        return await SwitchToAsync(tg, user, ct);
    }

    /// <summary>Switches this Telegram to one of its saved accounts — no password.</summary>
    [HttpPost("accounts/{userId}/switch")]
    [Authorize]
    public async Task<ActionResult<TgAuthResponse>> SwitchAccount(string userId, CancellationToken ct)
    {
        if (SessionTelegram() is not { } tg) return TelegramOnly();
        if (!await db.TelegramSavedAccounts.AnyAsync(s => s.TelegramUserId == tg && s.UserId == userId, ct))
            return NotFound(new { code = "account_not_saved", message = "Bu akkaunt shu Telegram'da saqlanmagan — login va parol bilan qo'shing." });
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null || await SessionFactory.IsBlockedAsync(db, user, ct))
        {
            await TelegramSavedAccounts.ForgetAsync(db, tg, userId, ct);
            return Conflict(new { code = "account_blocked", message = "Akkaunt arxivlangan yoki to'xtatilgan — ro'yxatdan olib tashlandi." });
        }
        return await SwitchToAsync(tg, user, ct);
    }

    /// <summary>Drops a saved account from this Telegram's list. The active one leaves through "Chiqish".</summary>
    [HttpDelete("accounts/{userId}")]
    [Authorize]
    public async Task<IActionResult> ForgetAccount(string userId, CancellationToken ct)
    {
        if (SessionTelegram() is not { } tg) return TelegramOnly();
        var link = await db.TelegramAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.TelegramUserId == tg, ct);
        if ((link?.UserId ?? CurrentUserId()) == userId)
            return Conflict(new { code = "active_account", message = "Hozirgi akkauntdan «Chiqish» tugmasi orqali chiqiladi." });
        await TelegramSavedAccounts.ForgetAsync(db, tg, userId, ct);
        return NoContent();
    }

    /// <summary>The Telegram's link now opens <paramref name="user"/>; a fresh session for it.</summary>
    private async Task<ActionResult<TgAuthResponse>> SwitchToAsync(long tg, AppUser user, CancellationToken ct)
    {
        var link = await db.TelegramAccounts.FirstOrDefaultAsync(a => a.TelegramUserId == tg, ct);
        if (link is null)
            return Unauthorized(new { code = "not_linked", message = "Telegram bog'lanishi topilmadi — ilovani qaytadan oching." });
        link.UserId = user.Id;
        link.LastSeenAt = AppClock.Now;
        await TelegramSavedAccounts.RememberAsync(db, tg, user.Id, ct);
        var session = await SessionFactory.IssueAsync(db, jwt, user, ct, tg);
        return Ok(new TgAuthResponse("ok", session.Token, session.User,
            new TgTelegramUserDto(tg.ToString(CultureInfo.InvariantCulture), link.DisplayName, link.Username), null));
    }

    /// <summary>The most recently used saved account that can still sign in; archived ones are dropped.</summary>
    private async Task<AppUser?> NextSavedAsync(long telegramUserId, CancellationToken ct)
    {
        foreach (var saved in await TelegramSavedAccounts.ListAsync(db, telegramUserId, ct))
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == saved.UserId, ct);
            if (user is not null && !await SessionFactory.IsBlockedAsync(db, user, ct)) return user;
            await TelegramSavedAccounts.ForgetAsync(db, telegramUserId, saved.UserId, ct);
        }
        return null;
    }

    /// <summary>The Telegram a Mini App session was opened from (<see cref="JwtTokenService.TelegramClaim"/>).</summary>
    private long? SessionTelegram() =>
        long.TryParse(User.FindFirst(JwtTokenService.TelegramClaim)?.Value, CultureInfo.InvariantCulture, out var tg) ? tg : null;

    private string? CurrentUserId() => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    private BadRequestObjectResult TelegramOnly() =>
        BadRequest(new { code = "telegram_only", message = "Akkaunt almashtirish faqat Telegram ilovasida ishlaydi." });

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
    private const string BlockedMessage =
        "Avval ulangan akkaunt arxivlangan yoki to'xtatilgan. Maktab bergan boshqa login va parol bilan kiring.";

    private ActionResult<TgAuthResponse> NotLinked(TelegramWebAppUser u, string? message = null)
    {
        var ticket = IssueLinkTicket(u);
        return Conflict(new TgNotLinkedDto(
            "not_linked",
            message ?? "Telegram akkauntingiz maktab tizimiga bog'lanmagan. Maktab bergan login va parol bilan kiring.",
            Profile(u), ticket));
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

    /// <summary>Also accepted from this header — the cookie may not reach a cross-site iframe (Telegram Web).</summary>
    private const string LinkTicketHeader = "X-Telegram-Link-Ticket";

    /// <returns>The ticket value (also set as the cookie).</returns>
    private string IssueLinkTicket(TelegramWebAppUser user)
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
        return value;
    }

    private void ClearLinkTicket() =>
        Response.Cookies.Delete(LinkTicketCookie, new CookieOptions { Path = "/api/tg" });

    /// <summary>Chipta yaroqli bo'lsa — undagi Telegram foydalanuvchisi, aks holda null.</summary>
    private TelegramWebAppUser? ReadLinkTicket()
    {
        if (!Request.Cookies.TryGetValue(LinkTicketCookie, out var raw) || string.IsNullOrEmpty(raw))
            raw = Request.Headers[LinkTicketHeader].ToString();
        if (string.IsNullOrEmpty(raw)) return null;

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

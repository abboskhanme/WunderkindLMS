using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;
using System.Security.Claims;
using SchoolLms.Domain;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Hubs;
using SchoolLms.Application.Services;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Server.Controllers;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// So'rov yo'lidagi BARCHA DbContext shu satr bilan ishlaydi. Prodda bu `app_rw` —
// hech narsaga ega bo'lmagan, shuning uchun `REVOKE` unga haqiqatan ta'sir qiladigan rol
// (SPEC §4.1). Jadval EGASI `REVOKE` ni chetlab o'tadi, shuning uchun bu ajratish shart.
var defaultConn = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default sozlanmagan.");

// Migratsiya ALOHIDA rol bilan bajariladi: `Migrator` = sxema egasi (schoollms_owner).
// Berilmagan bo'lsa — `Default` ga qaytadi, faqat dev qulayligi uchun.
// PRODDA IKKALASI HAM BERILISHI SHART: agar `Migrator` tushib qolsa, ilova o'z roli bilan
// migratsiya qilishga urinadi va `app_rw` da DDL huquqi yo'qligi uchun BALAND xato beradi —
// jimgina himoyasiz ishlab ketmaydi. Tafsilot: deploy/README.md.
var migratorConn = builder.Configuration.GetConnectionString("Migrator");

// Apex (asosiy domen) → landing sahifa; subdomen → ilova (SPA). Faqat shu uchun root domen kerak.
var rootDomains = (builder.Configuration["Tenancy:RootDomain"] ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

// ---------- Xizmatlar ----------
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(defaultConn,
            npg =>
            {
                // Vaqtinchalik DB uzilishlarini avtomatik qayta urinish bilan chidaydi.
                npg.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorCodesToAdd: null);
                // Ko'p kolleksiyali Include'larni alohida so'rovlarga ajratadi — kartezian portlashning oldini oladi.
                npg.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            })
       // Jadval va ustun nomlari PostgreSQL uslubida: AppUser.FullName -> users.full_name
       .UseSnakeCaseNamingConvention());

// Application qatlamidagi xizmatlar konkret AppDbContext o'rniga IAppDbContext'ga
// bog'lanadi — uni o'sha scoped AppDbContext instansiyasiga ulaymiz.
builder.Services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

// Kam o'zgaradigan ma'lumotlar (meta, fan/o'qituvchi nomlari) uchun qisqa-TTL kesh.
builder.Services.AddMemoryCache();

// Taqsimlangan kesh. `ConnectionStrings:Redis` berilgan bo'lsa — Redis (konteynerlar/restartlar
// orasida saqlanadi); berilmasa — jarayon ichidagi xotira. YA'NI REDIS BO'LMASA HAM ILOVA
// ISHLAYDI (dev'da `cache` konteynerini ko'tarmaslik mumkin).
// Rollback: `ConnectionStrings__Redis` muhit o'zgaruvchisini olib tashlash kifoya — kod
// avtomatik xotira keshiga qaytadi, qayta build shart emas.
var redisConn = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConn))
{
    builder.Services.AddStackExchangeRedisCache(o =>
    {
        o.Configuration = redisConn;
        // Kalit prefiksi — bitta Redis instansiyasini boshqa ilova bilan bo'lishganda to'qnashmaydi.
        o.InstanceName = "wklms:";
    });
    Console.WriteLine($"[cache] Redis: {redisConn}");
}
else
{
    builder.Services.AddDistributedMemoryCache();
    Console.WriteLine("[cache] ConnectionStrings:Redis berilmagan — jarayon ichidagi xotira keshi.");
}

builder.Services.AddSingleton<ReferenceCache>();

// DataProtection kalitlarini DOIMIY volume'ga saqlaymiz. Aks holda kalitlar konteyner ichida
// (/root/.aspnet) turadi va HAR deploy'da yo'qoladi — natijada eski tokenlar/shifrlangan
// ma'lumotlar yaroqsiz bo'lib qoladi. /app/keys docker volume'iga ulangan (qarang docker-compose).
var keysDir = builder.Configuration["DataProtection:KeysPath"] ?? "/app/keys";
try { Directory.CreateDirectory(keysDir); } catch { /* dev'da yo'l bo'lmasligi mumkin — e'tiborsiz */ }
if (Directory.Exists(keysDir))
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keysDir))
        .SetApplicationName("SchoolLms");

// JWT sozlamalari. Imzo kaliti appsettings'da SAQLANMAYDI (repoga tushmasligi uchun) —
// uni `Jwt__Key` muhit o'zgaruvchisi yoki `dotnet user-secrets` orqali bering.
var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwtOptions.Key) || jwtOptions.Key.Length < 32)
{
    if (builder.Environment.IsDevelopment())
    {
        // Dev'da kalit berilmasa — vaqtinchalik tasodifiy kalit (server restartida tokenlar bekor bo'ladi).
        jwtOptions.Key = Convert.ToBase64String(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
        Console.WriteLine("[WARN] Jwt:Key berilmagan — DEV uchun vaqtinchalik tasodifiy kalit ishlatilmoqda. "
            + "Prod'da Jwt__Key muhit o'zgaruvchisini o'rnating.");
    }
    else
    {
        throw new InvalidOperationException(
            "Jwt:Key sozlanmagan yoki 32 belgidan qisqa. Uni `Jwt__Key` muhit o'zgaruvchisi "
            + "yoki user-secrets orqali bering (hech qachon appsettings.json'ga yozmang).");
    }
}
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<JwtTokenService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
        };

        // SignalR (WebSocket) token'ni Authorization header'da yubora olmaydi —
        // chat hub uchun tokenni query string'dan ("access_token") qabul qilamiz.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            },

            // Token bekor qilish (revocation): imzo/muddat to'g'ri bo'lsa ham, akkaunt holatini
            // HAR so'rovda tekshiramiz — arxivlangan o'qituvchi/o'quvchi yoki o'chirilgan xodim/admin
            // eski tokeni bilan KIRA OLMAYDI. Parent (telefon orqali bog'lanadi) tekshirilmaydi.
            OnTokenValidated = async context =>
            {
                var p = context.Principal;
                var userId = p?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                             ?? p?.FindFirst("sub")?.Value;
                if (p is null || string.IsNullOrEmpty(userId)) return;

                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();

                bool blocked;
                if (p.IsInRole(Roles.Teacher))
                    blocked = !await db.Teachers.AnyAsync(t => t.UserId == userId && !t.IsArchived);
                else if (p.IsInRole(Roles.Student))
                    blocked = !await db.Students.AnyAsync(s => s.UserId == userId && !s.IsArchived);
                else if (p.IsInRole(Roles.Staff) || p.IsInRole(Roles.Admin) || p.IsInRole(Roles.SuperAdmin))
                {
                    var u = await db.Users.FirstOrDefaultAsync(x => x.Id == userId);
                    blocked = u is null;
                    // Xodim (staff) ruxsatlarini HAR so'rovda DB'dan claim sifatida qo'shamiz — tokenga
                    // yozilmaydi, shuning uchun superadmin ruxsatni o'zgartirsa darrov amal qiladi
                    // (qayta login shart emas). AdminPerm atributi shu claim'larni tekshiradi.
                    if (!blocked && p.IsInRole(Roles.Staff) && u!.Permissions is { Count: > 0 } perms
                        && p.Identity is ClaimsIdentity ident)
                        foreach (var perm in perms)
                            ident.AddClaim(new Claim(AdminPermAttribute.ClaimType, perm));
                }
                else
                    blocked = false; // parent / boshqa — tegmaymiz

                if (blocked) context.Fail("Akkaunt arxivlangan yoki o'chirilgan");
            },
        };
    });
builder.Services.AddAuthorization();

// Login endpoint uchun rate-limit — parol brute-force / credential-stuffing'ni sekinlashtiradi
// (IP bo'yicha daqiqada 10 urinish). Oshib ketsa 429 qaytadi.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", httpContext =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

// Real-time guruh chati (SignalR)
builder.Services.AddSignalR();
builder.Services.AddScoped<ChatService>();

// Oylik to'lovlarni avtomatik hisoblovchi fon xizmati
builder.Services.AddHostedService<SchoolLms.Application.Services.TuitionAccrualService>();
builder.Services.AddHostedService<SchoolLms.Application.Services.TurnstileLiveService>();

// Telegram bot (e'lon yuborish + ota-onalarni kontakt orqali ro'yxatga olish).
// Token appsettings "Telegram:BotToken" da; bo'sh bo'lsa bot ishga tushmaydi.
builder.Services.AddHttpClient();
builder.Services.AddSingleton<TelegramService>();
builder.Services.AddHostedService<TelegramBotService>();
// FCM (Firebase push) — service account SchoolMeta'da; token keshi uchun singleton.
builder.Services.AddSingleton<FcmService>();

// O'zgarishlar tarixi (audit) — joriy foydalanuvchini aniqlash uchun HttpContext kerak
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<SchoolLms.Application.Services.AuditService>();

// Shartnoma andozasini (Word) to'ldirish xizmati
builder.Services.AddScoped<SchoolLms.Application.Services.ContractService>();

// Turniket/FaceID integratsiyasi — o'qituvchilar davomatini avtomatik yuklash
builder.Services.AddScoped<SchoolLms.Application.Services.TurnstileService>();

// Kamera (videokuzatuv) media-shlyuzi (MediaMTX) bilan ishlash
builder.Services.AddHttpClient<SchoolLms.Application.Services.CameraGateway>();

// Javoblarni siqish (Brotli + Gzip). Level.Fastest — TTFB ga ortiqcha CPU yuk qo'ymaydi.
// Eslatma: Cloudflare orqasida bo'lsa, CF chetda allaqachon siqadi — bu origin uchun foydali.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);

// OutputCache — faqat OCHIQ (auth talab qilmaydigan) endpointlar uchun ([OutputCache] qo'yilganlar).
builder.Services.AddOutputCache(options =>
{
    options.AddPolicy("public-tenant", b => b.Expire(TimeSpan.FromSeconds(30)));
});

builder.Services.AddControllers();

var app = builder.Build();

// ---------- Bazani yaratish va seed ----------
using (var scope = app.Services.CreateScope())
{
    // Migratsiyani `Default` (app_rw) EMAS, `Migrator` (sxema egasi) bajaradi.
    // Buning uchun shu yerda bir martalik, alohida ulanishli DbContext quriladi —
    // DI dagi scoped kontekst tegilmaydi.
    //
    // Database__AutoMigrate=false qo'ysangiz bu bosqich butunlay o'tkazib yuboriladi va
    // migratsiya alohida qadamga aylanadi (avval backup, keyin migratsiya). Moliya moduli
    // (P1-04) kelganda prodda shunday qilish tavsiya etiladi — deploy/README.md ga qarang.
    // Rollback: o'zgaruvchini olib tashlash kifoya, qayta build SHART EMAS.
    var autoMigrate = app.Configuration.GetValue("Database:AutoMigrate", true);
    if (!autoMigrate)
    {
        Console.WriteLine("[db] Database__AutoMigrate=false — migratsiya O'TKAZIB YUBORILDI (qo'lda bajariladi).");
    }
    else if (!string.IsNullOrWhiteSpace(migratorConn))
    {
        // Pooling=false ATAYLAB: aks holda Npgsql pooli sxema EGASI nomidagi ulanishni
        // jarayon tugaguncha ochiq saqlaydi — migratsiya tugagandan keyin ham serverda
        // bo'sh turgan, to'liq huquqli ulanish qoladi (va 60 ta ulanish limitidan bittasi
        // yeb ketiladi). Migratsiya bir martalik ish, pool undan foyda bermaydi.
        var migratorCs = new Npgsql.NpgsqlConnectionStringBuilder(migratorConn)
        {
            Pooling = false,
            ApplicationName = "SchoolLms.Migrator",
        }.ConnectionString;

        var migratorOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(migratorCs, npg => npg.EnableRetryOnFailure(
                maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null))
            .UseSnakeCaseNamingConvention()
            .Options;
        using (var migratorDb = new AppDbContext(migratorOptions))
        {
            migratorDb.Database.Migrate();
        }
        Console.WriteLine("[db] migratsiya `Migrator` roli bilan bajarildi; egalik ulanishi yopildi.");
    }
    else
    {
        // Dev: bitta ulanish satri yetarli. Prodda bu yo'lga tushish — konfiguratsiya xatosi.
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
        Console.WriteLine("[db] ConnectionStrings:Migrator berilmagan — migratsiya `Default` bilan bajarildi (dev rejimi).");
    }

    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // Ilgari bu yerda 45 ta SQL Server'ga xos `ExecuteSqlRaw` bo'lgan (ustun/jadval qo'shish).
    // PostgreSQL'ga o'tishda ularning hammasi normal EF migratsiyasiga ko'chirildi —
    // sxema endi faqat model orqali boshqariladi.

    // Telegram bot tokeni — restartdan keyin bot avtomatik ishga tushadi; token yo'q bo'lsa
    // admin Sozlamadan kiritguncha kutadi.
    scope.ServiceProvider.GetRequiredService<TelegramService>().Load(db);
}

// ---------- Pipeline ----------

// Cloudflare Tunnel / reverse-proxy orqasida: haqiqiy mijoz IP'si (X-Forwarded-For) va
// HTTPS sxemasi (X-Forwarded-Proto) tiklanadi. Busiz login rate-limit hamma uchun bitta
// IP'ga (tunnel) tushib qoladi va HTTPS-redirect tsikli yuzaga kelishi mumkin.
// FAQAT prod'da yoqamiz (dev'da Vite proxy bu sarlavhalarni yubormaydi).
// MUHIM: konteyner portini internetga OCHMANG — unga faqat cloudflared kirsin.
if (!app.Environment.IsDevelopment())
{
    var fwd = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    };
    // .NET 10: KnownNetworks eskirdi (ASPDEPR005) — o'rniga KnownIPNetworks.
    fwd.KnownIPNetworks.Clear();
    fwd.KnownProxies.Clear();
    app.UseForwardedHeaders(fwd);
}

// Javoblarni siqish — pipeline boshida (statik fayllar va API javoblari ham siqilsin).
app.UseResponseCompression();

// Xavfsizlik sarlavhalari — barcha javoblarga (statik fayllar va /uploads ham). MIME-sniffing,
// clickjacking va (prod'da) saqlangan XSS'ga qarshi himoya.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    // CSP faqat prod'da — dev'da SPA Vite serverida alohida beriladi.
    // Leaflet xaritasi unpkg/openstreetmap'dan rasm yuklaydi (img https:).
    if (!app.Environment.IsDevelopment())
    {
        headers["Content-Security-Policy"] =
            "default-src 'self'; " +
            "img-src 'self' data: blob: https:; " +
            "style-src 'self' 'unsafe-inline'; " +
            // gstatic — FCM web SW (firebase-messaging-sw.js) importScripts qiladi.
            "script-src 'self' https://www.gstatic.com; " +
            "worker-src 'self'; " +
            // googleapis/gstatic — FCM web token olish (getToken) so'rovlari.
            "connect-src 'self' ws: wss: https://*.googleapis.com https://*.gstatic.com https://fcm.googleapis.com; " +
            "font-src 'self' data:; " +
            "frame-ancestors 'none'; object-src 'none'; base-uri 'self'";
    }
    await next();
});

if (!app.Environment.IsDevelopment())
    app.UseHsts();

// DIQQAT: UseDefaultFiles ATAYLAB ishlatilmaydi — `/` ni o'zimiz fallback'da hostga qarab beramiz
// (apex → landing, subdomen → SPA). Statik fayllar (assets, landing.css/js) quyida xizmat qilinadi.
// SPA statik fayllari: Vite assetlari kontent-hash bilan (immutable, 1 yil); index.html/landing — no-cache.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var headers = ctx.Context.Response.Headers;
        var path = ctx.Context.Request.Path.Value ?? "";
        // Faqat Vite kontent-hashli assetlar (/assets/...) abadiy keshlanadi (nomi har build'da
        // o'zgaradi). Qolganlari — html, landing.css/landing.js, favicon (nomi o'zgarmaydi) —
        // no-cache, aks holda yangilanishlar brauzer/Cloudflare keshida ko'rinmay qoladi.
        if (path.Contains("/assets/", StringComparison.OrdinalIgnoreCase))
            headers.CacheControl = "public,max-age=31536000,immutable";
        else
            headers.CacheControl = "no-cache";
    },
});

// Yuklangan materiallar (/uploads) — alohida papkadan, 1 kunlik kesh bilan.
var uploadsDir = Path.Combine(app.Environment.ContentRootPath, "uploads");
Directory.CreateDirectory(uploadsDir);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsDir),
    RequestPath = "/uploads",
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "public,max-age=86400",
});

// Swagger ATAYLAB o'chirilgan (global) — butun API yuzasini ochib qo'ymaslik uchun
// `/api/swagger` UI/JSON endpointlari berilmaydi.

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
// OutputCache middleware — tayyor turadi, lekin [OutputCache] faqat ochiq endpointlarga qo'yiladi
// (multi-tenant xavfsizligi uchun; pastdagi izohga qarang). Auth'dan keyin turishi shart.
app.UseOutputCache();

app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");
app.MapHub<LiveHub>("/hubs/live");

// API "tirikligi": https://<domen>/api ochilganda SPA HTML emas, JSON qaytaradi.
app.MapGet("/api", () => Results.Ok(new
{
    name = "SchoolLms API",
    status = "ok",
    environment = app.Environment.EnvironmentName,
    timeUtc = DateTime.UtcNow,
}));
// Healthcheck. ATAYLAB bazaga HAQIQIY so'rov yuboradi — "jarayon tirik" emas, "ilova ishlayapti"
// degan javob kerak (DB tushsa, konteyner tirik bo'lsa ham xizmat ishlamaydi).
// Redis ATAYLAB tekshirilmaydi: u ixtiyoriy kesh, tushsa ilova sekinlashadi, lekin ishlaydi —
// uni "unhealthy" deb belgilash keraksiz restart tsikliga olib kelardi.
app.MapGet("/api/health", async (AppDbContext db, CancellationToken ct) =>
{
    try
    {
        await db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
        return Results.Ok(new { status = "healthy" });
    }
    catch (Exception ex)
    {
        // Xato MATNI qaytarilmaydi — unda ulanish satri (parol bilan) bo'lishi mumkin.
        return Results.Json(new { status = "unhealthy", error = ex.GetType().Name },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

// Noma'lum /api/* yo'llari — SPA HTML emas, 404 JSON qaytsin (mobil/klient uchun toza).
app.MapFallback("/api/{**slug}", () => Results.NotFound(new { message = "API endpoint topilmadi" }));

// SPA / landing fallback:
//  • Faqat ILOVA HOSTI (App:Host, masalan `test.wunderkindschool.uz`) → React SPA (index.html);
//  • boshqa hammasi (apex `wunderkindschool.uz`, `www`, `admin` va h.k.) → landing sahifa (landing.html).
//  • App:Host sozlanmagan bo'lsa (dev) — apex/www dan boshqa hammasi SPA (eski xulq).
var webRoot = app.Environment.WebRootPath
    ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
var appHost = (builder.Configuration["App:Host"] ?? "").Trim();
bool IsLandingHost(string h) => rootDomains.Any(r =>
    h.Equals(r, StringComparison.OrdinalIgnoreCase) ||
    h.Equals("www." + r, StringComparison.OrdinalIgnoreCase));

// DIQQAT: o'qituvchi PWA (`/teacher/`) statik fayllari (assets, manifest, sw.js, ikonlar)
// yuqoridagi UseStaticFiles orqali beriladi. Ularni ALOHIDA pattern'li fallback bilan tutmaymiz —
// pattern'li `MapFallback("/teacher/{**slug}")` `nonfile` cheklovisiz bo'lib, real fayllarni ham
// tutib statik middleware'ni soyalaydi. Buning o'rniga `/teacher/` ni quyidagi GENERIC fallback
// ichida (u `nonfile` cheklovli — fayllarga tegmaydi) hal qilamiz.
app.MapFallback(async ctx =>
{
    var path = ctx.Request.Path.Value ?? "";

    // O'qituvchi PWA: `/teacher` → trailing-slash'ga (relative manifest/icon to'g'ri yechilishi uchun);
    // `/teacher/` va ichki nonfile yo'llar → teacher index.html.
    if (path.Equals("/teacher", StringComparison.OrdinalIgnoreCase))
    {
        ctx.Response.Redirect("/teacher/", permanent: false);
        return;
    }
    if (path.StartsWith("/teacher/", StringComparison.OrdinalIgnoreCase))
    {
        var teacherIndex = Path.Combine(webRoot, "teacher", "index.html");
        if (!File.Exists(teacherIndex)) { ctx.Response.StatusCode = StatusCodes.Status404NotFound; return; }
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.Headers.CacheControl = "no-cache";
        await ctx.Response.SendFileAsync(teacherIndex);
        return;
    }

    var host = ctx.Request.Host.Host;
    var isApp = appHost.Length > 0
        ? host.Equals(appHost, StringComparison.OrdinalIgnoreCase)
        : !IsLandingHost(host); // dev: App:Host yo'q — apex/www dan boshqa hammasi ilova
    var file = Path.Combine(webRoot, isApp ? "index.html" : "landing.html");
    if (!File.Exists(file)) { ctx.Response.StatusCode = StatusCodes.Status404NotFound; return; }
    ctx.Response.ContentType = "text/html; charset=utf-8";
    ctx.Response.Headers.CacheControl = "no-cache";
    await ctx.Response.SendFileAsync(file);
});

app.Run();

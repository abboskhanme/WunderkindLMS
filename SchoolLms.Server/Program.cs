using Microsoft.AspNetCore.Authentication.JwtBearer;
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
using SchoolLms.Infrastructure.Tenancy;
using SchoolLms.Server.Tenancy;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ---------- Multi-tenant sozlamalari ----------
var defaultConn = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default sozlanmagan.");

var tenancy = builder.Configuration.GetSection("Tenancy").Get<TenantingOptions>() ?? new TenantingOptions();
builder.Services.AddSingleton(tenancy);
builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
builder.Services.AddScoped<ITenantStore, TenantStore>();
builder.Services.AddScoped<ITenantDbRunner, TenantDbRunner>();
builder.Services.AddScoped<ProvisioningService>();

// ---------- Xizmatlar ----------
// Shared (yagona) baza — barcha maktablar + Control Plane (Owners/Tenants) shu yerda.
// Har so'rovning joriy tenanti (ITenantContext) global query filter orqali qatorlarni ajratadi.
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlServer(defaultConn,
            // Vaqtinchalik DB uzilishlarini (ayniqsa Azure SQL) avtomatik qayta urinish bilan chidaydi.
            sql => sql.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null))
        // Global query filter + majburiy navigatsiya o'zaro ta'siri haqidagi ogohlantirishni o'chiramiz
        // (barcha entity'larda bir xil TenantId filtri — xavfsiz).
        .ConfigureWarnings(w => w.Ignore(
            Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId
                .PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning)));

// Application qatlamidagi xizmatlar konkret AppDbContext o'rniga IAppDbContext'ga
// bog'lanadi — uni o'sha scoped AppDbContext instansiyasiga ulaymiz.
builder.Services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

// Kam o'zgaradigan ma'lumotlar (meta, fan/o'qituvchi nomlari) uchun qisqa-TTL kesh.
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ReferenceCache>();

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
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/chat"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            },

            // Token bekor qilish (revocation): imzo/muddat to'g'ri bo'lsa ham, akkaunt holatini
            // HAR so'rovda tekshiramiz — arxivlangan o'qituvchi/o'quvchi yoki o'chirilgan xodim/admin
            // eski tokeni bilan KIRA OLMAYDI. Parent (telefon orqali bog'lanadi) va platformowner
            // (AppUser emas) tekshirilmaydi. Filtrlardan xoli (IgnoreQueryFilters) — tenant kontekstidan
            // mustaqil. GUID id'lar global unikal, shuning uchun tenantlararo to'qnashuv yo'q.
            OnTokenValidated = async context =>
            {
                var p = context.Principal;
                var userId = p?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                             ?? p?.FindFirst("sub")?.Value;
                if (p is null || string.IsNullOrEmpty(userId)) return;

                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();

                bool blocked;
                if (p.IsInRole(Roles.Teacher))
                    blocked = !await db.Teachers.IgnoreQueryFilters()
                        .AnyAsync(t => t.UserId == userId && !t.IsArchived);
                else if (p.IsInRole(Roles.Student))
                    blocked = !await db.Students.IgnoreQueryFilters()
                        .AnyAsync(s => s.UserId == userId && !s.IsArchived);
                else if (p.IsInRole(Roles.Staff) || p.IsInRole(Roles.Admin) || p.IsInRole(Roles.SuperAdmin))
                    blocked = !await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == userId);
                else
                    blocked = false; // parent / platformowner / boshqa — tegmaymiz

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

// OutputCache — DIQQAT (multi-tenant): javoblarni FAQAT X-Tenant bo'yicha ajratib keshlash mumkin,
// aks holda bir maktab javobi boshqasiga ketadi. Bu siyosat faqat OCHIQ (auth talab qilmaydigan)
// endpointlar uchun. Auth talab qiladigan (Authorization sarlavhali) so'rovlarni default policy
// baribir keshlamaydi. Shuning uchun [OutputCache] ni HAMMA GET'ga qo'yMAYMIZ (pastdagi izohga qarang).
builder.Services.AddOutputCache(options =>
{
    options.AddPolicy("public-tenant", b => b
        .Expire(TimeSpan.FromSeconds(30))
        .SetVaryByHeader("X-Tenant"));
});

builder.Services.AddControllers();

var app = builder.Build();

// ---------- Bazani yaratish va seed ----------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    // Default Control Plane egasi (loyiha boshlig'i). Parol Owner:Password (env Owner__Password)
    // dan olinadi; berilmasa kuchli tasodifiy parol generatsiya qilinib LOG'ga bir marta yoziladi.
    var ownerPassword = builder.Configuration["Owner:Password"];
    var seededPassword = DbSeeder.SeedPlatformOwner(db, ownerPassword);
    if (seededPassword is not null)
        Console.WriteLine(
            "[WARN] Default platform owner yaratildi: owner@schoollms.uz — parol: "
            + $"{seededPassword}\n[WARN] Bu parolni hoziroq saqlang, Control Plane'ga kirib ALMASHTIRING. "
            + "(Barqaror parol uchun Owner__Password env o'rnating.)");

    // Telegram bot tokeni (shared-DB: tenant'lararo birinchi tokenli maktabdan yuklanadi — restartdan
    // keyin bot avtomatik ishga tushadi; token yo'q bo'lsa admin Sozlamadan kiritguncha kutadi).
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
    fwd.KnownNetworks.Clear();
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
            "script-src 'self'; " +
            "connect-src 'self' ws: wss:; " +
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
        if (ctx.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            headers.CacheControl = "no-cache";
        else
            headers.CacheControl = "public,max-age=31536000,immutable";
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
// Joriy maktab (tenant)ni aniqlaydi — autentifikatsiyadan KEYIN, chunki kirgan foydalanuvchi uchun
// tokendagi tenant claim ASOSIY manba (tenantlararo kirishni bloklaydi). Global query filter shu
// kontekstdan o'qiydi, shuning uchun controller'lardan (AppDbContext'dan foydalanish) OLDIN turadi.
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();
// OutputCache middleware — tayyor turadi, lekin [OutputCache] faqat ochiq endpointlarga qo'yiladi
// (multi-tenant xavfsizligi uchun; pastdagi izohga qarang). Auth'dan keyin turishi shart.
app.UseOutputCache();

app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");

// API "tirikligi": https://<domen>/api ochilganda SPA HTML emas, JSON qaytaradi va qaysi maktab
// (tenant) aniqlanganini ko'rsatadi — subdomen yo'naltirishni tekshirish uchun qulay.
app.MapGet("/api", (ITenantContext t) => Results.Ok(new
{
    name = "SchoolLms API",
    status = "ok",
    environment = app.Environment.EnvironmentName,
    tenant = t.IsPlatform ? "platform" : t.Slug,
    timeUtc = DateTime.UtcNow,
}));
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" }));

// Noma'lum /api/* yo'llari — SPA HTML emas, 404 JSON qaytsin (mobil/klient uchun toza).
app.MapFallback("/api/{**slug}", () => Results.NotFound(new { message = "API endpoint topilmadi" }));

// SPA / landing fallback:
//  • apex (bare domen `intellectschool.uz`) yoki `www` → landing sahifa (public/landing.html);
//  • `admin` va maktab subdomenlari → React SPA (index.html).
var webRoot = app.Environment.WebRootPath
    ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
var landingRoots = tenancy.Roots();
bool IsLandingHost(string h) => landingRoots.Any(r =>
    h.Equals(r, StringComparison.OrdinalIgnoreCase) ||
    h.Equals("www." + r, StringComparison.OrdinalIgnoreCase));

app.MapFallback(async ctx =>
{
    var landing = Path.Combine(webRoot, "landing.html");
    var spa = Path.Combine(webRoot, "index.html");
    var file = IsLandingHost(ctx.Request.Host.Host) && File.Exists(landing) ? landing : spa;
    if (!File.Exists(file)) { ctx.Response.StatusCode = StatusCodes.Status404NotFound; return; }
    ctx.Response.ContentType = "text/html; charset=utf-8";
    ctx.Response.Headers.CacheControl = "no-cache";
    await ctx.Response.SendFileAsync(file);
});

app.Run();

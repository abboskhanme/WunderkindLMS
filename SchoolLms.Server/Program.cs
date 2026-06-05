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
using System.Text;

var builder = WebApplication.CreateBuilder(args);

var defaultConn = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default sozlanmagan.");

// Apex (asosiy domen) → landing sahifa; subdomen → ilova (SPA). Faqat shu uchun root domen kerak.
var rootDomains = (builder.Configuration["Tenancy:RootDomain"] ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

// ---------- Xizmatlar ----------
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlServer(defaultConn,
            sql =>
            {
                // Vaqtinchalik DB uzilishlarini avtomatik qayta urinish bilan chidaydi.
                sql.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorNumbersToAdd: null);
                // Ko'p kolleksiyali Include'larni alohida so'rovlarga ajratadi — kartezian portlashning oldini oladi.
                sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            }));

// Application qatlamidagi xizmatlar konkret AppDbContext o'rniga IAppDbContext'ga
// bog'lanadi — uni o'sha scoped AppDbContext instansiyasiga ulaymiz.
builder.Services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

// Kam o'zgaradigan ma'lumotlar (meta, fan/o'qituvchi nomlari) uchun qisqa-TTL kesh.
builder.Services.AddMemoryCache();
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
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/chat"))
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
                    blocked = !await db.Users.AnyAsync(u => u.Id == userId);
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
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    // EvaluationGrades.SubjectId — fan bo'yicha baholash uchun. Migratsiyasiz, idempotent qo'shamiz
    // (WDAC `dotnet ef` ni bloklaydi; ustun mavjud bo'lsa hech narsa qilmaydi).
    db.Database.ExecuteSqlRaw(
        "IF COL_LENGTH('EvaluationGrades','SubjectId') IS NULL " +
        "ALTER TABLE [EvaluationGrades] ADD [SubjectId] nvarchar(max) NOT NULL DEFAULT '';");

    // Sinfni arxivlash — Classes.IsArchived/ArchivedAt + Students.ArchivedWithClass (sinf bilan
    // arxivlangan o'quvchi belgisi). Migratsiyasiz, idempotent (WDAC `dotnet ef` ni bloklaydi).
    db.Database.ExecuteSqlRaw(
        "IF COL_LENGTH('Classes','IsArchived') IS NULL " +
        "ALTER TABLE [Classes] ADD [IsArchived] bit NOT NULL DEFAULT 0;");
    db.Database.ExecuteSqlRaw(
        "IF COL_LENGTH('Classes','ArchivedAt') IS NULL " +
        "ALTER TABLE [Classes] ADD [ArchivedAt] nvarchar(max) NULL;");
    db.Database.ExecuteSqlRaw(
        "IF COL_LENGTH('Students','ArchivedWithClass') IS NULL " +
        "ALTER TABLE [Students] ADD [ArchivedWithClass] bit NOT NULL DEFAULT 0;");

    // O'qituvchi maoshi — toifa bo'yicha avtomatik hisoblash. Teachers.Category (toifa) +
    // SchoolMeta'da har toifa uchun bir soat narxi. Migratsiyasiz, idempotent.
    db.Database.ExecuteSqlRaw(
        "IF COL_LENGTH('Teachers','Category') IS NULL " +
        "ALTER TABLE [Teachers] ADD [Category] nvarchar(max) NOT NULL DEFAULT '';");
    db.Database.ExecuteSqlRaw(
        "IF COL_LENGTH('Teachers','BonusPct') IS NULL " +
        "ALTER TABLE [Teachers] ADD [BonusPct] decimal(18,2) NOT NULL DEFAULT 0;");
    db.Database.ExecuteSqlRaw(
        "IF COL_LENGTH('Teachers','SalaryStartDate') IS NULL " +
        "ALTER TABLE [Teachers] ADD [SalaryStartDate] nvarchar(max) NOT NULL DEFAULT '';");
    foreach (var col in new[] { "SalaryRateOliy", "SalaryRate1", "SalaryRate2", "SalaryRateMutaxasis" })
        db.Database.ExecuteSqlRaw(
            $"IF COL_LENGTH('SchoolMeta','{col}') IS NULL " +
            $"ALTER TABLE [SchoolMeta] ADD [{col}] decimal(18,2) NOT NULL DEFAULT 0;");

    // O'qituvchilar davomati — yangi jadval (migratsiyasiz; shadow TenantId ustuni bilan global filterga
    // mos). Idempotent: faqat yo'q bo'lsa yaratiladi.
    db.Database.ExecuteSqlRaw(
        "IF OBJECT_ID('TeacherAttendances') IS NULL " +
        "CREATE TABLE [TeacherAttendances] (" +
        "  [Id] nvarchar(450) NOT NULL CONSTRAINT [PK_TeacherAttendances] PRIMARY KEY," +
        "  [TeacherId] nvarchar(max) NOT NULL DEFAULT ''," +
        "  [Date] nvarchar(max) NOT NULL DEFAULT ''," +
        "  [Status] nvarchar(max) NOT NULL DEFAULT ''," +
        "  [Note] nvarchar(max) NOT NULL DEFAULT ''," +
        "  [TenantId] nvarchar(64) NOT NULL DEFAULT '');");
    db.Database.ExecuteSqlRaw(
        "IF OBJECT_ID('TeacherAttendances') IS NOT NULL AND NOT EXISTS " +
        "(SELECT 1 FROM sys.indexes WHERE name='IX_TeacherAttendances_TenantId') " +
        "CREATE INDEX [IX_TeacherAttendances_TenantId] ON [TeacherAttendances]([TenantId]);");

    // Turniket/FaceID integratsiyasi — o'qituvchilar davomatini avtomatik yuklash. Migratsiyasiz, idempotent.
    db.Database.ExecuteSqlRaw(
        "IF COL_LENGTH('Teachers','DeviceUserId') IS NULL " +
        "ALTER TABLE [Teachers] ADD [DeviceUserId] nvarchar(max) NOT NULL DEFAULT '';");
    foreach (var c in new[] { "CheckIn", "CheckOut" })
        db.Database.ExecuteSqlRaw(
            $"IF COL_LENGTH('TeacherAttendances','{c}') IS NULL " +
            $"ALTER TABLE [TeacherAttendances] ADD [{c}] nvarchar(max) NOT NULL DEFAULT '';");
    db.Database.ExecuteSqlRaw(
        "IF COL_LENGTH('TeacherAttendances','Source') IS NULL " +
        "ALTER TABLE [TeacherAttendances] ADD [Source] nvarchar(max) NOT NULL DEFAULT 'manual';");
    foreach (var c in new[] { "TurnstileVendor", "TurnstileHost", "TurnstileUsername", "TurnstilePassword", "WorkStartTime", "TurnstileLastSync" })
        db.Database.ExecuteSqlRaw(
            $"IF COL_LENGTH('SchoolMeta','{c}') IS NULL " +
            $"ALTER TABLE [SchoolMeta] ADD [{c}] nvarchar(max) NOT NULL DEFAULT '';");
    db.Database.ExecuteSqlRaw(
        "IF COL_LENGTH('SchoolMeta','TurnstileEnabled') IS NULL " +
        "ALTER TABLE [SchoolMeta] ADD [TurnstileEnabled] bit NOT NULL DEFAULT 0;");
    db.Database.ExecuteSqlRaw(
        "IF COL_LENGTH('SchoolMeta','TurnstilePort') IS NULL " +
        "ALTER TABLE [SchoolMeta] ADD [TurnstilePort] int NOT NULL DEFAULT 80;");
    db.Database.ExecuteSqlRaw(
        "IF COL_LENGTH('SchoolMeta','LateGraceMinutes') IS NULL " +
        "ALTER TABLE [SchoolMeta] ADD [LateGraceMinutes] int NOT NULL DEFAULT 10;");
    db.Database.ExecuteSqlRaw(
        "IF OBJECT_ID('TurnstileEvents') IS NULL " +
        "CREATE TABLE [TurnstileEvents] (" +
        "  [Id] nvarchar(450) NOT NULL CONSTRAINT [PK_TurnstileEvents] PRIMARY KEY," +
        "  [TeacherId] nvarchar(max) NOT NULL DEFAULT ''," +
        "  [DeviceUserId] nvarchar(max) NOT NULL DEFAULT ''," +
        "  [EventAt] nvarchar(max) NOT NULL DEFAULT ''," +
        "  [Direction] nvarchar(max) NOT NULL DEFAULT 'in'," +
        "  [DeviceName] nvarchar(max) NOT NULL DEFAULT ''," +
        "  [CreatedAt] nvarchar(max) NOT NULL DEFAULT ''," +
        "  [TenantId] nvarchar(64) NOT NULL DEFAULT '');");
    db.Database.ExecuteSqlRaw(
        "IF OBJECT_ID('TurnstileEvents') IS NOT NULL AND NOT EXISTS " +
        "(SELECT 1 FROM sys.indexes WHERE name='IX_TurnstileEvents_TenantId') " +
        "CREATE INDEX [IX_TurnstileEvents_TenantId] ON [TurnstileEvents]([TenantId]);");

    // GPS — maktab avtobuslarini kuzatish. Migratsiyasiz, idempotent.
    foreach (var c in new[] { "GpsIngestToken" })
        db.Database.ExecuteSqlRaw(
            $"IF COL_LENGTH('SchoolMeta','{c}') IS NULL " +
            $"ALTER TABLE [SchoolMeta] ADD [{c}] nvarchar(max) NOT NULL DEFAULT '';");
    db.Database.ExecuteSqlRaw(
        "IF COL_LENGTH('SchoolMeta','GpsEnabled') IS NULL " +
        "ALTER TABLE [SchoolMeta] ADD [GpsEnabled] bit NOT NULL DEFAULT 0;");
    foreach (var (c, def) in new[] { ("GpsOnlineMinutes", 5), ("GpsStopRadiusM", 60), ("GpsStopMinMinutes", 3) })
        db.Database.ExecuteSqlRaw(
            $"IF COL_LENGTH('SchoolMeta','{c}') IS NULL " +
            $"ALTER TABLE [SchoolMeta] ADD [{c}] int NOT NULL DEFAULT {def};");
    db.Database.ExecuteSqlRaw(
        "IF OBJECT_ID('Buses') IS NULL " +
        "CREATE TABLE [Buses] (" +
        "  [Id] nvarchar(450) NOT NULL CONSTRAINT [PK_Buses] PRIMARY KEY," +
        "  [Name] nvarchar(max) NOT NULL DEFAULT ''," +
        "  [PlateNumber] nvarchar(max) NOT NULL DEFAULT ''," +
        "  [DriverName] nvarchar(max) NOT NULL DEFAULT ''," +
        "  [DriverPhone] nvarchar(max) NOT NULL DEFAULT ''," +
        "  [DeviceId] nvarchar(max) NOT NULL DEFAULT ''," +
        "  [Route] nvarchar(max) NOT NULL DEFAULT ''," +
        "  [IsActive] bit NOT NULL DEFAULT 1," +
        "  [Note] nvarchar(max) NOT NULL DEFAULT ''," +
        "  [TenantId] nvarchar(64) NOT NULL DEFAULT '');");
    db.Database.ExecuteSqlRaw(
        "IF OBJECT_ID('Buses') IS NOT NULL AND NOT EXISTS " +
        "(SELECT 1 FROM sys.indexes WHERE name='IX_Buses_TenantId') " +
        "CREATE INDEX [IX_Buses_TenantId] ON [Buses]([TenantId]);");
    db.Database.ExecuteSqlRaw(
        "IF OBJECT_ID('BusLocations') IS NULL " +
        "CREATE TABLE [BusLocations] (" +
        "  [Id] nvarchar(450) NOT NULL CONSTRAINT [PK_BusLocations] PRIMARY KEY," +
        "  [BusId] nvarchar(max) NOT NULL DEFAULT ''," +
        "  [Latitude] float NOT NULL DEFAULT 0," +
        "  [Longitude] float NOT NULL DEFAULT 0," +
        "  [Speed] float NOT NULL DEFAULT 0," +
        "  [RecordedAt] nvarchar(max) NOT NULL DEFAULT ''," +
        "  [CreatedAt] nvarchar(max) NOT NULL DEFAULT ''," +
        "  [TenantId] nvarchar(64) NOT NULL DEFAULT '');");
    db.Database.ExecuteSqlRaw(
        "IF OBJECT_ID('BusLocations') IS NOT NULL AND NOT EXISTS " +
        "(SELECT 1 FROM sys.indexes WHERE name='IX_BusLocations_TenantId') " +
        "CREATE INDEX [IX_BusLocations_TenantId] ON [BusLocations]([TenantId]);");

    // Kamera (videokuzatuv) — media-shlyuz (MediaMTX) orqali. Migratsiyasiz, idempotent.
    db.Database.ExecuteSqlRaw(
        "IF COL_LENGTH('SchoolMeta','CameraEnabled') IS NULL " +
        "ALTER TABLE [SchoolMeta] ADD [CameraEnabled] bit NOT NULL DEFAULT 0;");
    db.Database.ExecuteSqlRaw(
        "IF OBJECT_ID('Cameras') IS NULL " +
        "CREATE TABLE [Cameras] (" +
        "  [Id] nvarchar(450) NOT NULL CONSTRAINT [PK_Cameras] PRIMARY KEY," +
        "  [Name] nvarchar(max) NOT NULL DEFAULT ''," +
        "  [Location] nvarchar(max) NOT NULL DEFAULT ''," +
        "  [RtspUrl] nvarchar(max) NOT NULL DEFAULT ''," +
        "  [RtspSubUrl] nvarchar(max) NOT NULL DEFAULT ''," +
        "  [IsActive] bit NOT NULL DEFAULT 1," +
        "  [Note] nvarchar(max) NOT NULL DEFAULT ''," +
        "  [TenantId] nvarchar(64) NOT NULL DEFAULT '');");
    db.Database.ExecuteSqlRaw(
        "IF OBJECT_ID('Cameras') IS NOT NULL AND NOT EXISTS " +
        "(SELECT 1 FROM sys.indexes WHERE name='IX_Cameras_TenantId') " +
        "CREATE INDEX [IX_Cameras_TenantId] ON [Cameras]([TenantId]);");
    db.Database.ExecuteSqlRaw(
        "IF COL_LENGTH('Cameras','RetentionDays') IS NULL " +
        "ALTER TABLE [Cameras] ADD [RetentionDays] int NOT NULL DEFAULT 7;");

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
        var path = ctx.Context.Request.Path.Value ?? "";
        // Faqat Vite kontent-hashli assetlar (/assets/...) abadiy keshlanadi (nomi har build'da
        // o'zgaradi). Qolganlari — html, landing.css/landing.js, favicon (nomi o'zgarmaydi) —
        // no-cache, aks holda yangilanishlar brauzer/Cloudflare keshida ko'rinmay qoladi.
        if (path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase))
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

// API "tirikligi": https://<domen>/api ochilganda SPA HTML emas, JSON qaytaradi.
app.MapGet("/api", () => Results.Ok(new
{
    name = "SchoolLms API",
    status = "ok",
    environment = app.Environment.EnvironmentName,
    timeUtc = DateTime.UtcNow,
}));
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" }));

// Noma'lum /api/* yo'llari — SPA HTML emas, 404 JSON qaytsin (mobil/klient uchun toza).
app.MapFallback("/api/{**slug}", () => Results.NotFound(new { message = "API endpoint topilmadi" }));

// SPA / landing fallback:
//  • Faqat ILOVA HOSTI (App:Host, masalan `test.intellectschool.uz`) → React SPA (index.html);
//  • boshqa hammasi (apex `intellectschool.uz`, `www`, `admin` va h.k.) → landing sahifa (landing.html).
//  • App:Host sozlanmagan bo'lsa (dev) — apex/www dan boshqa hammasi SPA (eski xulq).
var webRoot = app.Environment.WebRootPath
    ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
var appHost = (builder.Configuration["App:Host"] ?? "").Trim();
bool IsLandingHost(string h) => rootDomains.Any(r =>
    h.Equals(r, StringComparison.OrdinalIgnoreCase) ||
    h.Equals("www." + r, StringComparison.OrdinalIgnoreCase));

app.MapFallback(async ctx =>
{
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

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
                // QAYTA URINISH (EnableRetryOnFailure) ATAYLAB O'CHIRILGAN.
                //
                // Moliya moduli (Faza 1) aniq tranzaksiyalar ishlatadi — to'lov, hisob-faktura
                // va kassa smenasi. EF Core qayta urinish strategiyasi bilan aniq tranzaksiya
                // birga ishlamaydi: "The configured execution strategy does not support
                // user-initiated transactions".
                //
                // Ikki yo'l bor edi: (a) har bir tranzaksiyani `CreateExecutionStrategy()`
                // ichiga o'rash, (b) qayta urinishni o'chirish. Hozircha (b) tanlandi —
                // baza ilova bilan bitta Docker tarmog'ida, vaqtinchalik uzilish kam uchraydi,
                // pul amallarining to'g'riligi esa muhimroq. (a) ni to'liq qilish alohida
                // vazifa sifatida `docs/PENDING_WIRING.md` ga yozildi.
                //
                // DIQQAT: testlar ham aynan shu sozlama bilan ishlashi SHART — aks holda bu
                // sinf xatolar faqat prodda chiqadi (aynan shunday bo'lgan edi).
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
                // `cashier` (P1-04) SHU YERDA bo'lishi SHART: aks holda u pastdagi
                // `else` ga tushib, o'chirilgan kassir eski tokeni bilan to'lov qabul
                // qilishda davom etardi. Pul oladigan rol uchun token bekor qilish
                // ixtiyoriy emas.
                else if (p.IsInRole(Roles.Staff) || p.IsInRole(Roles.Admin)
                         || p.IsInRole(Roles.SuperAdmin) || p.IsInRole(Roles.Cashier))
                {
                    var u = await db.Users.FirstOrDefaultAsync(x => x.Id == userId);
                    blocked = u is null;
                    // Xodim (staff) ruxsatlarini HAR so'rovda DB'dan claim sifatida qo'shamiz — tokenga
                    // yozilmaydi, shuning uchun superadmin ruxsatni o'zgartirsa darrov amal qiladi
                    // (qayta login shart emas). AdminPerm atributi shu claim'larni tekshiradi.
                    if (!blocked && p.IsInRole(Roles.Staff) && u!.Permissions is { Count: > 0 } perms
                        && p.Identity is ClaimsIdentity ident)
                        foreach (var perm in perms)
                        {
                            ident.AddClaim(new Claim(AdminPermAttribute.ClaimType, perm));
                            // Moliya kabi rolga qarab yopilgan bo'limlar: ruxsatdan ichki rol
                            // hosil bo'ladi (Roles.PermissionRoles) — hammasi xodim roli orqali.
                            if (Roles.PermissionRoles.TryGetValue(perm, out var derived))
                                ident.AddClaim(new Claim(ident.RoleClaimType, derived));
                        }
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

    // Telegram Mini App kirishi va bog'lash (`/api/tg/auth`, `/api/tg/link`).
    // `initData` imzosini soxtalashtirib bo'lmaydi, lekin bir martalik BOG'LASH
    // KODINI taxmin qilishga urinish mumkin — 20/daqiqa uni ma'nosiz qiladi
    // (kod 8 belgi, 32 harfli alifbo, umri 15 daqiqa). Chegara login'nikidan
    // yumshoqroq: Mini App qayta ochilganda `auth` har safar chaqiriladi.
    options.AddPolicy("telegram", httpContext =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

    // Ommaviy ariza formasi (`/ariza/{slug}`, sales-marketing.md §2.5). Topshirish:
    // 10 daqiqada 5 ta — uch farzandli oilaga yetadi, skriptga esa yo'q. Oyna
    // daqiqa emas, 10 daqiqa: bitta 4G NAT ortida butun bir ko'p qavatli uy
    // turishi mumkin. O'qish (sahifa ochilishi) alohida va yumshoq.
    options.AddPolicy("survey", httpContext =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: PublicFormPartition(httpContext),
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0,
            }));
    options.AddPolicy("survey-read", httpContext =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: PublicFormPartition(httpContext),
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

// Ommaviy forma uchun chastota kaliti. IPv6 manzil /64 prefiksi bo'yicha: bitta
// abonentga odatda butun /64 beriladi va har bir manzil alohida "savat" bo'lsa,
// cheklovni manzil almashtirib aylanib o'tish mumkin edi. IPv4 — o'z holicha.
static string PublicFormPartition(HttpContext ctx)
{
    var ip = ctx.Connection.RemoteIpAddress;
    if (ip is null) return "unknown";
    if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
    return ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
        ? Convert.ToHexString(ip.GetAddressBytes(), 0, 8) + "/64"
        : ip.ToString();
}

// Real-time guruh chati (SignalR)
builder.Services.AddSignalR();
builder.Services.AddScoped<ChatService>();

// Oylik to'lovlarni avtomatik hisoblovchi fon xizmati
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

// ---------- Moliya: tungi tekshiruv (P1-14, SPEC §4.6) ----------
builder.Services.AddScoped<SchoolLms.Application.Billing.IAnomalyService,
                           SchoolLms.Application.Billing.AnomalyService>();

// Tungi tekshiruv: ishga tushishda bir marta, keyin har kuni 03:00 da.
builder.Services.AddHostedService<SchoolLms.Application.Billing.AnomalyScanService>();
// Rejali xarajat eslatmasi (F6.01) — har kuni 08:00 (Toshkent) da o'sha kunga
// belgilangan shablonlarni direktorga Telegram orqali yig'ib yuboradi.
// ATAYLAB ishga tushganda YURMAYDI: `AnomalyScanService` dan farqli, bu yerda
// bir kunda ikki marta yuborishdan saqlaydigan bazadagi belgi yo'q — redeploy
// paytida takroriy xabar ketmasligi uchun faqat soat bo'yicha ishlaydi.
builder.Services.AddHostedService<SchoolLms.Application.Billing.ExpenseTemplateReminderService>();

// Shartnoma andozasini (Word) to'ldirish xizmati
builder.Services.AddScoped<SchoolLms.Application.Services.ContractService>();

// Turniket/FaceID integratsiyasi — o'qituvchilar davomatini avtomatik yuklash
builder.Services.AddScoped<SchoolLms.Application.Services.TurnstileService>();

// ---------- Savdo va marketing (docs/modules/sales-marketing.md) ----------
// Ommaviy ariza → lid. Qolgan xizmatlar (SurveyService, NewsService,
// SurveySubmissionQuery, NewsFeedQuery) statik — ro'yxatga olish shart emas.
builder.Services.AddScoped<SchoolLms.Application.Services.SurveySubmissionService>();
// Kechki dars va yotoqxona davomati (2026-09-23).
builder.Services.AddScoped<SchoolLms.Application.Services.BoardingAttendanceService>();
// Yangilik e'lon qilinganda Telegram tarqatmasi (§3.3 N4, N6).
builder.Services.AddScoped<SchoolLms.Application.Services.INewsTelegramNotifier,
                           SchoolLms.Application.Services.NewsTelegramNotifier>();

// ---------- Moliya (Faza 1) ----------
// Oylik hisoblashning YAGONA egasi — `BillingAccrualService` (pastda). Eski
// `TuitionAccrualService` fayli bilan birga P1-21 da o'chirildi: ikkalasi bir
// vaqtda ishlaganda har o'quvchi IKKI MARTA hisob olardi.
builder.Services.AddScoped<SchoolLms.Application.Billing.ILedgerService,
                           SchoolLms.Application.Billing.LedgerService>();
builder.Services.AddScoped<SchoolLms.Application.Billing.IInvoiceService,
                           SchoolLms.Application.Billing.InvoiceService>();
builder.Services.AddScoped<SchoolLms.Application.Billing.ISubscriptionService,
                           SchoolLms.Application.Billing.SubscriptionService>();
builder.Services.AddScoped<SchoolLms.Application.Billing.IDiscountService,
                           SchoolLms.Application.Billing.DiscountService>();
builder.Services.AddScoped<SchoolLms.Application.Billing.ICashShiftService,
                           SchoolLms.Application.Billing.CashShiftService>();
builder.Services.AddScoped<SchoolLms.Application.Billing.IPaymentService,
                           SchoolLms.Application.Billing.PaymentService>();
builder.Services.AddScoped<SchoolLms.Application.Billing.IReceiptService,
                           SchoolLms.Application.Billing.ReceiptService>();
builder.Services.AddHostedService<SchoolLms.Application.Billing.BillingAccrualService>();

// Kamera (videokuzatuv) media-shlyuzi (MediaMTX) bilan ishlash
builder.Services.AddHttpClient<SchoolLms.Application.Services.CameraGateway>();

// ---------- Moliya (billing) ----------
// DIQQAT: qolgan moliya xizmatlari (to'lov, smena, hisob-faktura, chek) hali
// ro'yxatdan o'tmagan — ular P1-15 ning ishi, ro'yxati docs/PENDING_WIRING.md
// da. Bu yerda FAQAT chiqim yo'li uchun kerak bo'lgan ikkitasi bor.
//
// `ILedgerService` — jurnalga yozadigan yagona kod (SPEC §2.2). U `ExpenseService`
// ning konstruktor bog'liqligi, ya'ni usiz chiqim endpoint'i so'rov vaqtida
// "Unable to resolve service" bilan yiqilardi (build vaqtida emas).
builder.Services.AddScoped<SchoolLms.Application.Billing.ILedgerService,
                           SchoolLms.Application.Billing.LedgerService>();
builder.Services.AddScoped<SchoolLms.Application.Billing.IExpenseService,
                           SchoolLms.Application.Billing.ExpenseService>();

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

// Reverse-proxy (Caddy) orqasida: haqiqiy mijoz IP'si (X-Forwarded-For) va HTTPS
// sxemasi (X-Forwarded-Proto) tiklanadi. Busiz chastota chegaralari (login, telegram,
// ommaviy ariza formasi) hamma uchun BITTA IP'ga tushib qoladi.
// FAQAT prod'da yoqamiz (dev'da Vite proxy bu sarlavhalarni yubormaydi).
//
// ZANJIR: Internet → Cloudflare → Caddy → shu konteyner. Caddy `X-Forwarded-For` ni
// AYNAN BITTA yozuv bilan qayta yozadi (`deploy/Caddyfile`, `header_up ... {client_ip}`),
// shuning uchun bu yerda `ForwardLimit = 1` to'g'ri qiymat: eng o'ngdagi (yagona) yozuv
// — haqiqiy tashrifchi. Ikkiga ko'tarish MUMKIN EMAS: u holda mijoz yuborgan qiymat
// hukmga kirib, chegarani soxtalashtirish mumkin bo'lib qolardi (2026-09-22 audit).
// MUHIM: konteyner portini internetga OCHMANG — unga faqat `proxy` xizmati kirsin.
if (!app.Environment.IsDevelopment())
{
    var fwd = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        // Bitta bosqich — Caddy. Sukut ham 1, lekin bu qiymat xavfsizlik qaroridir,
        // shuning uchun oshkor yozilgan.
        ForwardLimit = 1,
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
    // X-Frame-Options YO'Q: u ruxsat ro'yxatini bilmaydi (faqat DENY/SAMEORIGIN), Telegram Web esa Mini App'ni
    // iframe'da ochadi. Clickjacking himoyasi — pastdagi CSP `frame-ancestors` (faqat Telegram domenlari).
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
            // telegram.org — Mini App SDK (telegram-web-app.js). Usiz Mini App Telegram imzosini ololmaydi va
            // o'zini "brauzerda" deb o'ylaydi: bog'lanish saqlanmaydi, har safar parol so'raladi (2026-09-25).
            "script-src 'self' https://www.gstatic.com https://telegram.org; " +
            "worker-src 'self'; " +
            // googleapis/gstatic — FCM web token olish (getToken) so'rovlari.
            "connect-src 'self' ws: wss: https://*.googleapis.com https://*.gstatic.com https://fcm.googleapis.com; " +
            "font-src 'self' data:; " +
            // Faqat Telegram (web.telegram.org Mini App'ni iframe'da ochadi) — boshqa saytlar freymga ololmaydi.
            "frame-ancestors 'self' https://web.telegram.org https://*.telegram.org; object-src 'none'; base-uri 'self'";
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

    // Telegram Mini App: o'qituvchi PWA'si bilan bir xil naqsh. Telegram
    // ilovani `/tg/` bilan ochadi, ichkarida esa marshrut yo'q — shuning uchun
    // `/tg/` ostidagi har qanday fayl bo'lmagan yo'l o'sha index.html ni oladi.
    if (path.Equals("/tg", StringComparison.OrdinalIgnoreCase))
    {
        ctx.Response.Redirect("/tg/", permanent: false);
        return;
    }
    if (path.StartsWith("/tg/", StringComparison.OrdinalIgnoreCase))
    {
        var miniIndex = Path.Combine(webRoot, "tg", "index.html");
        if (!File.Exists(miniIndex)) { ctx.Response.StatusCode = StatusCodes.Status404NotFound; return; }
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.Headers.CacheControl = "no-cache";
        await ctx.Response.SendFileAsync(miniIndex);
        return;
    }

    // Ommaviy ariza formasi: HAR QANDAY hostda SPA `index.html`. Havola maktabning
    // apex domeniga (Instagram bio, Telegram post) qo'yiladi, u yerda esa pastdagi
    // qoida `landing.html` berardi — havola jimgina bosh sahifaga aylanib qolardi.
    // docs/modules/sales-marketing.md D2.
    if (path.StartsWith("/ariza/", StringComparison.OrdinalIgnoreCase))
    {
        var spaIndex = Path.Combine(webRoot, "index.html");
        if (!File.Exists(spaIndex)) { ctx.Response.StatusCode = StatusCodes.Status404NotFound; return; }
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.Headers.CacheControl = "no-cache";
        await ctx.Response.SendFileAsync(spaIndex);
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

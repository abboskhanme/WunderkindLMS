using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Hubs;
using SchoolLms.Application.Services;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ---------- Xizmatlar ----------
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("Default"),
        // Vaqtinchalik DB uzilishlarini (ayniqsa Azure SQL) avtomatik qayta urinish bilan chidaydi.
        sql => sql.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorNumbersToAdd: null)));

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

// O'zgarishlar tarixi (audit) — joriy foydalanuvchini aniqlash uchun HttpContext kerak
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<SchoolLms.Application.Services.AuditService>();

// Shartnoma andozasini (Word) to'ldirish xizmati
builder.Services.AddScoped<SchoolLms.Application.Services.ContractService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Swagger — Bearer token bilan
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "SchoolLms API", Version = "v1" });
    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT tokenni kiriting (Bearer prefiksisiz).",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
    };
    c.AddSecurityDefinition("Bearer", scheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = Array.Empty<string>() });
});

var app = builder.Build();

// ---------- Bazani yaratish va seed ----------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    DbSeeder.Seed(db);
    // Telegram bot tokenini bazadan (yoki bir martalik appsettings urug'idan) yuklaymiz.
    scope.ServiceProvider.GetRequiredService<TelegramService>().Load(db);
}

// ---------- Pipeline ----------

// Xavfsizlik sarlavhalari — barcha javoblarga (statik fayllar va /uploads ham). MIME-sniffing,
// clickjacking va (prod'da) saqlangan XSS'ga qarshi himoya.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    // CSP faqat prod'da — dev'da Swagger UI inline skript/uslublardan foydalanadi, SPA esa Vite
    // serverida alohida beriladi. Leaflet xaritasi unpkg/openstreetmap'dan rasm yuklaydi (img https:).
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

app.UseDefaultFiles();
app.UseStaticFiles();

// Yuklangan materiallar (/uploads) — alohida papkadan xizmat ko'rsatiladi.
var uploadsDir = Path.Combine(app.Environment.ContentRootPath, "uploads");
Directory.CreateDirectory(uploadsDir);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsDir),
    RequestPath = "/uploads",
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");

app.MapFallbackToFile("/index.html");

app.Run();

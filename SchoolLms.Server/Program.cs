using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using SchoolLms.Server.Auth;
using SchoolLms.Server.Data;
using SchoolLms.Server.Hubs;
using SchoolLms.Server.Services;
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

// Kam o'zgaradigan ma'lumotlar (meta, fan/o'qituvchi nomlari) uchun qisqa-TTL kesh.
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ReferenceCache>();

// JWT sozlamalari
var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
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

// Real-time guruh chati (SignalR)
builder.Services.AddSignalR();
builder.Services.AddScoped<ChatService>();

// Oylik to'lovlarni avtomatik hisoblovchi fon xizmati
builder.Services.AddHostedService<SchoolLms.Server.Services.TuitionAccrualService>();

// Telegram bot (e'lon yuborish + ota-onalarni kontakt orqali ro'yxatga olish).
// Token appsettings "Telegram:BotToken" da; bo'sh bo'lsa bot ishga tushmaydi.
builder.Services.AddHttpClient();
builder.Services.AddSingleton<TelegramService>();
builder.Services.AddHostedService<TelegramBotService>();

// O'zgarishlar tarixi (audit) — joriy foydalanuvchini aniqlash uchun HttpContext kerak
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<SchoolLms.Server.Services.AuditService>();

// Shartnoma andozasini (Word) to'ldirish xizmati
builder.Services.AddScoped<SchoolLms.Server.Services.ContractService>();

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

app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");

app.MapFallbackToFile("/index.html");

app.Run();

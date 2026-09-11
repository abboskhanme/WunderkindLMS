using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Tests.Fixtures;

/// <summary>
/// Haqiqiy ilovani (butun middleware pipeline bilan) xotirada ko'taradi va uni
/// <see cref="TestDatabase"/> ga ulaydi.
///
/// <para>Uch narsani kafolatlaydi:</para>
/// <list type="bullet">
///   <item><b>Hech qachon dev bazasiga tegmaydi.</b> appsettings.json dagi
///   <c>localhost:5432</c> satri DI'da almashtiriladi (<see cref="ConfigureWebHost"/>), va
///   <c>ApiFactoryTests.Ilova_throwaway_bazaga_ulanadi</c> buni har yurishda tekshiradi.</item>
///   <item><b>Fon xizmatlari o'chirilgan.</b> <c>TuitionAccrualService</c> ishga tushishi
///   bilanoq oylik to'lovlarni hisoblab bazaga YOZADI — test ma'lumotini buzardi.</item>
///   <item><b>Token ilovaning O'Z kaliti bilan imzolanadi.</b> Kalit DI'dan
///   (<see cref="JwtOptions"/>) olinadi, testda qayta yozilmaydi — ya'ni test tokeni
///   prod validatsiyasidan o'tadi, aks holda o'tmaydi.</item>
/// </list>
///
/// <para>
/// TEntryPoint sifatida <see cref="SchoolLms.Server.Controllers.AuthController"/> olingan,
/// <c>Program</c> emas: <c>Program.cs</c> top-level statements'da yozilgan, uning
/// <c>Program</c> klassi <c>internal</c>. Odatiy yechim — prod fayliga
/// <c>public partial class Program { }</c> qo'shish, lekin test uchun PROD KODINI
/// O'ZGARTIRMASLIK qoidasi bor. WebApplicationFactory TEntryPoint'dan faqat ASSEMBLY'ni
/// oladi (entry point'ni <c>assembly.EntryPoint</c> orqali topadi), shuning uchun
/// SchoolLms.Server ichidagi istalgan public tip yetarli.
/// </para>
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<SchoolLms.Server.Controllers.AuthController>
{
    /// <summary>
    /// Test JWT kaliti. Program.cs Development'dan tashqari muhitda kalitni MAJBURIY qiladi
    /// va uni <c>builder.Configuration</c> dan <c>Build()</c> gacha o'qiydi — ya'ni
    /// WebApplicationFactory.ConfigureAppConfiguration KECH qoladi. Shuning uchun muhit
    /// o'zgaruvchisi orqali beramiz: u <c>WebApplication.CreateBuilder</c> ning standart
    /// AddEnvironmentVariables() manbasiga darrov tushadi.
    /// </summary>
    private const string TestJwtKey = "test-only-jwt-signing-key-0123456789-abcdefghijklmnop";

    static ApiFactory()
    {
        // Butun test yurishi uchun O'ZGARMAS qiymatlar — parallel kolleksiyalar uchun xavfsiz
        // (hammasi bir xil qiymat yozadi). Bazaga oid, testdan testga farq qiladigan
        // sozlamalar bu yerda EMAS, DI'da almashtiriladi.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("Jwt__Key", TestJwtKey);
        // DataProtection standart yo'li — "/app/keys" (prod konteyneri). Testda vaqtinchalik papka.
        Environment.SetEnvironmentVariable("DataProtection__KeysPath",
            Path.Combine(Path.GetTempPath(), "schoollms-tests-keys"));
        // Telegram boti ishga tushmasin (fon xizmati baribir o'chiriladi, bu ikkinchi qulf).
        Environment.SetEnvironmentVariable("Telegram__BotToken", "");
    }

    /// <summary>
    /// Ayni paytda tirik bo'lgan ApiFactory'ning bazasi. Ulanish satrlari muhit
    /// o'zgaruvchilari orqali ham beriladi (pastga qarang) — ular JARAYON BO'YICHA umumiy,
    /// shuning uchun bir vaqtda IKKITA har xil bazali ApiFactory bo'lishi mumkin emas.
    /// Buni jimgina noto'g'ri ishlashiga qo'ymaymiz: konstruktor aniq xato bilan yiqiladi.
    /// </summary>
    private static string? _liveDatabaseName;
    private static readonly Lock LiveLock = new();

    public ApiFactory(TestDatabase database)
    {
        lock (LiveLock)
        {
            if (_liveDatabaseName is not null && _liveDatabaseName != database.Name)
                throw new InvalidOperationException(
                    $"Bir vaqtda ikkita ApiFactory ochib bo'lmaydi ('{_liveDatabaseName}' hali tirik, "
                    + $"yangisi '{database.Name}'). Ulanish satrlari muhit o'zgaruvchilari orqali "
                    + "ham uzatiladi (Program.cs konfiguratsiyani Build() dan oldin o'qiydi), "
                    + "ular esa jarayon bo'yicha umumiy. Avvalgisini Dispose qiling.");
            _liveDatabaseName = database.Name;
        }

        Database = database;

        // Program.cs `builder.Configuration` ni Build() dan OLDIN o'qiydi
        // (ConnectionStrings:Default -> 21-qator). Muhit o'zgaruvchisi — o'sha nuqtada
        // ishlaydigan yagona kanal. DI almashtiruvi (ConfigureWebHost) buni TAKRORLAYDI:
        // ikki qulf, chunki xato qilsak testlar dev bazasiga yozib yuborardi.
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", database.AppRwConnectionString);
        // P1-02 dan keyin Program.cs migratsiyani shu satr bilan qiladi. Hozir o'qilmaydi,
        // lekin oldindan beramiz — o'sha o'zgarish harness'ni buzmasin.
        Environment.SetEnvironmentVariable("ConnectionStrings__Migrator", database.OwnerConnectionString);
        // Redis testda kerak emas — bo'sh bo'lsa ilova xotira keshiga tushadi.
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis", "");

        // HTTPS: Testing muhitida UseHttpsRedirection/UseHsts yoqilgan. `https` bilan
        // so'rasak redirect bo'lmaydi va prod pipeline'i aynan takrorlanadi.
        ClientOptions.BaseAddress = new Uri("https://localhost");
        // Redirect'ni AVTOMATIK kuzatmaymiz — 302/307 ni testda KO'RISHIMIZ kerak,
        // aks holda noto'g'ri yo'naltirish "200 OK" bo'lib yashirinib qolardi.
        ClientOptions.AllowAutoRedirect = false;
    }

    /// <summary>Shu ilova ishlayotgan throwaway baza.</summary>
    public TestDatabase Database { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // ---- 1. Ulanish satrini almashtirish ----
            // Program.cs `builder.Configuration` ni Build() dan OLDIN o'qiydi, shuning uchun
            // konfiguratsiya orqali kech bo'ladi. DbContext registratsiyasini butunlay
            // olib tashlab, o'zimiznikini qo'yamiz — bu Build() dan oldin qo'llanadi.
            Remove(services, d =>
                d.ServiceType == typeof(DbContextOptions<AppDbContext>)
                || d.ServiceType == typeof(DbContextOptions)
                || d.ServiceType == typeof(AppDbContext)
                // EF Core 9+ da AddDbContext qo'shimcha ravishda shuni ro'yxatdan o'tkazadi.
                || (d.ServiceType.IsGenericType
                    && d.ServiceType.Name.StartsWith("IDbContextOptionsConfiguration", StringComparison.Ordinal)));

            services.AddDbContext<AppDbContext>(opt => opt
                .UseNpgsql(Database.AppRwConnectionString)
                .UseSnakeCaseNamingConvention());

            // ---- 2. Fon xizmatlari ----
            // FAQAT loyihaning o'z xizmatlari olinadi. IHostedService'ning HAMMASINI
            // o'chirish mumkin emas: web-serverning o'zi (GenericWebHostService) ham shu
            // interfeys orqali ishga tushadi.
            Remove(services, d => d.ServiceType == typeof(IHostedService)
                && d.ImplementationType?.FullName?.StartsWith("SchoolLms.", StringComparison.Ordinal) == true);
        });
    }

    private static void Remove(IServiceCollection services, Func<ServiceDescriptor, bool> match)
    {
        foreach (var descriptor in services.Where(match).ToList())
            services.Remove(descriptor);
    }

    // ------------------------------------------------------------------
    // Token va klient
    // ------------------------------------------------------------------

    /// <summary>
    /// Istalgan rol uchun JWT — bitta qatorda.
    /// <para>
    /// Rol ATAYLAB <c>string</c>: <c>cashier</c> hali <see cref="Roles"/> da yo'q (P1-04 da
    /// qo'shiladi), shuning uchun enum/konstantaga bog'lanmaydi.
    /// </para>
    /// <para>
    /// DIQQAT: bu metod bazaga YOZMAYDI. <c>admin</c>/<c>superadmin</c>/<c>staff</c>/<c>teacher</c>
    /// rollarida Program.cs ning <c>OnTokenValidated</c> hodisasi foydalanuvchi qatorini
    /// TALAB qiladi (token revocation) — bunday holatda <see cref="ClientAsAsync"/> ni
    /// yoki avval <see cref="SeedUserAsync"/> ni ishlating.
    /// </para>
    /// </summary>
    /// <param name="lifetime">Manfiy qiymat = muddati o'tgan token. JWT standart clock
    /// skew'i 5 daqiqa, shuning uchun "eskirgan" test uchun kamida -6 daqiqa bering.</param>
    public string TokenFor(
        string role,
        string userId,
        string? fullName = null,
        string? email = null,
        IEnumerable<string>? extraClaims = null,
        TimeSpan? lifetime = null)
    {
        var options = Services.GetRequiredService<JwtOptions>();

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, fullName ?? $"Test {role}"),
            new(ClaimTypes.Email, email ?? $"{userId}@test.local"),
            new(ClaimTypes.Role, role),
        };
        if (extraClaims is not null)
            claims.AddRange(extraClaims.Select(c => new Claim(c, "true")));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Key));
        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromHours(options.ExpiresHours)),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Token'siz klient — 401 testlari uchun.</summary>
    public HttpClient AnonymousClient() => CreateClient();

    /// <summary>Tayyor token bilan klient.</summary>
    public HttpClient ClientWithToken(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Rol uchun foydalanuvchi yaratadi va uning tokeni bilan klient qaytaradi — bitta qatorda:
    /// <code>var client = await factory.ClientAsAsync("cashier");</code>
    /// <c>teacher</c> roli uchun qo'shimcha <c>Teacher</c> qatori ham yaratiladi, aks holda
    /// token revocation tekshiruvi uni bloklaydi.
    /// </summary>
    public async Task<HttpClient> ClientAsAsync(string role, params string[] permissions)
    {
        var (user, _) = await SeedUserAsync(role, permissions: permissions);
        return ClientWithToken(TokenFor(role, user.Id, user.FullName, user.Email));
    }

    /// <summary>
    /// Bazaga foydalanuvchi qo'shadi (parol bilan) va (user, ochiq parol) qaytaradi.
    /// OWNER ulanishi orqali yozadi — P1-02 dan keyin <c>app_rw</c> ba'zi jadvallarga
    /// yoza olmaydi, test ma'lumoti esa baribir tayyorlanishi kerak.
    /// </summary>
    public async Task<(AppUser User, string Password)> SeedUserAsync(
        string role,
        string? password = null,
        string? fullName = null,
        string? email = null,
        IEnumerable<string>? permissions = null)
    {
        password ??= "Test-" + Guid.NewGuid().ToString("N")[..10];
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var user = new AppUser
        {
            FullName = fullName ?? $"Test {role} {suffix}",
            Role = role,
            Email = email ?? $"{role}.{suffix}",
            Permissions = permissions?.ToList() ?? new List<string>(),
        };
        user.SetInitialPassword(password);

        await using var db = PostgresFixture.NewContext(Database.OwnerConnectionString);
        db.Users.Add(user);

        if (role == Roles.Teacher)
            db.Teachers.Add(new Teacher { FullName = user.FullName, UserId = user.Id });

        await db.SaveChangesAsync();
        return (user, password);
    }

    /// <summary>Owner ulanishi bilan bazaga bevosita murojaat (seed / tekshirish).</summary>
    public async Task WithDbAsync(Func<AppDbContext, Task> action)
    {
        await using var db = PostgresFixture.NewContext(Database.OwnerConnectionString);
        await action(db);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) ReleaseLive();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        ReleaseLive();
    }

    private void ReleaseLive()
    {
        lock (LiveLock)
            if (_liveDatabaseName == Database.Name)
                _liveDatabaseName = null;
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Copying the canteen menu week by week (client, 2026-10-16) —
/// <c>POST /api/admin/canteen/menu/copy-week</c>, docs/modules/canteen.md.
///
/// <para>
/// Every test works in its own block of far-future weeks (<see cref="NextBlock"/>), so tests never
/// see each other's dishes in the shared throwaway database.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class CanteenWeekCopyTests(ApiFixture fixture)
{
    private const string Url = "/api/admin/canteen/menu/copy-week";

    /// <summary>A Monday no other test touches; each block spans 12 weeks.</summary>
    private static int _block;
    private static readonly DateOnly FirstMonday = new(2100, 1, 4);

    private static DateOnly NextBlock() => FirstMonday.AddDays(84 * Interlocked.Increment(ref _block));

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd");

    // ------------------------------------------------------------------
    //  Copying
    // ------------------------------------------------------------------

    [Fact]
    public async Task Bosh_haftaga_hamma_kunlar_kochadi()
    {
        var from = NextBlock();
        var to = from.AddDays(7);
        await SeedAsync(
            Dish(from, "breakfast", "Bo'tqa", image: "/uploads/botqa.jpg"),
            Dish(from, "breakfast", "Tuxum"),
            Dish(from.AddDays(2), "lunch", "Mastava"),
            Dish(from.AddDays(5), "dinner", "Makaron"),
            Dish(from.AddDays(6), "breakfast", "Yakshanba nonushtasi"));

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var response = await admin.PostAsJsonAsync(Url, new { fromWeekStart = Iso(from), toWeekStart = Iso(to) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<CanteenCopyWeekResultDto>())!;
        Assert.Equal(5, result.Copied);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(0, result.Replaced);
        Assert.Equal([Iso(to)], result.TargetWeeks);
        Assert.Equal([Iso(to), Iso(to.AddDays(2)), Iso(to.AddDays(5)), Iso(to.AddDays(6))], result.Days);

        var week = await WeekAsync(admin, to);
        Assert.Equal(["Bo'tqa", "Tuxum"], week[0].Meals["breakfast"].Select(d => d.Name).Order());
        Assert.Equal("/uploads/botqa.jpg", week[0].Meals["breakfast"].Single(d => d.Name == "Bo'tqa").ImageUrl);
        Assert.Equal("Mastava", Assert.Single(week[2].Meals["lunch"]).Name);
        Assert.Equal("Makaron", Assert.Single(week[5].Meals["dinner"]).Name);
        Assert.Equal("Yakshanba nonushtasi", Assert.Single(week[6].Meals["breakfast"]).Name);
        Assert.Empty(week[1].Meals.Values.SelectMany(x => x));

        // The source week is read, never moved.
        Assert.Equal(5, (await WeekAsync(admin, from)).Sum(Count));
    }

    [Fact]
    public async Task Toldirilgan_kun_ustiga_yozishsiz_409_va_hech_narsa_yozilmaydi()
    {
        var (from, to) = await SeedConflictAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await admin.PostAsJsonAsync(Url,
            new { fromWeekStart = Iso(from), toWeekStart = Iso(to), overwrite = false });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        // Only Tuesday clashes: Thursday is filled too, but the source has no Thursday to copy.
        Assert.Equal([Iso(to.AddDays(1))],
            body.RootElement.GetProperty("days").EnumerateArray().Select(e => e.GetString()!).ToList());
        var message = body.RootElement.GetProperty("message").GetString()!;
        Assert.Contains($"Seshanba {to.AddDays(1):dd.MM}", message);
        Assert.Contains("Ustiga yozish", message);

        var week = await WeekAsync(admin, to);
        Assert.Equal(0, Count(week[0]));
        Assert.Equal("Eski tushlik", Assert.Single(week[1].Meals["lunch"]).Name);
        Assert.Equal("Payshanba taomi", Assert.Single(week[3].Meals["lunch"]).Name);
    }

    [Fact]
    public async Task Ustiga_yozish_faqat_kochadigan_kunlarni_almashtiradi()
    {
        var (from, to) = await SeedConflictAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await admin.PostAsJsonAsync(Url,
            new { fromWeekStart = Iso(from), toWeekStart = Iso(to), overwrite = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<CanteenCopyWeekResultDto>())!;
        Assert.Equal(2, result.Copied);
        Assert.Equal(1, result.Replaced);

        var week = await WeekAsync(admin, to);
        Assert.Equal("Palov", Assert.Single(week[0].Meals["lunch"]).Name);
        Assert.Equal("Sho'rva", Assert.Single(week[1].Meals["lunch"]).Name);
        // A day the source does not fill stays as it was.
        Assert.Equal("Payshanba taomi", Assert.Single(week[3].Meals["lunch"]).Name);
    }

    [Fact]
    public async Task Menyuda_korinmaydigan_taomlar_otkazib_yuboriladi()
    {
        var from = NextBlock();
        var to = from.AddDays(7);
        await SeedAsync(
            Dish(from, "breakfast", "Bo'tqa"),
            Dish(from, "snack", "Noma'lum mahal"),
            Dish(from.AddDays(1), "lunch", "   "));

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var response = await admin.PostAsJsonAsync(Url, new { fromWeekStart = Iso(from), toWeekStart = Iso(to) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<CanteenCopyWeekResultDto>())!;
        Assert.Equal(1, result.Copied);
        Assert.Equal(2, result.Skipped);
        Assert.Equal([Iso(to)], result.Days);

        var copies = await DishesAsync(to, to.AddDays(6));
        Assert.Equal("Bo'tqa", Assert.Single(copies).Name);
    }

    [Fact]
    public async Task Faqat_korinmaydigan_taomli_hafta_400()
    {
        var from = NextBlock();
        await SeedAsync(Dish(from, "snack", "Noma'lum mahal"));

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var response = await admin.PostAsJsonAsync(Url,
            new { fromWeekStart = Iso(from), toWeekStart = Iso(from.AddDays(7)) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await DishesAsync(from.AddDays(7), from.AddDays(13)));
    }

    [Fact]
    public async Task Bir_necha_haftaga_kochiradi()
    {
        var from = NextBlock();
        var to = from.AddDays(7);
        await SeedAsync(Dish(from, "lunch", "Palov"), Dish(from.AddDays(4), "dinner", "Manti"));

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var response = await admin.PostAsJsonAsync(Url,
            new { fromWeekStart = Iso(from), toWeekStart = Iso(to), weeks = 3 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<CanteenCopyWeekResultDto>())!;
        Assert.Equal(6, result.Copied);
        Assert.Equal([Iso(to), Iso(to.AddDays(7)), Iso(to.AddDays(14))], result.TargetWeeks);
        for (var w = 0; w < 3; w++)
        {
            var week = await WeekAsync(admin, to.AddDays(7 * w));
            Assert.Equal("Palov", Assert.Single(week[0].Meals["lunch"]).Name);
            Assert.Equal("Manti", Assert.Single(week[4].Meals["dinner"]).Name);
        }
    }

    [Fact]
    public async Task Bir_necha_haftada_bitta_toqnashuv_hammasini_toxtatadi()
    {
        var from = NextBlock();
        var to = from.AddDays(7);
        await SeedAsync(Dish(from, "lunch", "Palov"), Dish(to.AddDays(14), "lunch", "Uchinchi hafta"));

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var response = await admin.PostAsJsonAsync(Url,
            new { fromWeekStart = Iso(from), toWeekStart = Iso(to), weeks = 3 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        // All or nothing: weeks one and two did not receive a partial copy.
        Assert.Empty(await DishesAsync(to, to.AddDays(13)));
    }

    // ------------------------------------------------------------------
    //  Validation
    // ------------------------------------------------------------------

    [Fact]
    public async Task Notogri_sorovlar_400()
    {
        var from = NextBlock();
        await SeedAsync(Dish(from, "lunch", "Palov"));
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        async Task<HttpStatusCode> Post(object body) => (await admin.PostAsJsonAsync(Url, body)).StatusCode;

        // Not a Monday, not a date.
        Assert.Equal(HttpStatusCode.BadRequest,
            await Post(new { fromWeekStart = Iso(from.AddDays(1)), toWeekStart = Iso(from.AddDays(7)) }));
        Assert.Equal(HttpStatusCode.BadRequest,
            await Post(new { fromWeekStart = Iso(from), toWeekStart = Iso(from.AddDays(9)) }));
        Assert.Equal(HttpStatusCode.BadRequest,
            await Post(new { fromWeekStart = "04.01.2100", toWeekStart = Iso(from.AddDays(7)) }));
        // Week count outside 1..8.
        Assert.Equal(HttpStatusCode.BadRequest,
            await Post(new { fromWeekStart = Iso(from), toWeekStart = Iso(from.AddDays(7)), weeks = 0 }));
        Assert.Equal(HttpStatusCode.BadRequest,
            await Post(new { fromWeekStart = Iso(from), toWeekStart = Iso(from.AddDays(7)), weeks = 9 }));
        // Onto itself, directly or as one of several weeks.
        Assert.Equal(HttpStatusCode.BadRequest,
            await Post(new { fromWeekStart = Iso(from), toWeekStart = Iso(from) }));
        Assert.Equal(HttpStatusCode.BadRequest,
            await Post(new { fromWeekStart = Iso(from), toWeekStart = Iso(from.AddDays(-7)), weeks = 2 }));
        // An empty source week.
        Assert.Equal(HttpStatusCode.BadRequest,
            await Post(new { fromWeekStart = Iso(from.AddDays(7)), toWeekStart = Iso(from.AddDays(14)) }));

        Assert.Empty(await DishesAsync(from.AddDays(-7), from.AddDays(-1)));
    }

    // ------------------------------------------------------------------
    //  RBAC — the controller's [AdminPerm("app", AlsoPerm = "settings")] gate
    // ------------------------------------------------------------------

    [Fact]
    public async Task Tokensiz_401()
    {
        var from = NextBlock();
        using var client = fixture.Api.AnonymousClient();

        var response = await client.PostAsJsonAsync(Url,
            new { fromWeekStart = Iso(from), toWeekStart = Iso(from.AddDays(7)) });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Teacher)]
    public async Task App_ruxsatisiz_403(string role)
    {
        var from = NextBlock();
        await SeedAsync(Dish(from, "lunch", "Palov"));
        using var client = await fixture.Api.ClientAsAsync(role);

        var response = await client.PostAsJsonAsync(Url,
            new { fromWeekStart = Iso(from), toWeekStart = Iso(from.AddDays(7)) });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await DishesAsync(from.AddDays(7), from.AddDays(13)));
    }

    [Fact]
    public async Task App_ruxsatli_xodim_kochiradi()
    {
        var from = NextBlock();
        await SeedAsync(Dish(from, "lunch", "Palov"));
        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff, "app");

        var response = await staff.PostAsJsonAsync(Url,
            new { fromWeekStart = Iso(from), toWeekStart = Iso(from.AddDays(7)) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Palov", Assert.Single(await DishesAsync(from.AddDays(7), from.AddDays(13))).Name);
    }

    /// <summary>
    /// The menu sits in Sozlamalar → Umumiy sozlamalar, which the role editor grants as `settings`
    /// (client, 2026-10-18: a staff member given that page could not see or edit the menu).
    /// </summary>
    [Fact]
    public async Task Sozlamalar_ruxsatli_xodim_kochiradi()
    {
        var from = NextBlock();
        await SeedAsync(Dish(from, "lunch", "Palov"));
        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff, "settings");

        var response = await staff.PostAsJsonAsync(Url,
            new { fromWeekStart = Iso(from), toWeekStart = Iso(from.AddDays(7)) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Palov", Assert.Single(await DishesAsync(from.AddDays(7), from.AddDays(13))).Name);
    }

    [Fact]
    public async Task Sozlamalar_faqat_korish_xodim_403()
    {
        var from = NextBlock();
        await SeedAsync(Dish(from, "lunch", "Palov"));
        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff, "settings" + Roles.ViewSuffix);

        var response = await staff.PostAsJsonAsync(Url,
            new { fromWeekStart = Iso(from), toWeekStart = Iso(from.AddDays(7)) });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await DishesAsync(from.AddDays(7), from.AddDays(13)));
    }

    // ------------------------------------------------------------------
    //  Helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Source Mon + Tue lunch; target Tue and Thu already filled. Tuesday clashes, Thursday does not
    /// (nothing to copy onto it).
    /// </summary>
    private async Task<(DateOnly From, DateOnly To)> SeedConflictAsync()
    {
        var from = NextBlock();
        var to = from.AddDays(7);
        await SeedAsync(
            Dish(from, "lunch", "Palov"),
            Dish(from.AddDays(1), "lunch", "Sho'rva"),
            Dish(to.AddDays(1), "lunch", "Eski tushlik"),
            Dish(to.AddDays(3), "lunch", "Payshanba taomi"));
        return (from, to);
    }

    private static Dish Dish(DateOnly date, string meal, string name, string? image = null) => new()
    {
        Date = Iso(date), Meal = meal, Name = name, Ingredients = $"{name} tarkibi", ImageUrl = image,
    };

    /// <summary>Straight into the table — the API refuses an unknown meal, and these tests need one.</summary>
    private Task SeedAsync(params Dish[] dishes) => fixture.Api.WithDbAsync(async db =>
    {
        db.Dishes.AddRange(dishes);
        await db.SaveChangesAsync();
    });

    private async Task<List<Dish>> DishesAsync(DateOnly start, DateOnly end)
    {
        var (s, e) = (Iso(start), Iso(end));
        var rows = new List<Dish>();
        await fixture.Api.WithDbAsync(async db => rows = await db.Dishes.AsNoTracking()
            .Where(d => string.Compare(d.Date, s) >= 0 && string.Compare(d.Date, e) <= 0)
            .ToListAsync());
        return rows;
    }

    /// <summary>The week as the admin page reads it — seven days, Monday first.</summary>
    private static async Task<List<DayMenuDto>> WeekAsync(HttpClient client, DateOnly monday)
    {
        var week = await client.GetFromJsonAsync<List<DayMenuDto>>(
            $"/api/admin/canteen?start={Iso(monday)}&end={Iso(monday.AddDays(6))}");
        Assert.NotNull(week);
        Assert.Equal(7, week.Count);
        return week;
    }

    private static int Count(DayMenuDto day) => day.Meals.Values.Sum(list => list.Count);
}

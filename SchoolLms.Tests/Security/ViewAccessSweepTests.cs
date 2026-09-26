using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using SchoolLms.Domain;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Security;

/// <summary>
/// Boshqaruv → Rollar, "faqat ko'rish" darajasi (2026-09-26). Mijoz qoidasi: ruxsat bor
/// bo'limning HAR QANDAY ma'lumoti chiqishi kerak — "chiqmay qoladigan holat bo'lmasin".
///
/// <para>
/// Qo'lda ro'yxat tuzish o'rniga serverning o'zi aylanib chiqiladi: <c>api/admin</c> va
/// <c>api/cash</c> ostidagi parametrsiz HAR BIR GET amali "faqat ko'rish" ruxsatlari bor
/// xodimga ochiq bo'lishi shart. Rol bilan ATAYLAB yopilganlari (<see cref="RoleLocked"/>)
/// nomma-nom yozilgan: yangi yopiq endpoint qo'shilsa, bu test uni ko'rsatadi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class ViewAccessSweepTests(ApiFixture fixture)
{
    /// <summary>Admin panel bo'limlari — frontend `adminPermissions` bilan bir xil.</summary>
    internal static readonly string[] Sections =
    [
        "dashboard", "leads", "marketing", "admission", "exams", "seasonalMarks", "students",
        "teachers", "attendance", "attendanceEvening", "attendanceDorm", "schedule", "classes",
        "journal", "messages", "app", "gradesReport", "teacherReports", "contracts", "finance",
        "academicYear", "settings", "staff", "feedback", "gps", "cameras", "discipline",
    ];

    /// <summary>
    /// Rol bilan ATAYLAB yopilgan GET'lar — "faqat ko'rish" ularni ochmaydi. Har biri sababli.
    /// </summary>
    private static readonly HashSet<string> RoleLocked = new(StringComparer.OrdinalIgnoreCase)
    {
        // Login + OCHIQ dastlabki parol eksporti — faqat superadmin (akkaunt egaligi).
        "api/admin/students/export",
        "api/admin/teachers/export",
        // Filiallar — bitta maktab, tizim egasining sozlamasi (menyuda ham faqat superadmin).
        "api/admin/branches",
    };

    public static TheoryData<string> AdminGets()
    {
        var data = new TheoryData<string>();
        foreach (var route in DiscoverGets()) data.Add(route);
        return data;
    }

    [Theory]
    [MemberData(nameof(AdminGets))]
    public async Task Faqat_korish_xodimi_har_bir_admin_GET_ni_oqiydi(string route)
    {
        if (RoleLocked.Contains(route)) return;

        using var viewer = await fixture.Api.ClientAsAsync(
            Roles.Staff, [.. Sections.Select(s => s + Roles.ViewSuffix)]);

        var status = (await viewer.GetAsync("/" + route)).StatusCode;

        Assert.True(status != HttpStatusCode.Forbidden && status != HttpStatusCode.Unauthorized,
            $"/{route} → {(int)status} (faqat ko'rish xodimiga yopiq)");
    }

    /// <summary>"Faqat ko'rish" — yozish yo'q: oddiy bo'lim va moliya ikkalasida ham.</summary>
    [Fact]
    public async Task Faqat_korish_xodimi_yoza_olmaydi()
    {
        using var viewer = await fixture.Api.ClientAsAsync(
            Roles.Staff, [.. Sections.Select(s => s + Roles.ViewSuffix)]);

        var category = await viewer.PostAsJsonAsync("/api/admin/billing/categories",
            new { code = "v" + Guid.NewGuid().ToString("N")[..8], name = "V", isActive = true });
        Assert.Equal(HttpStatusCode.Forbidden, category.StatusCode);

        var student = await viewer.PostAsJsonAsync("/api/admin/students", new { fullName = "Ko'ruvchi Test" });
        Assert.Equal(HttpStatusCode.Forbidden, student.StatusCode);

        var payment = await viewer.PostAsJsonAsync("/api/cash/payments", new { });
        Assert.Equal(HttpStatusCode.Forbidden, payment.StatusCode);
    }

    /// <summary>Kutilmagan rol-darvoza paydo bo'lsa — nomi bilan ko'rsatadi.</summary>
    [Fact]
    public void Topilgan_GET_lar_bor()
    {
        Assert.True(DiscoverGets().Count() > 50, "Aylanib chiqish endpointlarni topmadi.");
    }

    private static IEnumerable<string> DiscoverGets()
    {
        var controllers = typeof(AdminPermAttribute).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in controllers)
        {
            var prefix = c.GetCustomAttribute<RouteAttribute>()?.Template ?? "";
            if (!(prefix.StartsWith("api/admin", StringComparison.OrdinalIgnoreCase)
                  || prefix.StartsWith("api/cash", StringComparison.OrdinalIgnoreCase))) continue;

            foreach (var m in c.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                foreach (var get in m.GetCustomAttributes<HttpGetAttribute>())
                {
                    var route = string.IsNullOrEmpty(get.Template) ? prefix : $"{prefix}/{get.Template}";
                    if (route.Contains('{')) continue; // parametrli marshrut — id kerak
                    if (seen.Add(route)) yield return route;
                }
            }
        }
    }
}

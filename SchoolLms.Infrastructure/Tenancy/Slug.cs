using System.Text.RegularExpressions;

namespace SchoolLms.Infrastructure.Tenancy;

/// <summary>Subdomen yorliqlari (slug) uchun yordamchi: normallashtirish va tekshirish.</summary>
public static partial class Slug
{
    // Tenant sifatida ishlatib bo'lmaydigan (asosiy domen/xizmat) yorliqlari.
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "www", "app", "admin", "api", "platform", "static", "assets", "mail", "ftp",
    };

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,30}[a-z0-9])?$")]
    private static partial Regex Valid();

    /// <summary>Kichik harf, faqat a-z0-9-, bo'shliq/underscore → '-'. Bo'sh natija bo'lishi mumkin.</summary>
    public static string Normalize(string input)
    {
        var s = (input ?? string.Empty).Trim().ToLowerInvariant();
        s = Regex.Replace(s, @"[\s_]+", "-");
        s = Regex.Replace(s, "[^a-z0-9-]", "");
        s = Regex.Replace(s, "-{2,}", "-").Trim('-');
        return s;
    }

    public static bool IsValid(string slug) =>
        !string.IsNullOrEmpty(slug) && !Reserved.Contains(slug) && Valid().IsMatch(slug);
}

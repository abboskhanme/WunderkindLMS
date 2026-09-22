using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;

namespace SchoolLms.Application.Services;

/// <summary>Yuqori paneldagi umumiy qidiruvning bitta natijasi.</summary>
/// <param name="Kind">student | teacher | class | group | parent | lead</param>
/// <param name="Url">Bosilganda ochiladigan admin sahifasi.</param>
/// <param name="Archived">Arxivdagi yozuv — ro'yxat oxirida, belgi bilan.</param>
public record GlobalSearchHitDto(
    string Kind, string Id, string Title, string Subtitle, string Url, bool Archived = false);

/// <summary>
/// Umumiy qidiruv (mijoz, 2026-09-22: "istalgan narsani qidira olsin — o'quvchi,
/// o'qituvchi, guruh; ism, familiya, telefon bo'yicha ham muammosiz topsin").
///
/// <para><b>Ism bo'yicha</b>: so'rov so'zlarga bo'linadi va HAR BIR so'z ismning
/// istalgan joyida bo'lishi kerak — "Ali Valiyev" ham, "valiyev ali" ham, "val" ham
/// topadi. Katta-kichik harf va o'zbekcha tutuq belgisining turlari (ʻ ʼ ' ` ‘ ’)
/// farq qilmaydi: "Go'zal" bilan "Goʻzal" bir xil.</para>
///
/// <para><b>Telefon bo'yicha</b>: so'rovdagi faqat raqamlar olinadi (kamida 3 ta) va
/// saqlangan raqamning raqamlari ichidan qidiriladi — "+998 90 123-45-67",
/// "901234567", "4567" hammasi bitta yozuvni topadi.</para>
///
/// <para><b>Ruxsat</b>: har bo'lim o'z ruxsati bilan — ruxsati yo'q bo'limning
/// natijasi umuman qaytmaydi (ism ham, telefon ham). Pul ma'lumoti bu yerda yo'q.</para>
///
/// <para>Xotirada filtrlanadi: maktab bitta, eng katta jadval — bir necha yuz
/// o'quvchi; faqat kerakli ustunlar o'qiladi. Shu tufayli tutuq belgisi va
/// raqamlarni normallashtirish bazadagi formatga bog'liq emas.</para>
/// </summary>
public static class GlobalSearchQuery
{
    public const int MinLength = 2;
    public const int PerKind = 6;

    public static async Task<List<GlobalSearchHitDto>> RunAsync(
        IAppDbContext db, string? query, Func<string, bool> canSee, CancellationToken ct = default)
    {
        var q = Normalize(query);
        if (q.Length < MinLength) return [];
        var tokens = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var digits = PhoneUtil.DigitsOnly(query);
        // Raqam juda qisqa bo'lsa telefon bo'yicha qidirilmaydi — "5" hamma raqamda bor.
        var phoneNeedle = digits.Length >= 3 ? digits : null;

        bool NameMatch(params string?[] parts)
        {
            var hay = Normalize(string.Join(' ', parts.Where(p => !string.IsNullOrWhiteSpace(p))));
            return tokens.All(hay.Contains);
        }
        bool PhoneMatch(params string?[] phones) =>
            phoneNeedle is not null && phones.Any(p => PhoneUtil.DigitsOnly(p).Contains(phoneNeedle));
        // Ism so'rovning birinchi so'zi bilan boshlansa — tepada.
        int Rank(string title) => Normalize(title).StartsWith(tokens[0], StringComparison.Ordinal) ? 0 : 1;

        var hits = new List<GlobalSearchHitDto>();

        if (canSee("students"))
        {
            var students = await db.Students.AsNoTracking()
                .Select(s => new { s.Id, s.FullName, s.ClassName, s.Phone, s.ParentPhone, s.ParentFullName, s.IsArchived })
                .ToListAsync(ct);
            hits.AddRange(students
                .Where(s => NameMatch(s.FullName) || PhoneMatch(s.Phone, s.ParentPhone))
                .OrderBy(s => s.IsArchived).ThenBy(s => Rank(s.FullName)).ThenBy(s => s.FullName, StringComparer.Ordinal)
                .Take(PerKind)
                .Select(s => new GlobalSearchHitDto(
                    "student", s.Id, s.FullName,
                    Join(string.IsNullOrWhiteSpace(s.ClassName) ? "Sinfsiz" : s.ClassName, s.ParentPhone),
                    $"/admin/students/{s.Id}", s.IsArchived)));

            var guardians = await db.Guardians.AsNoTracking()
                .Select(g => new { g.Id, g.FullName, g.Phone })
                .ToListAsync(ct);
            hits.AddRange(guardians
                .Where(g => NameMatch(g.FullName) || PhoneMatch(g.Phone))
                .OrderBy(g => Rank(g.FullName)).ThenBy(g => g.FullName, StringComparer.Ordinal)
                .Take(PerKind)
                .Select(g => new GlobalSearchHitDto(
                    "parent", g.Id, g.FullName, g.Phone,
                    $"/admin/parents?q={Uri.EscapeDataString(g.FullName)}")));
        }

        if (canSee("teachers"))
        {
            var teachers = await db.Teachers.AsNoTracking()
                .Select(t => new { t.Id, t.FullName, t.Phone, t.HomeroomClass, t.IsArchived })
                .ToListAsync(ct);
            hits.AddRange(teachers
                .Where(t => NameMatch(t.FullName) || PhoneMatch(t.Phone))
                .OrderBy(t => t.IsArchived).ThenBy(t => Rank(t.FullName)).ThenBy(t => t.FullName, StringComparer.Ordinal)
                .Take(PerKind)
                .Select(t => new GlobalSearchHitDto(
                    "teacher", t.Id, t.FullName,
                    Join(string.IsNullOrWhiteSpace(t.HomeroomClass) ? null : $"{t.HomeroomClass} sinf rahbari", t.Phone),
                    $"/admin/teachers?q={Uri.EscapeDataString(t.FullName)}{(t.IsArchived ? "&tab=archived" : "")}",
                    t.IsArchived)));
        }

        if (canSee("classes"))
        {
            var classes = await db.Classes.AsNoTracking()
                .Where(c => !c.IsArchived)
                .Select(c => new { c.Id, c.Name, c.Grade })
                .ToListAsync(ct);
            hits.AddRange(classes
                .Where(c => NameMatch(c.Name))
                .OrderBy(c => c.Grade).ThenBy(c => c.Name, StringComparer.Ordinal)
                .Take(PerKind)
                .Select(c => new GlobalSearchHitDto("class", c.Id, c.Name, "Sinf", $"/admin/classes/{c.Id}")));

            var groups = await db.StudyGroups.AsNoTracking()
                .Where(g => !g.IsArchived)
                .Select(g => new { g.Id, g.Name })
                .ToListAsync(ct);
            hits.AddRange(groups
                .Where(g => NameMatch(g.Name))
                .OrderBy(g => Rank(g.Name)).ThenBy(g => g.Name, StringComparer.Ordinal)
                .Take(PerKind)
                .Select(g => new GlobalSearchHitDto(
                    "group", g.Id.ToString(), g.Name, "O'quv guruhi", $"/admin/groups/{g.Id}/students")));
        }

        if (canSee("leads"))
        {
            var leads = await db.Leads.AsNoTracking()
                .Select(l => new { l.Id, l.FullName, l.ParentFullName, l.ParentPhone })
                .ToListAsync(ct);
            hits.AddRange(leads
                .Where(l => NameMatch(l.FullName, l.ParentFullName) || PhoneMatch(l.ParentPhone))
                .OrderBy(l => Rank(l.FullName)).ThenBy(l => l.FullName, StringComparer.Ordinal)
                .Take(PerKind)
                // Lidlar doskasi dizayni muzlatilgan — alohida lidni ochmaymiz, doskaga olib boramiz.
                .Select(l => new GlobalSearchHitDto("lead", l.Id, l.FullName, Join("Lid", l.ParentPhone), "/admin/leads")));
        }

        return hits;
    }

    private static string Join(params string?[] parts) =>
        string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    /// <summary>Kichik harf, tutuq belgisining barcha turlari → <c>'</c>, bo'shliqlar bittaga.</summary>
    public static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var chars = s.Trim().ToLowerInvariant().Select(c => c switch
        {
            'ʻ' or 'ʼ' or '‘' or '’' or '`' or '´' => '\'',
            _ => c,
        }).ToArray();
        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}

using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Finding the account behind what a person typed as "login" (2026-10-07). Logins look like
//  "rasulovmuhammadiso" (surname + name, lower case). Over a month on the server, 148 distinct
//  sign-ins failed in the Mini App: people type "Rasulov Muhammadiso", the phone keyboard
//  capitalises the first letter, add the patronymic ("... Ma'rufjon o'g'li"). So, in order:
//
//    1. the login exactly as typed (trimmed);
//    2. the same ignoring case, spaces and apostrophes (ʻ ’ ' `);
//    3. with three or more words — the first two (surname + name) the same way.
//
//  Steps 2–3 count only when exactly ONE account matches — never a guess between two people.
//  The password is still checked by the caller; this only maps spellings to the same login.
// ===========================================================================

public static class LoginLookup
{
    private static readonly char[] Apostrophes = ['\'', 'ʻ', 'ʼ', '’', '‘', '`'];

    /// <summary>Lower case without spaces and apostrophes — "Rasulov Muhammadiso" → "rasulovmuhammadiso".</summary>
    public static string Normalize(string? value)
    {
        var chars = (value ?? "").ToLowerInvariant()
            .Where(c => !char.IsWhiteSpace(c) && Array.IndexOf(Apostrophes, c) < 0).ToArray();
        return new string(chars);
    }

    public static async Task<AppUser?> FindAsync(IAppDbContext db, string? typed, CancellationToken ct = default)
    {
        var login = (typed ?? "").Trim();
        if (login.Length == 0) return null;

        var exact = await db.Users.FirstOrDefaultAsync(u => u.Email == login, ct);
        if (exact is not null) return exact;

        var whole = Normalize(login);
        if (whole.Length >= 3 && await UniqueAsync(db, whole, ct) is { } byWhole) return byWhole;

        var words = login.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 3)
        {
            var firstTwo = Normalize(words[0] + words[1]);
            if (firstTwo.Length >= 3 && await UniqueAsync(db, firstTwo, ct) is { } byName) return byName;
        }
        return null;
    }

    private static async Task<AppUser?> UniqueAsync(IAppDbContext db, string normalized, CancellationToken ct)
    {
        var matches = await db.Users
            .Where(u => u.Email.ToLower().Replace(" ", "").Replace("'", "").Replace("ʻ", "").Replace("ʼ", "")
                            .Replace("’", "").Replace("‘", "").Replace("`", "") == normalized)
            .Take(2).ToListAsync(ct);
        return matches.Count == 1 ? matches[0] : null;
    }
}

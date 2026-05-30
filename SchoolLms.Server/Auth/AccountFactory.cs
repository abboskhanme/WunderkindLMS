using SchoolLms.Server.Data;
using SchoolLms.Server.Models;
using System.Security.Cryptography;
using System.Text;

namespace SchoolLms.Server.Auth;

/// <summary>
/// O'quvchi/o'qituvchi yaratilganda ularga login va parol generatsiya qilib,
/// tizim akkaunti (AppUser) yaratadi. Login — FISH (ism-familiya) dan tuzilgan,
/// email EMAS, takrorlanmas username (masalan "voxidjonovabduxalil"). Agar bunday
/// login band bo'lsa, oxiriga raqam qo'shiladi (voxidjonovabduxalil2, ...3).
/// </summary>
public static class AccountFactory
{
    // Chalkashtirmaydigan belgilar (0/O, 1/l/I kabilar yo'q) — qo'lda terish oson.
    private static readonly char[] Alphabet = "abcdefghijkmnpqrstuvwxyz23456789".ToCharArray();

    /// <summary>Tasodifiy parol (standart 6 belgi).</summary>
    public static string GeneratePassword(int length = 6)
    {
        var bytes = RandomNumberGenerator.GetBytes(length);
        var sb = new StringBuilder(length);
        foreach (var b in bytes) sb.Append(Alphabet[b % Alphabet.Length]);
        return sb.ToString();
    }

    /// <summary>
    /// FISH dan takrorlanmas login (username) tuzadi: familiya + ism (birinchi ikkita so'z)
    /// lotin harflariga keltirilib, belgisiz qo'shiladi (masalan "voxidjonovabduxalil").
    /// Bunday login band bo'lsa, oxiriga raqam qo'shiladi (voxidjonovabduxalil2).
    /// Unikallik BARCHA foydalanuvchilar bo'yicha tekshiriladi — bazadagi va hali
    /// saqlanmagan (shu kontekstga qo'shilgan) akkauntlar.
    /// </summary>
    public static string GenerateUsername(AppDbContext db, string fullName)
    {
        var baseName = BuildBase(fullName);
        if (baseName.Length == 0) baseName = "user";

        // Band bo'lgan login'lar: bazadagi + hali saqlanmagan (Local) akkauntlar.
        var taken = new HashSet<string>(
            db.Users.Select(u => u.Email).ToList(), StringComparer.OrdinalIgnoreCase);
        foreach (var local in db.Users.Local) taken.Add(local.Email);

        var candidate = baseName;
        var n = 1;
        while (taken.Contains(candidate))
        {
            n++;
            candidate = baseName + n;
        }
        return candidate;
    }

    /// <summary>
    /// Berilgan rol va FISH uchun yangi akkaunt yaratadi (db.Users ga qo'shadi, lekin saqlamaydi —
    /// chaqiruvchi SaveChanges qiladi). Generatsiya qilingan parol AppUser.PlainPassword da saqlanadi.
    /// </summary>
    public static AppUser CreateAccountFor(AppDbContext db, string role, string fullName)
    {
        var password = GeneratePassword();
        var user = new AppUser
        {
            FullName = fullName,
            Role = role,
            Email = GenerateUsername(db, fullName),
            PasswordHash = PasswordHasher.Hash(password),
            PlainPassword = password,
        };
        db.Users.Add(user);
        return user;
    }

    /// <summary>FISH ning birinchi ikkita so'zini (familiya + ism) lotinlashtirib qo'shadi.</summary>
    private static string BuildBase(string fullName)
    {
        var words = (fullName ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        foreach (var word in words.Take(2)) sb.Append(Translit(word));
        return sb.ToString();
    }

    /// <summary>So'zni kichik lotin harf + raqamga keltiradi (kirill→lotin; apostrof/belgilar tashlanadi).</summary>
    private static string Translit(string word)
    {
        var sb = new StringBuilder(word.Length);
        foreach (var ch in word.ToLowerInvariant())
        {
            if (Cyrillic.TryGetValue(ch, out var mapped)) sb.Append(mapped);
            else if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            // qolgan belgilar (o', g', ʻ, '-', bo'sh joy va h.k.) tashlab yuboriladi
        }
        return sb.ToString();
    }

    // Kirill (o'zbek) harflarini lotinga o'tkazish — FISH kirillda kiritilsa ham ishlashi uchun.
    private static readonly Dictionary<char, string> Cyrillic = new()
    {
        ['а'] = "a",
        ['б'] = "b",
        ['в'] = "v",
        ['г'] = "g",
        ['ғ'] = "g",
        ['д'] = "d",
        ['е'] = "e",
        ['ё'] = "yo",
        ['ж'] = "j",
        ['з'] = "z",
        ['и'] = "i",
        ['й'] = "y",
        ['к'] = "k",
        ['қ'] = "q",
        ['л'] = "l",
        ['м'] = "m",
        ['н'] = "n",
        ['о'] = "o",
        ['п'] = "p",
        ['р'] = "r",
        ['с'] = "s",
        ['т'] = "t",
        ['у'] = "u",
        ['ў'] = "o",
        ['ф'] = "f",
        ['х'] = "x",
        ['ҳ'] = "h",
        ['ц'] = "ts",
        ['ч'] = "ch",
        ['ш'] = "sh",
        ['щ'] = "sh",
        ['ъ'] = "",
        ['ы'] = "i",
        ['ь'] = "",
        ['э'] = "e",
        ['ю'] = "yu",
        ['я'] = "ya",
    };
}

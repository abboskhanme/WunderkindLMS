using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Telegram akkauntini bog'lash — bir martalik kod. SPEC §6 Faza 3.
// ===========================================================================
//
//  OQIM
//  ----
//    1. Admin panelda xodim kerakli foydalanuvchi uchun kod chiqaradi
//       (`POST /api/admin/telegram/link-codes`). Kod EKRANDA bir marta
//       ko'rinadi va telefon orqali egasiga aytiladi.
//    2. Ota-ona / o'qituvchi Mini App'da kodni kiritadi
//       (`POST /api/tg/link`, ichida imzolangan `initData` ham bor).
//    3. Kod to'g'ri bo'lsa `telegram_accounts` qatori yoziladi va shu zahoti
//       JWT beriladi — keyingi kirishlarda kod kerak emas.
//
//  NEGA RAQAM ORQALI EMAS
//  ----------------------
//  Telegram bergan telefon raqami ishonchli, lekin raqamdan AKKAUNTGA
//  o'tish bu bazada ishonchli emas: `students.parent_phone` qo'lda
//  kiritiladi, oilada bitta raqam bo'ladi, aka-ukalarda takrorlanadi va
//  hech kim uni tasdiqlamaydi. Ya'ni "raqamni aytdim — akkauntni oldim"
//  bo'lib qolardi. Kod bilan bog'lanishning aniq egasi, vaqti va uni
//  bergan xodimi bor. Batafsil: docs/ASSUMPTIONS.md.
//
//  KOD OCHIQ SAQLANMAYDI
//  ---------------------
//  Bazada faqat SHA-256 hash'i. Kodning umri 15 daqiqa va u faqat tirik
//  Telegram sessiyasi bilan birga ishlaydi, shuning uchun tuz (salt)
//  qo'shilmagan — buning evaziga qidiruv bitta indeksli so'rov bo'lib
//  qoladi. Kod uzunligi (8 belgi × 32 harfli alifbo ≈ 2^40) oldindan
//  hisoblangan jadvalni ma'nosiz qiladi.
// ===========================================================================

/// <summary>Kodni ishlatish natijasi.</summary>
public enum LinkCodeResult
{
    Ok = 0,
    /// <summary>Bunday kod yo'q, muddati o'tgan yoki allaqachon ishlatilgan.</summary>
    Invalid,
    /// <summary>Bu Telegram akkaunti allaqachon boshqa foydalanuvchiga bog'langan.</summary>
    TelegramAlreadyLinked,
    /// <summary>Bu foydalanuvchiga allaqachon boshqa Telegram akkaunti bog'langan.</summary>
    UserAlreadyLinked,
}

/// <summary>Chiqarilgan kod — ochiq matni FAQAT shu javobda bir marta ko'rinadi.</summary>
public sealed record IssuedLinkCode(string Code, DateTime ExpiresAt);

/// <summary>
/// Telegram bog'lanishini boshqaradi: kod chiqarish, kodni ishlatish,
/// bog'lanishni topish va uzish. Holatsiz — chaqiruv joyida yasaladi.
/// </summary>
public sealed class TelegramLinkService(IAppDbContext db)
{
    /// <summary>Kod shuncha vaqt yashaydi. Qisqa — u telefonda aytiladi va darrov kiritiladi.</summary>
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Chalkashtirmaydigan alifbo: 0/O va 1/I/L yo'q (telefon orqali aytiladi).</summary>
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private const int CodeLength = 8;

    /// <summary>
    /// Foydalanuvchi uchun yangi kod chiqaradi. Shu foydalanuvchining oldingi
    /// ISHLATILMAGAN kodlari darhol muddati o'tgan holatga o'tkaziladi — bir
    /// vaqtda ikkita tirik kod bo'lsa, qaysi biri kimga aytilgani noaniq bo'lardi.
    /// </summary>
    public async Task<IssuedLinkCode> IssueAsync(
        string userId, string createdByUserId, CancellationToken ct = default)
    {
        var now = AppClock.Now;

        var live = await db.TelegramLinkCodes
            .Where(c => c.UserId == userId && c.UsedAt == null && c.ExpiresAt > now)
            .ToListAsync(ct);
        foreach (var old in live) old.ExpiresAt = now;

        // To'qnashuv amalda deyarli bo'lmaydi (unikal indeks), lekin bo'lsa —
        // qayta uriniladi, xato qaytarilmaydi.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var code = NewCode();
            var hash = HashOf(code);
            if (await db.TelegramLinkCodes.AnyAsync(c => c.CodeHash == hash, ct)) continue;

            var expiresAt = now.Add(CodeLifetime);
            db.TelegramLinkCodes.Add(new TelegramLinkCode
            {
                CodeHash = hash,
                UserId = userId,
                CreatedByUserId = createdByUserId,
                CreatedAt = now,
                ExpiresAt = expiresAt,
            });
            await db.SaveChangesAsync(ct);
            return new IssuedLinkCode(Format(code), expiresAt);
        }

        throw new InvalidOperationException("Bog'lanish kodini yaratib bo'lmadi (takroriy to'qnashuv).");
    }

    /// <summary>
    /// Kodni ishlatadi va bog'lanishni yozadi. Muvaffaqiyatda bog'langan
    /// foydalanuvchi qaytadi.
    /// </summary>
    public async Task<(LinkCodeResult Result, AppUser? User)> RedeemAsync(
        string? rawCode, TelegramWebAppUser tgUser, CancellationToken ct = default)
    {
        var normalized = Normalize(rawCode);
        if (normalized.Length != CodeLength) return (LinkCodeResult.Invalid, null);

        var now = AppClock.Now;
        var hash = HashOf(normalized);

        var entry = await db.TelegramLinkCodes
            .FirstOrDefaultAsync(c => c.CodeHash == hash, ct);
        if (entry is null || entry.UsedAt is not null || entry.ExpiresAt <= now)
            return (LinkCodeResult.Invalid, null);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == entry.UserId, ct);
        if (user is null) return (LinkCodeResult.Invalid, null);

        // Ikki tomonlama unikallik BAZADA ham bor (GuardianModel) — bu yerdagi
        // tekshiruv 23505 o'rniga tushunarli xabar berish uchun.
        if (await db.TelegramAccounts.AnyAsync(a => a.TelegramUserId == tgUser.Id, ct))
            return (LinkCodeResult.TelegramAlreadyLinked, null);
        if (await db.TelegramAccounts.AnyAsync(a => a.UserId == user.Id, ct))
            return (LinkCodeResult.UserAlreadyLinked, null);

        db.TelegramAccounts.Add(new TelegramAccount
        {
            TelegramUserId = tgUser.Id,
            UserId = user.Id,
            Username = tgUser.Username,
            DisplayName = tgUser.DisplayName,
            LinkedByUserId = entry.CreatedByUserId,
            LinkedAt = now,
            LastSeenAt = now,
        });

        entry.UsedAt = now;
        entry.UsedByTelegramUserId = tgUser.Id;
        await db.SaveChangesAsync(ct);

        return (LinkCodeResult.Ok, user);
    }

    /// <summary>
    /// Login va parol tekshirilgandan keyin bevosita bog'lash (kodsiz). Bitta Telegram — bitta akkaunt; bitta
    /// akkauntga esa bir nechta Telegram bog'lanishi mumkin.
    /// </summary>
    public async Task<LinkCodeResult> LinkDirectAsync(AppUser user, TelegramWebAppUser tgUser, CancellationToken ct = default)
    {
        var existing = await db.TelegramAccounts.FirstOrDefaultAsync(a => a.TelegramUserId == tgUser.Id, ct);
        if (existing is not null)
            return existing.UserId == user.Id ? LinkCodeResult.Ok : LinkCodeResult.TelegramAlreadyLinked;
        // Bitta hisobga bir nechta Telegram mumkin (mijoz, 2026-09-25) — login va parolni bilgan istalgan
        // Telegram'dan kiriladi.

        var now = AppClock.Now;
        db.TelegramAccounts.Add(new TelegramAccount
        {
            TelegramUserId = tgUser.Id,
            UserId = user.Id,
            Username = tgUser.Username,
            DisplayName = tgUser.DisplayName,
            LinkedByUserId = user.Id,
            LinkedAt = now,
            LastSeenAt = now,
        });
        await db.SaveChangesAsync(ct);
        return LinkCodeResult.Ok;
    }

    /// <summary>Telegram id bo'yicha bog'langan akkaunt (yo'q bo'lsa null).</summary>
    public async Task<TelegramAccount?> FindAsync(long telegramUserId, CancellationToken ct = default) =>
        await db.TelegramAccounts.FirstOrDefaultAsync(a => a.TelegramUserId == telegramUserId, ct);

    /// <summary>Bog'lanishni uzadi. Topilmasa false (chaqiruvchi 404 beradi).</summary>
    public async Task<bool> UnlinkAsync(long telegramUserId, CancellationToken ct = default)
    {
        var account = await db.TelegramAccounts
            .FirstOrDefaultAsync(a => a.TelegramUserId == telegramUserId, ct);
        if (account is null) return false;

        db.TelegramAccounts.Remove(account);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ------------------------------------------------------------------
    //  Kod: yaratish, formatlash, normallashtirish, hash
    // ------------------------------------------------------------------

    private static string NewCode()
    {
        // Modulo siljishi yo'q: alifbo uzunligi 32 = 2^5, ya'ni bayt teng bo'linadi.
        var bytes = RandomNumberGenerator.GetBytes(CodeLength);
        var sb = new StringBuilder(CodeLength);
        foreach (var b in bytes) sb.Append(Alphabet[b % Alphabet.Length]);
        return sb.ToString();
    }

    /// <summary>Ko'rsatish shakli: "ABCD-EFGH" (telefon orqali aytish oson).</summary>
    private static string Format(string code) => $"{code[..4]}-{code[4..]}";

    /// <summary>
    /// Kiritilgan matndan kodni ajratadi: katta harf, alifboda yo'q belgilar
    /// (chiziqcha, bo'sh joy) tashlanadi. "abcd-efgh" ham, "ABCDEFGH" ham ishlaydi.
    /// </summary>
    private static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var sb = new StringBuilder(CodeLength);
        foreach (var ch in raw.ToUpperInvariant())
            if (Alphabet.Contains(ch)) sb.Append(ch);
        return sb.ToString();
    }

    private static string HashOf(string normalizedCode) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedCode)));
}

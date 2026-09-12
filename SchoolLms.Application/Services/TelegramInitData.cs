using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Telegram Mini App `initData` tekshiruvi — SPEC §6 Faza 3.
// ===========================================================================
//
//  ALGORITM (Telegram hujjati: "Validating data received via the Mini App")
//  -----------------------------------------------------------------------
//    secret_key        = HMAC_SHA256(key: "WebAppData", message: bot_token)
//    data_check_string = `hash` OLIB TASHLANGAN, kalit bo'yicha alifbo
//                        tartibida saralangan "key=value" qatorlari, '\n' bilan
//    kutilgan          = hex(HMAC_SHA256(key: secret_key, message: data_check_string))
//    to'g'ri, agar     = kutilgan == initData'dagi `hash`
//
//  DIQQAT — E'TIBORDAN QOLADIGAN UCH NUQTA:
//
//  1) `hash` DAN BOSHQA HECH NARSA OLIB TASHLANMAYDI. Telegram yangi
//     `signature` maydonini ham yuboradi va u data-check-string ICHIDA
//     qoladi (u faqat uchinchi tomon uchun Ed25519 tekshiruvida chiqariladi).
//     Noma'lum maydonni "ehtiyot uchun" tashlab yuborish — imzoni buzish.
//
//  2) Qiymatlar URL-dekod QILINADI, `Uri.UnescapeDataString` bilan. HTML-form
//     dekoderi (`HttpUtility.UrlDecode`) `+` ni bo'sh joyga aylantiradi;
//     Telegram esa `encodeURIComponent` ishlatadi, ya'ni bo'sh joy `%20`, `+`
//     esa haqiqiy `+`. Noto'g'ri dekoder `user` JSON'idagi `+` ni yeb qo'yib,
//     to'g'ri imzoni rad etardi.
//
//  3) TAQQOSLASH DOIM DOIMIY VAQTDA (`CryptographicOperations.FixedTimeEquals`).
//     Oddiy `==` javob vaqti orqali hash'ni belgima-belgi topishga yo'l ochadi.
//
//  BU KLASS BAZAGA VA HTTP GA TEGMAYDI — sof funksiya, shuning uchun uni
//  ma'lum vektorlar bilan test qilish mumkin (SchoolLms.Tests/TelegramInitDataTests.cs).
//
//  TOKEN LOG'GA CHIQMAYDI. Xato holatida qaytadigan yagona narsa —
//  <see cref="InitDataError"/> enum'i; unda na token, na hash, na foydalanuvchi
//  ma'lumoti bor.
// ===========================================================================

/// <summary>Tekshiruv nega o'tmadi. Mijozga faqat umumiy xabar ko'rsatiladi.</summary>
public enum InitDataError
{
    None = 0,
    /// <summary>Bo'sh satr yoki `hash` maydoni yo'q.</summary>
    Malformed,
    /// <summary>Bot tokeni sozlanmagan — tekshirib bo'lmaydi (ilova xatosi, mijoz xatosi emas).</summary>
    BotNotConfigured,
    /// <summary>HMAC mos kelmadi — ma'lumot Telegram'dan emas yoki o'zgartirilgan.</summary>
    BadSignature,
    /// <summary>`auth_date` yo'q yoki son emas.</summary>
    MissingAuthDate,
    /// <summary>`auth_date` juda eski (replay) yoki kelajakdan.</summary>
    Expired,
    /// <summary>`user` maydoni yo'q yoki ichida `id` yo'q.</summary>
    NoUser,
}

/// <summary>`initData.user` — bizga kerak bo'lgan maydonlar.</summary>
public sealed record TelegramWebAppUser(
    long Id, string FirstName, string? LastName, string? Username, string? LanguageCode)
{
    /// <summary>Ko'rsatish uchun ism ("Ism Familiya" yoki `@username`).</summary>
    public string DisplayName
    {
        get
        {
            var name = string.Join(" ", new[] { FirstName, LastName }
                .Where(x => !string.IsNullOrWhiteSpace(x)));
            if (!string.IsNullOrWhiteSpace(name)) return name;
            return string.IsNullOrWhiteSpace(Username) ? Id.ToString() : "@" + Username;
        }
    }
}

/// <summary>Tekshiruv natijasi. <see cref="Ok"/> false bo'lsa <see cref="User"/> DOIM null.</summary>
public sealed record InitDataResult(bool Ok, InitDataError Error, TelegramWebAppUser? User, DateTimeOffset AuthDate)
{
    public static InitDataResult Fail(InitDataError error) =>
        new(false, error, null, DateTimeOffset.MinValue);
}

/// <summary>Telegram Mini App `initData` satrini tekshiradi (SPEC §6 Faza 3).</summary>
public static class TelegramInitData
{
    /// <summary>
    /// `auth_date` shundan eski bo'lsa rad etiladi.
    ///
    /// <para>
    /// <b>24 soat</b> — Telegram hujjatining o'z tavsiyasi. Undan qisqasi bu yerda
    /// deyarli hech narsa bermaydi: muvaffaqiyatli tekshiruv 12 soatlik JWT beradi
    /// (<c>JwtOptions.ExpiresHours</c>), ya'ni o'g'irlangan `initData` ning eng yomon
    /// natijasi baribir o'sha 12 soat. Undan uzog'i esa "abadiy replay" bo'lardi —
    /// aynan shuning oldi olinmoqda. Qisqartirish kerak bo'lsa — bitta konstanta.
    /// </para>
    /// </summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    /// <summary>Soat farqiga yo'l qo'yiladigan oraliq — `auth_date` biroz kelajakda bo'lishi mumkin.</summary>
    private static readonly TimeSpan FutureSkew = TimeSpan.FromMinutes(5);

    /// <summary>Telegram belgilagan HMAC "urug'i" — o'zgarmas satr.</summary>
    private const string SecretSalt = "WebAppData";

    /// <summary>
    /// `initData` ni tekshiradi. <paramref name="now"/> — test uchun (odatda
    /// <c>DateTimeOffset.UtcNow</c>).
    /// </summary>
    /// <param name="initData">Telegram sahifaga bergan XOM satr (`window.Telegram.WebApp.initData`).</param>
    /// <param name="botToken">Bot tokeni. Bo'sh bo'lsa <see cref="InitDataError.BotNotConfigured"/>.</param>
    public static InitDataResult Validate(string? initData, string? botToken, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(botToken)) return InitDataResult.Fail(InitDataError.BotNotConfigured);
        if (string.IsNullOrWhiteSpace(initData)) return InitDataResult.Fail(InitDataError.Malformed);

        var fields = Parse(initData);
        if (!fields.TryGetValue("hash", out var providedHash) || providedHash.Length == 0)
            return InitDataResult.Fail(InitDataError.Malformed);

        // ---- 1. Imzo ----
        if (!SignatureMatches(fields, providedHash, botToken))
            return InitDataResult.Fail(InitDataError.BadSignature);

        // ---- 2. Yoshi (replay) ----
        // TARTIB MUHIM: avval imzo, keyin muddat. Teskarisida imzosi yolg'on, lekin
        // `auth_date` i yangi bo'lgan so'rov "muddati o'tmagan" degan javob olardi va
        // hujumchi shu farqdan foydalanardi.
        if (!fields.TryGetValue("auth_date", out var authRaw)
            || !long.TryParse(authRaw, out var authUnix))
            return InitDataResult.Fail(InitDataError.MissingAuthDate);

        var authDate = DateTimeOffset.FromUnixTimeSeconds(authUnix);
        if (now - authDate > MaxAge || authDate - now > FutureSkew)
            return InitDataResult.Fail(InitDataError.Expired);

        // ---- 3. Foydalanuvchi ----
        if (!fields.TryGetValue("user", out var userJson)) return InitDataResult.Fail(InitDataError.NoUser);
        var user = ReadUser(userJson);
        if (user is null) return InitDataResult.Fail(InitDataError.NoUser);

        return new InitDataResult(true, InitDataError.None, user, authDate);
    }

    /// <summary>
    /// Test va seeder uchun: berilgan maydonlardan HAQIQIY imzolangan `initData`
    /// yig'adi. Prod kodida ishlatilmaydi — imzoni faqat Telegram qo'yadi.
    /// Shu yerda turishining sababi: tekshiruv va yig'ish BITTA algoritmni
    /// ishlatishi kerak, ikkita nusxa esa sekin-asta farq qilib ketardi.
    /// </summary>
    public static string Sign(IReadOnlyDictionary<string, string> fields, string botToken)
    {
        var checkString = DataCheckString(fields);
        var hash = Convert.ToHexStringLower(Hmac(SecretKey(botToken), checkString));
        var query = fields
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}")
            .Append($"hash={hash}");
        return string.Join("&", query);
    }

    // ------------------------------------------------------------------
    //  Ichki
    // ------------------------------------------------------------------

    private static bool SignatureMatches(
        Dictionary<string, string> fields, string providedHash, string botToken)
    {
        var expected = Hmac(SecretKey(botToken), DataCheckString(fields));

        // `hash` — hex satr; noto'g'ri uzunlik/belgi bo'lsa Convert yiqiladi.
        byte[] provided;
        try { provided = Convert.FromHexString(providedHash); }
        catch (FormatException) { return false; }

        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }

    /// <summary>secret_key = HMAC_SHA256(key: "WebAppData", message: bot_token).</summary>
    private static byte[] SecretKey(string botToken) =>
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(SecretSalt), Encoding.UTF8.GetBytes(botToken));

    private static byte[] Hmac(byte[] key, string message) =>
        HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(message));

    /// <summary>`hash` siz, kalit bo'yicha saralangan "key=value" qatorlari '\n' bilan.</summary>
    private static string DataCheckString(IReadOnlyDictionary<string, string> fields) =>
        string.Join("\n", fields
            .Where(kv => kv.Key != "hash")
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value}"));

    /// <summary>
    /// `a=1&amp;b=2` ni lug'atga ochadi. Dekoder ATAYLAB `Uri.UnescapeDataString`
    /// (fayl boshidagi 2-izoh). Buzuq bo'lak jimgina tashlab yuboriladi — u
    /// data-check-string'ga tushmagani uchun imzo baribir mos kelmaydi.
    /// </summary>
    private static Dictionary<string, string> Parse(string initData)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in initData.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            var key = Uri.UnescapeDataString(pair[..eq]);
            var value = Uri.UnescapeDataString(pair[(eq + 1)..]);
            result[key] = value;
        }
        return result;
    }

    private static TelegramWebAppUser? ReadUser(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("id", out var idEl) || !idEl.TryGetInt64(out var id) || id <= 0)
                return null;

            return new TelegramWebAppUser(
                id,
                Text(root, "first_name") ?? "",
                Text(root, "last_name"),
                Text(root, "username"),
                Text(root, "language_code"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;
}

using System.Text.Json;
using SchoolLms.Application.Services;

namespace SchoolLms.Tests;

// ===========================================================================
//  `initData` imzosi — SOF FUNKSIYA TESTLARI (baza va HTTP'siz). Faza 3.
// ===========================================================================
//
//  Bu yerdagi har bir test bitta savolga javob beradi: SOXTA MA'LUMOT
//  O'TIB KETADIMI? Tekshiruvning o'zi bir necha qatorlik kod, lekin u butun
//  Mini App'ning yagona eshigi — u yerdagi xato "hamma hammaning ma'lumotini
//  ko'radi" degani.
//
//  Imzolangan `initData` `TelegramInitData.Sign` bilan yig'iladi: tekshiruv va
//  yig'ish BITTA algoritmni ishlatadi, ya'ni test "o'zim yozgan narsani o'zim
//  tasdiqlash" emas — Sign faqat data-check-string va HMAC'ni chaqiradi,
//  Validate esa mustaqil ravishda uni qayta hisoblaydi va TAQQOSLAYDI.
// ===========================================================================

public class TelegramInitDataTests
{
    /// <summary>Soxta, hech qayerda ishlatilmaydigan token (haqiqiysi hech qachon repoda bo'lmaydi).</summary>
    private const string BotToken = "123456789:TEST-ONLY-BOT-TOKEN-not-a-real-one";

    private const string OtherBotToken = "987654321:TEST-ONLY-OTHER-BOT-TOKEN";

    private static readonly DateTimeOffset Now = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    private static Dictionary<string, string> Fields(
        long userId = 555_000_111, DateTimeOffset? authDate = null) => new()
    {
        ["auth_date"] = (authDate ?? Now).ToUnixTimeSeconds().ToString(),
        ["query_id"] = "AAHdF6IQAAAAAN0XohDhrOrc",
        ["user"] = JsonSerializer.Serialize(new
        {
            id = userId,
            first_name = "Dilnoza",
            last_name = "Karimova",
            username = "dilnoza",
            language_code = "uz",
        }),
    };

    [Fact]
    public void Togri_imzolangan_initData_qabul_qilinadi()
    {
        var initData = TelegramInitData.Sign(Fields(), BotToken);

        var result = TelegramInitData.Validate(initData, BotToken, Now);

        Assert.True(result.Ok);
        Assert.Equal(InitDataError.None, result.Error);
        Assert.Equal(555_000_111, result.User!.Id);
        Assert.Equal("Dilnoza Karimova", result.User.DisplayName);
        Assert.Equal("dilnoza", result.User.Username);
    }

    [Fact]
    public void Boshqa_bot_tokeni_bilan_imzolangan_initData_rad_etiladi()
    {
        // Hujumchi o'z botining tokeni bilan mukammal imzo qo'ya oladi —
        // lekin u BIZNING token bilan mos kelmaydi.
        var initData = TelegramInitData.Sign(Fields(), OtherBotToken);

        var result = TelegramInitData.Validate(initData, BotToken, Now);

        Assert.False(result.Ok);
        Assert.Equal(InitDataError.BadSignature, result.Error);
        Assert.Null(result.User);
    }

    [Fact]
    public void Imzolangandan_keyin_ozgartirilgan_user_rad_etiladi()
    {
        // Eng muhim holat: to'g'ri imzo, lekin foydalanuvchi id'si almashtirilgan.
        // Bu o'tib ketsa, istalgan odam istalgan akkauntga kira olardi.
        var fields = Fields(userId: 111);
        var signed = TelegramInitData.Sign(fields, BotToken);
        var tampered = signed.Replace(
            Uri.EscapeDataString(fields["user"]),
            Uri.EscapeDataString(fields["user"].Replace("\"id\":111", "\"id\":999")),
            StringComparison.Ordinal);

        Assert.NotEqual(signed, tampered);      // almashtirish haqiqatan bo'ldi
        var result = TelegramInitData.Validate(tampered, BotToken, Now);

        Assert.False(result.Ok);
        Assert.Equal(InitDataError.BadSignature, result.Error);
    }

    [Fact]
    public void Ozgartirilgan_hash_rad_etiladi()
    {
        var signed = TelegramInitData.Sign(Fields(), BotToken);
        // Oxirgi hex belgisini almashtiramiz.
        var tampered = signed[..^1] + (signed[^1] == 'a' ? 'b' : 'a');

        var result = TelegramInitData.Validate(tampered, BotToken, Now);

        Assert.False(result.Ok);
        Assert.Equal(InitDataError.BadSignature, result.Error);
    }

    [Fact]
    public void Hash_umuman_bolmasa_rad_etiladi()
    {
        var result = TelegramInitData.Validate("auth_date=1&user=%7B%22id%22%3A1%7D", BotToken, Now);

        Assert.False(result.Ok);
        Assert.Equal(InitDataError.Malformed, result.Error);
    }

    [Fact]
    public void Hash_hex_bolmasa_rad_etiladi()
    {
        var result = TelegramInitData.Validate("auth_date=1&hash=zzzz", BotToken, Now);

        Assert.False(result.Ok);
        Assert.Equal(InitDataError.BadSignature, result.Error);
    }

    [Fact]
    public void Muddati_otgan_initData_rad_etiladi()
    {
        // Imzo TO'G'RI, lekin `auth_date` oynadan tashqarida: ushlab olingan
        // `initData` abadiy ishlamasligi kerak.
        var old = Now - TelegramInitData.MaxAge - TimeSpan.FromMinutes(1);
        var initData = TelegramInitData.Sign(Fields(authDate: old), BotToken);

        var result = TelegramInitData.Validate(initData, BotToken, Now);

        Assert.False(result.Ok);
        Assert.Equal(InitDataError.Expired, result.Error);
    }

    [Fact]
    public void Oyna_chegarasidagi_initData_hali_qabul_qilinadi()
    {
        var edge = Now - TelegramInitData.MaxAge + TimeSpan.FromMinutes(1);
        var initData = TelegramInitData.Sign(Fields(authDate: edge), BotToken);

        Assert.True(TelegramInitData.Validate(initData, BotToken, Now).Ok);
    }

    [Fact]
    public void Kelajakdan_kelgan_initData_rad_etiladi()
    {
        // Soat farqiga 5 daqiqa yo'l qo'yiladi; bir soat oldinga — yo'q.
        var future = Now + TimeSpan.FromHours(1);
        var initData = TelegramInitData.Sign(Fields(authDate: future), BotToken);

        var result = TelegramInitData.Validate(initData, BotToken, Now);

        Assert.False(result.Ok);
        Assert.Equal(InitDataError.Expired, result.Error);
    }

    [Fact]
    public void Auth_date_yoq_bolsa_rad_etiladi()
    {
        var fields = new Dictionary<string, string> { ["user"] = "{\"id\":5}" };
        var initData = TelegramInitData.Sign(fields, BotToken);

        var result = TelegramInitData.Validate(initData, BotToken, Now);

        Assert.False(result.Ok);
        Assert.Equal(InitDataError.MissingAuthDate, result.Error);
    }

    [Fact]
    public void User_yoq_bolsa_rad_etiladi()
    {
        var fields = new Dictionary<string, string>
        {
            ["auth_date"] = Now.ToUnixTimeSeconds().ToString(),
            ["query_id"] = "x",
        };
        var initData = TelegramInitData.Sign(fields, BotToken);

        var result = TelegramInitData.Validate(initData, BotToken, Now);

        Assert.False(result.Ok);
        Assert.Equal(InitDataError.NoUser, result.Error);
    }

    [Fact]
    public void Bot_tokeni_sozlanmagan_bolsa_tekshirilmaydi()
    {
        var initData = TelegramInitData.Sign(Fields(), BotToken);

        var result = TelegramInitData.Validate(initData, "", Now);

        Assert.False(result.Ok);
        Assert.Equal(InitDataError.BotNotConfigured, result.Error);
    }

    /// <summary>
    /// Telegram yangi `signature` maydonini ham yuboradi va u data-check-string
    /// ICHIDA qoladi (faqat `hash` chiqariladi). Agar kimdir uni "noma'lum
    /// maydon" deb tashlab yuborsa, HAQIQIY Telegram ma'lumoti rad etila boshlaydi.
    /// </summary>
    [Fact]
    public void Notanish_maydonlar_ham_imzoga_kiradi()
    {
        var fields = Fields();
        fields["signature"] = "Xk3nQ_fake_ed25519_signature";
        fields["chat_type"] = "sender";
        var initData = TelegramInitData.Sign(fields, BotToken);

        Assert.True(TelegramInitData.Validate(initData, BotToken, Now).Ok);

        // O'sha maydonni olib tashlash imzoni buzadi — ya'ni u haqiqatan hisobga olingan.
        var without = string.Join("&", initData.Split('&')
            .Where(p => !p.StartsWith("signature=", StringComparison.Ordinal)));
        Assert.Equal(InitDataError.BadSignature,
            TelegramInitData.Validate(without, BotToken, Now).Error);
    }

    /// <summary>
    /// `user` JSON'ida `+` bo'lsa ham imzo mos kelishi kerak. HTML-form dekoderi
    /// (`HttpUtility.UrlDecode`) `+` ni bo'sh joyga aylantirib, to'g'ri imzoni
    /// rad etardi — shuning uchun `Uri.UnescapeDataString` ishlatiladi.
    /// </summary>
    [Fact]
    public void Plus_belgisi_bor_qiymat_tori_dekod_qilinadi()
    {
        var fields = Fields();
        fields["user"] = JsonSerializer.Serialize(new
        {
            id = 42L,
            first_name = "A+B",
            photo_url = "https://t.me/i/a+b.jpg",
        });
        var initData = TelegramInitData.Sign(fields, BotToken);

        var result = TelegramInitData.Validate(initData, BotToken, Now);

        Assert.True(result.Ok);
        Assert.Equal("A+B", result.User!.DisplayName);
    }
}

using QRCoder;

namespace SchoolLms.Application.Billing;

/// <summary>
/// Chekdagi QR kod va logo (mijoz, 2026-09-24): "har bir tranzaksiyani o'zgarmas qr kodi bo'lishi kerak, yani
/// chekdan scaner qilib to'lov haqida malumot ham ololsin, qrcode checkni pasida joylashsin, shuningdek maktab
/// logosi ham bo'lishi kerak".
///
/// <para>
/// QR ichida faqat ochiq sahifa manzili: <c>{asos}/r/{payments.receipt_token}</c>. Token to'lov yaratilganda bir
/// marta beriladi va o'zgarmaydi, shuning uchun bir chekning QR kodi har safar bir xil chiqadi. Sahifa to'lovni
/// shu kalit bo'yicha topadi va uning HOZIRGI holatini ko'rsatadi (storno qilingan bo'lsa — "bekor qilingan").
/// </para>
/// </summary>
public static class ReceiptQr
{
    /// <summary>Sozlama kaliti: QR ichidagi manzil asosi, masalan <c>https://lms.wunderkindedu.uz</c>.</summary>
    public const string BaseUrlKey = "Receipts:PublicBaseUrl";

    /// <summary>Tekshirish sahifasining manzili.</summary>
    public static string VerifyUrl(string baseUrl, string token) => $"{baseUrl.TrimEnd('/')}/r/{token}";

    /// <summary>
    /// QR rasm (PNG). M darajali xato tuzatish — termal chekdagi kichik dog' yoki bukilishda ham o'qiladi;
    /// modul 10 px — chop etilganda ham, ekranda ham tiniq.
    /// </summary>
    public static byte[] Png(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(10);
    }

    public static string PngDataUrl(string text) => "data:image/png;base64," + Convert.ToBase64String(Png(text));

    private static readonly Lazy<byte[]?> Logo = new(() =>
    {
        using var stream = typeof(ReceiptQr).Assembly.GetManifestResourceStream("SchoolLms.ReceiptLogo.jpg");
        if (stream is null) return null;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    });

    /// <summary>Maktab logosi (JPEG) yoki <c>null</c>, agar resurs yo'q bo'lsa.</summary>
    public static byte[]? LogoJpeg => Logo.Value;
}

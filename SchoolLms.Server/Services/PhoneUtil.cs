namespace SchoolLms.Server.Services;

/// <summary>Telefon raqamlarini solishtirish uchun yordamchi (turli format: +998, bo'sh joy, qavs).</summary>
public static class PhoneUtil
{
    /// <summary>Faqat raqamlarni qoldiradi.</summary>
    public static string DigitsOnly(string? s) =>
        new(string.IsNullOrEmpty(s) ? Array.Empty<char>() : s.Where(char.IsDigit).ToArray());

    /// <summary>
    /// Solishtirish kaliti — oxirgi 9 raqam (mamlakat kodisiz mahalliy raqam).
    /// "+998 90 123 45 67", "998901234567", "901234567" → barchasi "901234567".
    /// </summary>
    public static string Key(string? s)
    {
        var d = DigitsOnly(s);
        return d.Length <= 9 ? d : d[^9..];
    }
}

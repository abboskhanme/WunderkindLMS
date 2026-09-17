using System.Globalization;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// Bitta o'quvchi uchun Word andozaga tushadigan <c>@</c>-o'rinbosarlar
/// (docs/modules/students-parity.md §2.10, K-2).
///
/// <para>
/// <b>Mavjud token to'plami KENGAYTIRILDI, almashtirilmadi.</b>
/// <c>ContractsController</c> ota-onalarga yuboradigan andozadagi beshta token
/// (<c>@ota_ona @telefon @farzandlar @sana @raqam</c>) shu yerda ham AYNAN
/// o'sha ma'noda bor, shuning uchun bugun ishlayotgan andozani o'quvchi
/// shartnomasini hosil qilishda qayta ishlatish mumkin — hech narsani qayta
/// yozmasdan. <c>@farzandlar</c> bu yerda bitta bolaning ismini beradi
/// (shartnoma bitta bola bilan tuziladi).
/// </para>
/// <para>
/// <b>Nega alohida fayl.</b> Tokenlar — sof funksiya: kirish ma'lumoti va
/// lug'at. Controller'da qolsa uni faqat HTTP orqali sinab ko'rish mumkin
/// bo'lardi; bu yerda esa to'g'ridan-to'g'ri test yoziladi.
/// </para>
/// </summary>
public static class StudentContractTokens
{
    /// <summary>Andoza yordamida ko'rsatiladigan tokenlar ro'yxati (UI dagi maslahat).</summary>
    public static readonly string[] Names =
    [
        "@oquvchi", "@sinf", "@tugilgan_kun", "@manzil", "@oquvchi_telefon",
        "@ota_ona", "@telefon", "@ota", "@ota_telefon", "@ona", "@ona_telefon",
        "@farzandlar", "@sana", "@raqam", "@tugash_sana",
    ];

    /// <summary>
    /// Lug'atni quradi. Topilmagan qiymat BO'SH SATR bo'ladi (token andozada
    /// o'z holicha qolib ketmasin — bosilgan shartnomada "@ota_telefon"
    /// yozuvi turgani xato bo'lardi).
    /// </summary>
    /// <param name="guardians">
    /// (Turi, To'liq ismi, Telefoni) — <c>student_guardians</c> dan. Bo'sh
    /// bo'lsa ota-ona ustunlari o'quvchi qatoridagi eski maydonlardan olinadi.
    /// </param>
    public static Dictionary<string, string> Build(
        Student student,
        IEnumerable<(string Relation, string FullName, string Phone)> guardians,
        string number,
        DateOnly signedOn,
        DateOnly? endsOn)
    {
        var list = guardians.ToList();

        var father = list.FirstOrDefault(g => g.Relation == GuardianRelation.Father);
        var mother = list.FirstOrDefault(g => g.Relation == GuardianRelation.Mother);
        // Asosiy vasiy: ota/ona ajratilmagan bo'lsa birinchi qator, u ham
        // bo'lmasa — o'quvchi qatoridagi eski `parent_*` maydonlari.
        var primary = list.Count > 0 ? list[0] : default;

        var parentName = primary.FullName ?? "";
        var parentPhone = primary.Phone ?? "";
        if (string.IsNullOrWhiteSpace(parentName)) parentName = student.ParentFullName;
        if (string.IsNullOrWhiteSpace(parentPhone)) parentPhone = student.ParentPhone;

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["@oquvchi"] = student.FullName,
            ["@sinf"] = student.ClassName,
            ["@tugilgan_kun"] = FormatIso(student.BirthDate),
            ["@manzil"] = student.Address,
            ["@oquvchi_telefon"] = student.Phone ?? "",

            ["@ota_ona"] = parentName,
            ["@telefon"] = parentPhone,
            ["@ota"] = father.FullName ?? "",
            ["@ota_telefon"] = father.Phone ?? "",
            ["@ona"] = mother.FullName ?? "",
            ["@ona_telefon"] = mother.Phone ?? "",

            // Mavjud ota-ona andozasi bilan moslik: u yerda bu "farzandlar
            // ro'yxati", bu yerda bitta bola — shartnoma bitta bola bilan.
            ["@farzandlar"] = $"{student.FullName} ({student.ClassName})",

            ["@sana"] = Format(signedOn),
            ["@raqam"] = number,
            ["@tugash_sana"] = endsOn is { } e ? Format(e) : "",
        };
    }

    /// <summary>"dd.MM.yyyy" — bosma hujjatda o'zbekcha odat shunday.</summary>
    public static string Format(DateOnly d) =>
        d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    /// <summary>ISO ("yyyy-MM-dd") satrini "dd.MM.yyyy" ga o'giradi; o'girib bo'lmasa — o'z holicha.</summary>
    private static string FormatIso(string iso) =>
        DateOnly.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? Format(d)
            : iso;
}

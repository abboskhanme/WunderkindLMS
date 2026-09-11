namespace SchoolLms.Application.Billing;

/// <summary>
/// Moliya qoidasi buzilganda tashlanadigan YAGONA istisno. Vazifa: P1-08.
///
/// <para>
/// <b>Nega alohida tip, oddiy <see cref="InvalidOperationException"/> emas.</b>
/// Controller xatoni HTTP kodiga o'girishi kerak. Agar u shunchaki
/// <c>InvalidOperationException</c> ni tutsa, DI'da xizmat ro'yxatdan
/// o'tmagani ("Unable to resolve service…"), bo'sh ketma-ketlik va boshqa
/// HAQIQIY dastur xatolari ham 409 bo'lib chiqadi — ya'ni buzilgan tizim
/// "normal ish oqimi" ko'rinishida yashirinadi. Bu esa aynan P1-15 gacha
/// mavjud bo'lgan holat (xizmatlar hali ulanmagan). Aniq tip bilan: qoida
/// buzilishi — kutilgan javob, qolgan hamma narsa — 500.
/// </para>
///
/// <para>
/// <b>Nega <see cref="Fault"/> enum, HTTP raqami emas.</b> Application qatlami
/// HTTP haqida bilmasligi kerak (SPEC §2.2). Enum'ni raqamga o'girish —
/// controller ishi, va u AYNAN bitta joyda (BillingCatalogController'dagi
/// <c>BillingFaultAttribute</c>) qilinadi.
/// </para>
///
/// <para>
/// <see cref="InvalidOperationException"/> dan meros olinadi, chunki ma'nosi
/// aynan shu ("hozirgi holatda bu amalni bajarib bo'lmaydi") va eski umumiy
/// <c>catch</c> bloklari uni baribir tutadi.
/// </para>
/// </summary>
public sealed class BillingRuleException : InvalidOperationException
{
    private BillingRuleException(BillingFault fault, string code, string message) : base(message)
    {
        Fault = fault;
        Code = code;
    }

    /// <summary>Xato turi — controller uni HTTP statusiga o'giradi.</summary>
    public BillingFault Fault { get; }

    /// <summary>
    /// Mashina o'qiydigan kod (<c>subscription_overlap</c>, <c>self_approval</c>, …).
    /// Frontend shartli mantiq uchun SHUNGA qaraydi, xabar matniga emas —
    /// matn o'zbekcha va tahrirlanadi.
    /// </summary>
    public string Code { get; }

    /// <summary>So'rov noto'g'ri to'ldirilgan (400).</summary>
    public static BillingRuleException Invalid(string code, string message) =>
        new(BillingFault.Invalid, code, message);

    /// <summary>Yozuv topilmadi (404).</summary>
    public static BillingRuleException NotFound(string code, string message) =>
        new(BillingFault.NotFound, code, message);

    /// <summary>Shaxsan bu foydalanuvchiga taqiqlangan — masalan ikki qavatli nazorat (403).</summary>
    public static BillingRuleException Forbidden(string code, string message) =>
        new(BillingFault.Forbidden, code, message);

    /// <summary>So'rov to'g'ri, lekin joriy holat unga yo'l bermaydi (409).</summary>
    public static BillingRuleException Conflict(string code, string message) =>
        new(BillingFault.Conflict, code, message);
}

/// <summary>Moliya qoidasi buzilishining turi (<see cref="BillingRuleException.Fault"/>).</summary>
public enum BillingFault
{
    /// <summary>So'rovdagi qiymat noto'g'ri (HTTP 400).</summary>
    Invalid,

    /// <summary>Murojaat qilingan yozuv yo'q (HTTP 404).</summary>
    NotFound,

    /// <summary>Rol/shaxs cheklovi — SPEC §4.3, §4.5 (HTTP 403).</summary>
    Forbidden,

    /// <summary>Joriy holat bilan ziddiyat: ustma-ust obuna, qayta tasdiqlash (HTTP 409).</summary>
    Conflict,
}

namespace SchoolLms.Domain;

/// <summary>
/// Tizim rollari (AppUser.Role qiymatlari). Yangi rol qo'shilsa shu joyga qo'shiladi.
///
/// <para>
/// <see cref="SuperAdmin"/> — tizim egasi. Admin'ga teng huquqlar ortida QO'SHIMCHA imtiyozlar:
/// guruhlash kabi o'quv yili boshida bloklanadigan amallarni istalgan vaqtda o'zgartira oladi
/// (kelajakda boshqa "lock"lar ham shu rolga override bo'ladi).
/// </para>
/// <para>
/// <see cref="Admin"/> — oddiy administrator. Hamma admin endpoint'lardan foydalanadi, lekin
/// muzlatilgan ma'lumotlarni (masalan, o'quv yili boshlangach guruhlarni) o'zgartira olmaydi.
/// </para>
/// </summary>
public static class Roles
{
    public const string SuperAdmin = "superadmin";
    public const string Admin = "admin";
    public const string Teacher = "teacher";
    public const string Student = "student";
    /// <summary>O'qituvchi bo'lmagan xodim (administrator, hisobchi, ...). Admin paneliga
    /// kiradi, lekin faqat <see cref="AppUser.Permissions"/> dagi bo'limlarni ko'radi.</summary>
    public const string Staff = "staff";

    /// <summary>
    /// Kassir — SPEC §3.1 bo'yicha birinchi darajali rol (P1-04).
    ///
    /// <para>
    /// Ilgari kassa <see cref="Staff"/> + "finance" ruxsat kaliti bilan ishlardi. Bu yetarli
    /// emas edi: <c>AdminPermAttribute</c> har qanday xodimga har qanday bo'limni O'QISHga
    /// ruxsat beradi, ya'ni kassir butun moliyani ko'rardi; smena, chek raqami va sanalgan
    /// naqd tushunchasi esa umuman yo'q edi (docs/TASKS.md §1.2, 4-teshik).
    /// </para>
    /// <para>
    /// Kassir CHEGARALARI (SPEC §4.3): to'lov qabul qiladi va chek beradi, o'z smenasini
    /// yopadi — lekin to'lovni storno qila olmaydi, oylik narx/obunani o'zgartira olmaydi,
    /// chegirma bera olmaydi, chiqim yoza olmaydi va boshqa kassirlarning nomuvofiqlik
    /// hisobotini ko'ra olmaydi. Bu chegara <c>FinanceRoleAttribute</c> da ma'lumot
    /// sifatida yozilgan (P1-06), <c>if</c> zanjiri sifatida emas.
    /// </para>
    /// </summary>
    public const string Cashier = "cashier";

    /// <summary>Loyiha boshlig'i — Control Plane (asosiy domen) egasi. Maktab rollaridan
    /// butunlay alohida: faqat maktablarni (tenant) ochish/boshqarish uchun. Hech bir maktab
    /// DB'siga kirmaydi.</summary>
    public const string PlatformOwner = "platformowner";

    /// <summary>
    /// Admin endpoint'larida ishlatish uchun: ikkala rol ham ruxsat etiladi.
    /// <c>[Authorize(Roles = Roles.AdminOrSuper)]</c> ko'rinishida foydalaniladi.
    /// </summary>
    public const string AdminOrSuper = Admin + "," + SuperAdmin;

    // ----- Moliya: TASHQI darvozalar (SPEC §4.3) -----
    //
    // DIQQAT: bu ikkisi FAQAT "shu controller'ga umuman kira oladimi" degan qo'pol
    // filtr. HAQIQIY qoida — qaysi rol qaysi AMALNI bajara oladi — bitta joyda,
    // `FinanceMatrix.Rules` da (SchoolLms.Server/Controllers/FinanceRoleAttribute.cs).
    // Bu yerga amal darajasidagi mantiq YOZILMAYDI, aks holda ruxsatning ikkita
    // manbasi paydo bo'ladi va ular albatta bir-biridan uzoqlashadi.

    /// <summary>
    /// Kassa oldidagi rollar: to'lov qabul qilish, chek, o'z smenasi.
    /// Admin va direktor ham kassaga tura oladi (SPEC §4.3 birinchi qator).
    /// <c>[Authorize(Roles = Roles.CashierOrAdmin)]</c>.
    /// </summary>
    public const string CashierOrAdmin = Cashier + "," + Admin + "," + SuperAdmin + "," + FinanceDelegate + "," + FinanceViewer;

    // ----- Moliya: xodim roli orqali (Boshqaruv → Rollar), 2026-09-25 -----
    //
    // Mijoz: "hamma narsa role bilan boshqarilsin". Bu ikkisi tokenda YO'Q — xodimning
    // ruxsatlaridan HAR SO'ROVDA hosil bo'ladi (Program.cs, OnTokenValidated), xuddi
    // `perm` claim'lari kabi: rol o'zgarsa darrov amal qiladi.

    /// <summary>"finance" ruxsatli xodim — moliyada admin darajasi (direktor tasdiqlari bundan tashqari).</summary>
    public const string FinanceDelegate = "financestaff";

    /// <summary>
    /// "finance:view" ruxsatli xodim — moliyani FAQAT O'QIYDI. Moliya darvozalari
    /// (<see cref="FinanceStaff"/>, <see cref="CashierOrAdmin"/>) uni o'tkazadi, har qanday
    /// yozish so'rovini esa <c>ViewOnlyWriteGuard</c> rad etadi; FinanceMatrix'da u yo'q.
    /// </summary>
    public const string FinanceViewer = "financeviewer";

    /// <summary>Ruxsat kalitining "faqat ko'rish" ko'rinishi: <c>students</c> → <c>students:view</c>.</summary>
    public const string ViewSuffix = ":view";

    /// <summary>Xodim ruxsat kaliti → hosil bo'ladigan ichki rol.</summary>
    public static readonly IReadOnlyDictionary<string, string> PermissionRoles = new Dictionary<string, string>
    {
        ["finance"] = FinanceDelegate,
        ["finance" + ViewSuffix] = FinanceViewer,
    };

    /// <summary>
    /// Moliya "orqa ofisi": katalog, obunalar, chegirmalar, chiqimlar, hisobotlar,
    /// sozlamalar. Kassir bu yerda YO'Q — SPEC §4.3 unga bu qatorlarning birortasini
    /// bermaydi. <see cref="Staff"/> ham yo'q: §4.3 da "staff" ustuni umuman yo'q,
    /// eski xodim-ruxsat yo'li esa P1-21 gacha eski <c>FinanceController</c> da
    /// <c>AdminPermAttribute</c> orqali ishlashda davom etadi.
    /// </summary>
    public const string FinanceStaff = Admin + "," + SuperAdmin + "," + FinanceDelegate + "," + FinanceViewer;
}

using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

/// <summary>
/// Buxgalteriya hisoblari — <b>YOPIQ RO'YXAT</b> (SPEC §3.7). Vazifa: P1-07.
///
/// <para>
/// <b>Nega yopiq?</b> Jurnal ustunida <c>account</c> oddiy <c>text</c>. Agar
/// ilova unga istalgan satrni yoza olsa, bir kun kimdir <c>"revenue:tution"</c>
/// deb xato yozadi va o'sha pul hisobotdan JIMGINA yo'qoladi — hech qanday
/// xato chiqmaydi, shunchaki yig'indi kam chiqadi. Yopiq ro'yxat bu xatoni
/// hisobot vaqtida emas, yozish vaqtida topadi.
/// </para>
/// <para>
/// Yangi hisob qo'shish = shu faylga bitta qator. Bu ataylab "biroz
/// noqulay": hisoblar rejasi tez-tez o'zgaradigan narsa emas.
/// </para>
/// </summary>
public static class Accounts
{
    // ----- Aktivlar -----

    /// <summary>Kassadagi naqd pul. Smena yopilishida AYNAN shu sanaladi.</summary>
    public const string Cash = "cash";

    /// <summary>Bank hisobi: karta, o'tkazma va onlayn to'lovlar shu yerga tushadi.</summary>
    public const string Bank = "bank";

    /// <summary>Debitorlik: hisob-faktura yozildi, lekin hali to'lanmadi (o'quvchi qarzi).</summary>
    public const string Receivable = "receivable";

    // ----- Daromadlar (toifalar bo'yicha) -----

    public const string RevenueTuition = "revenue:tuition";
    public const string RevenueBus = "revenue:bus";
    public const string RevenueDormitory = "revenue:dormitory";
    public const string RevenueMeals = "revenue:meals";

    /// <summary>Boshqa daromad. Admin qo'shgan nostandart toifalar ham shu yerga tushadi.</summary>
    public const string RevenueOther = "revenue:other";

    // ----- Chiqimlar -----

    public const string ExpenseSalary = "expense:salary";
    public const string ExpenseOther = "expense:other";

    /// <summary>Ruxsat etilgan barcha hisob kodlari. P1-22 va hisobotlar shu ro'yxatdan aylanadi.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Cash, Bank, Receivable,
        RevenueTuition, RevenueBus, RevenueDormitory, RevenueMeals, RevenueOther,
        ExpenseSalary, ExpenseOther,
    ];

    private static readonly HashSet<string> Known = new(All, StringComparer.Ordinal);

    /// <summary>Toifa kodi (<c>fee_categories.code</c>) → daromad hisobi.</summary>
    private static readonly Dictionary<string, string> RevenueByCategory = new(StringComparer.Ordinal)
    {
        ["tuition"] = RevenueTuition,
        ["bus"] = RevenueBus,
        ["dormitory"] = RevenueDormitory,
        ["meals"] = RevenueMeals,
        ["other"] = RevenueOther,
    };

    public static bool IsKnown(string? account) =>
        account is not null && Known.Contains(account);

    /// <summary>
    /// Kodni tekshiradi va o'zini qaytaradi. Noma'lum bo'lsa —
    /// <see cref="ArgumentOutOfRangeException"/>, ruxsat etilganlar ro'yxati bilan.
    /// </summary>
    public static string Require(string? account) =>
        IsKnown(account)
            ? account!
            : throw new ArgumentOutOfRangeException(
                nameof(account), account,
                $"Noma'lum hisob kodi. Ruxsat etilganlar: {string.Join(", ", All)}.");

    /// <summary>
    /// To'lov toifasiga mos daromad hisobi. Admin qo'shgan nostandart toifa
    /// uchun <see cref="RevenueOther"/> — bu ataylab: hisobot toifasi
    /// yo'qolgandan ko'ra "boshqa" ga tushgani yaxshiroq.
    /// </summary>
    public static string RevenueFor(string? categoryCode) =>
        categoryCode is not null && RevenueByCategory.TryGetValue(categoryCode, out var account)
            ? account
            : RevenueOther;

    /// <summary>
    /// To'lov usuli pul qayerga tushishini belgilaydi: naqd — kassaga,
    /// qolgani — bankka (mijoz javobi, SPEC §8.1 Q13).
    ///
    /// <para>
    /// Qoida <see cref="PaymentMethod.CountsAsCash"/> orqali o'tadi — "naqdmi
    /// yoki yo'q" degan savolning YAGONA javobi shu yerda bo'lishi uchun.
    /// Smena yopilishidagi <c>expected_cash</c> ham aynan shunga tayanadi.
    /// </para>
    /// </summary>
    public static string SettlementFor(string method) =>
        PaymentMethod.CountsAsCash(method) ? Cash : Bank;
}

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

    /// <summary>
    /// "Mahsulot savdosi" — books, uniforms, club accessories sold at the cash desk (mini-magazin,
    /// docs/modules/shop.md, 2026-10-15). Only a Kirim transaction type can choose it; no
    /// <c>fee_categories</c> code maps here.
    /// </summary>
    public const string RevenueSales = "revenue:sales";

    // ----- Chiqimlar (toifalar bo'yicha) -----
    //
    //  Bittasi = `expenses.category` ning bitta qiymati. Ro'yxat mijoz javobidan
    //  olingan (docs/TASKS.md §8 Q16: "salary, utilities, supplies, rent, other";
    //  `repair` P1-26 ning pul aylanmasi halqasida ham ko'rsatilgan) va
    //  `ExpenseByCategory` orqali AYNAN shu toifalarga bog'langan.
    //
    //  Nega har toifaga alohida hisob, hammasi `expense:other` ga emas: P&L va
    //  pul aylanmasi hisoboti jurnalni AKKAUNT bo'yicha yig'adi (`expense:*`
    //  prefiksi). Toifa faqat `expenses` jadvalida qolsa, direktor "qayerga
    //  ketdi" degan savolga hisobotdan javob ololmasdi — hamma chiqim bitta
    //  "boshqa" ustunida turardi.

    public const string ExpenseSalary = "expense:salary";
    public const string ExpenseUtilities = "expense:utilities";
    public const string ExpenseSupplies = "expense:supplies";
    public const string ExpenseRent = "expense:rent";
    public const string ExpenseRepair = "expense:repair";
    public const string ExpenseOther = "expense:other";

    // ----- Kassa chiqimi turlari uchun (2026-10-08) -----
    //
    //  EduSchool'dan kelgan chiqim turlari (oshxona, avtobus, yotoqxona, marketing,
    //  soliqlar) P&L'da hammasi "Boshqa chiqim" ichida ~190 mln bo'lib qolardi.
    //  Ular `expenses.category` EMAS — `ExpenseCategories` o'zgarmaydi, faqat
    //  tranzaksiya turi (`transaction_types.ledger_account`) ularni tanlay oladi.

    public const string ExpenseFood = "expense:food";
    public const string ExpenseTransport = "expense:transport";
    public const string ExpenseDormitory = "expense:dormitory";
    public const string ExpenseMarketing = "expense:marketing";
    public const string ExpenseTaxes = "expense:taxes";

    /// <summary>Ruxsat etilgan barcha hisob kodlari. P1-22 va hisobotlar shu ro'yxatdan aylanadi.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Cash, Bank, Receivable,
        RevenueTuition, RevenueBus, RevenueDormitory, RevenueMeals, RevenueOther,
        ExpenseSalary, ExpenseUtilities, ExpenseSupplies, ExpenseRent, ExpenseRepair, ExpenseOther,
        ExpenseFood, ExpenseTransport, ExpenseDormitory, ExpenseMarketing, ExpenseTaxes,
        // Mini-magazin sales (2026-10-15) — chosen only by a Kirim transaction type, as the five Chiqim ones above are.
        RevenueSales,
    ];

    /// <summary>Accounts a Chiqim transaction type may post to (<see cref="ForTransactionType"/>).</summary>
    public static readonly IReadOnlyList<string> TransactionTypeExpenseAccounts =
    [
        ExpenseSalary, ExpenseRent, ExpenseUtilities, ExpenseSupplies, ExpenseFood, ExpenseTransport,
        ExpenseDormitory, ExpenseMarketing, ExpenseTaxes, ExpenseRepair, ExpenseOther,
    ];

    /// <summary>Accounts a Kirim transaction type may post to.</summary>
    public static readonly IReadOnlyList<string> TransactionTypeRevenueAccounts =
    [
        RevenueTuition, RevenueBus, RevenueDormitory, RevenueMeals, RevenueSales, RevenueOther,
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

    /// <summary>Chiqim toifasi (<c>expenses.category</c>) → chiqim hisobi.</summary>
    private static readonly Dictionary<string, string> ExpenseByCategory = new(StringComparer.Ordinal)
    {
        ["salary"] = ExpenseSalary,
        ["utilities"] = ExpenseUtilities,
        ["supplies"] = ExpenseSupplies,
        ["rent"] = ExpenseRent,
        ["repair"] = ExpenseRepair,
        ["other"] = ExpenseOther,
    };

    /// <summary>
    /// Ruxsat etilgan chiqim toifalari — <b>YOPIQ RO'YXAT</b> (mijoz javobi,
    /// docs/TASKS.md §8 Q16). Chiqim endpoint'i shu ro'yxatdan tashqari
    /// qiymatni qabul qilmaydi.
    ///
    /// <para>
    /// Nega yopiq, <c>fee_categories</c> kabi jadval emas: chiqim toifasi
    /// jurnalda AKKAUNT bo'lib qoladi, akkauntlar esa yopiq ro'yxat. Erkin
    /// matn qabul qilinsa, "utilites" deb xato yozilgan qator jimgina
    /// <c>expense:other</c> ga tushib, hisobotda topib bo'lmas farq qoldirardi.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<string> ExpenseCategories = [.. ExpenseByCategory.Keys];

    public static bool IsKnown(string? account) =>
        account is not null && Known.Contains(account);

    /// <summary>Shu satr ruxsat etilgan chiqim toifasimi?</summary>
    public static bool IsExpenseCategory(string? category) =>
        category is not null && ExpenseByCategory.ContainsKey(category);

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
    /// Chiqim toifasiga mos chiqim hisobi. <see cref="RevenueFor"/> dan farqli
    /// o'laroq noma'lum toifa "boshqa" ga TUSHMAYDI, balki
    /// <see cref="ArgumentOutOfRangeException"/> beradi — chiqim toifasi
    /// foydalanuvchi yaratadigan qator emas, yopiq ro'yxatdagi qiymat
    /// (<see cref="ExpenseCategories"/>). Chaqiruvchi buni oldindan
    /// <see cref="IsExpenseCategory"/> bilan tekshirib, tushunarli 400 beradi.
    /// </summary>
    public static string ExpenseFor(string? category) =>
        category is not null && ExpenseByCategory.TryGetValue(category, out var account)
            ? account
            : throw new ArgumentOutOfRangeException(
                nameof(category), category,
                $"Noma'lum chiqim toifasi. Ruxsat etilganlar: {string.Join(", ", ExpenseCategories)}.");

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

    /// <summary>Is <paramref name="account"/> allowed for a transaction type of <paramref name="kind"/>?</summary>
    public static bool FitsTransactionType(string kind, string? account) =>
        account is not null && (kind == TransactionTypeKind.Out
            ? TransactionTypeExpenseAccounts.Contains(account, StringComparer.Ordinal)
            : TransactionTypeRevenueAccounts.Contains(account, StringComparer.Ordinal));

    /// <summary>
    /// The account a typed cash-box Kirim/Chiqim posts to: the type's own choice, or the "other"
    /// account when none (or a stale one) is set — money is never dropped from the report.
    /// </summary>
    public static string ForTransactionType(string kind, string? ledgerAccount) =>
        FitsTransactionType(kind, ledgerAccount)
            ? ledgerAccount!
            : kind == TransactionTypeKind.Out ? ExpenseOther : RevenueOther;

    /// <summary>
    /// First guess of a type's account from its name — EduSchool's names, Latin and Cyrillic
    /// (prod, 2026-10-08). Only fills a type that has none; an admin's choice is never overwritten.
    /// </summary>
    public static string GuessForTransactionType(string kind, string name)
    {
        var n = name.ToLowerInvariant().Replace('’', '\'').Replace('ʻ', '\'').Replace('ʼ', '\'');
        bool Has(params string[] parts) => parts.Any(p => n.Contains(p, StringComparison.Ordinal));

        if (kind == TransactionTypeKind.In)
            return Has("oylik to'lov", "ойлик тўлов") ? RevenueTuition
                : Has("avtobus", "автобус", "transport", "транспорт") ? RevenueBus
                : Has("yotoqxona", "ётоқхона", "ётокхона") ? RevenueDormitory
                : Has("ovqat", "oshxona", "oshhona", "овқат", "ошхона") ? RevenueMeals
                : StartsWord(n, "kitob", "forma", "do'ppi", "doppi", "kiyim", "китоб", "форма", "дўппи", "кийим")
                    ? RevenueSales
                : RevenueOther;

        // Loans repaid from salaries mention "иш хаки" — they are not salary, so they go first.
        if (Has("кредит", "kredit")) return ExpenseOther;
        if (Has("oylik", "avans", "ish haqi", "maosh", "ойлик", "аванс", "иш хаки", "иш ҳақи", "маош")) return ExpenseSalary;
        if (Has("ijara", "ижара")) return ExpenseRent;
        if (Has("soliq", "solik", "solig", "inps", "солиқ", "солик")) return ExpenseTaxes;
        if (Has("oshxona", "oshhona", "ovqat", "ошхона", "озиқ", "овқат")) return ExpenseFood;
        if (Has("avtobus", "автобус", "transport", "транспорт")) return ExpenseTransport;
        if (Has("yotoqxona", "ётоқхона", "ётокхона")) return ExpenseDormitory;
        if (Has("marketing", "маркетинг", "маркент", "reklama", "реклам")) return ExpenseMarketing;
        if (Has("elektr", "lektir", "energ", "электр", "энерг", "svet", "internet", "интернет", "telefon",
                "ichimlik suv", "suv", "gaz", "kommunal", "musir", "чиқинди")) return ExpenseUtilities;
        if (Has("konstavar", "kanstovar", "kantstovar", "kansel", "канцел", "qog'oz", "maxsulot", "mahsulot", "jihoz"))
            return ExpenseSupplies;
        if (Has("ta'mir", "remont", "таъмир")) return ExpenseRepair;
        return ExpenseOther;
    }

    /// <summary>
    /// Does a word of <paramref name="text"/> start with one of <paramref name="stems"/>? A plain
    /// substring would send "Informatika to'garagi" to sales because of "forma".
    /// </summary>
    private static bool StartsWord(string text, params string[] stems)
    {
        foreach (var stem in stems)
        {
            for (var at = text.IndexOf(stem, StringComparison.Ordinal); at >= 0;
                 at = text.IndexOf(stem, at + 1, StringComparison.Ordinal))
            {
                if (at == 0 || !char.IsLetter(text[at - 1])) return true;
            }
        }
        return false;
    }
}

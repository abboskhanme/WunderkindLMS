using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

/// <summary>
/// Maktabning PUL AYLANMASI — halqa (Sankey shaklidagi) vizualizatsiya uchun
/// tugunlar va bog'lanishlar. Vazifa: P1-26.
///
/// <para>
/// <b>Yagona manba — <c>ledger_entries</c>.</b> <c>students.balance</c> ham,
/// eski <c>finance_transactions</c> ham O'QILMAYDI. Sabab: birinchisi 6 joyda
/// qo'lda o'zgartiriladigan saqlangan raqam (docs/TASKS.md §1.2, 3-teshik),
/// ikkinchisi esa tahrirlanadigan tekis jadval. Ikkalasi ham "shu davrda
/// qancha pul aylandi" degan savolga ISHONCHLI javob bera olmaydi. Ikki
/// yoqlama jurnal esa faqat INSERT qilinadi va har partiyasi balanslashgan
/// (<see cref="LedgerService"/>) — ya'ni yig'indi ta'rifi bo'yicha to'g'ri.
/// </para>
///
/// <para>
/// <b>Nega <c>cash</c>, <c>bank</c> va <c>receivable</c> halqada YO'Q.</b>
/// Ular — daromad/chiqimning IKKINCHI oyog'i: har bir <c>credit revenue:*</c>
/// qatoriga <c>debit receivable</c> (yoki <c>cash</c>) qatori juft bo'ladi.
/// Ularni ham tugun qilib qo'ysak, aynan bir xil pul halqada IKKI MARTA
/// ko'rinardi va "kirim = chiqim" tekshiruvi ma'nosini yo'qotardi. Bu
/// hisoblar SPEC §3.7 da pul QAYERDA turgani uchun kerak, pul QAYERDAN
/// kelib qayerga ketgani uchun emas.
/// </para>
///
/// <para>
/// <b>Aniqlik.</b> <c>ledger_entries.amount</c> — <c>numeric(14,2)</c>, ya'ni
/// har qiymat aynan tiyingacha. <c>decimal</c> yig'indisi ham aniq, shuning
/// uchun bu yerda suzuvchi nuqta umuman ishlatilmaydi va yaxlitlash YO'Q.
/// Markaziy tugun qiymati kiruvchi bog'lanishlar yig'indisidan HISOBLANADI
/// (mustaqil hisoblanmaydi) — shu sababli "kirim = markaz = chiqim"
/// tengligi tuzilish bo'yicha kafolatlanadi, tasodifan emas.
/// </para>
/// </summary>
public static class MoneyFlowQueries
{
    /// <summary>Markaziy tugun id'si — hamma kirim shunga oqadi, hamma chiqim shundan.</summary>
    public const string HubId = "hub";

    /// <summary>Sof natija tuguni id'si (foyda yoki qoplangan farq).</summary>
    public const string NetId = "net";

    /// <summary>
    /// Davr uchun pul aylanmasi grafi.
    /// </summary>
    /// <param name="db">Baza konteksti (faqat o'qish).</param>
    /// <param name="from">Boshlanish sanasi (kiritiladi).</param>
    /// <param name="to">Tugash sanasi (kiritiladi).</param>
    public static async Task<MoneyFlowDto> BuildAsync(
        IAppDbContext db, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        // Guruhlash BAZADA bajariladi: jurnal o'sib boradigan jadval, uni
        // ilovaga to'liq tortib olish bir yildan keyin sahifani o'ldirardi.
        var totals = await db.LedgerEntries
            .AsNoTracking()
            .Where(e => e.EntryDate >= from && e.EntryDate <= to)
            .GroupBy(e => new { e.Account, e.Direction })
            .Select(g => new LedgerAccountTotal(g.Key.Account, g.Key.Direction, g.Sum(x => x.Amount)))
            .ToListAsync(ct);

        return Compose(totals);
    }

    /// <summary>
    /// Hisob kesimidagi yig'indilardan grafni yig'adi. BAZASIZ, sof funksiya —
    /// shuning uchun balans tengligini testda to'g'ridan-to'g'ri, o'ylab
    /// topilgan noqulay summalar bilan sinash mumkin.
    /// </summary>
    public static MoneyFlowDto Compose(IEnumerable<LedgerAccountTotal> totals)
    {
        ArgumentNullException.ThrowIfNull(totals);

        // Hisob → sof harakat. Daromad hisobining normal qoldig'i KREDIT,
        // chiqimniki DEBET — shuning uchun belgilar qarama-qarshi olinadi.
        // Storno (reversal) qatorlari teskari yo'nalishda yozilgani uchun shu
        // ayirmaning o'zida avtomatik hisobga olinadi.
        var income = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var expense = new Dictionary<string, decimal>(StringComparer.Ordinal);

        foreach (var t in totals)
        {
            if (t.Account is null) continue;
            var signed = t.Direction == LedgerDirection.Credit ? t.Amount : -t.Amount;

            if (t.Account.StartsWith(RevenuePrefix, StringComparison.Ordinal))
                income[t.Account] = income.GetValueOrDefault(t.Account) + signed;
            else if (t.Account.StartsWith(ExpensePrefix, StringComparison.Ordinal))
                expense[t.Account] = expense.GetValueOrDefault(t.Account) - signed;
            // cash / bank / receivable — ataylab tashlab yuboriladi (yuqoridagi izoh).
        }

        var incomeNodes = Order(income, MoneyFlowKind.Income);
        var expenseNodes = Order(expense, MoneyFlowKind.Expense);

        // Jami TUGUNLAR qiymatidan yig'iladi, jurnaldan qayta hisoblanmaydi:
        // shunda "kiruvchi bog'lanishlar yig'indisi = markaz" tengligi
        // arifmetik tasodif emas, tuzilish xossasi bo'ladi.
        var incomeTotal = incomeNodes.Sum(n => n.Value);
        var expenseTotal = expenseNodes.Sum(n => n.Value);
        var net = incomeTotal - expenseTotal;

        if (incomeNodes.Count == 0 && expenseNodes.Count == 0)
            return new MoneyFlowDto([], []);

        var nodes = new List<MoneyFlowNode>(incomeNodes.Count + expenseNodes.Count + 2);
        var links = new List<MoneyFlowLink>(incomeNodes.Count + expenseNodes.Count + 1);

        nodes.AddRange(incomeNodes);
        foreach (var n in incomeNodes)
            links.Add(new MoneyFlowLink(n.Id, HubId, n.Value));

        // Kamomad (chiqim kirimdan katta) holatida farq KIRUVCHI tomonga
        // qo'yiladi — aks holda chiquvchi yig'indi markazdan katta bo'lib,
        // halqa "qayerdan olindi" degan savolni javobsiz qoldirardi.
        if (net < 0)
            links.Add(new MoneyFlowLink(NetId, HubId, -net));

        // Markaz qiymati = kiruvchi bog'lanishlar yig'indisi. AYNAN shu raqam
        // chiquvchi tomonda ham qaytadi.
        var hubValue = links.Sum(l => l.Value);
        nodes.Add(new MoneyFlowNode(HubId, "Umumiy aylanma", MoneyFlowKind.Hub, hubValue));

        nodes.AddRange(expenseNodes);
        foreach (var n in expenseNodes)
            links.Add(new MoneyFlowLink(HubId, n.Id, n.Value));

        if (net > 0)
            links.Add(new MoneyFlowLink(HubId, NetId, net));

        if (net != 0)
            nodes.Add(new MoneyFlowNode(
                NetId,
                net > 0 ? "Sof foyda" : "Qoplangan farq",
                MoneyFlowKind.Net,
                net > 0 ? net : -net));

        return new MoneyFlowDto(nodes, links);
    }

    private const string RevenuePrefix = "revenue:";
    private const string ExpensePrefix = "expense:";

    /// <summary>
    /// Nol bo'lganlarini tashlab, kattaligi bo'yicha kamayish tartibida
    /// joylashtiradi — halqada eng yo'g'on naycha birinchi yoyga tushsin.
    /// Tartib id bo'yicha ham barqarorlashtiriladi: bir xil summali ikki
    /// toifa har so'rovda joyini almashtirib, sahnani "sakratmasin".
    /// </summary>
    private static List<MoneyFlowNode> Order(Dictionary<string, decimal> byAccount, string kind) =>
        byAccount
            .Where(kv => kv.Value != 0m)
            .OrderByDescending(kv => Math.Abs(kv.Value))
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new MoneyFlowNode(kv.Key, LabelFor(kv.Key), kind, kv.Value))
            .ToList();

    /// <summary>
    /// Hisob kodining o'zbekcha nomi. Ro'yxat <see cref="Accounts"/> bilan bir
    /// xil yopiq to'plam; noma'lum kod kelsa (masalan kelajakdagi migratsiya)
    /// kodning o'zi ko'rsatiladi — tugun YO'QOLMAYDI, chunki yo'qolgan tugun
    /// balansni buzardi.
    /// </summary>
    public static string LabelFor(string account) => account switch
    {
        Accounts.RevenueTuition => "O'quv to'lovi",
        Accounts.RevenueBus => "Avtobus",
        Accounts.RevenueDormitory => "Yotoqxona",
        Accounts.RevenueMeals => "Ovqatlanish",
        Accounts.RevenueOther => "Boshqa daromad",
        Accounts.ExpenseSalary => "Oylik maosh",
        Accounts.ExpenseOther => "Boshqa chiqim",
        _ => account,
    };
}

/// <summary>Hisob va yo'nalish kesimidagi yig'indi (<see cref="MoneyFlowQueries.Compose"/> kirishi).</summary>
public sealed record LedgerAccountTotal(string Account, string Direction, decimal Amount);

/// <summary>Tugun turlari — frontend rang va joylashuvni shunga qarab tanlaydi.</summary>
public static class MoneyFlowKind
{
    public const string Income = "income";
    public const string Hub = "hub";
    public const string Expense = "expense";
    public const string Net = "net";
}

/// <param name="Id">Hisob kodi yoki <c>hub</c> / <c>net</c>.</param>
/// <param name="Label">O'zbekcha nom — ekranda shu ko'rinadi.</param>
/// <param name="Kind">income | hub | expense | net.</param>
/// <param name="Value">
/// So'mda, tiyingacha. Odatda musbat; storno davr chegarasida qolib ketsa
/// manfiy bo'lishi mumkin — bunday holat YASHIRILMAYDI, chunki yashirish
/// balansni buzardi.
/// </param>
public sealed record MoneyFlowNode(string Id, string Label, string Kind, decimal Value);

/// <param name="Source">Manba tugun id'si.</param>
/// <param name="Target">Maqsad tugun id'si.</param>
/// <param name="Value">Oqim summasi (so'm).</param>
public sealed record MoneyFlowLink(string Source, string Target, decimal Value);

/// <summary>
/// Halqa uchun to'liq graf. Bo'sh davr uchun ikkala ro'yxat ham bo'sh —
/// frontend shuni ko'rib "ma'lumot yo'q" ekranini chiqaradi.
/// </summary>
public sealed record MoneyFlowDto(IReadOnlyList<MoneyFlowNode> Nodes, IReadOnlyList<MoneyFlowLink> Links);

using Microsoft.EntityFrameworkCore;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  P&L 2.0 — "REJA / FAKT / FARQ" OYLIK KO'RINISH
//  (docs/modules/finance-parity.md §2.6 — FINANCE_ALL.PNL_EXPECTATION,
//  EduSchool'da `/pnl-expectation`, beta. Ilgari rad etilgan edi
//  — existing-module-gaps.md §3.6, "Verdict: defer... Revisit at the start
//  of the next academic year" — mijoz shu qarorni 2026-09-18 da bekor qildi.)
// ===========================================================================
//
//  NEGA "2.0": ODDIY P&L FAQAT FAKTNI KO'RSATADI
//  ----------------------------------------------
//  <see cref="FinanceReportQueries.ProfitLossAsync"/> (§2.5, "1.0") —
//  jurnaldagi HAQIQIY harakat: pul kelgandagina daromad ko'rinadi. "2.0"
//  esa REJANI ham qo'shadi — shu oy uchun necha o'quvchidan qancha pul
//  kutilyapti (faol obunalar va hisob-fakturalardan), keyin buni haqiqiy
//  jurnal bilan solishtiradi. "1.0" ning savoli — "necha pul keldi";
//  "2.0" niki — "necha pul kelishi KERAK edi va qancha farq bor".
//
//  BU FAYL — TO'LIQ 2.0 EMAS, UNING ENG KICHIK HALOL QISMI
//  ---------------------------------------------------------
//  EduSchool'dagi ekran besh tabdan iborat (§2.6.1): `expectation`,
//  `dynamics`, `changes`, `yearly`, `planned`. Shu vazifada FAQAT
//  birinchisi — asosiy "reja · fakt · farq" jadvali — quriladi:
//    · `planned` tabi rejalashtirilgan CHIQIM SHABLONlariga tayanadi
//      (F6.01) — buning uchun yangi jadval (`expense_templates`) kerak,
//      bu vazifada esa MIGRATSIYA YO'Q, shuning uchun chiqim tomonida
//      "reja" yo'q, faqat fakt ko'rsatiladi;
//    · to'liq o'zgarishlar jurnali (sahifalash bilan, `/changes` tabi),
//      kunlik dinamika (`dynamics`) va yillik jadval (`yearly`) —
//      alohida, kattaroq vazifalar (F6.03–F6.05, jami ~14 soat) va bu
//      faylga kirmaydi;
//    · filial (`branchId`) va "asOf" (kesim sanasi) — EduSchool ko'p
//      filialli va voqealar jurnaliga tayanadi; bizda bitta maktab va
//      voqealar jurnali yo'q, shuning uchun ikkovi ham qoldirilgan.
//  Bu — to'ldirish uchun emas, halollik uchun: yo'q narsa "keyinroq"
//  deb yozilgan, soxta ustun chizilmagan.
//
//  YANGI ARIFMETIKA YO'Q (FinanceReportQueries.cs dagi qoida bu yerda ham)
//  -------------------------------------------------------------------
//  "Fakt" ustunining daromad/chiqim/sof natija qatorlari —
//  <see cref="FinanceReportQueries.ProfitLossAsync"/> ning O'SHA oyga
//  qilingan chaqiruvi, boshqa hisob EMAS. Demak bu ekran eski P&L bilan
//  HECH QACHON kelisha olmay qolmaydi — ular bitta funksiyani chaqiradi.
//  Buni test tekshiradi (<c>FinanceStatementsTests.cs</c> dagi "Matritsa
//  katagi = P&L" namunasi bilan bir xil mezon).
//
//  "REJA" MANBAI
//  -------------
//  Reja — shu OYNING hisob-fakturalari (<c>invoices.period_month</c>),
//  "1.0" dagi qarzdorlar va yig'ilish darajasi bilan BIR XIL manba
//  (<see cref="FinanceReportQueries.BillableInvoices"/>,
//  <see cref="FinanceReportQueries.EffectiveAllocations"/>) — yangi jadval
//  yo'q, yangi ta'rif yo'q.
//
//  O'QUVCHI HARAKATI
//  -----------------
//  "Qabul qilingan / ketgan" — <c>student_subscriptions.starts_on</c> /
//  <c>ends_on</c> shu oy ichida bo'lgan o'quvchilar soni. Bu maktabga
//  RASMAN "qabul qilingan kun" emas (bunday ustun yo'q), balki BILLING
//  nuqtai nazaridan — qachon puldor bo'lgan/bo'lmay qolgan. EduSchool'ning
//  <c>studentJoined</c>/<c>studentLeft</c> tushunchasiga eng yaqin, mavjud,
//  ishonchli manba (finance-parity.md §2.6.2: "Nearest inputs...
//  active student_subscriptions").

/// <summary>
/// Bir oy uchun "reja · fakt · farq": faol obunalardan kutilgan daromad,
/// shu oyning hisob-fakturasidan kelib chiqqan yig'ilish, va jurnaldagi
/// haqiqiy daromad/chiqim/natija (§2.6, "P&amp;L 2.0" ning eng kichik
/// halol qismi — to'liq izoh fayl boshida).
/// </summary>
/// <param name="Month">Oyning birinchi kuni.</param>
/// <param name="StudentsActive">Shu oyning istalgan kunida faol bo'lgan obunalar soni (o'quvchi bo'yicha aniq).</param>
/// <param name="StudentsAdmitted">Shu oyda YANGI obuna boshlagan o'quvchilar soni.</param>
/// <param name="StudentsDeparted">Shu oyda obunasi tugagan o'quvchilar soni.</param>
/// <param name="StudentsExpected">Shu oy uchun hisob-fakturasi bor o'quvchilar soni ("reja").</param>
/// <param name="StudentsPaid">Ulardan shu oy uchun TO'LIQ to'lagan o'quvchilar soni ("fakt").</param>
/// <param name="GrossExpected">Shu oy hisob-fakturalarining chegirmasiz yig'indisi.</param>
/// <param name="DiscountAmount">Shu oy hisob-fakturalariga qo'llangan chegirma yig'indisi.</param>
/// <param name="DiscountRate">Chegirma foizi — <c>DiscountAmount / GrossExpected × 100</c>.
/// <c>GrossExpected = 0</c> bo'lsa — <c>null</c> (nolga bo'linish emas, "ma'nosiz" degani).</param>
/// <param name="NetExpected">Kutilgan sof daromad — <c>GrossExpected − DiscountAmount</c> ("reja").</param>
/// <param name="PerStudentNet">O'quvchi boshiga kutilgan sof daromad. <c>StudentsExpected = 0</c> bo'lsa — <c>null</c>.</param>
/// <param name="CollectedForPeriod">Shu oyning hisob-fakturalariga HAQIQATAN tushgan pul (storno chiqarib tashlangan).</param>
/// <param name="CollectionRateForPeriod">Yig'ilish foizi — <c>CollectedForPeriod / NetExpected × 100</c>.
/// <c>NetExpected = 0</c> bo'lsa — <c>null</c>.</param>
/// <param name="OutstandingForPeriod">Qolgan qarz — <c>NetExpected − CollectedForPeriod</c> (manfiy bo'lishi mumkin: avans).</param>
/// <param name="RevenueActual">
/// Jurnaldagi haqiqiy daromad shu KALENDAR oyda (<see cref="FinanceReportQueries.ProfitLossAsync"/>
/// ning aynan o'zi — "fakt"). Eski P&amp;L bilan solishtiriladigan raqam AYNAN shu (qabul mezoni).
/// </param>
/// <param name="ExpenseActual">Jurnaldagi haqiqiy chiqim shu kalendar oyda (ham <c>ProfitLossAsync</c> dan).
/// Chiqim tomonida REJA yo'q — sabab fayl boshida (F6.01, migratsiya kerak).</param>
/// <param name="ProfitActual">Sof natija — <c>RevenueActual − ExpenseActual</c> (<c>ProfitLossAsync.Net</c> bilan teng).</param>
/// <param name="Margin">Foyda darajasi — <c>ProfitActual / RevenueActual × 100</c>. <c>RevenueActual = 0</c> bo'lsa — <c>null</c>.</param>
/// <param name="ProfitPerStudent">O'quvchi boshiga sof natija. <c>StudentsActive = 0</c> bo'lsa — <c>null</c>.</param>
/// <param name="RevenueDiff">
/// Farq — <c>RevenueActual − NetExpected</c>. Musbat: bu oyga jurnalda REJADAN
/// ko'proq daromad tan olindi (masalan, o'tgan oylarning kech to'lovi shu oyga
/// tushdi). Manfiy: shu oy uchun kutilgan pulning bir qismi hali jurnalga
/// tushmagan (qarz sifatida qoladi).
/// </param>
public sealed record RevenueExpectationDto(
    DateOnly Month,
    int StudentsActive, int StudentsAdmitted, int StudentsDeparted,
    int StudentsExpected, int StudentsPaid,
    decimal GrossExpected, decimal DiscountAmount, decimal? DiscountRate,
    decimal NetExpected, decimal? PerStudentNet,
    decimal CollectedForPeriod, decimal? CollectionRateForPeriod, decimal OutstandingForPeriod,
    decimal RevenueActual, decimal ExpenseActual, decimal ProfitActual,
    decimal? Margin, decimal? ProfitPerStudent,
    decimal RevenueDiff);

public sealed partial class FinanceReportQueries
{
    /// <summary>
    /// Bir oy uchun reja/fakt/farq (§2.6). To'rt so'rov: reja (hisob-fakturalar,
    /// o'quvchi kesimida guruhlangan), fakt-pul (taqsimotlar, o'quvchi kesimida
    /// guruhlangan), o'quvchi harakati (obunalar) — va ustiga bitta
    /// <see cref="ProfitLossAsync"/> chaqiruvi (jurnal fakti, ikkita so'rov).
    /// Sikl ichida <c>await</c> yo'q.
    /// </summary>
    /// <param name="month">Istalgan sana — faqat oy va yil ishlatiladi (kuni e'tiborsiz).</param>
    /// <param name="ct">Bekor qilish belgisi.</param>
    public async Task<RevenueExpectationDto> RevenueExpectationAsync(
        DateOnly month, CancellationToken ct = default)
    {
        var periodMonth = FirstDayOfMonth(month);
        var monthEnd = periodMonth.AddMonths(1).AddDays(-1);

        var billable = BillableInvoices().Where(i => i.PeriodMonth == periodMonth);

        // ---- So'rov 1: REJA — o'quvchi kesimida hisoblangan (gross/discount) ----
        var accruedByStudent = await billable
            .GroupBy(i => i.StudentId)
            .Select(g => new
            {
                StudentId = g.Key,
                Gross = g.Sum(x => x.Amount),
                Discount = g.Sum(x => x.Discount),
            })
            .ToListAsync(ct);

        // ---- So'rov 2: FAKT-PUL — o'sha oyning hisob-fakturalariga tushgan pul ----
        // `EffectiveAllocations` storno qatorini ham, storno qilingan asl
        // to'lovni ham chiqarib tashlaydi (fayl boshidagi qoida — 1.0 bilan
        // BIR XIL manba).
        var paidByStudent = await (
            from a in EffectiveAllocations()
            join inv in billable on a.InvoiceId equals inv.Id
            group a by inv.StudentId
            into g
            select new { StudentId = g.Key, Paid = g.Sum(x => x.Amount) })
            .ToListAsync(ct);
        var paidLookup = paidByStudent.ToDictionary(x => x.StudentId, x => x.Paid);

        // ---- So'rov 3: O'QUVCHI HARAKATI — shu oyda faol/boshlagan/tugagan obunalar ----
        var subscriptions = await db.StudentSubscriptions.AsNoTracking()
            .Where(s => s.StartsOn <= monthEnd && (s.EndsOn == null || s.EndsOn >= periodMonth))
            .Select(s => new { s.StudentId, s.StartsOn, s.EndsOn })
            .ToListAsync(ct);

        var studentsActive = subscriptions.Select(s => s.StudentId).Distinct().Count();
        var studentsAdmitted = subscriptions
            .Where(s => s.StartsOn >= periodMonth && s.StartsOn <= monthEnd)
            .Select(s => s.StudentId).Distinct().Count();
        var studentsDeparted = subscriptions
            .Where(s => s.EndsOn is { } end && end >= periodMonth && end <= monthEnd)
            .Select(s => s.StudentId).Distinct().Count();

        var grossExpected = accruedByStudent.Sum(r => r.Gross);
        var discountAmount = accruedByStudent.Sum(r => r.Discount);
        var netExpected = grossExpected - discountAmount;

        var studentsExpected = accruedByStudent.Count;
        var studentsPaid = accruedByStudent.Count(r =>
            paidLookup.GetValueOrDefault(r.StudentId) >= r.Gross - r.Discount);

        var collectedForPeriod = accruedByStudent.Sum(r => paidLookup.GetValueOrDefault(r.StudentId));
        var outstandingForPeriod = netExpected - collectedForPeriod;

        // ---- So'rov 4-5: FAKT — jurnaldagi haqiqiy daromad/chiqim (YANGI ARIFMETIKA YO'Q) ----
        var pnl = await ProfitLossAsync(periodMonth, monthEnd, ct);

        return new RevenueExpectationDto(
            Month: periodMonth,
            StudentsActive: studentsActive,
            StudentsAdmitted: studentsAdmitted,
            StudentsDeparted: studentsDeparted,
            StudentsExpected: studentsExpected,
            StudentsPaid: studentsPaid,
            GrossExpected: grossExpected,
            DiscountAmount: discountAmount,
            DiscountRate: grossExpected == 0m ? null : decimal.Round(discountAmount / grossExpected * 100m, RateScale),
            NetExpected: netExpected,
            PerStudentNet: studentsExpected == 0 ? null : decimal.Round(netExpected / studentsExpected, RateScale),
            CollectedForPeriod: collectedForPeriod,
            CollectionRateForPeriod: netExpected == 0m
                ? null
                : decimal.Round(collectedForPeriod / netExpected * 100m, RateScale),
            OutstandingForPeriod: outstandingForPeriod,
            RevenueActual: pnl.RevenueTotal,
            ExpenseActual: pnl.ExpenseTotal,
            ProfitActual: pnl.Net,
            Margin: pnl.RevenueTotal == 0m ? null : decimal.Round(pnl.Net / pnl.RevenueTotal * 100m, RateScale),
            ProfitPerStudent: studentsActive == 0 ? null : decimal.Round(pnl.Net / studentsActive, RateScale),
            RevenueDiff: pnl.RevenueTotal - netExpected);
    }
}

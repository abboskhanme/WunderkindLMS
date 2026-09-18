namespace SchoolLms.Domain;

// ===========================================================================
//  Rejalashtirilgan chiqim shabloni — F6.01 (finance-parity.md §2.6, `planned`
//  tab). Manba: docs/modules/finance-parity.md §2.6.3 (F6.01), §3.3 (C7).
// ===========================================================================
//
//  SHABLON — CHIQIM EMAS
//  ----------------------
//  Bu yozuv "har oyning falon kunida taxminan shuncha xarajat bo'ladi" degan
//  KUTISH, pul harakati emas. U hech qachon jurnalga (`ledger_entries`)
//  o'z-o'zidan tushmaydi — haqiqiy chiqim hamon `ExpenseService.CreateAsync`
//  orqali, SPEC §4.5 ning ikki qavatli nazorati bilan yoziladi. Shablon faqat
//  direktorga ESLATMA yuborish va P&L 2.0'ning `planned` jadvalini to'ldirish
//  uchun (finance-parity.md §2.6.1: `colName · colBranchPlan · colTotalPlan ·
//  colAccrued · colDiff · colState`).
//
//  MOLIYAVIY EMAS — TO'LIQ CRUD
//  -----------------------------
//  `expense_templates` pul harakati emas, sozlama katalogi — xuddi
//  `AdjustmentReason` yoki `FeeCategory` kabi. SPEC §4.1 ning
//  o'zgarmaslik qoidasi (faqat INSERT) bunga tegishli EMAS: `app_rw` ga
//  SELECT/INSERT/UPDATE/DELETE to'liq beriladi (`students_parity_p1_guards.sql`
//  naqshi, `Migrations/Sql/expense_templates_guards.sql`).
//
//  TOIFA — Accounts.cs NING YOPIQ RO'YXATIDAN
//  ---------------------------------------------
//  `Category` — `Accounts.ExpenseCategories` (salary/utilities/supplies/rent/
//  repair/other) dan biri, XUDDI `Expense.Category` kabi (ExpenseService.cs).
//  Baza darajasida CHECK YO'Q — `expenses.category` ning o'zida ham yo'q
//  (tekshiruv `ExpenseTemplateService`da, ilova qatlamida, `Accounts.cs`
//  izohidagi qoida: "yopiq ro'yxat" ATAYLAB DB emas, kod darajasida
//  saqlanadi — ro'yxat yiliga bir marta ham o'zgarmaydi, lekin o'zgarsa
//  migratsiyasiz o'zgarishi kerak emas).
// ===========================================================================

/// <summary>
/// Rejalashtirilgan chiqim shabloni (F6.01). Har oy <see cref="DayOfMonth"/>
/// kunida direktorga Telegram orqali eslatma yuboriladi
/// (<c>ExpenseTemplateReminderService</c>) va P&amp;L 2.0'ning <c>planned</c>
/// jadvalida "rejalashtirilgan" qator sifatida ko'rinadi.
///
/// <para>
/// <b>Haqiqiy chiqimga AYLANMAYDI o'zi.</b> Direktor yoki admin eslatmani
/// ko'rib, xohlasa <c>ExpenseService.CreateAsync</c> orqali haqiqiy chiqim
/// yozadi — bu ikkinchi, alohida qadam (fayl boshidagi izoh).
/// </para>
/// </summary>
public class ExpenseTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Ko'rsatiladigan nom, masalan "Internet to'lovi".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Chiqim toifasi — <see cref="SchoolLms.Application.Billing.Accounts.ExpenseCategories"/>
    /// dan biri (fayl boshidagi izoh). Xizmat qatlamida tekshiriladi.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Kutilayotgan summa (so'm, har doim &gt; 0).</summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Oyning qaysi kunida eslatma yuborilsin (1..28 — <see cref="SchoolLms.Application.Billing.BillingSettings.PaymentDueDay"/>
    /// bilan bir xil chegara: 29/30/31 hamma oyda bo'lavermaydi).
    /// </summary>
    public short DayOfMonth { get; set; }

    /// <summary>Faolsiz shablon uchun eslatma yuborilmaydi va u yangi hisobotda ko'rinmaydi.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
}

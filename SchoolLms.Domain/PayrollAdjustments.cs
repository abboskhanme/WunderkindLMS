namespace SchoolLms.Domain;

// ===========================================================================
//  Xodimga bonus / jarima — F11.01, F11.02 (finance-parity.md §2.11, Batch B).
//  Manba: docs/modules/finance-parity.md §3.2 (B2, B3); docs/modules/hr.md
//  §2.7, §4.2 (`hr_employees`), §11 (waylonlar).
// ===========================================================================
//
//  BU HR-01 EMAS — VA ATAYLAB
//  ---------------------------
//  `hr.md` §4.2 ning to'liq "to'lov ustuni" (`hr_employees`, 22 jadval) hali
//  qurilmagan — u HR-01/02/03 ning ishi (~118 soat), bu vazifa esa faqat
//  F11.01/F11.02: Bonus/Jarima registri. `payroll_adjustments.employee_id
//  uuid references hr_employees(id)` (finance-parity §3.2 B3) yoza olmaymiz —
//  nishon jadval yo'q.
//
//  Shuning uchun bu yerda `hr_employees` ning O'ZI emas, balki uning
//  IDENTIFIKATSIYA naqshi qayta ishlatiladi: `hr_employees` ham xuddi shunday
//  "aynan bitta" qoidasi bilan `teacher_id` YOKI `user_id` ga bog'lanadi
//  (`ck_hr_employees_identity`, hr.md §4.2). HR-01 qurilganda, bu yozuvlar
//  `hr_employees.teacher_id` / `.user_id` orqali JOIN bilan topiladi — yangi
//  ustun yoki ma'lumot ko'chirish shart emas. Bu ataylab qilingan tanlov,
//  chunki HR-01 ning butun sxemasini oldindan yozib qo'yish boshqa
//  migratsiya egasining ishiga bostirib kirish bo'lardi (CLAUDE.md: faqat
//  qo'shib boriladi, kelishilmagan qayta qurish yo'q).
//
//  PUL QOIDASI (SPEC §4)
//  ----------------------
//  `payroll_adjustments` — FAQAT QO'SHILADI (`app_rw` da UPDATE/DELETE yo'q,
//  `Migrations/Sql/payroll_adjustments_guards.sql`). Xato yozuv
//  <see cref="PayrollAdjustment.ReversalOf"/> bilan qarshi qator qo'shib
//  tuzatiladi — xuddi `CashHandover` dagidek. Bu yozuv JURNALGA (`ledger_entries`)
//  TUSHMAYDI: pul hali hech qayerga ko'chmadi, faqat oylik hisob-kitobga
//  KIRITILADIGAN raqam qayd etilmoqda (hr.md §2.7 — "employee balance"). Pulning
//  o'zi HR-09 (`PayrollService`) oylik hujjatni POST qilganda jurnalga tushadi
//  (hr.md §5.2, Batch 1: bonus/jarima allaqachon `gross` ichida).
//
//  `adjustment_reasons` — sabab KATALOGI (F11.02): moliyaviy emas, to'liq CRUD
//  (`students_parity_p1_guards.sql` naqshi — katalog jadvallarida REVOKE yo'q).
//  O'chirish o'rniga FAOLSIZLASH (`is_active=false`, `CategoriesPage.tsx`
//  naqshi) — xizmat qatlamida.

/// <summary>Bonus / jarima turi. Ikkalasi ham shu ikki qiymatdan birini oladi.</summary>
public static class AdjustmentKind
{
    public const string Bonus = "bonus";
    public const string Penalty = "penalty";

    public static readonly IReadOnlyList<string> All = [Bonus, Penalty];
}

/// <summary>
/// Bonus/jarima sababi katalogi (F11.02) — EduSchool transaction type
/// daraxtining "Bonus"/"Jarima" sub-tablari o'rnini bosadi. Moliyaviy EMAS —
/// to'liq CRUD, o'chirish o'rniga <see cref="IsActive"/> = false.
/// </summary>
public class AdjustmentReason
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary><see cref="AdjustmentKind"/>.</summary>
    public string Kind { get; set; } = AdjustmentKind.Bonus;

    public string Name { get; set; } = string.Empty;

    /// <summary>Faolsiz sabab yangi yozuvda tanlanmaydi, eski yozuvlarda ko'rinaveradi.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Ro'yxatdagi tartib (kichigi tepada).</summary>
    public int Position { get; set; }
}

/// <summary>
/// Xodimga bir martalik bonus yoki jarima (F11.01) — EduSchool
/// <c>FINANCE_ALL.BONUS</c> / <c>FINANCE_ALL.PENALTY</c> ning ko'zgusi
/// (hr.md §2.7: HR qoidalar dvigateli — <c>hr_rules</c> — bilan ARALASHTIRMANG,
/// bu qo'lda kiritiladigan, bir martalik yozuv).
///
/// <para>
/// <b>Xodim identifikatsiyasi.</b> Aynan bitta: <see cref="TeacherId"/> YOKI
/// <see cref="UserId"/> (`ck_payroll_adjustments_identity` — `hr_employees`
/// dagi bilan bir xil naqsh, fayl boshidagi izoh). O'qituvchi bo'lmagan
/// xodim (kassir, administrator, ...) <c>app_users</c> orqali, o'qituvchi —
/// <c>teachers</c> orqali bog'lanadi.
/// </para>
///
/// <para>
/// <b>FAQAT QO'SHILADI</b> (SPEC §4.1): <c>app_rw</c> da UPDATE va DELETE yo'q.
/// Xato yozuv <see cref="ReversalOf"/> bilan qarshi qator qo'shib tuzatiladi —
/// sabab <see cref="ReversalReason"/> da MAJBURIY (bo'sh <c>reversal_of</c> —
/// bo'sh <c>reversal_reason</c>, va aksincha: `ck_payroll_adjustments_reversal`).
/// </para>
///
/// <para>
/// <b>Oylik davr orqaga surilmaydi</b> (SPEC §4, "no back-dating"): xizmat
/// <see cref="PeriodYear"/>/<see cref="PeriodMonth"/> joriy oydan OLDINGI
/// bo'lishiga yo'l qo'ymaydi — bu bazada CHECK emas (joriy oy vaqt bilan
/// o'zgaradi), <c>PayrollAdjustmentService</c> da tekshiriladi.
/// </para>
/// </summary>
public class PayrollAdjustment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>O'qituvchi bo'lsa — <c>teachers.id</c>. Aynan bitta identifikatsiya (yuqoridagi izoh).</summary>
    public string? TeacherId { get; set; }

    /// <summary>O'qituvchi bo'lmagan xodim bo'lsa — <c>app_users.id</c>.</summary>
    public string? UserId { get; set; }

    /// <summary><see cref="AdjustmentKind"/>.</summary>
    public string Kind { get; set; } = AdjustmentKind.Bonus;

    /// <summary>Sabab — <see cref="AdjustmentReason"/>, kind bilan MOS kelishi shart (xizmatda tekshiriladi).</summary>
    public Guid ReasonId { get; set; }

    /// <summary>Summa (har doim &gt; 0; storno ham musbat — uni <see cref="ReversalOf"/> belgilaydi).</summary>
    public decimal Amount { get; set; }

    public short PeriodYear { get; set; }

    /// <summary>1..12 — qaysi oy uchun (oylik hisob-kitobga shu oyda kiradi).</summary>
    public short PeriodMonth { get; set; }

    public string? Comment { get; set; }

    /// <summary>Rasm (Jarima uchun, EduSchool naqshi) — <c>POST /api/admin/uploads</c> dan qaytgan URL.</summary>
    public string? ImageUrl { get; set; }

    /// <summary>Kim yozdi (app_users.id) — JWT'dan, so'rov tanasidan EMAS (SPEC §4.4).</summary>
    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Storno: qaysi yozuvni bekor qilmoqda. null = oddiy yozuv.</summary>
    public Guid? ReversalOf { get; set; }

    /// <summary>Bekor qilish sababi — <see cref="ReversalOf"/> to'lganda MAJBURIY.</summary>
    public string? ReversalReason { get; set; }
}

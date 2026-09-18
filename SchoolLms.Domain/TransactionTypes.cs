namespace SchoolLms.Domain;

// ===========================================================================
//  Tranzaksiya turi katalogi — mijoz yuborgan EduSchool kassa kirim shakli
//  ("Tranzaksiya turi *" — majburiy dropdown: Do'ppi uchun, Kitob uchun
//  to'lov, Mock uchun to'lov, ...) va moliya sozlamalari ekrani (pill-tab:
//  Kirim · Chiqim · Bonus · Jarima, jadval "№ / Nomi / Amallar").
// ===========================================================================
//
//  NEGA FAQAT `in`/`out`, EDUSCHOOL'DAGI TO'RTTA PILL EMAS
//  ---------------------------------------------------------
//  Mijoz skrinshotida to'rtta pill bor: Kirim, Chiqim, Bonus, Jarima. Bizda
//  Bonus/Jarima uchun bu katalog ALLAQACHON mavjud —
//  <see cref="AdjustmentReason"/> (F11.02, `PayrollAdjustments.cs`), aynan
//  shu shaklda (kind, name, is_active, position) va aynan shu vazifada:
//  uning o'z izohi buni to'g'ridan-to'g'ri aytadi — "EduSchool transaction
//  type daraxtining 'Bonus'/'Jarima' sub-tablari o'rnini bosadi". Uni shu
//  yerda TAKRORLASH ikkita mustaqil, bir-biridan uzoqlashadigan katalog
//  degani bo'lardi (bittasi `payroll_adjustments.reason_id` orqali ishlatiladi,
//  ikkinchisi hech qayerga bog'lanmagan) — CLAUDE.md "additive, no
//  unrequested duplication" qoidasiga zid. Shuning uchun bu yerda faqat
//  Kirim/Chiqim — kassa (`cash_box_transactions`) tomoni, hali qamrab
//  olinmagan yagona qism.
//
//  NEGA `Accounts.cs` GA TEGMAYDI — YOPIQ RO'YXAT QOIDASI BUZILMAYDI
//  --------------------------------------------------------------------
//  `docs/modules/existing-module-gaps.md` §3.4 (P0 jadvali, "Editable
//  transaction-type tree"): **declined** — "Accounts.cs is closed on
//  purpose... an editable free-text account string means one typo silently
//  loses money from a report." Bu yerdagi katalog o'sha qarorni BUZMAYDI:
//  u faqat kassa tranzaksiyasiga (`cash_box_transactions.transaction_type_id`)
//  yopishtiriladigan YORLIQ — hisobot AKKAUNT bo'yicha emas, hamon
//  `Accounts.cs`/`method` bo'yicha yig'iladi (`CashBoxService.Contributions`,
//  o'zgarmagan). Tur o'chirilsa yoki nomi xato yozilsa — kassa balansi va
//  P&L bitta so'm ham farq qilmaydi, faqat jadval qatoridagi izoh matni
//  o'zgaradi. Shuning uchun bu yerda to'liq CRUD xavfsiz: `Accounts.cs`
//  YOPIQ va FLAT qolaveradi (vazifa topshirig'i, qattiq cheklov).
//
//  MOLIYAVIY EMAS — TO'LIQ CRUD (`AdjustmentReason`/`ExpenseTemplate` naqshi)
//  ----------------------------------------------------------------------------
//  Ichida summa yo'q, faqat nom. `app_rw` ga SELECT/INSERT/UPDATE/DELETE
//  to'liq beriladi (`transaction_types_guards.sql`). SPEC §4.1 ning
//  o'zgarmaslik qoidasi bu yerga tegishli EMAS.
//
//  `IsSeeded` — NEGA KERAK
//  ------------------------
//  Mijoz yuborgan moliya sozlamalari skrinshotida: "bitta seed qilingan
//  qatorda o'chirish tugmasi yo'q, faqat tahrirlash". Migratsiya qo'shgan
//  qatorlar (seed) shu bayroq bilan belgilanadi — xizmat qatlami ularni
//  NOMINI o'zgartirishga (rename) ruxsat beradi, lekin O'CHIRISHNI rad
//  etadi (`TransactionTypeService.DeleteAsync`). Admin qo'shgan yangi
//  qator esa erkin o'chiriladi.

/// <summary>
/// Tranzaksiya turi kassa harakati bo'yicha: <see cref="In"/> — Kirim,
/// <see cref="Out"/> — Chiqim. Bonus/Jarima uchun <see cref="AdjustmentReason"/>
/// ishlatiladi — fayl boshidagi izoh.
/// </summary>
public static class TransactionTypeKind
{
    public const string In = "in";
    public const string Out = "out";

    public static readonly IReadOnlyList<string> All = [In, Out];
}

/// <summary>
/// Tranzaksiya turi katalogi qatori — <see cref="CashBoxTransaction.TransactionTypeId"/>
/// orqali kassa harakatiga YORLIQ sifatida yopishtiriladi (fayl boshidagi izoh:
/// hisobot/aккаунт bilan bog'liq EMAS).
/// </summary>
public class TransactionType
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary><see cref="TransactionTypeKind"/>.</summary>
    public string Kind { get; set; } = TransactionTypeKind.In;

    public string Name { get; set; } = string.Empty;

    /// <summary>Faolsiz tur yangi tranzaksiyada tanlanmaydi, eski yozuvlarda ko'rinaveradi.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Migratsiya seed qilganmi? true bo'lsa — <see cref="TransactionTypeService.DeleteAsync"/>
    /// uni o'chirishni rad etadi (faqat nomi/faolligi o'zgaradi). Fayl boshidagi izoh.
    /// </summary>
    public bool IsSeeded { get; set; }

    /// <summary>Ro'yxatdagi tartib (kichigi tepada) — <see cref="AdjustmentReason.Position"/> bilan bir xil naqsh.</summary>
    public int Position { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

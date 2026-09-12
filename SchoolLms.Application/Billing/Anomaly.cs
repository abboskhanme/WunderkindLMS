using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  Tungi tekshiruv — DTO'lar, sozlamalar va xizmat shartnomasi (SPEC §4.6).
//  Vazifa: P1-14.
// ===========================================================================
//
//  BU FAYLDA EF YO'Q. Jadval entity'si — `SchoolLms.Domain/Billing.cs` dagi
//  `FinanceAnomalyFlag` (repozitoriyadagi barcha entity'lar Domain'da). Bu yerda
//  faqat tashqariga chiqadigan shakllar va tekshiruvning sozlanadigan
//  chegaralari turadi.
//
//  NEGA CHEGARALAR KONSTANTA, JADVAL EMAS
//  --------------------------------------
//  "Kassirning odatdagi ish soati" uchun sxemada ustun YO'Q va uni qo'shish
//  yangi jadval degani (`users` — eski, `text` id'li jadval; unga ustun qo'shish
//  P1-21 bilan to'qnashadi). Chegaralar shu yerda, BITTA joyda yozilgan va
//  hujjatlashtirilgan; ularni haqiqiy sozlamaga aylantirish kerak bo'lsa —
//  `billing_settings` ga ikkita ustun va bitta migratsiya (docs/PENDING_WIRING.md).

/// <summary>
/// Tekshiruvning sozlanadigan chegaralari — SPEC §4.6 shartlarining
/// "qancha" qismi. Hammasi BITTA joyda: raqamni o'zgartirish uchun kodni
/// qidirib yurish kerak emas.
/// </summary>
public static class AnomalySettings
{
    /// <summary>
    /// Kassirning odatdagi ish kuni BOSHLANISHI (maktab mintaqasi, UTC+5).
    /// Bundan oldin qabul qilingan to'lov bayroqlanadi.
    /// </summary>
    public static readonly TimeOnly WorkDayStart = new(8, 0);

    /// <summary>
    /// Kassirning odatdagi ish kuni TUGASHI. Shu vaqtdan keyin (yoki
    /// <see cref="WorkDayStart"/> dan oldin) qabul qilingan to'lov bayroqlanadi.
    /// </summary>
    public static readonly TimeOnly WorkDayEnd = new(20, 0);

    /// <summary>
    /// "Tez storno" chegarasi (SPEC §4.6: "reversals within 24 h of the original").
    /// </summary>
    public static readonly TimeSpan FastReversalWindow = TimeSpan.FromHours(24);

    /// <summary>
    /// Har tunda qancha orqaga qaralsin.
    ///
    /// <para>
    /// Chegara BOR, chunki tekshiruv har tunda yuradi: chegarasiz so'rov bir
    /// kun kelib butun tarixni (yuz minglab to'lov) qayta ko'rib chiqardi va
    /// natija baribir o'zgarmasdi — eski hodisalar allaqachon bayroqlangan.
    /// 90 kun = uchta yopilgan oy, ya'ni ilova bir necha hafta o'chib turgan
    /// bo'lsa ham hech narsa o'tkazib yuborilmaydi.
    /// </para>
    /// </summary>
    public static readonly TimeSpan ScanLookback = TimeSpan.FromDays(90);

    /// <summary>
    /// Tungi yurish soati (maktab mintaqasi). 03:00 — kassa yopiq, accrual
    /// (12 soatlik tsikl) bilan ustma-ust tushmaydi.
    /// </summary>
    public static readonly TimeOnly NightlyRunAt = new(3, 0);

    /// <summary>Ish soatlari oralig'ida bo'lmagan lahzami? (maktab mintaqasida).</summary>
    public static bool IsOutsideWorkHours(DateTimeOffset instant)
    {
        var local = TimeOnly.FromDateTime(AppClock.ToLocal(instant));
        return local < WorkDayStart || local >= WorkDayEnd;
    }
}

/// <summary>
/// Bayroqning tashqi ko'rinishi (direktor paneli va API).
/// </summary>
/// <param name="Id">Bayroq id'si — yopish endpoint'i shuni oladi.</param>
/// <param name="Kind">To'rt shartdan biri — <see cref="AnomalyKind"/>.</param>
/// <param name="KindLabel">O'zbekcha nom (UI shuni ko'rsatadi).</param>
/// <param name="RefType">Manba jadval turi — <see cref="AnomalyRefType"/>.</param>
/// <param name="RefId">Manba yozuv id'si (smena / to'lov / hisob-faktura).</param>
/// <param name="OccurredAt">Hodisaning o'zi qachon bo'lgan.</param>
/// <param name="DetectedAt">Tekshiruv uni qachon topgan.</param>
/// <param name="Amount">Pul o'lchami (nomuvofiqlik, storno summasi). null = yo'q.</param>
/// <param name="Summary">O'zbekcha izoh.</param>
/// <param name="Details">Raqamlar (JSON matni) — tekshiruvni qayta hisoblash uchun.</param>
/// <param name="ResolvedAt">Yopilgan lahza. null = ochiq.</param>
/// <param name="ResolvedBy">Kim yopgan (users.id).</param>
/// <param name="ResolvedByName">Kim yopgan (ism) — panelda ko'rsatish uchun.</param>
/// <param name="ResolvedReason">Yozma sabab (majburiy).</param>
public record AnomalyFlagDto(
    Guid Id,
    string Kind,
    string KindLabel,
    string RefType,
    Guid RefId,
    DateTimeOffset OccurredAt,
    DateTimeOffset DetectedAt,
    decimal? Amount,
    string Summary,
    string? Details,
    DateTimeOffset? ResolvedAt,
    string? ResolvedBy,
    string? ResolvedByName,
    string? ResolvedReason);

/// <summary>Turlar kesimidagi hisoblagich (panelning yuqori qatori).</summary>
/// <param name="Kind">Shart kodi.</param>
/// <param name="KindLabel">O'zbekcha nom.</param>
/// <param name="Unresolved">Yopilmaganlar soni.</param>
/// <param name="Total">Jami (yopilganlar bilan).</param>
public record AnomalyKindCountDto(string Kind, string KindLabel, int Unresolved, int Total);

/// <summary>
/// <c>GET /api/admin/finance/flags</c> javobi.
///
/// <para>
/// <b>Nega ro'yxat emas, o'ram (envelope)?</b> SPEC §4.6: direktor panelida
/// "yopilmagan nomuvofiqlik HISOBLAGICHI" turadi va uni bekor qilib bo'lmaydi.
/// Hisoblagich ro'yxat uzunligidan HISOBLANMASLIGI kerak — ro'yxat sahifalangan
/// va filtrlangan, hisoblagich esa har doim to'liq bo'lishi shart.
/// </para>
/// </summary>
/// <param name="Unresolved">Yopilmagan bayroqlar soni — panel shuni ko'rsatadi.</param>
/// <param name="Total">Jami bayroqlar soni.</param>
/// <param name="UnresolvedAmount">Yopilmagan bayroqlarning pul o'lchami yig'indisi (so'm).</param>
/// <param name="ByKind">Turlar kesimi.</param>
/// <param name="Items">Ro'yxat (so'rovdagi filtrga mos, eng yangisidan).</param>
public record AnomalyFlagsDto(
    int Unresolved,
    int Total,
    decimal UnresolvedAmount,
    IReadOnlyList<AnomalyKindCountDto> ByKind,
    IReadOnlyList<AnomalyFlagDto> Items);

/// <summary>
/// Bayroqni yopish so'rovi. <see cref="Reason"/> dan boshqa maydon YO'Q:
/// yopgan shaxs va vaqt serverda aniqlanadi (SPEC §4.4).
/// </summary>
/// <param name="Reason">Yozma sabab — bo'sh yoki faqat probel bo'lsa 400.</param>
public record ResolveAnomalyRequest(string Reason);

/// <summary>Bitta tekshiruv yurishining natijasi (jurnal va test uchun).</summary>
/// <param name="Scanned">Tekshiruv qaysi lahzadan boshlab qaradi.</param>
/// <param name="Created">Shu yurishda YANGI yozilgan bayroqlar soni.</param>
/// <param name="CreatedByKind">Turlar kesimida yangi bayroqlar soni.</param>
public record AnomalyScanResult(
    DateTimeOffset Scanned,
    int Created,
    IReadOnlyDictionary<string, int> CreatedByKind);

/// <summary>
/// Tungi tekshiruv va bayroqlar bilan ishlash. Implementatsiya:
/// <see cref="AnomalyService"/>.
/// </summary>
public interface IAnomalyService
{
    /// <summary>
    /// Bitta tekshiruv yurishi: to'rtta shartni qaraydi va YETISHMAYOTGAN
    /// bayroqlarni qo'shadi. <b>Idempotent</b> — ikkinchi yurish dublikat
    /// yaratmaydi (<c>(kind, ref_id)</c> unikal).
    /// </summary>
    /// <param name="since">Shu lahzadan keyingi hodisalar. null =
    /// <see cref="AnomalySettings.ScanLookback"/>.</param>
    Task<AnomalyScanResult> ScanAsync(DateTimeOffset? since = null, CancellationToken ct = default);

    /// <summary>Bayroqlar ro'yxati va hisoblagichlar.</summary>
    /// <param name="unresolved">true = faqat yopilmaganlar (panel uchun).</param>
    /// <param name="kind">Tur bo'yicha filtr (ixtiyoriy).</param>
    /// <param name="limit">Ro'yxat uzunligi chegarasi.</param>
    Task<AnomalyFlagsDto> ListAsync(
        bool unresolved = false, string? kind = null, int limit = 200, CancellationToken ct = default);

    /// <summary>
    /// Bayroqni YOZMA SABAB bilan yopadi (SPEC §4.6). Bo'sh sabab — 400,
    /// allaqachon yopilgan bayroq — 409. O'chirish metodi YO'Q va bo'lmaydi.
    /// </summary>
    Task<AnomalyFlagDto> ResolveAsync(
        Guid flagId, string reason, string resolvedByUserId, CancellationToken ct = default);
}

/// <summary>O'zbekcha nomlar — UI va audit matnlari uchun yagona manba.</summary>
public static class AnomalyLabels
{
    public static string For(string kind) => kind switch
    {
        AnomalyKind.ShiftVariance => "Smena nomuvofiqligi",
        AnomalyKind.FastReversal => "24 soat ichidagi storno",
        AnomalyKind.OffHoursPayment => "Ish vaqtidan tashqari to'lov",
        AnomalyKind.PaidWithoutAllocation => "Taqsimotsiz \"to'langan\" hisob-faktura",
        _ => kind,
    };
}

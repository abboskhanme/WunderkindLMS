using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  OYMA-OY QARZDORLIK (arrears pivot) — QO'SHIMCHA YURTIB TURUVCHI KOD
//  (docs/modules/finance-parity.md §2.13, gaplar F13.02/F13.03/F13.06.)
// ===========================================================================
//
//  NEGA ALOHIDA FAYL
//  ------------------
//  `FinanceReportQueries.cs` — qarzdorlar hisobotining (`DebtorsAsync`) yagona
//  egasi (finance-parity.md §4, "Shared files" jadvali: "FinanceReportQueries.cs
//  — S5 only; S4 (va boshqa hamma) yangi fayl yozadi"). Shu qoidaga rioya
//  qilib, `ArrearsPivotAsync` uchun YANGI, alohida yordamchi mantiq shu yerda —
//  asosiy faylga esa faqat CHAQIRUV qo'shildi (bir necha qator, qo'shimcha
//  parametr bilan). Arifmetikaning O'ZI (qarz ta'rifi, `EffectiveAllocations`,
//  `MutableCell`) BITTA joyda — asosiy faylda — qoladi: bu yerda uni
//  TAKRORLASH yo'q, faqat o'sha metodga kiruvchi ma'lumotni tayyorlash bor.
//
//  BU FAYLDAGI IKKI NARSA
//  -----------------------
//  1. <see cref="ArrearsAccrualCell"/> — `ArrearsPivotAsync` ichida
//     "hisoblangan katak" ni ifodalaydigan yordamchi tur (F13.02: toifa bilan
//     yoki toifasiz — <c>CategoryId</c> null bo'lsa "ajratish" o'chiq).
//     Anonim tur o'rniga NOM berilgan tur ishlatildi, chunki uni ikki xil
//     LINQ shoxobchasi (bo'lingan / qo'shilgan) BIR XIL shaklda qaytarishi
//     kerak — anonim turlar buni faqat maydonlar ANIQ bir xil tartibda va
//     nomda bo'lsagina qiladi, nomlangan tur esa niyatni ochiq yozadi.
//  2. <see cref="FinanceReportQueries.ActiveGroupMemberIds"/> — F13.06 uchun:
//     bitta o'quv guruhining HOZIRGI a'zolari (<c>study_group_members</c>,
//     <c>left_on is null</c>). Bitta qatorlik so'rov, lekin nomi bilan
//     alohida turishi "guruh a'zoligi" tushunchasini ArrearsStudents ichida
//     yashirmaydi — kim o'qisa ham darrov topadi.

/// <summary>
/// <see cref="FinanceReportQueries.ArrearsPivotAsync"/> ning bitta "hisoblangan
/// katagi" — bitta o'quvchi (+ ixtiyoriy toifa) × bitta oy.
/// </summary>
/// <param name="CategoryId">
/// F13.02 — "toifalar bo'yicha ajratish" YOQILGANDA shu katakning toifasi;
/// O'CHIQ bo'lsa <c>null</c> (toifalar allaqachon XOTIRADA qo'shilgan).
/// </param>
internal sealed record ArrearsAccrualCell(
    string StudentId, string FullName, string ClassName, string ParentPhone, bool IsArchived,
    DateOnly PeriodMonth, Guid? CategoryId, string? CategoryCode, string? CategoryName, decimal Amount);

public sealed partial class FinanceReportQueries
{
    /// <summary>
    /// F13.06 — bitta o'quv guruhining HOZIRGI a'zolari (student id'lar).
    ///
    /// <para>
    /// <c>study_group_members</c> — sanali (tarixiy) jadval: a'zo chiqqanda
    /// qator o'chirilmaydi, <c>left_on</c> to'ldiriladi (SPEC: `StudyGroups.cs`).
    /// Shuning uchun "hozirgi a'zo" — <c>left_on is null</c>, "qachondir a'zo
    /// bo'lgan" emas. Davr (<c>FromMonth</c>/<c>ToMonth</c>) bilan kesishtirish
    /// ATAYLAB qilinmadi: hisobot "bugun kim shu guruhda" degan savolga javob
    /// beradi, "o'sha oyda kim edi" ga emas — qarzdorlar hisoboti ham sinfni
    /// xuddi shunday, "hozirgi" holat bo'yicha filtrlaydi (<c>students.class_name</c>).
    /// </para>
    /// </summary>
    private IQueryable<string> ActiveGroupMemberIds(Guid groupId) =>
        db.StudyGroupMembers.AsNoTracking()
            .Where(m => m.GroupId == groupId && m.LeftOn == null)
            .Select(m => m.StudentId);
}

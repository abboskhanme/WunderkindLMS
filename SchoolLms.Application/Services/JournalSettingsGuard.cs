using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  §5.5 ning ikkita "ma'lumot sifati darvozasi" — jurnal tarafi.
//
//  NEGA XIZMATDA, CONTROLLERDA EMAS
//  --------------------------------
//  Jurnalga ikkita yo'l olib boradi: admin (`api/admin/journal`) va o'qituvchi
//  (`api/teacher/journal`). Ikkalasi ham `JournalService` ga tushadi. Qoida
//  controllerga yozilsa, ikkitasidan biriga qo'shishni unutish yetarli va
//  bayroq jimgina teshik bo'lib qolardi — bunga tayyor misol bor:
//  chorak bahosini yopish tekshiruvi (`Quarter.GradesOpen`) FAQAT o'qituvchi
//  controllerida bor, admin controllerida yo'q.
//
//  DAVOMAT SABABI — BIZDA SABAB O'ZI HOLATDIR
//  ------------------------------------------
//  Bu sxemada alohida "holat" ustuni YO'Q: `JournalEntry.ReasonId` = null
//  "keldi", null emas = "kelmadi/kechikdi" degani, sababning o'zi esa
//  `AbsenceReason` qatori. Ya'ni "sababsiz yo'qlik" ni yozishning yagona yo'li —
//  YARAROQSIZ sabab id'si: bo'sh satr yoki katalogdan o'chirilgan qator
//  (`SettingsController.SaveAbsenceReasons` sabablarni O'CHIRIB qayta yozadi,
//  eski jurnal yozuvlari esa eski id bilan qoladi). Bugun ikkalasi ham jimgina
//  qabul qilinadi va hisobotda "?" bo'lib chiqadi. Bayroq aynan shuni yopadi.
//
//  DARSNI YOPISH — IKKI YO'L, IKKI XIL JAVOB
//  -----------------------------------------
//  `LessonNote.Conducted` ikki joyda true bo'ladi: o'qituvchi "Dars o'tildi"
//  belgisini qo'yganda (ATAYLAB) va birinchi baho/davomat kiritilganda
//  (AVTOMATIK). Ikkalasini ham bir xil rad etib bo'lmaydi: avtomatik yo'l rad
//  etilsa jurnalga BIRINCHI bahoni ham kiritib bo'lmasdi (o'sha paytda qolgan
//  o'quvchilar baholanmagan). Shuning uchun:
//    · ataylab yopish — 400 bilan RAD ETILADI;
//    · avtomatik yopish — jim O'TKAZIB YUBORILADI (dars "o'tildi" bo'lmaydi,
//      lekin baho yoziladi), va baholar to'lgach o'zi yopiladi.
// ===========================================================================

/// <summary>
/// <c>make_attendance_reason_required</c> va <c>is_student_grade_required</c>
/// bayroqlarini o'qiydi va ularga mos tekshiruvlarni bajaradi.
/// </summary>
public static class JournalSettingsGuard
{
    /// <summary>Sabab yaroqsiz bo'lganda (bayroq yoqiq).</summary>
    public const string ReasonRequiredMessage =
        "Davomat sababi majburiy — ro'yxatdan sabab tanlang.";

    /// <summary>Baholar to'liq emas, dars ataylab yopilmoqchi (bayroq yoqiq).</summary>
    public const string GradesRequiredMessage =
        "Darsni yopib bo'lmaydi: darsda qatnashgan o'quvchilarning hammasiga baho qo'yilmagan.";

    /// <summary>Jurnalga tegishli ikkita bayroq (qator yo'q bo'lsa — ikkalasi ham o'chiq).</summary>
    public readonly record struct Flags(bool AttendanceReasonRequired, bool GradeRequired);

    public static async Task<Flags> FlagsAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var meta = await db.SchoolMeta.AsNoTracking()
            .Select(m => new { m.MakeAttendanceReasonRequired, m.IsStudentGradeRequired })
            .FirstOrDefaultAsync(ct);
        return new Flags(
            meta?.MakeAttendanceReasonRequired ?? false,
            meta?.IsStudentGradeRequired ?? false);
    }

    /// <summary>
    /// Berilgan sabab id'si haqiqiy katalog qatorimi. <c>null</c> — "keldi", ya'ni
    /// tekshiriladigan narsa yo'q; bo'sh satr yoki topilmagan id — yo'q.
    /// </summary>
    public static async Task<bool> ReasonIsUsableAsync(
        IAppDbContext db, string? reasonId, CancellationToken ct = default)
    {
        if (reasonId is null) return true;
        if (string.IsNullOrWhiteSpace(reasonId)) return false;
        return await db.AbsenceReasons.AsNoTracking().AnyAsync(r => r.Id == reasonId, ct);
    }

    /// <summary>
    /// Shu dars katagida (sinf+fan+chorak+sana+dars+guruh) qatnashgan HAR BIR o'quvchida
    /// baho bormi.
    ///
    /// <para>
    /// "Qatnashgan" = yo'q deb belgilanmagan. Kechikkan o'quvchi (<c>AbsenceReason.IsLate</c>)
    /// darsda BO'LGAN, ya'ni undan ham baho kutiladi; kelmagan o'quvchidan esa kutilmaydi —
    /// aks holda darsni umuman yopib bo'lmasdi.
    /// </para>
    /// <para>Ro'yxat bo'sh bo'lsa (sinf topilmadi yoki o'quvchisi yo'q) — <c>true</c>:
    /// bo'sh sinf darsni yopishga to'sqinlik qilmasligi kerak.</para>
    /// </summary>
    public static async Task<bool> SlotFullyGradedAsync(
        IAppDbContext db, string classId, string subjectId, int quarter,
        string date, int period, int subGroup, CancellationToken ct = default)
    {
        var cls = await db.Classes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == classId, ct);
        if (cls is null) return true;

        // Bo'lingan darsda (SubGroup != 0) faqat o'sha guruh qatnashadi; 0 — butun sinf.
        var roster = await db.Students.AsNoTracking()
            .Where(s => !s.IsArchived && s.ClassName == cls.Name)
            .Where(s => subGroup == 0 || s.SubGroup == subGroup)
            .Select(s => s.Id)
            .ToListAsync(ct);
        if (roster.Count == 0) return true;

        var entries = await db.JournalEntries.AsNoTracking()
            .Where(e => e.ClassId == classId && e.SubjectId == subjectId && e.Quarter == quarter
                && e.Date == date && e.Period == period)
            .Select(e => new { e.StudentId, e.Grade, e.ReasonId })
            .ToListAsync(ct);
        var byStudent = entries
            .GroupBy(e => e.StudentId)
            .ToDictionary(g => g.Key, g => g.First());

        var lateReasonIds = (await db.AbsenceReasons.AsNoTracking()
            .Where(r => r.IsLate).Select(r => r.Id).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);

        foreach (var studentId in roster)
        {
            byStudent.TryGetValue(studentId, out var entry);
            var marked = entry?.ReasonId;
            // Kelmagan — bahodan ozod.
            if (!string.IsNullOrEmpty(marked) && !lateReasonIds.Contains(marked)) continue;
            if (entry?.Grade is null) return false;
        }

        return true;
    }
}

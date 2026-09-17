using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  O'quvchini arxivlash — bitta joyda turadigan QOIDA (§2.2 va §5.5).
//
//  NEGA XIZMAT, CONTROLLER EMAS
//  ----------------------------
//  Arxivlashning ikkita kirish nuqtasi bor: bitta o'quvchi
//  (`POST students/{id}/archive`) va ommaviy
//  (`POST students/archive-many`). Qoida ikkalasida ham BIR XIL bo'lishi
//  shart — aks holda "qarzdorni arxivlab bo'lmaydi" qoidasini ommaviy tugma
//  orqali chetlab o'tish mumkin bo'lardi, ya'ni qoida umuman yo'q bo'lardi.
//
//  QARZDORLIK TO'SIG'I NEGA PULGA OID QARORDIR
//  -------------------------------------------
//  §9 Q4: arxivlash — qarzning yo'qolishining eng oson yo'li. O'quvchi
//  arxivga ketadi, faol ro'yxatdan chiqadi, qarzdorlar hisobotida ko'rinmaydi
//  — va pul jimgina bug'lanadi. Shuning uchun bayroq sukut bo'yicha YOQIQ
//  (`ArchiveOnlyNonDebtorStudents = true`, ParityModel.cs) va uni faqat
//  superadmin, faqat ATAYLAB (`force: true`) chetlab o'ta oladi.
//
//  BALANS ALOMATI
//  --------------
//  `StudentBalanceQuery` ning kelishuvi: MANFIY = qarzdor, MUSBAT = avans.
//  Shuning uchun to'siq `balance < 0` da ishlaydi va qarz miqdori `-balance`.
// ===========================================================================

/// <summary>
/// Arxivlash qoidalari: sabab (matn + katalog) va qarzdorlik to'sig'i.
/// </summary>
public sealed class StudentArchiveService(IAppDbContext db)
{
    /// <summary>Erkin matn bo'sh bo'lsa chiqadigan xabar — ikkala kirish nuqtasida bir xil.</summary>
    public const string ReasonRequiredMessage = "Arxivlash sababini yozing";

    /// <summary>Katalog qatori yaroqsiz bo'lsa.</summary>
    public const string ReasonNotFoundMessage = "Arxivlash sababi katalogdan topilmadi yoki faol emas";

    /// <summary>Qarzdorlik to'sig'i ishlaganda.</summary>
    public const string DebtorBlockedMessage =
        "Qarzi bor o'quvchini arxivlab bo'lmaydi. Avval qarzni yoping yoki "
        + "\"Sozlamalar → Umumiy\" bo'limidagi qoidani o'chiring.";

    /// <summary>Superadmin uchun — chetlab o'tish mumkinligini aytadigan qo'shimcha.</summary>
    public const string DebtorOverrideHintMessage =
        "Qarzi bor o'quvchi tanlangan. Superadmin sifatida qoidani chetlab o'tishingiz mumkin, "
        + "lekin qarz o'chmaydi — u arxivda ham qarzligicha qoladi.";

    /// <summary>
    /// Qarzdorlik to'sig'i shu so'rov uchun ishlaydimi.
    /// <para>
    /// <paramref name="isSuperAdmin"/> + <paramref name="force"/> = chetlab o'tish.
    /// Superadmin AVTOMATIK chetlab o'tmaydi: u ham avval rad javobini ko'rishi va
    /// qaytadan, ataylab tasdiqlashi kerak — aks holda direktor tasodifan bosgan
    /// tugma qarzni jimgina yo'qotardi.
    /// </para>
    /// </summary>
    public async Task<bool> DebtorGuardAppliesAsync(
        bool isSuperAdmin, bool force, CancellationToken ct = default)
    {
        if (isSuperAdmin && force) return false;
        var meta = await db.SchoolMeta.AsNoTracking().FirstOrDefaultAsync(ct);
        // Qator umuman yo'q bo'lsa — entity'ning sukut qiymati (`true`) amal qiladi.
        return meta?.ArchiveOnlyNonDebtorStudents ?? true;
    }

    /// <summary>
    /// Berilgan o'quvchilardan QARZI BORLARI. Qoldiqlar bitta partiyada olinadi
    /// (<see cref="StudentBalanceQuery.ForManyAsync"/>) — sikl ichida so'rov yo'q.
    /// </summary>
    public async Task<List<ArchiveBlockedStudentDto>> DebtorsAmongAsync(
        IReadOnlyList<Student> students, CancellationToken ct = default)
    {
        if (students.Count == 0) return [];

        var balances = await new StudentBalanceQuery(db)
            .ForManyAsync([.. students.Select(s => s.Id)], ct);

        return [.. students
            .Select(s => (Student: s, Balance: balances.GetValueOrDefault(s.Id)))
            .Where(x => x.Balance < 0)
            .OrderBy(x => x.Student.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(x => new ArchiveBlockedStudentDto(
                x.Student.Id, x.Student.FullName, x.Student.ClassName, -x.Balance))];
    }

    /// <summary>
    /// Katalog qatorini tekshiradi. <c>null</c> id — yaroqli ("Boshqa" tanlangan yoki
    /// katalog ishlatilmagan). Yaroqsiz bo'lsa <c>false</c>.
    /// </summary>
    public async Task<bool> ReasonIsUsableAsync(Guid? reasonId, CancellationToken ct = default)
    {
        if (reasonId is null) return true;
        return await db.StudentArchiveReasons.AsNoTracking()
            .AnyAsync(r => r.Id == reasonId && r.IsActive, ct);
    }

    /// <summary>
    /// Bitta o'quvchini arxivga ko'chiradi: bayroq, sana va sabab (matn + katalog).
    /// <b>SaveChanges CHAQIRILMAYDI</b> — ommaviy amal hammasini bitta tranzaksiyada
    /// yozishi uchun.
    /// </summary>
    /// <returns>
    /// O'quvchining tizim akkaunti (bo'lsa) — chaqiruvchi unga <c>BlockLogin()</c> qo'llaydi.
    /// Nega shu yerda emas: <c>BlockLogin</c> — <c>SchoolLms.Infrastructure.Auth</c> dagi
    /// kengaytma, Application qatlami esa Infrastructure'ga bog'lanmaydi (bog'liqlik yo'nalishi).
    /// </returns>
    public async Task<AppUser?> ApplyAsync(
        Student student, string reasonText, Guid? reasonId, CancellationToken ct = default)
    {
        student.IsArchived = true;
        student.ArchivedAt = AppClock.Today.ToString("yyyy-MM-dd");
        student.ArchiveReason = reasonText;
        student.ArchiveReasonId = reasonId;
        // Alohida (sinfsiz) arxivlash — sinf arxivdan chiqarilganda qaytmaydi.
        student.ArchivedWithClass = false;

        if (student.UserId is null) return null;
        return await db.Users.FirstOrDefaultAsync(u => u.Id == student.UserId, ct);
    }
}

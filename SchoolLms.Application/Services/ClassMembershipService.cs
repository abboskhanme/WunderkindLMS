using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Sinf a'zoligi — docs/modules/students-parity.md §2.2, C-1.
//
//  BITTA YOZUVCHI QOIDASI
//  ----------------------
//  Bugun o'quvchini sinfga bog'laydigan YAGONA narsa — `students.class_name`
//  (NOM). Butun tizimda ~14 ta server so'rovi va ikkita brauzer filtri
//  o'quvchini shu ustun orqali topadi (§2.1.2). `class_memberships` esa
//  migratsiya bilan kelgan YANGI, SANALI yozuv — u hali HECH NARSAni
//  boshqarmaydi.
//
//  Shuning uchun bu xizmatning butun ma'nosi bitta jumlada: **a'zolik yozuvi
//  o'zgargan har safar `class_name` ham AYNAN SHU tranzaksiyada o'zgaradi.**
//  Ikkalasi bir-biridan ajralsa, jurnal ro'yxati bilan sinf ro'yxati boshqa
//  narsa ko'rsata boshlaydi va qaysi biri to'g'riligini hech kim ayta olmaydi.
//
//  NEGA IKKI MARTA SaveChanges
//  ---------------------------
//  `ux_class_memberships_one_active` — QISMAN UNIKAL indeks
//  (`where left_on is null`) va u DEFERRABLE emas. Ya'ni "eski a'zolikni yop"
//  va "yangisini och" AYNAN shu tartibda bazaga tushishi shart. EF Core bitta
//  SaveChanges ichida UPDATE va INSERT tartibini kafolatlamaydi, shuning uchun
//  o'tkazish ikki qadam: avval yopish saqlanadi, keyin ochish. Ikkalasi
//  chaqiruvchi ochgan TRANZAKSIYA ichida yuradi — yarim o'tkazish bo'lmaydi.
//
//  SANA — MAKTAB MINTAQASIDA
//  -------------------------
//  `joined_on` / `left_on` — kun, lahza emas. <see cref="AppClock.Today"/>
//  (UTC+5) ishlatiladi: yarim tundan keyin serverda UTC kuni hali kechagi
//  bo'lardi va bola "kecha" qo'shilgan bo'lib qolardi.
// ===========================================================================

/// <summary>
/// Sinf a'zoligini va <c>students.class_name</c> ni BIRGA yurituvchi xizmat.
/// <b>SaveChanges chaqiruvlari xizmat ichida</b> — chaqiruvchi tranzaksiyani
/// o'zi ochadi va commit qiladi (SPEC §4.4 dagi moliya xizmatlaridan farqli
/// o'laroq bu yerda pul yo'q, lekin tartib muhim).
/// </summary>
public sealed class ClassMembershipService(IAppDbContext db)
{
    /// <summary>Baza kafolati buzilganda chiqadigan indeks nomi (StudyGroupModel.cs).</summary>
    public const string OneActiveIndex = "ux_class_memberships_one_active";

    public const string AlreadyInClassMessage =
        "Bu o'quvchi allaqachon boshqa sinfda. Avval uni sinfdan chiqaring yoki "
        + "\"Boshqa sinfga o'tkazish\" amalidan foydalaning.";

    public const string ReasonRequiredMessage = "Sinfdan chiqarish sababini yozing";

    public const string SameClassMessage = "O'quvchi allaqachon shu sinfda";

    public const string DifferentGradeMessage =
        "O'quvchini faqat SHU DARAJADAGI boshqa sinfga o'tkazish mumkin "
        + "(masalan 5-A dan 5-B ga). Boshqa darajaga o'tkazish — o'quv yilini yakunlash amali.";

    public const string ArchivedClassMessage = "Arxivlangan sinfga o'quvchi qo'shib bo'lmaydi";

    public const string ArchivedStudentMessage =
        "Arxivlangan o'quvchini sinfga qo'shib bo'lmaydi — avval uni arxivdan chiqaring.";

    /* -------------------------------------------------------------------
     *  O'qish
     * ---------------------------------------------------------------- */

    /// <summary>O'quvchining FAOL sinf a'zoligi (yo'q bo'lishi mumkin).</summary>
    public Task<ClassMembership?> ActiveAsync(string studentId, CancellationToken ct = default) =>
        db.ClassMemberships.FirstOrDefaultAsync(m => m.StudentId == studentId && m.LeftOn == null, ct);

    /* -------------------------------------------------------------------
     *  Qo'shish
     * ---------------------------------------------------------------- */

    /// <summary>
    /// O'quvchini sinfga qo'shadi: a'zolik yoziladi va <c>class_name</c> shu
    /// yerda yangilanadi. Faol a'zoligi bor o'quvchi RAD ETILADI — sinfni
    /// almashtirish <see cref="TransferAsync"/> ning ishi (§2.2.1: "faqat
    /// sinfsiz o'quvchilar" ro'yxati).
    /// </summary>
    /// <returns>Xato matni (o'zbekcha) yoki muvaffaqiyatda <c>null</c>.</returns>
    public async Task<string?> AddAsync(
        Student student, SchoolClass cls, string? userId, CancellationToken ct = default)
    {
        if (student.IsArchived) return ArchivedStudentMessage;
        if (cls.IsArchived) return ArchivedClassMessage;

        var active = await ActiveAsync(student.Id, ct);
        if (active is not null)
            return active.ClassId == cls.Id ? SameClassMessage : AlreadyInClassMessage;

        // A'zolik yozuvi yo'q, lekin `class_name` to'la bo'lishi mumkin (backfill
        // faqat NOMI sinfga MOS tushgan o'quvchilarni qamragan, §3.1). Bunday
        // holatda ham yangi sinfga o'tkazish — o'tkazish amali, qo'shish emas.
        if (!string.IsNullOrWhiteSpace(student.ClassName)
            && !string.Equals(student.ClassName, cls.Name, StringComparison.Ordinal))
            return AlreadyInClassMessage;

        Open(student, cls, userId);
        await db.SaveChangesAsync(ct);
        return null;
    }

    /* -------------------------------------------------------------------
     *  Chiqarish
     * ---------------------------------------------------------------- */

    /// <summary>
    /// O'quvchini sinfdan chiqaradi: a'zolik sana va sabab bilan YOPILADI
    /// (o'chirilmaydi — tarix qoladi), <c>class_name</c> bo'shaydi.
    /// </summary>
    public async Task<string?> RemoveAsync(
        Student student, ClassMembership membership, string reason, CancellationToken ct = default)
    {
        var text = (reason ?? "").Trim();
        if (text.Length == 0) return ReasonRequiredMessage;

        Close(membership, text);
        student.ClassName = string.Empty;
        await db.SaveChangesAsync(ct);
        return null;
    }

    /* -------------------------------------------------------------------
     *  O'tkazish
     * ---------------------------------------------------------------- */

    /// <summary>
    /// O'quvchini AYNI DARAJADAGI boshqa sinfga o'tkazadi.
    ///
    /// <para>
    /// <paramref name="keepGroups"/> false bo'lsa, YANGI sinf boqmaydigan
    /// guruhlardagi faol a'zoliklar yopiladi. true (sukut) bo'lsa guruhlarga
    /// tegilmaydi — EduSchool'dagi <c>shouldStayInGroups</c> ning sukut qiymati.
    /// </para>
    /// <para>
    /// Chaqiruvchi TRANZAKSIYA ochgan bo'lishi shart: bu metod ikki marta
    /// saqlaydi (yuqoridagi izoh).
    /// </para>
    /// </summary>
    public async Task<string?> TransferAsync(
        Student student,
        ClassMembership? membership,
        SchoolClass from,
        SchoolClass to,
        bool keepGroups,
        string? reason,
        string? userId,
        CancellationToken ct = default)
    {
        if (to.IsArchived) return ArchivedClassMessage;
        if (from.Id == to.Id) return SameClassMessage;
        if (from.Grade != to.Grade) return DifferentGradeMessage;

        var text = string.IsNullOrWhiteSpace(reason)
            ? $"{from.Name} → {to.Name} sinfiga o'tkazildi"
            : reason.Trim();

        // 1-qadam: eskisini yopamiz va SAQLAYMIZ — qisman unikal indeks
        // ikkinchi faol qatorni ko'rishga ulgurmasligi kerak.
        if (membership is not null) Close(membership, text);
        if (!keepGroups) await CloseGroupsNotFedByAsync(student.Id, to.Id, text, ct);
        await db.SaveChangesAsync(ct);

        // 2-qadam: yangisini ochamiz. `class_name` ham shu yerda o'zgaradi.
        Open(student, to, userId);
        await db.SaveChangesAsync(ct);
        return null;
    }

    /* -------------------------------------------------------------------
     *  O'quvchi formasidan kelgan sinf o'zgarishi
     * ---------------------------------------------------------------- */

    /// <summary>
    /// O'quvchi formasida sinf almashtirilganda a'zolikni <c>class_name</c> ga
    /// MOSLAYDI (C-1: "StudentsController.Update sinf o'zgarishi shu xizmatdan
    /// o'tadi").
    ///
    /// <para>
    /// Bu yerda daraja tekshirilMAYDI va guruhlar YOPILMAYDI: forma bugun ham
    /// istalgan sinfni qo'ya oladi va bu slice hech bir mavjud oqimni
    /// o'zgartirmasligi kerak. Yagona yangilik — o'zgarish endi a'zolik
    /// yozuvida ham qoladi.
    /// </para>
    /// <para>
    /// Chaqiruvchi <paramref name="newClassName"/> ni o'quvchi qatoriga
    /// ALLAQACHON qo'ygan bo'ladi; bu metod faqat a'zolik qatorlarini yuritadi.
    /// Tranzaksiyani chaqiruvchi ochadi.
    /// </para>
    /// </summary>
    public async Task SyncFromClassNameAsync(
        Student student, string? newClassName, string? userId, CancellationToken ct = default)
    {
        var name = (newClassName ?? "").Trim();
        var active = await ActiveAsync(student.Id, ct);

        var target = name.Length == 0
            ? null
            : await db.Classes.FirstOrDefaultAsync(c => c.Name == name, ct);

        // Sinf nomi katalogda topilmadi (erkin matn yozilgan yoki bo'shatilgan):
        // faol a'zolikni yopamiz, yangisini ochmaymiz — yo'q sinfga havola
        // yozishdan ko'ra yozuvsiz qolgan ma'qul.
        if (target is null)
        {
            if (active is null) return;
            Close(active, name.Length == 0
                ? "O'quvchi kartochkasida sinf bo'shatildi"
                : $"O'quvchi kartochkasida sinf \"{name}\" ga o'zgartirildi (katalogda topilmadi)");
            await db.SaveChangesAsync(ct);
            return;
        }

        if (active is not null && active.ClassId == target.Id) return;

        if (active is not null)
        {
            Close(active, $"O'quvchi kartochkasida sinf o'zgartirildi → {target.Name}");
            await db.SaveChangesAsync(ct);
        }

        Open(student, target, userId);
        await db.SaveChangesAsync(ct);
    }

    /* -------------------------------------------------------------------
     *  Yordamchilar
     * ---------------------------------------------------------------- */

    /// <summary>
    /// A'zolik ochadi va <c>class_name</c> ni BIRGA qo'yadi — ikkalasi shu
    /// yagona joyda yoziladi (xizmatning butun maqsadi).
    /// </summary>
    private void Open(Student student, SchoolClass cls, string? userId)
    {
        db.ClassMemberships.Add(new ClassMembership
        {
            StudentId = student.Id,
            ClassId = cls.Id,
            JoinedOn = AppClock.Today,
            CreatedBy = userId,
        });
        student.ClassName = cls.Name;
    }

    private static void Close(ClassMembership membership, string reason)
    {
        // `check (left_on is null or left_on >= joined_on)` — bir kunda qo'shib
        // chiqarilgan o'quvchida bugungi sana joined_on ga TENG bo'ladi, bu
        // check'ni buzmaydi. Kelajakdagi joined_on bo'lishi mumkin emas.
        membership.LeftOn = AppClock.Today < membership.JoinedOn ? membership.JoinedOn : AppClock.Today;
        membership.LeaveReason = reason;
    }

    /// <summary>
    /// "Guruhlarda qolmasin" tanlanganda: YANGI sinf boqmaydigan guruhlardagi
    /// faol a'zoliklarni yopadi. Yangi sinf ham boqadigan guruh saqlanadi —
    /// bola o'sha guruhda qolishga haqli.
    /// </summary>
    private async Task CloseGroupsNotFedByAsync(
        string studentId, string toClassId, string reason, CancellationToken ct)
    {
        var active = await db.StudyGroupMembers
            .Where(m => m.StudentId == studentId && m.LeftOn == null)
            .ToListAsync(ct);
        if (active.Count == 0) return;

        var groupIds = active.Select(m => m.GroupId).ToList();
        var fedByTarget = await db.StudyGroupClasses
            .Where(c => groupIds.Contains(c.GroupId) && c.ClassId == toClassId)
            .Select(c => c.GroupId)
            .ToListAsync(ct);

        foreach (var member in active.Where(m => !fedByTarget.Contains(m.GroupId)))
        {
            member.LeftOn = AppClock.Today < member.JoinedOn ? member.JoinedOn : AppClock.Today;
            member.LeaveReason = reason;
        }
    }

    /// <summary>
    /// Baza qisman unikal indeksi 23505 bilan yiqilganini ANIQLAYDI.
    /// Application qatlami Npgsql tipini bilmaydi (SPEC §2.2), shuning uchun
    /// tekshiruv <c>DbException.SqlState</c> va indeks NOMI bo'yicha.
    /// </summary>
    public static bool IsOneActiveViolation(DbUpdateException ex)
    {
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is DbException { SqlState: "23505" }
                && inner.Message.Contains(OneActiveIndex, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}

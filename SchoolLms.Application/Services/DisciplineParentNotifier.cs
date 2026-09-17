using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  §6.3, 4-qadam — intizomiy ball qo'yilganda OTA-ONAGA xabar.
//
//  KANAL: TELEGRAM, VA FAQAT TELEGRAM
//  ----------------------------------
//  CLAUDE.md: bu mahsulotda SMS ham, mobil push ham yo'q. Shuning uchun bu
//  yerda "kanal" abstraksiyasi YO'Q — bitta implementatsiyali interfeys
//  ortiqcha qavat bo'lardi. Mavjud yuboruvchi qayta ishlatiladi:
//  `TelegramService.SendMessageAsync`, chat id'lari esa
//  `TelegramRegistrations` dan — `MessagesController.SendBroadcast`,
//  `ReceiptService` va `ContractsController` bilan AYNAN bir xil yo'l.
//
//  QACHON YUBORILADI — TO'RTTA SHART, HAMMASI BIR VAQTDA
//  ----------------------------------------------------
//   1. ball QO'LDA qo'yilgan (`POST /api/admin/discipline/points`);
//   2. sababda `notify_parent = true` (sukut bo'yicha har bir sababda FALSE);
//   3. bot tokeni sozlangan (`TelegramService.IsConfigured`);
//   4. shu o'quvchi uchun ota-ona ro'yxatdan o'tgan (`TelegramRegistrations`).
//
//  Shartlardan biri bajarilmasa — hech narsa yuborilmaydi va hech narsa
//  yiqilmaydi: ball baribir yoziladi. Xabar yuborilmagani ball qo'yilmaganini
//  ANGLATMASLIGI kerak.
//
//  ESKI YOZUVLAR UCHUN HECH NARSA YUBORILMAYDI
//  -------------------------------------------
//  Backfill yo'q, "o'tgan oydagi ballar haqida xabar berish" yo'q. Xabar faqat
//  YANGI yozuv yaratilganda, o'sha so'rov ichida ketadi.
//
//  TESTDA JIM
//  ----------
//  `ApiFactory` bot tokenini bo'sh qoldiradi, ya'ni `IsConfigured == false` va
//  3-shart bajarilmaydi — HTTP chaqiruv umuman bo'lmaydi. Yuborish mantig'ini
//  tekshiradigan test `TelegramService` ning O'Z nusxasini (soxta
//  `IHttpClientFactory` bilan) yasaydi — `ReceiptTests` dagi naqsh.
// ===========================================================================

/// <summary>
/// Intizomiy ball haqida ota-onaga Telegram xabari. Yuborilgan chatlar sonini
/// qaytaradi (0 = yuborilmadi).
/// </summary>
public sealed class DisciplineParentNotifier(
    IAppDbContext db, TelegramService telegram, ILogger<DisciplineParentNotifier> logger)
{
    /// <summary>
    /// Sabab bayrog'i yoqilgan bo'lsa — o'quvchining ota-onasiga xabar yuboradi.
    /// Hech qachon otilmaydi (throw qilmaydi): <see cref="TelegramService"/> ning o'zi
    /// barcha xatolarni yutadi, bu yerda esa faqat qaror qabul qilinadi.
    /// </summary>
    public async Task<int> NotifyAsync(
        Student student, DisciplineReason reason, DisciplinePoint point, CancellationToken ct = default)
    {
        // 2-shart. Eng muhimi va eng birinchisi: bayroqsiz sabab — jim sabab.
        if (!reason.NotifyParent) return 0;

        // 3-shart. Token yo'q bo'lsa `SendMessageAsync` ham false qaytarardi, lekin
        // bu yerda to'xtash arzonroq: chat id'larni umuman qidirmaymiz.
        if (!telegram.IsConfigured)
        {
            logger.LogInformation(
                "Intizomiy ball xabari yuborilmadi (bot sozlanmagan): student={StudentId}", student.Id);
            return 0;
        }

        // 4-shart. Ota-ona botda ro'yxatdan o'tgan bo'lsagina chat id bo'ladi.
        var chatIds = await db.TelegramRegistrations.AsNoTracking()
            .Where(r => r.StudentId == student.Id)
            .Select(r => r.ChatId)
            .Distinct()
            .ToListAsync(ct);
        if (chatIds.Count == 0)
        {
            logger.LogInformation(
                "Intizomiy ball xabari yuborilmadi (ota-ona botga ulanmagan): student={StudentId}", student.Id);
            return 0;
        }

        var text = BuildMessage(student, point);
        var sent = 0;
        foreach (var chatId in chatIds)
            if (await telegram.SendMessageAsync(chatId, text, ct: ct)) sent++;

        logger.LogInformation(
            "Intizomiy ball xabari: student={StudentId}, reason={ReasonId}, chats={Total}, sent={Sent}",
            student.Id, reason.Id, chatIds.Count, sent);
        return sent;
    }

    /// <summary>
    /// Ota-ona O'QIYDIGAN matn. Ataylab quruq va qisqa: kim, nima uchun, necha ball,
    /// qachon. Bu xabar telefonda bildirishnoma bo'lib chiqadi — uzun matn o'qilmaydi.
    /// </summary>
    public static string BuildMessage(Student student, DisciplinePoint point)
    {
        var header = point.Points >= 0 ? "Rag'bat bali" : "Intizomiy ball";
        var points = point.Points > 0
            ? $"+{point.Points}"
            // Manfiy ishorasi — HAQIQIY minus belgisi (U+2212) emas, oddiy defis:
            // ba'zi Telegram mijozlari uni noto'g'ri ko'rsatadi.
            : point.Points.ToString(CultureInfo.InvariantCulture);

        var lines = new List<string>
        {
            header,
            "",
            $"O'quvchi: {student.FullName}"
                + (string.IsNullOrWhiteSpace(student.ClassName) ? "" : $" ({student.ClassName})"),
            $"Sabab: {point.ReasonName}",
            $"Ball: {points}",
        };

        if (!string.IsNullOrWhiteSpace(point.Note)) lines.Add($"Izoh: {point.Note}");
        lines.Add($"Vaqt: {FormatTime(point.CreatedAt)}");

        return string.Join("\n", lines);
    }

    /// <summary>
    /// ISO vaqtni ota-ona o'qiydigan ko'rinishga o'giradi ("16.09.2026 14:20"). Yozuvdagi
    /// SOAT o'zgarmaydi: <c>CreatedAt</c> allaqachon maktab vaqtida (<c>AppClock.Now</c>),
    /// uni server (konteyner — UTC) vaqtiga "to'g'rilash" soatni surib yuborardi.
    /// </summary>
    private static string FormatTime(string iso) =>
        DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
            ? at.DateTime.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)
            : iso;
}

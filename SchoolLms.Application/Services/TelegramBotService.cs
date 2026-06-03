using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;
using System.Text.Json;

namespace SchoolLms.Application.Services;

/// <summary>
/// Telegram botni long polling (getUpdates) orqali yurituvchi fon xizmati. Ota-ona botga
/// telefon raqamini ulashganda, raqam o'quvchining ParentPhone'i bilan (oxirgi 9 raqam bo'yicha)
/// solishtirilib, TelegramRegistration yoziladi — keyin shu sinf e'lonlari shu chatga boradi.
/// Token sozlanmagan bo'lsa xizmat darrov to'xtaydi (ilova baribir ishlaydi).
/// </summary>
public class TelegramBotService(
    IServiceProvider sp, TelegramService telegram, ILogger<TelegramBotService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        long offset = 0;
        var announced = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            // Token sozlanmagan bo'lsa kutamiz — admin keyin sozlamadan qo'shsa, bot o'zi ishga tushadi.
            if (!telegram.IsConfigured)
            {
                announced = false;
                if (!await DelayAsync(10000, stoppingToken)) break;
                continue;
            }
            if (!announced)
            {
                logger.LogInformation("Telegram bot ishga tushdi (long polling).");
                announced = true;
            }

            try
            {
                var updates = await telegram.GetUpdatesAsync(offset, 30, stoppingToken);
                if (updates is null)
                {
                    if (!await DelayAsync(2000, stoppingToken)) break;
                    continue;
                }
                foreach (var upd in updates.Value.EnumerateArray())
                {
                    if (upd.TryGetProperty("update_id", out var idEl))
                        offset = idEl.GetInt64() + 1;
                    await HandleUpdateAsync(upd, stoppingToken);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Telegram getUpdates xatosi");
                if (!await DelayAsync(3000, stoppingToken)) break;
            }
        }
    }

    /// <summary>Kutish; to'xtatilsa false qaytaradi (sikldan chiqish uchun).</summary>
    private static async Task<bool> DelayAsync(int ms, CancellationToken ct)
    {
        try { await Task.Delay(ms, ct); return true; }
        catch (OperationCanceledException) { return false; }
    }

    private async Task HandleUpdateAsync(JsonElement upd, CancellationToken ct)
    {
        if (!upd.TryGetProperty("message", out var msg)) return;
        if (!msg.TryGetProperty("chat", out var chat) || !chat.TryGetProperty("id", out var chatIdEl)) return;
        var chatId = chatIdEl.GetInt64();

        // Kontakt ulashildi — ro'yxatdan o'tkazamiz.
        if (msg.TryGetProperty("contact", out var contact))
        {
            var phone = contact.TryGetProperty("phone_number", out var p) ? p.GetString() : null;
            await RegisterByPhoneAsync(chatId, phone, SenderName(msg), ct);
            return;
        }

        var text = msg.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        if (text.StartsWith("/start"))
        {
            await telegram.SendMessageAsync(chatId,
                "Assalomu alaykum! Maktab e'lonlari va shartnomalarini olish uchun telefon raqamingizni " +
                "ulashing. Raqamingiz ota-ona yoki xodim ma'lumotidagi raqam bilan solishtiriladi.",
                ContactKeyboard, ct);
        }
        else
        {
            await telegram.SendMessageAsync(chatId,
                "Iltimos, pastdagi tugma orqali telefon raqamingizni ulashing.", ContactKeyboard, ct);
        }
    }

    private async Task RegisterByPhoneAsync(long chatId, string? phone, string senderName, CancellationToken ct)
    {
        var key = PhoneUtil.Key(phone);
        if (key.Length < 7)
        {
            await telegram.SendMessageAsync(chatId, "Telefon raqami noto'g'ri. Qaytadan urinib ko'ring.", ct: ct);
            return;
        }

        var digits = PhoneUtil.DigitsOnly(phone);
        var linked = new List<string>();

        // Bot fon xizmatida so'rov (tenant) konteksti yo'q — global query filter aks holda hamma
        // o'quvchini yashiradi. Shuning uchun HAR aktiv maktab DB'si bo'ylab to'g'ri tenant kontekstida
        // qidiramiz; topilgan joyda ro'yxat yozuvi o'sha maktabning TenantId'si bilan saqlanadi.
        using var scope = sp.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<ITenantDbRunner>();
        await runner.ForEachActiveTenantAsync(async db =>
        {
            var matchedStudents = (await db.Students.ToListAsync(ct))
                .Where(s => PhoneUtil.Key(s.ParentPhone) == key).ToList();
            var matchedTeachers = (await db.Teachers.Where(t => !t.IsArchived).ToListAsync(ct))
                .Where(t => PhoneUtil.Key(t.Phone) == key).ToList();

            if (matchedStudents.Count == 0 && matchedTeachers.Count == 0) return;

            foreach (var s in matchedStudents)
            {
                var exists = await db.TelegramRegistrations.AnyAsync(
                    r => r.StudentId == s.Id && r.ChatId == chatId, ct);
                if (!exists)
                    db.TelegramRegistrations.Add(new TelegramRegistration
                    {
                        StudentId = s.Id,
                        ChatId = chatId,
                        ParentName = senderName,
                        Phone = digits,
                        CreatedAt = AppClock.Now,
                    });
                linked.Add($"{s.FullName} ({s.ClassName})");
            }

            foreach (var t in matchedTeachers)
            {
                var exists = await db.TelegramRegistrations.AnyAsync(
                    r => r.TeacherId == t.Id && r.ChatId == chatId, ct);
                if (!exists)
                    db.TelegramRegistrations.Add(new TelegramRegistration
                    {
                        TeacherId = t.Id,
                        StudentId = "",
                        ChatId = chatId,
                        ParentName = senderName,
                        Phone = digits,
                        CreatedAt = AppClock.Now,
                    });
                linked.Add($"{t.FullName} (xodim)");
            }

            await db.SaveChangesAsync(ct);
        }, ct);

        if (linked.Count == 0)
        {
            await telegram.SendMessageAsync(chatId,
                $"Bu raqam ({phone}) hech bir ota-ona yoki xodim raqami bilan mos kelmadi. " +
                "Iltimos, maktab ma'muriyatiga murojaat qiling.", ct: ct);
            return;
        }

        await telegram.SendMessageAsync(chatId,
            "✅ Ro'yxatdan o'tdingiz. Endi quyidagilar bo'yicha e'lon va shartnomalarni olasiz:\n• " +
            string.Join("\n• ", linked), ct: ct);
    }

    private static string SenderName(JsonElement msg)
    {
        if (!msg.TryGetProperty("from", out var from)) return "";
        var fn = from.TryGetProperty("first_name", out var f) ? f.GetString() : null;
        var ln = from.TryGetProperty("last_name", out var l) ? l.GetString() : null;
        return string.Join(" ", new[] { fn, ln }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    /// <summary>Telefon raqamini so'rovchi klaviatura (request_contact tugmasi).</summary>
    private static object ContactKeyboard => new
    {
        keyboard = new[] { new[] { new { text = "📱 Telefon raqamni ulashish", request_contact = true } } },
        resize_keyboard = true,
        one_time_keyboard = true,
    };
}

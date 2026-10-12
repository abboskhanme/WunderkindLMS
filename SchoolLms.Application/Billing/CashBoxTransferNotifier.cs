using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Hr;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  A transfer that needs acceptance reaches the receiver on Telegram, and the answer reaches the
//  sender (client, 2026-10-12: the accountant accepts what the cashier sends, or returns it).
//
//  Telegram is the only channel (CLAUDE.md). Recipients are the person's linked chats, found the
//  same way as for a fine (PenaltyNotifier.RecipientsAsync). Never throws and never blocks the
//  money: the transfer and the answer are already saved, and the cash-box screen shows them anyway.
// ===========================================================================

/// <summary>Telegram messages about a cash-box transfer that waits for the receiver.</summary>
public sealed class CashBoxTransferNotifier(IAppDbContext db, TelegramService telegram, ILogger<CashBoxTransferNotifier> logger)
{
    /// <summary>A transfer was written and waits: tell the receiving box's responsible person.</summary>
    public Task<int> NotifyPendingAsync(Guid transferId, CancellationToken ct = default) =>
        SendAsync(transferId, toSender: false, ct);

    /// <summary>The receiver accepted or returned it: tell whoever sent it.</summary>
    public Task<int> NotifyDecidedAsync(Guid transferId, CancellationToken ct = default) =>
        SendAsync(transferId, toSender: true, ct);

    private async Task<int> SendAsync(Guid transferId, bool toSender, CancellationToken ct)
    {
        if (!telegram.IsConfigured) return 0;
        try
        {
            var t = await db.CashBoxTransactions.AsNoTracking()
                .Where(x => x.Id == transferId && x.NeedsAcceptance && x.TransferToBoxId != null)
                .Select(x => new { x.CashBoxId, ToBoxId = x.TransferToBoxId!.Value, x.Amount, x.Method, x.Note, x.CreatedBy })
                .FirstOrDefaultAsync(ct);
            if (t is null) return 0;

            var boxes = await db.CashBoxes.AsNoTracking()
                .Where(b => b.Id == t.CashBoxId || b.Id == t.ToBoxId)
                .Select(b => new { b.Id, b.Name, b.ResponsibleUserId })
                .ToDictionaryAsync(b => b.Id, ct);
            var from = boxes.GetValueOrDefault(t.CashBoxId)?.Name ?? "—";
            var to = boxes.GetValueOrDefault(t.ToBoxId);
            var decision = await db.CashBoxTransferDecisions.AsNoTracking()
                .FirstOrDefaultAsync(d => d.TransferId == transferId, ct);

            string? recipient;
            string text;
            var money = $"{Money(t.Amount)} so'm ({MethodLabel(t.Method)})";
            if (!toSender)
            {
                if (decision is not null) return 0;
                recipient = to?.ResponsibleUserId;
                text = $"💸 Sizga ko'chirish keldi: {money}\n"
                     + $"{from} → {to?.Name ?? "—"}\n"
                     + (t.Note is null ? "" : $"Izoh: {t.Note}\n")
                     + "\nPulni sanab, LMS → Kassa bo'limida «Qabul qilish» yoki «Qaytarish» ni bosing.";
            }
            else
            {
                if (decision is null) return 0;
                recipient = t.CreatedBy;
                text = decision.Decision == CashBoxTransferDecisionKind.Accepted
                    ? $"✅ Ko'chirish qabul qilindi: {money}\n{from} → {to?.Name ?? "—"}"
                    : $"↩️ Ko'chirish qaytarildi: {money}\n{from} → {to?.Name ?? "—"}\nSabab: {decision.Note}\n\nPul kassangizda qoldi.";
            }
            if (string.IsNullOrEmpty(recipient)) return 0;

            var chats = await PenaltyNotifier.RecipientsAsync(db, EmployeeKind.Staff, recipient, ct);
            var sent = 0;
            foreach (var chat in chats)
                if (await telegram.SendMessageAsync(chat, text, ct: ct)) sent++;
            logger.LogInformation("Kassa ko'chirishi xabari: transfer={Id}, toSender={ToSender}, chats={Total}, sent={Sent}",
                transferId, toSender, chats.Count, sent);
            return sent;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Kassa ko'chirishi xabari yuborilmadi: transfer={Id}", transferId);
            return 0;
        }
    }

    private static string Money(decimal amount) =>
        amount.ToString("#,0.##", CultureInfo.InvariantCulture).Replace(',', ' ');

    private static string MethodLabel(string method) => CashBoxTransactionsExport.MethodLabel(method);
}

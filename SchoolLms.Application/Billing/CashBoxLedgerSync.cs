using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  KASSA KIRIM/CHIQIMI → JURNAL (2026-10-08)
// ===========================================================================
//
//  MUAMMO
//  ------
//  Mijoz P&L ekranida "Yillik xarajat 0 so'm" ko'rdi, garchi EduSchool'dan
//  ko'chirilgan 169 ta chiqim (832 mln) kassada turgan bo'lsa ham. P&L,
//  "Moliya hisobotlari", "Pul oqimi" va "Kassa kuni" FAQAT jurnaldan
//  (`ledger_entries`) o'qiydi, `CashBoxService.PayOutAsync`/`PayInAsync` esa
//  jurnalga hech narsa yozmas edi — faqat `cash_box_transactions` ga.
//  EduSchool'da esa kassadan turi bilan qilingan chiqim P&L'da xarajat bo'ladi.
//  Mijoz: "eduschool bilan moliya bo'limlari funksionalliklari bir xil ishlashi kerak".
//
//  QOIDA
//  -----
//  · TURI BOR Kirim/Chiqim — bitta jurnal partiyasi (`ref_type = cash_box_transaction`,
//    `ref_id` = kassa qatori): chiqim — debet `expense:*` / kredit `cash|bank`,
//    kirim — debet `cash|bank` / kredit `revenue:*`. Hisob turning o'zidan
//    (`transaction_types.ledger_account`, `Accounts.ForTransactionType`).
//  · TURI YO'Q qator jurnalga TUSHMAYDI: EduSchool'dan kelgan bunday chiqimlar —
//    terminal/Click pulini hisob raqamga o'tkazish ("хисоб ракамга тушди",
//    "утказилди", "айрибошлаш"), ya'ni pul joyini o'zgartirgan, sarflanmagan.
//    Ekranda tur MAJBURIY (`CashBoxActionModal`), demak yangi turi yo'q chiqim yo'q.
//  · Ko'chirish (transfer) va ayirboshlash (exchange) — jurnalga tegmaydi (o'zgarmagan).
//  · Bekor qilish — partiya `LedgerService.ReverseCashBoxAsync` bilan teskari qilinadi.
//
//  ESKI QATORLAR — `RunAsync`
//  --------------------------
//  Ilova ishga tushganda (Program.cs, seederlar yonida) bir marta yuradi va
//  IDEMPOTENT: (1) hisobi tanlanmagan turlarga nomidan taxminiy hisob qo'yadi
//  (`Accounts.GuessForTransactionType`; admin tanlagani hech qachon
//  almashtirilmaydi), (2) partiyasi yo'q turi bor Kirim/Chiqim uchun partiya
//  yozadi — sanasi kassa qatorining o'z kuni, muallifi uning muallifi. Bekor
//  qilingan qator o'tkazib yuboriladi: uning sof ta'siri nol.
// ===========================================================================

/// <summary>Typed cash-box Kirim/Chiqim → ledger. File header.</summary>
public static class CashBoxLedgerSync
{
    /// <summary>The balanced batch of one typed Kirim/Chiqim.</summary>
    public static IReadOnlyList<LedgerPosting> PostingsFor(CashBoxTransaction row, TransactionType type)
    {
        var account = Accounts.ForTransactionType(type.Kind, type.LedgerAccount);
        var settlement = Accounts.SettlementFor(row.Method);
        var date = AppClock.LocalDateOf(row.CreatedAt);
        var isOut = row.Kind == CashBoxTransactionKind.PayOut;
        var memo = $"{(isOut ? "Kassadan chiqim" : "Kassaga kirim")} — {type.Name}"
                   + (string.IsNullOrWhiteSpace(row.Note) ? "" : $" — {row.Note.Trim()}");

        return
        [
            new(isOut ? account : settlement, LedgerDirection.Debit, row.Amount,
                LedgerRefType.CashBoxTransaction, row.Id, date, memo),
            new(isOut ? settlement : account, LedgerDirection.Credit, row.Amount,
                LedgerRefType.CashBoxTransaction, row.Id, date, memo),
        ];
    }

    /// <summary>Is this row one that goes to the ledger?</summary>
    public static bool Posts(CashBoxTransaction row) =>
        row.ReversalOf is null && row.TransactionTypeId is not null
        && (row.Kind == CashBoxTransactionKind.PayIn || row.Kind == CashBoxTransactionKind.PayOut);

    /// <summary>
    /// Posts the batch of a just-created row inside the caller's transaction
    /// (<see cref="LedgerService.PostAsync"/> saves the context, the row included).
    /// </summary>
    public static Task PostAsync(
        IAppDbContext db, CashBoxTransaction row, TransactionType type, string actorId, CancellationToken ct) =>
        new LedgerService(db).PostAsync(PostingsFor(row, type), actorId, ct);

    /// <summary>Reverses the row's batch if it has one (a cancelled Kirim/Chiqim).</summary>
    public static async Task ReverseIfPostedAsync(
        IAppDbContext db, Guid rowId, string reason, string actorId, CancellationToken ct)
    {
        var anchor = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.RefType == LedgerRefType.CashBoxTransaction && e.RefId == rowId && e.ReversalOf == null)
            .OrderBy(e => e.Id)
            .Select(e => (long?)e.Id)
            .FirstOrDefaultAsync(ct);
        if (anchor is { } id)
            await new LedgerService(db).ReverseCashBoxAsync(id, reason, actorId, ct);
    }

    /// <summary>
    /// Start-up sync (file header, "ESKI QATORLAR"): fills missing type accounts, then posts
    /// the batch of every typed, not-cancelled Kirim/Chiqim that has none. Safe to run again.
    /// </summary>
    /// <returns>How many types got an account and how many batches were posted.</returns>
    public static async Task<(int TypesFilled, int BatchesPosted)> RunAsync(
        IAppDbContext db, CancellationToken ct = default)
    {
        var unset = await db.TransactionTypes.Where(t => t.LedgerAccount == null).ToListAsync(ct);
        foreach (var t in unset) t.LedgerAccount = Accounts.GuessForTransactionType(t.Kind, t.Name);
        if (unset.Count > 0) await db.SaveChangesAsync(ct);

        var types = await db.TransactionTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, ct);
        var cancelled = db.CashBoxTransactions.Where(c => c.ReversalOf != null).Select(c => c.ReversalOf!.Value);
        var posted = db.LedgerEntries.Where(e => e.RefType == LedgerRefType.CashBoxTransaction && e.RefId != null)
            .Select(e => e.RefId!.Value);
        var rows = await db.CashBoxTransactions.AsNoTracking()
            .Where(r => r.ReversalOf == null && r.TransactionTypeId != null
                        && (r.Kind == CashBoxTransactionKind.PayIn || r.Kind == CashBoxTransactionKind.PayOut)
                        && !cancelled.Contains(r.Id)
                        && !posted.Contains(r.Id))
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);

        var ledger = new LedgerService(db);
        foreach (var row in rows)
            await ledger.PostAsync(PostingsFor(row, types[row.TransactionTypeId!.Value]), row.CreatedBy, ct);

        return (unset.Count, rows.Count);
    }
}

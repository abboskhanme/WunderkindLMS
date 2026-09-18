using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  Tranzaksiya turi katalogi (Kirim/Chiqim) — mijoz yuborgan EduSchool kassa
//  kirim shakli va moliya sozlamalari ekrani, 2026-09-18.
// ===========================================================================
//
//  "SHABLON — CHIQIM EMAS" NAQSHI: batafsil izoh `SchoolLms.Domain/TransactionTypes.cs`
//  boshida. Bu xizmat `cash_box_transactions`ga hech qachon YOZMAYDI — u faqat
//  kataloqni boshqaradi. `CashBoxService.PayInAsync` uni ixtiyoriy ravishda
//  O'QIYDI (tur mavjud/faol/kind='in' ekanini tekshirish uchun).
//
//  MOLIYAVIY EMAS — TO'LIQ CRUD (`ExpenseTemplateService`/`AdjustmentReason` naqshi)
//  --------------------------------------------------------------------------
//  `Update`/`Delete` bor. Faqat bitta qo'shimcha qoida: SEED QILINGAN qatorni
//  o'chirib bo'lmaydi (mijoz skrinshoti — "bitta seed qatorda o'chirish yo'q,
//  faqat tahrirlash"), va TARIXDA ISHLATILGAN turni ham o'chirib bo'lmaydi
//  (FK RESTRICT'ni xom 23503 o'rniga tushunarli 409 ga aylantiradi).
// ===========================================================================

/// <summary>Tur — o'qish uchun.</summary>
public record TransactionTypeDto(
    Guid Id, string Kind, string Name, bool IsActive, bool IsSeeded, int Position);

/// <summary>Yangi tur. <c>Kind</c> YARATISHDA belgilanadi va keyin O'ZGARMAYDI (pastga qarang).</summary>
public record CreateTransactionTypeRequest(
    string Kind, string Name, bool IsActive = true, int Position = 0);

/// <summary>
/// Turni tahrirlash — QISMAN (har bir maydon ixtiyoriy). <c>Kind</c> shu yerda
/// YO'Q ataylab: allaqachon yozilgan kassa tranzaksiyalari (agar bo'lsa) shu
/// turning ESKI kind'iga tayanib yozilgan — uni almashtirish ularning ma'nosini
/// orqaga qarab buzardi. Kind almashtirish kerak bo'lsa — yangi tur yaratiladi.
/// </summary>
public record UpdateTransactionTypeRequest(
    string? Name = null, bool? IsActive = null, int? Position = null);

public interface ITransactionTypeService
{
    /// <summary><paramref name="kind"/> berilsa — shu kind bo'yicha filtrlaydi, aks holda hammasi.</summary>
    Task<IReadOnlyList<TransactionTypeDto>> ListAsync(string? kind, CancellationToken ct = default);

    Task<TransactionTypeDto> CreateAsync(
        CreateTransactionTypeRequest request, string actorId, CancellationToken ct = default);

    Task<TransactionTypeDto> UpdateAsync(
        Guid id, UpdateTransactionTypeRequest request, string actorId, CancellationToken ct = default);

    /// <summary>
    /// Haqiqiy o'chirish. Rad etiladi: (1) tur SEED qilingan
    /// (<see cref="TransactionType.IsSeeded"/>), (2) tur allaqachon bitta yoki
    /// undan ko'p <c>cash_box_transactions</c> qatorida ishlatilgan.
    /// </summary>
    Task DeleteAsync(Guid id, string actorId, CancellationToken ct = default);
}

/// <inheritdoc cref="ITransactionTypeService"/>
public sealed class TransactionTypeService(IAppDbContext db, AuditService audit) : ITransactionTypeService
{
    /// <summary><c>audit_log.entity_type</c> — mahalliy konstanta, `ExpenseTemplateService.AuditEntity` naqshi.</summary>
    private const string AuditEntity = "TransactionType";

    public async Task<IReadOnlyList<TransactionTypeDto>> ListAsync(string? kind, CancellationToken ct = default)
    {
        var q = db.TransactionTypes.AsNoTracking();
        if (kind is not null) q = q.Where(t => t.Kind == RequireKind(kind));

        var rows = await q.OrderBy(t => t.Kind).ThenBy(t => t.Position).ThenBy(t => t.Name).ToListAsync(ct);
        return [.. rows.Select(ToDto)];
    }

    public async Task<TransactionTypeDto> CreateAsync(
        CreateTransactionTypeRequest request, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireActor(actorId);

        var kind = RequireKind(request.Kind);
        var name = RequireName(request.Name);
        await RequireNameFreeAsync(kind, name, null, ct);

        var type = new TransactionType
        {
            Kind = kind,
            Name = name,
            IsActive = request.IsActive,
            IsSeeded = false,
            Position = request.Position,
            CreatedAt = AppClock.NowInstant,
        };

        db.TransactionTypes.Add(type);

        audit.Record(AuditEntity, type.Id.ToString("D"), "create",
            $"Tranzaksiya turi qo'shildi: {name} ({KindLabel(kind)})",
            after: Snapshot(type));

        await db.SaveChangesAsync(ct);

        return ToDto(type);
    }

    public async Task<TransactionTypeDto> UpdateAsync(
        Guid id, UpdateTransactionTypeRequest request, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireActor(actorId);

        var type = await db.TransactionTypes.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw BillingRuleException.NotFound("transaction_type_not_found", "Tranzaksiya turi topilmadi.");

        var before = Snapshot(type);

        if (request.Name is not null)
        {
            var name = RequireName(request.Name);
            await RequireNameFreeAsync(type.Kind, name, type.Id, ct);
            type.Name = name;
        }
        if (request.IsActive is not null) type.IsActive = request.IsActive.Value;
        if (request.Position is not null) type.Position = request.Position.Value;

        audit.Record(AuditEntity, type.Id.ToString("D"), "update",
            $"Tranzaksiya turi o'zgardi: {type.Name} ({KindLabel(type.Kind)})",
            before: before, after: Snapshot(type));

        await db.SaveChangesAsync(ct);

        return ToDto(type);
    }

    public async Task DeleteAsync(Guid id, string actorId, CancellationToken ct = default)
    {
        RequireActor(actorId);

        var type = await db.TransactionTypes.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw BillingRuleException.NotFound("transaction_type_not_found", "Tranzaksiya turi topilmadi.");

        if (type.IsSeeded)
            throw BillingRuleException.Conflict("seeded_type_protected",
                "Bu tur tizim tomonidan yaratilgan — faqat nomini o'zgartirish mumkin, o'chirib bo'lmaydi.");

        // FK RESTRICT (`cash_box_transactions.transaction_type_id`) baribir
        // to'xtatardi — bu oldindan tekshiruv xom 23503 o'rniga tushunarli
        // 409 beradi (`RequireActiveBoxAsync` naqshi, `CashBoxService.cs`).
        if (await db.CashBoxTransactions.AsNoTracking().AnyAsync(t => t.TransactionTypeId == id, ct))
            throw BillingRuleException.Conflict("transaction_type_in_use",
                "Bu tur allaqachon kassa tranzaksiyasida ishlatilgan — o'chirib bo'lmaydi, "
                + "buning o'rniga faolsizlantiring.");

        audit.Record(AuditEntity, type.Id.ToString("D"), "delete",
            $"Tranzaksiya turi o'chirildi: {type.Name} ({KindLabel(type.Kind)})",
            before: Snapshot(type));

        db.TransactionTypes.Remove(type);
        await db.SaveChangesAsync(ct);
    }

    // -----------------------------------------------------------------
    //  Yordamchilar
    // -----------------------------------------------------------------

    private static TransactionTypeDto ToDto(TransactionType t) =>
        new(t.Id, t.Kind, t.Name, t.IsActive, t.IsSeeded, t.Position);

    private static object Snapshot(TransactionType t) => new
    {
        t.Id, t.Kind, t.Name, t.IsActive, t.IsSeeded, t.Position,
    };

    private static string KindLabel(string kind) => kind == TransactionTypeKind.In ? "Kirim" : "Chiqim";

    private static string RequireKind(string? kind) =>
        kind is not null && TransactionTypeKind.All.Contains(kind, StringComparer.Ordinal)
            ? kind
            : throw BillingRuleException.Invalid("invalid_kind",
                $"Noma'lum tur turkumi: '{kind}'. Ruxsat etilganlar: {string.Join(", ", TransactionTypeKind.All)}.");

    private static string RequireName(string? name)
    {
        var clean = (name ?? string.Empty).Trim();
        return clean.Length == 0
            ? throw BillingRuleException.Invalid("name_required", "Tur nomi majburiy.")
            : clean;
    }

    /// <summary>
    /// (kind, name) juftligi bo'sh ekanini tekshiradi — bazadagi unikal indeks
    /// (<c>ix_transaction_types_kind_name</c>) ORQA FRONT sifatida, tushunarli
    /// 400 xabari uchun oldindan.
    /// </summary>
    private async Task RequireNameFreeAsync(string kind, string name, Guid? exceptId, CancellationToken ct)
    {
        var taken = await db.TransactionTypes.AsNoTracking()
            .AnyAsync(t => t.Kind == kind && t.Name == name && (exceptId == null || t.Id != exceptId), ct);
        if (taken)
            throw BillingRuleException.Invalid("duplicate_name",
                $"'{name}' nomli tur shu turkumda allaqachon mavjud.");
    }

    private static void RequireActor(string? actorId)
    {
        if (string.IsNullOrWhiteSpace(actorId))
            throw new ArgumentException(
                "Shaxs noma'lum. U JWT claim'idan olinadi — SPEC §4.4.", nameof(actorId));
    }
}

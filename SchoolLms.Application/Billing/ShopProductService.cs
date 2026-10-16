using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  Mini-magazin catalogue — "Mahsulot nomi, turi va narxini kiritish" (client, 2026-10-15).
//  Moliya sozlamalari → "Mahsulotlar". docs/modules/shop.md.
// ===========================================================================
//
//  A plain catalogue, the `TransactionTypeService` pattern: create, partial update, delete.
//  Two money-shaped rules:
//    * a product is sold under ONE Kirim transaction type (kind `in`, active when chosen) — that type
//      decides the ledger account of every sale (`CashBoxLedgerSync`);
//    * a product that was ever sold cannot be deleted (409 `product_in_use`): its lines point at it.
//      Switch it off instead. A price change affects only later sales — a line keeps its own price.
// ===========================================================================

/// <summary>A catalogue item.</summary>
/// <param name="TransactionTypeName">The Kirim type's name, resolved — the screens show it, not the id.</param>
/// <param name="IsSold">At least one sale line points at it — it can be switched off, not deleted.</param>
public record ShopProductDto(
    Guid Id, string Name, string Kind, decimal Price, Guid TransactionTypeId, string TransactionTypeName,
    bool IsActive, int Position, bool IsSold);

/// <summary>A new catalogue item.</summary>
public record CreateShopProductRequest(
    string Name, string Kind, decimal Price, Guid TransactionTypeId, bool IsActive = true, int Position = 0);

/// <summary>Partial edit — only the fields given change.</summary>
public record UpdateShopProductRequest(
    string? Name = null, string? Kind = null, decimal? Price = null, Guid? TransactionTypeId = null,
    bool? IsActive = null, int? Position = null);

public interface IShopProductService
{
    /// <summary>The catalogue in list order; <paramref name="activeOnly"/> — only what the cash desk offers.</summary>
    Task<IReadOnlyList<ShopProductDto>> ListAsync(
        bool activeOnly, Guid? transactionTypeId, CancellationToken ct = default);

    Task<ShopProductDto> CreateAsync(CreateShopProductRequest request, string actorId, CancellationToken ct = default);

    Task<ShopProductDto> UpdateAsync(
        Guid id, UpdateShopProductRequest request, string actorId, CancellationToken ct = default);

    /// <summary>Deletes a product never sold; a sold one is refused with 409 <c>product_in_use</c>.</summary>
    Task DeleteAsync(Guid id, string actorId, CancellationToken ct = default);
}

/// <inheritdoc cref="IShopProductService"/>
public sealed class ShopProductService(IAppDbContext db, AuditService audit) : IShopProductService
{
    /// <summary><c>audit_log.entity_type</c> of catalogue changes.</summary>
    public const string AuditEntity = "ShopProduct";

    /// <summary>numeric(14,2) holds less than 10^12.</summary>
    private const decimal MaxPrice = 999_999_999_999.99m;

    private const int MaxNameLength = 200;

    public async Task<IReadOnlyList<ShopProductDto>> ListAsync(
        bool activeOnly, Guid? transactionTypeId, CancellationToken ct = default)
    {
        var q = db.ShopProducts.AsNoTracking();
        if (activeOnly) q = q.Where(p => p.IsActive);
        if (transactionTypeId is { } typeId) q = q.Where(p => p.TransactionTypeId == typeId);

        var rows = await q.OrderBy(p => p.Position).ThenBy(p => p.Name).ToListAsync(ct);
        return await ToDtosAsync(rows, ct);
    }

    public async Task<ShopProductDto> CreateAsync(
        CreateShopProductRequest request, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireActor(actorId);

        var name = RequireName(request.Name);
        await RequireNameFreeAsync(name, null, ct);
        var type = await RequireSaleTypeAsync(request.TransactionTypeId, ct);

        var product = new ShopProduct
        {
            Name = name,
            Kind = RequireKind(request.Kind),
            Price = RequirePrice(request.Price),
            TransactionTypeId = type.Id,
            IsActive = request.IsActive,
            Position = request.Position,
            CreatedAt = AppClock.NowInstant,
        };
        db.ShopProducts.Add(product);

        audit.Record(AuditEntity, product.Id.ToString("D"), "create",
            $"Mahsulot qo'shildi: {name} — {ShopSales.SumText(product.Price)} so'm ({type.Name})",
            after: Snapshot(product));

        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([product], ct))[0];
    }

    public async Task<ShopProductDto> UpdateAsync(
        Guid id, UpdateShopProductRequest request, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireActor(actorId);

        var product = await db.ShopProducts.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw BillingRuleException.NotFound("product_not_found", "Mahsulot topilmadi.");

        var before = Snapshot(product);
        var oldPrice = product.Price;

        if (request.Name is not null)
        {
            var name = RequireName(request.Name);
            await RequireNameFreeAsync(name, product.Id, ct);
            product.Name = name;
        }
        if (request.Kind is not null) product.Kind = RequireKind(request.Kind);
        if (request.Price is { } price) product.Price = RequirePrice(price);
        if (request.TransactionTypeId is { } typeId && typeId != product.TransactionTypeId)
            product.TransactionTypeId = (await RequireSaleTypeAsync(typeId, ct)).Id;
        if (request.IsActive is { } active) product.IsActive = active;
        if (request.Position is { } position) product.Position = position;
        product.UpdatedAt = AppClock.NowInstant;

        audit.Record(AuditEntity, product.Id.ToString("D"), "update",
            $"Mahsulot o'zgardi: {product.Name}"
            + (oldPrice == product.Price
                ? ""
                : $" — narx {ShopSales.SumText(oldPrice)} → {ShopSales.SumText(product.Price)} so'm"),
            before: before, after: Snapshot(product));

        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([product], ct))[0];
    }

    public async Task DeleteAsync(Guid id, string actorId, CancellationToken ct = default)
    {
        RequireActor(actorId);

        var product = await db.ShopProducts.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw BillingRuleException.NotFound("product_not_found", "Mahsulot topilmadi.");

        // The RESTRICT key from shop_sale_lines would stop it anyway — this turns a raw 23503 into a sentence.
        if (await db.ShopSaleLines.AsNoTracking().AnyAsync(l => l.ProductId == id, ct))
            throw BillingRuleException.Conflict("product_in_use",
                "Bu mahsulot sotilgan — o'chirib bo'lmaydi, buning o'rniga faolsizlantiring.");

        audit.Record(AuditEntity, product.Id.ToString("D"), "delete",
            $"Mahsulot o'chirildi: {product.Name}", before: Snapshot(product));

        db.ShopProducts.Remove(product);
        await db.SaveChangesAsync(ct);
    }

    // -----------------------------------------------------------------
    //  Helpers
    // -----------------------------------------------------------------

    /// <summary>Type names and "was it sold" for a page of products — two queries, whatever the page size.</summary>
    private async Task<List<ShopProductDto>> ToDtosAsync(IReadOnlyList<ShopProduct> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];

        var typeIds = rows.Select(p => p.TransactionTypeId).Distinct().ToList();
        var typeNames = await db.TransactionTypes.AsNoTracking()
            .Where(t => typeIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Name })
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        var ids = rows.Select(p => p.Id).ToList();
        var sold = (await db.ShopSaleLines.AsNoTracking()
            .Where(l => ids.Contains(l.ProductId))
            .Select(l => l.ProductId)
            .Distinct()
            .ToListAsync(ct)).ToHashSet();

        return [.. rows.Select(p => new ShopProductDto(
            p.Id, p.Name, p.Kind, p.Price, p.TransactionTypeId, typeNames.GetValueOrDefault(p.TransactionTypeId, "—"),
            p.IsActive, p.Position, sold.Contains(p.Id)))];
    }

    /// <summary>The Kirim type a product is sold under: exists, kind <c>in</c>, active.</summary>
    private async Task<TransactionType> RequireSaleTypeAsync(Guid typeId, CancellationToken ct)
    {
        var type = await db.TransactionTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == typeId, ct)
            ?? throw BillingRuleException.NotFound("transaction_type_not_found", "Tranzaksiya turi topilmadi.");
        if (type.Kind != TransactionTypeKind.In)
            throw BillingRuleException.Invalid("transaction_type_kind_mismatch",
                "Mahsulot faqat Kirim turi bilan sotiladi.");
        if (!type.IsActive)
            throw BillingRuleException.Invalid("transaction_type_inactive", "Bu tranzaksiya turi faol emas.");
        return type;
    }

    private async Task RequireNameFreeAsync(string name, Guid? exceptId, CancellationToken ct)
    {
        var taken = await db.ShopProducts.AsNoTracking()
            .AnyAsync(p => p.Name == name && (exceptId == null || p.Id != exceptId), ct);
        if (taken)
            throw BillingRuleException.Invalid("duplicate_name", $"'{name}' nomli mahsulot allaqachon bor.");
    }

    private static string RequireName(string? name)
    {
        var clean = (name ?? string.Empty).Trim();
        if (clean.Length == 0)
            throw BillingRuleException.Invalid("name_required", "Mahsulot nomi majburiy.");
        if (clean.Length > MaxNameLength)
            throw BillingRuleException.Invalid("name_too_long", $"Nomi {MaxNameLength} belgidan oshmasin.");
        return clean;
    }

    private static string RequireKind(string? kind) =>
        kind is not null && ShopProductKind.All.Contains(kind, StringComparer.Ordinal)
            ? kind
            : throw BillingRuleException.Invalid("invalid_kind",
                $"Noma'lum mahsulot turi: '{kind}'. Ruxsat etilganlar: {string.Join(", ", ShopProductKind.All)}.");

    /// <summary>≥ 0, at most 2 decimals (never rounded silently — the <c>CashBoxService.Money</c> rule), fits the column.</summary>
    private static decimal RequirePrice(decimal price)
    {
        if (decimal.Round(price, 2) != price)
            throw BillingRuleException.Invalid("invalid_price", $"Narx tiyin aniqligida (2 kasr) bo'lishi shart: {price}.");
        if (price < 0m || price > MaxPrice)
            throw BillingRuleException.Invalid("invalid_price", "Narx manfiy bo'lmasin va juda katta bo'lmasin.");
        return price;
    }

    private static object Snapshot(ShopProduct p) => new
    {
        p.Id, p.Name, p.Kind, p.Price, p.TransactionTypeId, p.IsActive, p.Position,
    };

    private static void RequireActor(string? actorId)
    {
        if (string.IsNullOrWhiteSpace(actorId))
            throw new ArgumentException("Shaxs noma'lum. U JWT claim'idan olinadi — SPEC §4.4.", nameof(actorId));
    }
}

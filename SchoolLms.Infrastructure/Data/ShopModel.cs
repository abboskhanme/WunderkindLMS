using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>
/// Mini-magazin — EF configuration (<c>Shop</c> migration, <c>docs/modules/shop.md</c>).
///
/// <para>
/// Check constraints live here, not in raw SQL, for the reason <see cref="CashBoxModel"/> gives: a
/// constraint in the model is in the snapshot, so the next <c>--autogenerate</c> does not drop it. The
/// raw SQL (<c>Migrations/Sql/shop_guards.sql</c>) holds only what EF cannot express — the REVOKE that
/// makes <c>shop_sale_lines</c> insert-only for <c>app_rw</c>.
/// </para>
/// </summary>
internal static class ShopModel
{
    public static void Apply(ModelBuilder b)
    {
        ConfigureProducts(b);
        ConfigureSaleLines(b);
    }

    private static void ConfigureProducts(ModelBuilder b)
    {
        b.Entity<ShopProduct>(e =>
        {
            e.ToTable("shop_products", t =>
            {
                t.HasCheckConstraint("ck_shop_products_kind", "kind in ('book','uniform','club','other')");
                t.HasCheckConstraint("ck_shop_products_name", "btrim(name) <> ''");
                t.HasCheckConstraint("ck_shop_products_price", "price >= 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Kind).HasMaxLength(16);
            e.Property(x => x.Price).HasPrecision(14, 2);
            e.Property(x => x.IsActive).HasDefaultValue(true);
            e.Property(x => x.Position).HasDefaultValue(0);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

            // One name, one product: the cashier picks by name and the report groups by it.
            e.HasIndex(x => x.Name).IsUnique();

            // The cash desk's query: "active products of this Kirim type, in order".
            e.HasIndex(x => new { x.TransactionTypeId, x.IsActive, x.Position });

            // RESTRICT: a type with products cannot be deleted (TransactionTypeService refuses a used
            // type anyway once something was sold under it).
            e.HasOne<TransactionType>().WithMany().HasForeignKey(x => x.TransactionTypeId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureSaleLines(ModelBuilder b)
    {
        b.Entity<ShopSaleLine>(e =>
        {
            e.ToTable("shop_sale_lines", t =>
            {
                t.HasCheckConstraint("ck_shop_sale_lines_quantity", "quantity > 0");
                t.HasCheckConstraint("ck_shop_sale_lines_unit_price", "unit_price >= 0");
                t.HasCheckConstraint("ck_shop_sale_lines_line_total", "line_total = unit_price * quantity");
                t.HasCheckConstraint("ck_shop_sale_lines_product_name", "btrim(product_name) <> ''");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.ProductName).HasMaxLength(200);
            e.Property(x => x.UnitPrice).HasPrecision(14, 2);
            e.Property(x => x.LineTotal).HasPrecision(14, 2);

            // Both RESTRICT: a sold line is money history — neither its Kirim nor its product may vanish.
            e.HasOne<CashBoxTransaction>().WithMany().HasForeignKey(x => x.CashBoxTransactionId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ShopProduct>().WithMany().HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            // A product appears once per sale (quantity says how many) — also the "lines of these rows" index.
            e.HasIndex(x => new { x.CashBoxTransactionId, x.ProductId }).IsUnique();
            e.HasIndex(x => x.ProductId);
        });
    }
}

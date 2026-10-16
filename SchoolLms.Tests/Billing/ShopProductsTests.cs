using System.Net;
using System.Net.Http.Json;
using SchoolLms.Application.Billing;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Billing;

// ===========================================================================
//  Mini-magazin catalogue (client, 2026-10-15): "Mahsulot nomi, turi va narxini kiritish."
//  `/api/admin/finance/products` — docs/modules/shop.md. The sale itself: ShopSalesTests.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class ShopProductsTests(ApiFixture fixture)
{
    private const string Products = "/api/admin/finance/products";
    private const string Types = "/api/admin/finance/transaction-types";

    private sealed record ErrorBody(string Code, string Message);

    [Fact]
    public async Task Mahsulot_qoshiladi_narxi_ozgaradi_va_sotilmagani_ochiriladi()
    {
        var owner = await fixture.Api.ClientAsAsync(Roles.SuperAdmin);
        var type = await NewInTypeAsync(owner);
        var name = "Ingliz tili kitobi " + Suffix();

        var created = await owner.PostAsJsonAsync(Products, new
        {
            name, kind = ShopProductKind.Book, price = 85_000m, transactionTypeId = type.Id, position = 2,
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var product = (await created.Content.ReadFromJsonAsync<ShopProductDto>())!;
        Assert.Equal(name, product.Name);
        Assert.Equal(85_000m, product.Price);
        Assert.Equal(type.Name, product.TransactionTypeName);
        Assert.True(product.IsActive);
        Assert.False(product.IsSold);

        var updated = await owner.PutAsJsonAsync($"{Products}/{product.Id}", new { price = 90_000m, isActive = false });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var after = (await updated.Content.ReadFromJsonAsync<ShopProductDto>())!;
        Assert.Equal(90_000m, after.Price);
        Assert.False(after.IsActive);
        Assert.Equal(name, after.Name);                       // untouched fields stay

        // The cash desk's list hides a switched-off product; the full list keeps it.
        Assert.DoesNotContain(await ListAsync(owner, $"?activeOnly=true&transactionTypeId={type.Id}"), p => p.Id == product.Id);
        Assert.Contains(await ListAsync(owner, $"?transactionTypeId={type.Id}"), p => p.Id == product.Id);

        // Every change leaves an audit line with the price move.
        await fixture.Api.WithDbAsync(db =>
        {
            var audit = db.AuditLogs.Where(a => a.EntityType == ShopProductService.AuditEntity
                                                && a.EntityId == product.Id.ToString("D")).ToList();
            Assert.Contains(audit, a => a.Action == "create");
            Assert.Contains(audit, a => a.Action == "update" && a.Summary.Contains("85 000 → 90 000"));
            return Task.CompletedTask;
        });

        var deleted = await owner.DeleteAsync($"{Products}/{product.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.DoesNotContain(await ListAsync(owner, ""), p => p.Id == product.Id);
    }

    [Fact]
    public async Task Notogri_mahsulot_rad_etiladi()
    {
        var owner = await fixture.Api.ClientAsAsync(Roles.SuperAdmin);
        var type = await NewInTypeAsync(owner);
        var outType = await NewTypeAsync(owner, "out", "Chiqim " + Suffix(), "expense:other");
        var name = "Forma " + Suffix();

        async Task<ErrorBody> Refused(object body, HttpStatusCode status = HttpStatusCode.BadRequest)
        {
            var response = await owner.PostAsJsonAsync(Products, body);
            Assert.Equal(status, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<ErrorBody>())!;
        }

        Assert.Equal("invalid_price", (await Refused(new { name, kind = "uniform", price = -1m, transactionTypeId = type.Id })).Code);
        Assert.Equal("invalid_price", (await Refused(new { name, kind = "uniform", price = 1.005m, transactionTypeId = type.Id })).Code);
        Assert.Equal("invalid_kind", (await Refused(new { name, kind = "shoes", price = 1m, transactionTypeId = type.Id })).Code);
        Assert.Equal("name_required", (await Refused(new { name = "  ", kind = "uniform", price = 1m, transactionTypeId = type.Id })).Code);
        Assert.Equal("transaction_type_kind_mismatch",
            (await Refused(new { name, kind = "uniform", price = 1m, transactionTypeId = outType.Id })).Code);
        Assert.Equal("transaction_type_not_found",
            (await Refused(new { name, kind = "uniform", price = 1m, transactionTypeId = Guid.NewGuid() }, HttpStatusCode.NotFound)).Code);

        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync(Products,
            new { name, kind = "uniform", price = 150_000m, transactionTypeId = type.Id })).StatusCode);
        Assert.Equal("duplicate_name", (await Refused(new { name, kind = "uniform", price = 1m, transactionTypeId = type.Id })).Code);
    }

    [Fact]
    public async Task Mahsulotli_turni_ochirib_bolmaydi()
    {
        var owner = await fixture.Api.ClientAsAsync(Roles.SuperAdmin);
        var type = await NewInTypeAsync(owner);
        await NewProductAsync(owner, type.Id, 10_000m);

        var response = await owner.DeleteAsync($"{Types}/{type.Id}");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("transaction_type_has_products", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    // =====================================================================
    //  RBAC — the cash desk reads, only finance management writes
    // =====================================================================

    [Theory]
    [InlineData(Roles.Cashier, null, HttpStatusCode.OK)]
    [InlineData(Roles.Admin, null, HttpStatusCode.OK)]
    [InlineData(Roles.SuperAdmin, null, HttpStatusCode.OK)]
    [InlineData(Roles.Staff, "finance:view", HttpStatusCode.OK)]       // finance viewer — read-only
    [InlineData(Roles.Staff, "finance", HttpStatusCode.OK)]            // finance delegate
    [InlineData(Roles.Staff, "students", HttpStatusCode.Forbidden)]     // staff without finance
    [InlineData(Roles.Teacher, null, HttpStatusCode.Forbidden)]
    public async Task Katalogni_oqish(string role, string? permission, HttpStatusCode expected)
    {
        var client = await ClientAsync(role, permission);
        Assert.Equal(expected, (await client.GetAsync(Products)).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Cashier, null, HttpStatusCode.Forbidden)]        // sells from it, never prices it
    [InlineData(Roles.Staff, "finance:view", HttpStatusCode.Forbidden)]
    [InlineData(Roles.Staff, "students", HttpStatusCode.Forbidden)]
    [InlineData(Roles.Teacher, null, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Admin, null, HttpStatusCode.OK)]
    [InlineData(Roles.Staff, "finance", HttpStatusCode.OK)]
    public async Task Katalogni_ozgartirish(string role, string? permission, HttpStatusCode expected)
    {
        var owner = await fixture.Api.ClientAsAsync(Roles.SuperAdmin);
        var type = await NewInTypeAsync(owner);
        var product = await NewProductAsync(owner, type.Id, 20_000m);
        var client = await ClientAsync(role, permission);

        var create = await client.PostAsJsonAsync(Products,
            new { name = "Do'ppi " + Suffix(), kind = "uniform", price = 30_000m, transactionTypeId = type.Id });
        var update = await client.PutAsJsonAsync($"{Products}/{product.Id}", new { price = 1m });
        var delete = await client.DeleteAsync($"{Products}/{product.Id}");

        Assert.Equal(expected, create.StatusCode);
        Assert.Equal(expected, update.StatusCode);
        Assert.Equal(expected == HttpStatusCode.OK ? HttpStatusCode.NoContent : expected, delete.StatusCode);

        if (expected != HttpStatusCode.OK)
        {
            // Refused means nothing changed.
            var list = await ListAsync(owner, $"?transactionTypeId={type.Id}");
            Assert.Equal(20_000m, Assert.Single(list).Price);
        }
    }

    [Fact]
    public async Task Tokensiz_401()
    {
        var anonymous = fixture.Api.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Products)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(Products, new { name = "x" })).StatusCode);
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    private static string Suffix() => Guid.NewGuid().ToString("N")[..8];

    private async Task<HttpClient> ClientAsync(string role, string? permission) =>
        permission is null
            ? await fixture.Api.ClientAsAsync(role)
            : await fixture.Api.ClientAsAsync(role, permission);

    internal static async Task<TransactionTypeDto> NewInTypeAsync(HttpClient owner, string ledgerAccount = "revenue:sales") =>
        await NewTypeAsync(owner, "in", "Kitob savdosi " + Suffix(), ledgerAccount);

    internal static async Task<TransactionTypeDto> NewTypeAsync(HttpClient owner, string kind, string name, string ledgerAccount)
    {
        var response = await owner.PostAsJsonAsync(Types, new { kind, name, isActive = true, position = 0, ledgerAccount });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TransactionTypeDto>())!;
    }

    internal static async Task<ShopProductDto> NewProductAsync(
        HttpClient owner, Guid typeId, decimal price, string kind = ShopProductKind.Book, string? name = null)
    {
        var response = await owner.PostAsJsonAsync(Products, new
        {
            name = name ?? "Mahsulot " + Suffix(), kind, price, transactionTypeId = typeId,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ShopProductDto>())!;
    }

    private static async Task<List<ShopProductDto>> ListAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync(Products + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<ShopProductDto>>())!;
    }
}

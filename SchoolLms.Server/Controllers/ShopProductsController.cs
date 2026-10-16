using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Services;

namespace SchoolLms.Server.Controllers;

// ===========================================================================
//  Mini-magazin — catalogue and "Savdo hisoboti" (client, 2026-10-15; docs/modules/shop.md).
// ===========================================================================
//
//  ROUTES
//    GET    /api/admin/finance/products?activeOnly=&transactionTypeId=
//    POST   /api/admin/finance/products
//    PUT    /api/admin/finance/products/{id}
//    DELETE /api/admin/finance/products/{id}                 (never-sold products only)
//    GET    /api/admin/finance/products/sales?from=&to=&boxId=&studentId=
//    GET    /api/admin/finance/products/sales/export?…       (.xlsx, same filter)
//
//  The sale itself is NOT here: it is the cash box's own Kirim with `lines`
//  (`POST /api/admin/cash-boxes/{id}/in`, `OperateCashBox`).
//
//  PERMISSIONS — one layer, per method (the `CashBoxesController` pattern, not
//  `TransactionTypesController`'s class-level FinanceStaff gate, because the cashier must get in):
//    * reading the catalogue and the report — `ViewCashBoxes` (the cash desk: cashier, admin, director,
//      finance staff; a finance viewer reads every GET). The cashier sells from the catalogue, and the
//      report shows what the Kassa page's journal already shows them, per product;
//    * changing the catalogue — `ManageShopProducts` (admin, director, finance staff; not the cashier).
//
//  The services are built here with `new`, as the neighbours do (Program.cs untouched).
// ===========================================================================

/// <summary>Catalogue CRUD and the sales report of the mini-magazin. File header.</summary>
[ApiController]
[Authorize]
[Route("api/admin/finance/products")]
[BillingFault]
[Produces("application/json")]
public sealed class ShopProductsController(IAppDbContext db, AuditService audit) : ControllerBase
{
    private readonly IShopProductService products = new ShopProductService(db, audit);

    /// <summary>The catalogue in list order; <paramref name="activeOnly"/> — what the cash desk can sell.</summary>
    [HttpGet]
    [FinanceRole(FinanceAction.ViewCashBoxes)]
    public async Task<ActionResult<IEnumerable<ShopProductDto>>> List(
        [FromQuery] bool activeOnly, [FromQuery] Guid? transactionTypeId, CancellationToken ct) =>
        Ok(await products.ListAsync(activeOnly, transactionTypeId, ct));

    /// <summary>A new product.</summary>
    [HttpPost]
    [FinanceRole(FinanceAction.ManageShopProducts)]
    public async Task<ActionResult<ShopProductDto>> Create(
        [FromBody] CreateShopProductRequest request, CancellationToken ct) =>
        Ok(await products.CreateAsync(request, FinanceActor.RequireUserId(User), ct));

    /// <summary>Partial edit. A new price applies to later sales only.</summary>
    [HttpPut("{id:guid}")]
    [FinanceRole(FinanceAction.ManageShopProducts)]
    public async Task<ActionResult<ShopProductDto>> Update(
        Guid id, [FromBody] UpdateShopProductRequest request, CancellationToken ct) =>
        Ok(await products.UpdateAsync(id, request, FinanceActor.RequireUserId(User), ct));

    /// <summary>Deletes a product that was never sold; a sold one answers 409 <c>product_in_use</c>.</summary>
    [HttpDelete("{id:guid}")]
    [FinanceRole(FinanceAction.ManageShopProducts)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await products.DeleteAsync(id, FinanceActor.RequireUserId(User), ct);
        return NoContent();
    }

    /// <summary>"Savdo hisoboti": totals per product, kind, Kirim type and cashier, plus the sales. Default — today.</summary>
    [HttpGet("sales")]
    [FinanceRole(FinanceAction.ViewCashBoxes)]
    public async Task<ActionResult<ShopSalesReportDto>> Sales(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? boxId,
        [FromQuery] string? studentId, CancellationToken ct) =>
        Ok(await new ShopSalesReport(db).BuildAsync(new ShopSalesQuery(from, to, boxId, studentId), ct));

    /// <summary>The same report as .xlsx — one sheet per table.</summary>
    [HttpGet("sales/export")]
    [FinanceRole(FinanceAction.ViewCashBoxes)]
    public async Task<IActionResult> ExportSales(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? boxId,
        [FromQuery] string? studentId, CancellationToken ct)
    {
        var report = await new ShopSalesReport(db).BuildAsync(new ShopSalesQuery(from, to, boxId, studentId), ct);

        var boxName = boxId is { } box
            ? await db.CashBoxes.AsNoTracking().Where(b => b.Id == box).Select(b => b.Name).FirstOrDefaultAsync(ct)
            : null;
        var studentName = string.IsNullOrWhiteSpace(studentId)
            ? null
            : await db.Students.AsNoTracking().Where(s => s.Id == studentId.Trim()).Select(s => s.FullName)
                .FirstOrDefaultAsync(ct);

        return File(ShopSalesReport.Export(report, boxName, studentName),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ShopSalesReport.FileName(report.From, report.To));
    }
}

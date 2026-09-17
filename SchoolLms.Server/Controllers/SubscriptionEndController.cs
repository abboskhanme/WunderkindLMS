using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Billing;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// F1.06 (docs/modules/finance-parity.md §2.1) — obunani yopishdan OLDINGI
/// ko'rish: yopilsa qaysi KELAJAK oylar qarz bo'lib qolishi mumkinligi.
///
/// <para>
/// <b>Nega alohida controller, <see cref="BillingCatalogController"/> emas.</b>
/// O'sha controller — bu vazifada TEGILMAYDIGAN fayl (sakkizta agent
/// parallel ishlayapti, u boshqasiniki). Uning marshruti allaqachon
/// <c>POST /api/admin/billing/subscriptions/{id}/end</c> ni band qilgan —
/// bu yerdagi <c>…/end/preview</c> ESA UNGA TO'QNASHMAYDI (ASP.NET
/// marshrutlash aniq segment sonini ko'radi). Ikkalasi ham AYNAN bitta
/// <see cref="ISubscriptionService"/> ni chaqiradi, ikkinchi mantiq
/// YOZILMAYDI.
/// </para>
/// <para>
/// <b>Bu — sof o'qish.</b> Hech narsa yozmaydi, hech narsani bekor qilmaydi.
/// Admin oldindan ko'rgach, TANLAGAN oylarni bekor qilish uchun mavjud
/// <c>POST /api/admin/billing/invoices/{id}/void</c> (F10.02,
/// <see cref="InvoicesController"/>) ni HAR BIRI uchun alohida chaqiradi —
/// bu yerda "bulk void" yozilmaydi (qarang
/// <see cref="SubscriptionService.PreviewEndAsync"/> izohi).
/// </para>
/// </summary>
[ApiController]
[Authorize(Roles = Roles.FinanceStaff)]
[Route("api/admin/billing/subscriptions")]
[BillingFault]
public sealed class SubscriptionEndController(ISubscriptionService subscriptions) : ControllerBase
{
    /// <summary>Oldindan ko'rish so'rovi — faqat maqsadli tugash sanasi.</summary>
    public sealed record PreviewEndSubscriptionRequest(DateOnly EndsOn);

    /// <summary>
    /// <c>POST /api/admin/billing/subscriptions/{id}/end/preview</c> —
    /// <paramref name="request"/>.<c>EndsOn</c> oyidan KEYINGI oylarga
    /// allaqachon hisoblangan (hali <c>void</c> qilinmagan) hisob-fakturalar.
    /// </summary>
    [HttpPost("{id:guid}/end/preview")]
    [FinanceRole(FinanceAction.ManageSubscriptions)]
    public async Task<ActionResult<EndSubscriptionPreviewDto>> PreviewEnd(
        Guid id, PreviewEndSubscriptionRequest request, CancellationToken ct) =>
        Ok(await subscriptions.PreviewEndAsync(id, request.EndsOn, ct));
}

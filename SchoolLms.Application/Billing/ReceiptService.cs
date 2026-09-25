using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

/// <summary>
/// Chek: PDF yasash va ota-onaning Telegramiga yuborish. Vazifa: P1-12. SPEC §4.7.
///
/// <para>
/// <b>NEGA BU MUHIM.</b> Chek ota-onaga yuborilgach, to'lov dalilining nusxasi
/// maktab o'zgartira olmaydigan joyda — to'lovchining telefonida — qoladi.
/// Kassir bazadagi yozuvni yo'q qila olmaydi (SPEC §4.1), lekin faraz qilaylik
/// qildi ham — ota-onadagi nusxa qoladi. Shu sabab yuborish "qo'shimcha imkoniyat"
/// emas, nazorat mexanizmining bir qismi.
/// </para>
///
/// <para>
/// <b>PUL QAYTMAYDI.</b> <see cref="SendToGuardianAsync"/> HECH QACHON istisno
/// tashlamaydi va hech narsani orqaga qaytarmaydi. Telegram javob bermasligi
/// yoki ota-onaning ro'yxatdan o'tmagani — bular chek YETKAZILMAGANini bildiradi,
/// pul QABUL QILINMAGANini emas. Bu ikki xil hodisa, va ularni aralashtirish
/// eng qimmat xato bo'lardi: pulni olib, keyin uni "yo'q" deb hisoblash.
/// </para>
///
/// <para>
/// <b>Ma'lumot manbai.</b> To'lov ma'lumoti <see cref="IPaymentService"/> dan
/// olinadi (P1-11) — o'quvchi/kassir ismi, toifalar, taqsimotlar allaqachon
/// o'sha yerda yig'iladi, uni ikkinchi marta yozish ikkita har xil haqiqat
/// manbai degani bo'lardi. Bazaga TO'G'RIDAN-TO'G'RI faqat uchta narsa uchun
/// murojaat qilinadi: maktab nomi (SchoolMeta), storno holati (payments) va
/// ota-onaning Telegram chat id'si (telegram_registrations).
/// </para>
/// </summary>
public sealed class ReceiptService(
    IPaymentService payments,
    IAppDbContext db,
    TelegramService telegram,
    ILogger<ReceiptService> logger,
    // Ixtiyoriy: berilmasa QR chizilmaydi (DI'siz yaratiladigan joylar va testlar buzilmasin).
    IConfiguration? config = null) : IReceiptService
{
    /// <summary>
    /// Chekdagi QR manzili: <c>{baseUrl}/r/{token}</c>. Asos berilmasa (sozlanmagan va so'rov yo'q — masalan
    /// Telegram'ga yuborishda) QR chizilmaydi: noto'g'ri manzilli QR umuman yo'qligidan yomonroq.
    /// </summary>
    public static async Task<string?> VerifyUrlAsync(
        IAppDbContext db, Guid paymentId, string? baseUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return null;
        var token = await db.Payments.AsNoTracking()
            .Where(p => p.Id == paymentId).Select(p => p.ReceiptToken).FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(token) ? null : ReceiptQr.VerifyUrl(baseUrl, token);
    }

    /// <summary>Telegram javob bermasa, qayta urinishdan oldingi pauza (bir marta).</summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    /// <inheritdoc />
    /// <exception cref="KeyNotFoundException">Bunday to'lov yo'q (controller buni 404 ga o'giradi).</exception>
    public async Task<byte[]> RenderPdfAsync(Guid paymentId, CancellationToken ct = default)
    {
        _ = await LoadAsync(paymentId, ct); // yo'q to'lov — KeyNotFoundException (404)
        return await RenderThermalPdfAsync(paymentId, ct);
    }

    /// <summary>
    /// PDF — 58 mm termal chek bilan bir xil (2026-09-25). Ma'lumot termal chek JSON'idan
    /// (<see cref="ReceiptPrintQuery"/>), shuning uchun kassada chop etilgan chek bilan qator-qator mos.
    /// </summary>
    private async Task<byte[]> RenderThermalPdfAsync(Guid paymentId, CancellationToken ct)
    {
        var receipt = await ReceiptPrintQuery.GetAsync(
            paymentId, payments, new InvoiceService(db, new LedgerService(db)), db, ct, config?[ReceiptQr.BaseUrlKey])
            ?? throw new KeyNotFoundException($"To'lov topilmadi: {paymentId}.");
        return new ThermalReceiptPdf(receipt).Render();
    }

    /// <inheritdoc />
    public async Task<bool> SendToGuardianAsync(Guid paymentId, CancellationToken ct = default)
    {
        // BUTUN metod himoyalangan. P1-11 buni to'lov oqimidan chaqiradi;
        // bu yerdan chiqqan istisno chaqiruvchining tranzaksiyasini orqaga
        // qaytarishi mumkin edi, ya'ni "Telegram ishlamadi -> to'lov yo'qoldi".
        // Bunga yo'l qo'yilmaydi: xato JURNALGA yoziladi, natija `false`.
        try
        {
            var (payment, model) = await LoadAsync(paymentId, ct);

            // Ota-ona -> chat: TelegramRegistration.StudentId -> ChatId.
            // Bir o'quvchida bir nechta ro'yxat bo'lishi mumkin (ota va ona) —
            // hammasiga yuboriladi.
            var chatIds = await db.TelegramRegistrations.AsNoTracking()
                .Where(r => r.StudentId == payment.StudentId)
                .Select(r => r.ChatId)
                .Distinct()
                .ToListAsync(ct);

            if (chatIds.Count == 0)
            {
                // KUTILGAN holat, xato emas (P1-12 qabul mezoni): ota-ona hali
                // botga ulanmagan. Kassir chekni qog'ozda beradi.
                logger.LogWarning(
                    "Chek № {ReceiptNo}: o'quvchi {StudentId} bo'yicha Telegram ro'yxati yo'q — yuborilmadi.",
                    payment.ReceiptNo, payment.StudentId);
                return false;
            }

            if (!telegram.IsConfigured)
            {
                // Bot tokeni kiritilmagan — bu ADMIN sozlamasidagi muammo, tarmoq
                // nosozligi emas. Qayta urinish ham, PDF yasash ham bu yerda
                // ma'nosiz: har chat uchun bekorga kutish bo'lardi.
                logger.LogWarning(
                    "Chek № {ReceiptNo} yuborilmadi: Telegram boti sozlanmagan "
                    + "(Sozlamalar -> Telegram). To'lov kuchda.", payment.ReceiptNo);
                return false;
            }

            var pdf = await RenderThermalPdfAsync(paymentId, ct);
            var fileName = ReceiptText.FileName(payment.ReceiptNo);
            var caption = Caption(payment, model);

            var delivered = false;
            foreach (var chatId in chatIds)
                delivered |= await SendWithOneRetryAsync(chatId, pdf, fileName, caption, payment.ReceiptNo, ct);

            return delivered;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Mijoz ulanishni uzdi (kassir sahifani yopdi). Bu nosozlik emas,
            // shuning uchun xato darajasida jurnalga yozilmaydi.
            logger.LogWarning(
                "Chek {PaymentId}: so'rov bekor qilindi, yuborish to'xtatildi. To'lov kuchda.",
                paymentId);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Chek {PaymentId} Telegramga yuborilmadi. TO'LOV KUCHDA QOLADI — "
                + "yetkazilmagan chek to'lovni bekor qilmaydi.", paymentId);
            return false;
        }
    }

    /// <summary>
    /// Bir marta yuboradi, muvaffaqiyatsiz bo'lsa BIR MARTA qayta uradi
    /// (P1-12 qabul mezoni). Ko'proq urinish bu yerda ma'nosiz: chaqiruv
    /// kassir kutib turgan so'rov ichida bo'lishi mumkin.
    /// </summary>
    private async Task<bool> SendWithOneRetryAsync(
        long chatId, byte[] pdf, string fileName, string caption, long receiptNo, CancellationToken ct)
    {
        if (await telegram.SendDocumentAsync(chatId, pdf, fileName, caption, ct))
            return true;

        logger.LogWarning(
            "Chek № {ReceiptNo}: {ChatId} chatiga yuborilmadi, qayta urinilmoqda.", receiptNo, chatId);

        await Task.Delay(RetryDelay, ct);

        if (await telegram.SendDocumentAsync(chatId, pdf, fileName, caption, ct))
        {
            logger.LogInformation(
                "Chek № {ReceiptNo}: {ChatId} chatiga ikkinchi urinishda yuborildi.", receiptNo, chatId);
            return true;
        }

        logger.LogError(
            "Chek № {ReceiptNo}: {ChatId} chatiga IKKI urinishdan keyin ham yuborilmadi. "
            + "To'lov kuchda — chekni qo'lda qayta yuborish kerak.", receiptNo, chatId);
        return false;
    }

    /// <summary>Telegramdagi fayl ostidagi izoh — ota-ona xabarni ochmasdan ham tushunsin.</summary>
    private static string Caption(PaymentDto payment, ReceiptModel model)
    {
        var head = model.CancelledAt is null ? "To'lov cheki" : $"To'lov cheki — {ReceiptText.CancelledStamp}";
        return $"{head}\n"
            + $"№ {payment.ReceiptNo} · {ReceiptText.DateTime(payment.ReceivedAt)}\n"
            + $"{payment.StudentName}\n"
            + $"Jami: {ReceiptText.Money(payment.Amount)}";
    }

    /// <summary>To'lov + chekka kerak bo'lgan qo'shimcha ma'lumot (maktab, storno holati).</summary>
    private async Task<(PaymentDto Payment, ReceiptModel Model)> LoadAsync(Guid paymentId, CancellationToken ct)
    {
        var payment = await payments.GetAsync(paymentId, ct)
            ?? throw new KeyNotFoundException($"To'lov topilmadi: {paymentId}.");

        var school = await db.SchoolMeta.AsNoTracking().FirstOrDefaultAsync(ct);
        if (school is null)
            logger.LogWarning("SchoolMeta qatori yo'q — chekda maktab nomi o'rniga standart matn chiqadi.");

        return (payment, BuildModel(payment, school, await CancelledAtAsync(paymentId, ct),
            await VerifyUrlAsync(db, paymentId, config?[ReceiptQr.BaseUrlKey], ct)));
    }

    /// <summary>
    /// To'lov bekor qilinganmi, qilingan bo'lsa QACHON.
    ///
    /// <para>
    /// Javob <see cref="PaymentDto"/> dan emas, BAZADAN olinadi. Sabab: shtamp
    /// yo'qligi "chek kuchda" degan ma'noni bildiradi, ya'ni uni noto'g'ri
    /// hisoblash bekor qilingan to'lovni haqiqiy ko'rsatib qo'yardi — jim
    /// turadigan va aynan firibgarlikka qulay xato. Shuning uchun bu yerda
    /// oraliq qatlam yo'q.
    /// </para>
    /// <para>
    /// Bitta so'rov, ikkita mumkin bo'lgan qator: to'lovning O'ZI va (bo'lsa)
    /// uni bekor qilgan storno qatori (<c>payments.reversal_of</c> ustida
    /// qisman unikal indeks bor — bittadan ortiq bo'la olmaydi).
    /// </para>
    /// </summary>
    private Task<DateTimeOffset?> CancelledAtAsync(Guid paymentId, CancellationToken ct) =>
        CancelledAtAsync(db, paymentId, ct);

    /// <summary>
    /// "Bu chek bekor qilinganmi" savolining YAGONA ta'rifi — PDF ham, 58 mm
    /// termal chek (<see cref="ReceiptPrintQuery"/>) ham shu metodni chaqiradi.
    /// Ikkinchi nusxa yozilsa, bir kun ikki chek ikki xil javob berardi
    /// (yuqoridagi izohga qarang: bu firibgarlikka qulay xato).
    /// </summary>
    /// <param name="db">Baza konteksti (faqat o'qiladi).</param>
    /// <param name="paymentId">To'lov yoki storno qatori id'si.</param>
    /// <param name="ct">Bekor qilish tokeni.</param>
    /// <returns>Storno sanasi; to'lov kuchda bo'lsa null.</returns>
    public static async Task<DateTimeOffset?> CancelledAtAsync(
        IAppDbContext db, Guid paymentId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var rows = await db.Payments.AsNoTracking()
            .Where(p => p.Id == paymentId || p.ReversalOf == paymentId)
            .Select(p => new { p.Id, p.ReversalOf, p.ReceivedAt })
            .ToListAsync(ct);

        var self = rows.FirstOrDefault(r => r.Id == paymentId);

        // To'lovning o'zi storno qatori bo'lsa, chek — bekor qilish hujjati:
        // shtamp sanasi shu qatorning o'z sanasi.
        if (self?.ReversalOf is not null) return self.ReceivedAt;

        return rows.FirstOrDefault(r => r.ReversalOf == paymentId)?.ReceivedAt;
    }

    /// <summary>
    /// Chekda chop etiladigan modelni yig'adi. <b>Sof funksiya</b> — bazasiz,
    /// shuning uchun chek mazmuni (qatorlar, jami, storno) to'g'ridan-to'g'ri
    /// sinaladi.
    ///
    /// <para>
    /// <b>Qatorlar yig'indisi = jami.</b> Taqsimotlar summasi to'lovdan kam
    /// bo'lsa (ota-ona avans to'lagan), qoldiq ALOHIDA qator bo'lib qo'shiladi.
    /// Busiz chekdagi qatorlar "JAMI" ga qo'shilmay qolardi va chekni o'qigan
    /// odam farqni o'zi qidirishga majbur bo'lardi.
    /// </para>
    /// </summary>
    /// <param name="payment">To'lov (P1-11 DTO'si).</param>
    /// <param name="school">Maktab ma'lumoti; null bo'lsa standart nom ishlatiladi.</param>
    /// <param name="cancelledAt">Storno sanasi yoki null.</param>
    public static ReceiptModel BuildModel(
        PaymentDto payment, SchoolMeta? school, DateTimeOffset? cancelledAt, string? verifyUrl = null)
    {
        ArgumentNullException.ThrowIfNull(payment);

        // Har toifa ALOHIDA qatorda (P1-12 qabul mezoni). Tartib — to'lov ustuvorligi bilan bir xil
        // (2026-09-24): avval oy, oy ichida O'qish → Yotoqxona → Avtobus → Ovqat → Boshqa.
        var lines = payment.Allocations
            .OrderBy(a => a.PeriodMonth)
            .ThenBy(a => PaymentService.CategoryRank(a.CategoryCode))
            .ThenBy(a => a.CategoryName, StringComparer.Ordinal)
            .Select(a => new ReceiptLine(a.CategoryName, a.PeriodMonth, a.Amount, a.InvoiceId))
            .ToList();

        if (payment.Unallocated > 0m)
        {
            // Storno qatorida taqsimot bo'lmaydi — u holda bu "avans" emas,
            // qaytarilgan summa.
            var label = payment.ReversalOf is null ? ReceiptText.AdvanceLine : ReceiptText.RefundLine;
            lines.Add(new ReceiptLine(label, null, payment.Unallocated));
        }

        return new ReceiptModel(
            SchoolName: string.IsNullOrWhiteSpace(school?.Name) ? "Maktab" : school!.Name,
            SchoolAddress: Trimmed(school?.Address),
            SchoolPhone: Trimmed(school?.Phone),
            ReceiptNo: payment.ReceiptNo,
            ReceivedAt: payment.ReceivedAt,
            StudentName: payment.StudentName,
            Lines: lines,
            Method: payment.Method,
            Total: payment.Amount,
            CashierName: payment.CashierName,
            CancelledAt: cancelledAt,
            VerifyUrl: verifyUrl);
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

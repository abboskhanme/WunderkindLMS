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
    public Task<bool> SendToGuardianAsync(Guid paymentId, CancellationToken ct = default) =>
        SendCoreAsync(paymentId, null, ct);

    /// <inheritdoc />
    public Task<bool> SendToGuardianAsync(Guid paymentId, string? sentBy, CancellationToken ct = default) =>
        SendCoreAsync(paymentId, sentBy, ct);

    /// <summary>Telegram's limit for a document caption, characters of visible text.</summary>
    internal const int CaptionLimit = 1024;

    /// <param name="sentBy">null — automatic (right after the payment); users.id — a manual resend.</param>
    private async Task<bool> SendCoreAsync(Guid paymentId, string? sentBy, CancellationToken ct)
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
            var chats = (await db.TelegramRegistrations.AsNoTracking()
                    .Where(r => r.StudentId == payment.StudentId)
                    .Select(r => new { r.ChatId, r.ParentName })
                    .ToListAsync(ct))
                .DistinctBy(r => r.ChatId)
                .ToList();

            if (chats.Count == 0)
            {
                // KUTILGAN holat, xato emas (P1-12 qabul mezoni): ota-ona hali
                // botga ulanmagan. Kassir chekni qog'ozda beradi.
                logger.LogWarning(
                    "Chek № {ReceiptNo}: o'quvchi {StudentId} bo'yicha Telegram ro'yxati yo'q — yuborilmadi.",
                    payment.ReceiptNo, payment.StudentId);
                await LogDeliveriesAsync(payment, sentBy,
                    [new Delivery(null, BroadcastDelivery.NoTelegram, "", 0, null)], ct);
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
            // The payment auto-message (2026-10-15): when switched on, its template is the caption.
            // Never for a cancellation receipt, and never instead of the PDF — the PDF always goes.
            string? template = null;
            Dictionary<string, string>? values = null;
            try
            {
                template = model.CancelledAt is null ? await PaymentTemplateAsync(ct) : null;
                values = template is null ? null : await PaymentValuesAsync(payment, template, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The template is a courtesy; the receipt goes with its usual caption.
                logger.LogWarning(ex, "Chek № {ReceiptNo}: to'lov xabari shabloni tayyorlanmadi — odatiy izoh bilan yuboriladi.",
                    payment.ReceiptNo);
                template = null;
            }

            var delivered = false;
            var log = new List<Delivery>();
            foreach (var chat in chats)
            {
                string? html = null;
                if (template is not null && values is not null)
                {
                    values["{ota_ona}"] = string.IsNullOrWhiteSpace(chat.ParentName) ? "Ota-ona" : chat.ParentName;
                    html = AutoMessageService.Render(template, values);
                    if (string.IsNullOrWhiteSpace(TelegramHtml.ToPlain(html))) html = null;
                }

                bool ok, textOk = true;
                int attempts;
                if (html is null)
                    (ok, attempts) = await SendWithOneRetryAsync(chat.ChatId, payment.ReceiptNo,
                        () => telegram.SendDocumentAsync(chat.ChatId, pdf, fileName, caption, ct), ct);
                else if (TelegramHtml.ToPlain(html).Length <= CaptionLimit)
                    (ok, attempts) = await SendWithOneRetryAsync(chat.ChatId, payment.ReceiptNo,
                        () => telegram.SendDocumentHtmlAsync(chat.ChatId, pdf, fileName, html, ct), ct);
                else
                {
                    // Too long for a caption: the receipt with its usual caption, then the text.
                    (ok, attempts) = await SendWithOneRetryAsync(chat.ChatId, payment.ReceiptNo,
                        () => telegram.SendDocumentAsync(chat.ChatId, pdf, fileName, caption, ct), ct);
                    textOk = ok && await telegram.SendRichAsync(chat.ChatId, html, ct);
                }
                delivered |= ok;
                log.Add(new Delivery(chat.ChatId,
                    ok && textOk ? BroadcastDelivery.Sent : BroadcastDelivery.Failed,
                    html ?? TelegramHtml.Escape(caption), attempts,
                    !ok ? "Telegram chekni qabul qilmadi" : !textOk ? "Chek yetdi, alohida matn yetmadi" : null));
            }

            await LogDeliveriesAsync(payment, sentBy, log, ct);
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
    private async Task<(bool Ok, int Attempts)> SendWithOneRetryAsync(
        long chatId, long receiptNo, Func<Task<bool>> send, CancellationToken ct)
    {
        if (await send())
            return (true, 1);

        logger.LogWarning(
            "Chek № {ReceiptNo}: {ChatId} chatiga yuborilmadi, qayta urinilmoqda.", receiptNo, chatId);

        await Task.Delay(RetryDelay, ct);

        if (await send())
        {
            logger.LogInformation(
                "Chek № {ReceiptNo}: {ChatId} chatiga ikkinchi urinishda yuborildi.", receiptNo, chatId);
            return (true, 2);
        }

        logger.LogError(
            "Chek № {ReceiptNo}: {ChatId} chatiga IKKI urinishdan keyin ham yuborilmadi. "
            + "To'lov kuchda — chekni qo'lda qayta yuborish kerak.", receiptNo, chatId);
        return (false, 2);
    }

    /// <summary>One chat's outcome for the delivery log.</summary>
    private sealed record Delivery(long? ChatId, string Status, string Text, int Attempts, string? Error);

    /// <summary>The payment rule's text when it is switched on, else null.</summary>
    private async Task<string?> PaymentTemplateAsync(CancellationToken ct)
    {
        var rule = await db.AutoMessageRules.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Key == AutoMessageKind.Payment, ct);
        return rule is { Enabled: true } && !string.IsNullOrWhiteSpace(rule.Text) ? rule.Text : null;
    }

    /// <summary>The payment rule's token values (plain text; escaped when rendered).</summary>
    private async Task<Dictionary<string, string>> PaymentValuesAsync(PaymentDto payment, string template, CancellationToken ct)
    {
        var student = await db.Students.AsNoTracking().Where(s => s.Id == payment.StudentId)
            .Select(s => new { s.FullName, s.FirstName, s.ClassName }).FirstOrDefaultAsync(ct);
        var fullName = student?.FullName ?? payment.StudentName;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["{fish}"] = fullName,
            ["{ism}"] = AutoMessageService.FirstNameOf(fullName, student?.FirstName),
            ["{sinf}"] = student?.ClassName ?? "",
            ["{summa}"] = AutoMessageService.Money(payment.Amount),
            ["{sana}"] = ReceiptText.Date(payment.ReceivedAt),
            ["{vaqt}"] = AppClock.ToLocal(payment.ReceivedAt).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            ["{chek}"] = payment.ReceiptNo.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["{usul}"] = ReceiptText.MethodLabel(payment.Method),
        };
        // The debt after this payment — the "Qarzdorlar" report's number; asked only when the text uses it.
        if (template.Contains("{qarzdorlik}", StringComparison.OrdinalIgnoreCase))
        {
            var debt = (await new FinanceReportQueries(db).DebtorsAsync(
                    new DebtorReportQuery(IncludeArchived: true, StudentIds: [payment.StudentId]), ct))
                .Sum(d => d.Debt);
            values["{qarzdorlik}"] = AutoMessageService.Money(debt);
        }
        return values;
    }

    /// <summary>
    /// Every receipt send, per chat, goes into the automatic message log (rule "payment", ref = the
    /// receipt number), whether the payment rule is on or off. Logging never affects the send:
    /// a failure here is only written to the application log.
    /// </summary>
    private async Task LogDeliveriesAsync(PaymentDto payment, string? sentBy, List<Delivery> deliveries, CancellationToken ct)
    {
        var added = new List<AutoMessageLogEntry>();
        try
        {
            if (!await db.Students.AsNoTracking().AnyAsync(s => s.Id == payment.StudentId, ct)) return;
            var now = AppClock.NowInstant;
            var period = AppClock.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            foreach (var d in deliveries)
            {
                var entry = new AutoMessageLogEntry
                {
                    RuleKey = AutoMessageKind.Payment, Period = period, StudentId = payment.StudentId, ChatId = d.ChatId,
                    RefId = payment.ReceiptNo.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Status = d.Status, Text = d.Text, Attempts = d.Attempts, Error = d.Error, At = now,
                    Source = sentBy is null ? AutoMessageSource.Auto : AutoMessageSource.Manual, SentBy = sentBy,
                };
                db.AutoMessageLog.Add(entry);
                added.Add(entry);
            }
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            foreach (var entry in added) db.AutoMessageLog.Entry(entry).State = EntityState.Detached;
            logger.LogWarning(ex, "Chek № {ReceiptNo}: yetkazish jurnaliga yozilmadi (chek yuborilishiga ta'sir qilmaydi).",
                payment.ReceiptNo);
        }
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

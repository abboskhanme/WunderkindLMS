using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  To'lov cheki — PDF chizmasi. Vazifa: P1-12. SPEC §4.7.
// ===========================================================================
//
//  NEGA CHEK UMUMAN BOR (SPEC §4.7)
//  --------------------------------
//  Chek ota-onaning Telegramiga yuboriladi, ya'ni to'lov dalili MAKTAB
//  O'ZGARTIRA OLMAYDIGAN joyda ham saqlanadi. Kassir yozuvni yo'q qilsa ham,
//  to'lovchida nusxa qoladi. Bu — eng arzon firibgarlik nazorati.
//
//  NEGA BU FAYL XIZMATDAN AJRATILGAN
//  ---------------------------------
//  Bu yerda BAZA HAM, TARMOQ HAM YO'Q — faqat `ReceiptModel` dan PDF bayt
//  oqimini yasash. Shu sabab chek MAZMUNINI (qatorlar, summalar, o'zbekcha
//  yozuvlar) bazasiz, tez va aniq sinash mumkin; `ReceiptService` esa faqat
//  "ma'lumotni qayerdan olish" bilan shug'ullanadi.
//
//  TIL — O'ZBEKCHA
//  --------------
//  Chek MIJOZGA (ota-onaga) ko'rinadigan hujjat, shuning uchun undagi barcha
//  matn o'zbekcha. Kod, identifikator va izohlar esa repozitoriyadagi qoidaga
//  bo'ysunadi.
//
//  KUTUBXONA: QuestPDF Community (litsenziya docs/ASSUMPTIONS.md da)
//  ----------------------------------------------------------------
//  Yillik yalpi tushumi 1 000 000 AQSh dollaridan kam tashkilotlar uchun
//  BEPUL. Pul sarflanmaydi. Litsenziya turi `ReceiptDocument` ning statik
//  konstruktorida belgilanadi (pastga qarang) — `Program.cs` da emas, chunki
//  (1) `Program.cs` P1-15 ning fayli, (2) PDF yasaydigan YAGONA kod shu klass,
//  ya'ni sozlama uni ishlatadigan joyning yonida turadi va uni ishga tushirishni
//  unutib bo'lmaydi.

/// <summary>
/// Chekning bitta qatori. <b>Har toifa ALOHIDA qatorda</b> (P1-12 qabul
/// mezoni): ota-ona pulining qaysi qismi o'qishga, qaysi qismi avtobusga
/// ketganini ko'rishi shart.
/// </summary>
/// <param name="CategoryName">Toifa nomi (o'zbekcha): "Maktab to'lovi", "Avtobus", ...</param>
/// <param name="PeriodMonth">Qaysi oy uchun. null = oyga bog'lanmagan (avans, qaytarish).</param>
/// <param name="Amount">Shu qatorga tushgan summa.</param>
public sealed record ReceiptLine(string CategoryName, DateOnly? PeriodMonth, decimal Amount);

/// <summary>
/// Chekda CHOP ETILADIGAN hamma narsa — boshqa hech narsa. Bu tip baza
/// entity'siga ham, DTO'ga ham bog'lanmagan: chizma faqat shu yozuvni biladi.
///
/// <para>
/// <b>Invariant:</b> <c>Lines</c> summasi <c>Total</c> ga TENG. Taqsimlanmagan
/// qoldiq (avans) ham alohida qator bo'lib qo'shiladi — aks holda chekdagi
/// qatorlar jamiga qo'shilmay qolardi va chekni o'qigan odam farqni qidirib
/// qolardi. Buni <c>ReceiptService.BuildModel</c> ta'minlaydi, test tekshiradi.
/// </para>
/// </summary>
/// <param name="SchoolName">Maktab nomi (SchoolMeta).</param>
/// <param name="SchoolAddress">Manzil — bo'sh bo'lsa chop etilmaydi.</param>
/// <param name="SchoolPhone">Telefon — bo'sh bo'lsa chop etilmaydi.</param>
/// <param name="ReceiptNo">Chek raqami (smena ichida uzluksiz, SPEC §4.2).</param>
/// <param name="ReceivedAt">To'lov qabul qilingan LAHZA; chekda Toshkent vaqtida ko'rsatiladi.</param>
/// <param name="StudentName">O'quvchi F.I.SH.</param>
/// <param name="Lines">Toifalar kesimi — har biri alohida qator.</param>
/// <param name="Method">cash | card | transfer | online (<see cref="PaymentMethod"/>).</param>
/// <param name="Total">Jami summa = <c>payments.amount</c>.</param>
/// <param name="CashierName">Pulni qabul qilgan kassir F.I.SH.</param>
/// <param name="CancelledAt">Storno sanasi. null = chek kuchda.</param>
public sealed record ReceiptModel(
    string SchoolName,
    string? SchoolAddress,
    string? SchoolPhone,
    long ReceiptNo,
    DateTimeOffset ReceivedAt,
    string StudentName,
    IReadOnlyList<ReceiptLine> Lines,
    string Method,
    decimal Total,
    string CashierName,
    DateTimeOffset? CancelledAt);

/// <summary>
/// Chekdagi barcha MATN — bitta joyda. Sof funksiyalar: bazasiz, holatsiz,
/// shuning uchun to'g'ridan-to'g'ri sinaladi.
///
/// <para>
/// Nega <c>CultureInfo("uz-UZ")</c> ishlatilmagan: konteynerda ICU ma'lumoti
/// bo'lmasligi yoki versiyadan versiyaga o'zgarishi mumkin, oy nomi esa
/// chekda — mijoz ko'radigan hujjatda. Qo'lda yozilgan ro'yxat har muhitda
/// bir xil natija beradi va uni ko'z bilan tekshirsa bo'ladi.
/// </para>
/// </summary>
public static class ReceiptText
{
    /// <summary>Storno shtampi. ATAYLAB faqat ASCII harflar — PDF metama'lumotiga ham shu matn tushadi.</summary>
    public const string CancelledStamp = "BEKOR QILINGAN";

    /// <summary>Taqsimlanmagan qoldiq qatori (oldindan to'lov).</summary>
    public const string AdvanceLine = "Avans (keyingi oylarga)";

    /// <summary>Storno chekidagi qaytarilgan summa qatori.</summary>
    public const string RefundLine = "Qaytarilgan summa";

    private static readonly string[] Months =
    [
        "yanvar", "fevral", "mart", "aprel", "may", "iyun",
        "iyul", "avgust", "sentyabr", "oktyabr", "noyabr", "dekabr",
    ];

    /// <summary>
    /// Pul: <c>1 250 000 so'm</c>. Guruh ajratgichi — uzilmas probel (U+00A0),
    /// ya'ni raqam qator oxirida ikkiga bo'linib ketmaydi.
    /// Tiyin faqat nolga teng bo'lmasa ko'rsatiladi (ustun <c>numeric(14,2)</c>).
    /// </summary>
    public static string Money(decimal amount)
    {
        var format = new System.Globalization.NumberFormatInfo
        {
            // "\u00A0" = uzilmas probel (non-breaking space). Oddiy probel
            // bo'lganda "1 250 000" qator oxirida ikkiga bo'linib ketishi mumkin edi.
            NumberGroupSeparator = "\u00A0",
            NumberDecimalSeparator = ",",
            NumberGroupSizes = [3],
        };
        var digits = amount == decimal.Truncate(amount)
            ? amount.ToString("N0", format)
            : amount.ToString("N2", format);
        return $"{digits} so'm";
    }

    /// <summary>Sana-vaqt Toshkent mintaqasida: <c>11.09.2026 14:35</c> (SPEC §7, <see cref="AppClock"/>).</summary>
    public static string DateTime(DateTimeOffset instant) =>
        AppClock.ToLocal(instant).ToString("dd.MM.yyyy HH:mm");

    /// <summary>Sana Toshkent mintaqasida: <c>11.09.2026</c>.</summary>
    public static string Date(DateTimeOffset instant) =>
        AppClock.ToLocal(instant).ToString("dd.MM.yyyy");

    /// <summary>Hisob-faktura oyi: <c>2026-yil sentyabr</c>. null = chiziqcha.</summary>
    public static string Period(DateOnly? month) =>
        month is { } m ? $"{m.Year}-yil {Months[m.Month - 1]}" : "—";

    /// <summary>
    /// To'lov usuli — o'zbekcha yorliq. Noma'lum qiymat kelsa xom kodning
    /// o'zi chiqadi: chekda "" turgandan ko'ra "paynet" turgani yaxshiroq.
    /// </summary>
    public static string MethodLabel(string method) => method switch
    {
        PaymentMethod.Cash => "Naqd pul",
        PaymentMethod.Card => "Bank kartasi (terminal)",
        PaymentMethod.Transfer => "Bank o'tkazmasi",
        PaymentMethod.Online => "Onlayn to'lov (Payme/Click/Uzum)",
        _ => method,
    };

    /// <summary>Telegramga yuboriladigan fayl nomi.</summary>
    public static string FileName(long receiptNo) => $"chek-{receiptNo}.pdf";

    /// <summary>
    /// PDF metama'lumotidagi sarlavha. ATAYLAB sof ASCII: PDF spetsifikatsiyasi
    /// bo'yicha ASCII satr faylga <c>(Chek 1042)</c> ko'rinishida ochiq yoziladi,
    /// ASCII bo'lmagani esa UTF-16 ga o'tadi. Ochiq yozuv tufayli chek raqamini
    /// PDF ichidan qidirib topish mumkin — test aynan shuni tekshiradi.
    /// </summary>
    public static string MetadataTitle(long receiptNo) => $"Chek {receiptNo}";

    /// <summary>PDF metama'lumotidagi mavzu. Storno bo'lsa shtamp matni shu yerga ham tushadi.</summary>
    public static string MetadataSubject(DateTimeOffset? cancelledAt) =>
        cancelledAt is { } at ? $"{CancelledStamp} {Date(at)}" : "To'lov cheki";
}

/// <summary>
/// Chek PDF'i (A5). Faqat chizadi — ma'lumotni <see cref="ReceiptService"/> yig'adi.
///
/// <para>
/// Storno qilingan to'lovning cheki <b>"BEKOR QILINGAN"</b> shtampi va bekor
/// qilingan sanasi bilan chiqadi (P1-12 qabul mezoni). Chek MATNI o'chirilmaydi
/// va o'zgartirilmaydi: bekor qilingan to'lov ham ko'rinib turishi kerak —
/// bu `payments` jadvalidagi "tahrirlash emas, storno" qoidasining
/// qog'ozdagi ko'rinishi (SPEC §4.1).
/// </para>
/// </summary>
public sealed class ReceiptDocument(ReceiptModel model) : IDocument
{
    static ReceiptDocument()
    {
        // QuestPDF Community — bepul (yillik tushum < 1 mln USD; docs/ASSUMPTIONS.md).
        // Statik konstruktorda: PDF yasashdan OLDIN, kafolatlangan tarzda va
        // `Program.cs` ga tegmasdan bajariladi.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static readonly Color Ink = Colors.Black;
    private static readonly Color Muted = Colors.Grey.Darken1;
    private static readonly Color Rule = Colors.Grey.Lighten1;
    private static readonly Color Alarm = Colors.Red.Darken2;

    public DocumentMetadata GetMetadata() => new()
    {
        Title = ReceiptText.MetadataTitle(model.ReceiptNo),
        Subject = ReceiptText.MetadataSubject(model.CancelledAt),
        Author = model.SchoolName,
        Language = "uz",
    };

    public DocumentSettings GetSettings() => DocumentSettings.Default;

    /// <summary>
    /// Chekni PDF bayt oqimiga o'giradi.
    ///
    /// <para>
    /// PDF kutubxonasi AYNAN shu klassdan tashqariga chiqmaydi: xizmat ham,
    /// controller ham, testlar ham faqat <c>Render()</c> ni ko'radi. Kutubxona
    /// almashtirilsa (yoki litsenziya sharti o'zgarsa) o'zgaradigan fayl —
    /// bittagina, shu fayl.
    /// </para>
    /// </summary>
    public byte[] Render() => this.GeneratePdf();

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            // A5 — kassa printerida ham, A4 varaqda ham normal chiqadi.
            page.Size(PageSizes.A5);
            page.Margin(12, Unit.Millimetre);
            page.DefaultTextStyle(t => t.FontSize(10).FontColor(Ink));

            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeBody);
            page.Footer().Element(ComposeFooter);
        });
    }

    private void ComposeHeader(IContainer container) =>
        container.PaddingBottom(8).Column(col =>
        {
            col.Item().Text(model.SchoolName).Bold().FontSize(15);

            if (!string.IsNullOrWhiteSpace(model.SchoolAddress))
                col.Item().Text(model.SchoolAddress).FontSize(8).FontColor(Muted);

            if (!string.IsNullOrWhiteSpace(model.SchoolPhone))
                col.Item().Text($"Tel: {model.SchoolPhone}").FontSize(8).FontColor(Muted);

            col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Rule);
        });

    private void ComposeBody(IContainer container) =>
        container.Column(col =>
        {
            col.Spacing(8);

            col.Item().AlignCenter().Text($"TO'LOV CHEKI № {model.ReceiptNo}").Bold().FontSize(13);

            if (model.CancelledAt is { } cancelledAt)
                col.Item().Element(c => ComposeCancelledStamp(c, cancelledAt));

            col.Item().Column(info =>
            {
                Field(info, "Sana, vaqt", $"{ReceiptText.DateTime(model.ReceivedAt)} (Toshkent)");
                Field(info, "O'quvchi", model.StudentName);
                Field(info, "To'lov turi", ReceiptText.MethodLabel(model.Method));
                Field(info, "Kassir", model.CashierName);
            });

            col.Item().Element(ComposeLines);
            col.Item().Element(ComposeTotal);
        });

    /// <summary>
    /// Storno shtampi. Qizil fonda oq harflar — chekni qo'lida ushlab turgan
    /// odam buni CHEKDAGI BOSHQA HECH NARSANI o'qimasdan ham ko'radi.
    /// </summary>
    private static void ComposeCancelledStamp(IContainer container, DateTimeOffset cancelledAt) =>
        container.Background(Alarm).Padding(6).Column(col =>
        {
            col.Item().AlignCenter().Text(ReceiptText.CancelledStamp)
                .Bold().FontSize(16).FontColor(Colors.White);
            col.Item().AlignCenter().Text($"Bekor qilingan sana: {ReceiptText.DateTime(cancelledAt)}")
                .FontSize(9).FontColor(Colors.White);
        });

    /// <summary>Toifalar jadvali — <b>har toifa alohida qatorda</b> (P1-12 qabul mezoni).</summary>
    private void ComposeLines(IContainer container) =>
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(5);   // Toifa
                columns.RelativeColumn(4);   // Davr
                columns.RelativeColumn(3);   // Summa
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("Toifa").SemiBold();
                header.Cell().Element(HeaderCell).Text("Davr").SemiBold();
                header.Cell().Element(HeaderCell).AlignRight().Text("Summa").SemiBold();
            });

            foreach (var line in model.Lines)
            {
                table.Cell().Element(BodyCell).Text(line.CategoryName);
                table.Cell().Element(BodyCell).Text(ReceiptText.Period(line.PeriodMonth)).FontColor(Muted);
                table.Cell().Element(BodyCell).AlignRight().Text(ReceiptText.Money(line.Amount));
            }

            static IContainer HeaderCell(IContainer c) =>
                c.PaddingVertical(4).BorderBottom(1).BorderColor(Rule);

            static IContainer BodyCell(IContainer c) =>
                c.PaddingVertical(3).BorderBottom(1).BorderColor(Colors.Grey.Lighten3);
        });

    private void ComposeTotal(IContainer container) =>
        container.PaddingTop(2).Row(row =>
        {
            row.RelativeItem().Text("JAMI").Bold().FontSize(12);
            row.RelativeItem().AlignRight().Text(ReceiptText.Money(model.Total)).Bold().FontSize(12);
        });

    private void ComposeFooter(IContainer container) =>
        container.PaddingTop(8).Column(col =>
        {
            col.Item().LineHorizontal(1).LineColor(Rule);
            col.Item().PaddingTop(4).Text(text =>
            {
                text.DefaultTextStyle(t => t.FontSize(7).FontColor(Muted));
                text.Span("Chek maktab axborot tizimida avtomatik yaratilgan. Saqlab qo'ying — ");
                text.Span("bu to'lov dalili.");
            });
            col.Item().AlignRight().Text(text =>
            {
                text.DefaultTextStyle(t => t.FontSize(7).FontColor(Muted));
                text.CurrentPageNumber();
                text.Span(" / ");
                text.TotalPages();
            });
        });

    /// <summary>"Yorliq: qiymat" juftligi — yorliq ustuni qat'iy kenglikda, qiymatlar tekis turadi.</summary>
    private static void Field(ColumnDescriptor column, string label, string value) =>
        column.Item().PaddingVertical(1).Row(row =>
        {
            row.ConstantItem(32, Unit.Millimetre).Text(label).FontColor(Muted);
            row.RelativeItem().Text(value).SemiBold();
        });
}

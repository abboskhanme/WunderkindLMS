using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;

namespace SchoolLms.Application.Services;

/// <summary>
/// Shartnoma Word (.docx) andozasini to'ldiradi: `@` bilan boshlanuvchi o'rinbosarlarni
/// (masalan <c>@fish</c>) berilgan qiymatlar bilan almashtiradi. Word matnni bir nechta "run"ga
/// bo'lib yozishi mumkinligi sababli almashtirish PARAGRAF darajasida bajariladi (run matnlari
/// birlashtiriladi, almashtiriladi, birinchi runga yoziladi). Noma'lum tokenlar o'z holicha qoladi.
///
/// <para>
/// <b>Raqamlash rejimi (K-6) ham shu yerda.</b> <see cref="GetNumberModeAsync"/> va
/// <see cref="Resolve"/> — <c>school_meta.contract_number_mode</c> ustidagi
/// yagona haqiqat manbai, ikkala chaqiruvchi uchun ham bir xil: eski umumiy generator
/// (<c>ContractsController</c>, <c>Contracts</c> jadvali) va o'quvchi reyestri
/// (<c>StudentContractsController</c>, <c>StudentContracts</c> jadvali, K-1..K-5).
/// </para>
/// </summary>
public class ContractService(IWebHostEnvironment env, IAppDbContext db)
{
    private static readonly Regex TokenRx = new(@"@[A-Za-z_]+", RegexOptions.Compiled);

    /// <summary>Andoza faylini "/uploads/..." manzilidan o'qiydi (topilmasa null).</summary>
    public byte[]? ReadTemplate(string fileUrl)
    {
        var name = Path.GetFileName(fileUrl);
        if (string.IsNullOrEmpty(name)) return null;
        var path = Path.Combine(env.ContentRootPath, "uploads", name);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    /// <summary>
    /// Hosil qilingan .docx ni <c>uploads/</c> papkasiga yozadi va uning
    /// <c>/uploads/...</c> manzilini qaytaradi (K-2).
    ///
    /// <para>
    /// <b>Bu YUKLASH yo'li emas.</b> Foydalanuvchi fayli baribir
    /// <c>UploadsController</c> + <see cref="UploadGuard"/> orqali keladi
    /// (K-3). Bu yerda esa SERVERNING O'ZI hosil qilgan hujjat saqlanadi:
    /// tashqaridan hech narsa kelmaydi, shuning uchun tekshiradigan narsa
    /// ham yo'q. Papka va nom qoidasi <see cref="UploadGuard.SafeName"/>
    /// bilan bir xil — fayllar bitta joyda tursin va <c>/uploads</c> statik
    /// yo'nalishi (Program.cs) ularni bir xil yetkazsin.
    /// </para>
    /// </summary>
    public string SaveGenerated(byte[] docxBytes)
    {
        var dir = Path.Combine(env.ContentRootPath, "uploads");
        Directory.CreateDirectory(dir);
        var stored = $"{Guid.NewGuid():N}.docx";
        File.WriteAllBytes(Path.Combine(dir, stored), docxBytes);
        return $"/uploads/{stored}";
    }

    /// <summary>Andoza baytlarini nusxalab, tokenlarni almashtiradi va yangi .docx baytlarini qaytaradi.</summary>
    public byte[] FillTemplate(byte[] docxBytes, IDictionary<string, string> tokens)
    {
        using var ms = new MemoryStream();
        ms.Write(docxBytes, 0, docxBytes.Length);
        ms.Position = 0;
        using (var doc = WordprocessingDocument.Open(ms, true))
        {
            var main = doc.MainDocumentPart;
            if (main?.Document is not null)
            {
                ReplaceIn(main.Document, tokens);
                foreach (var h in main.HeaderParts) ReplaceIn(h.Header, tokens);
                foreach (var f in main.FooterParts) ReplaceIn(f.Footer, tokens);
                main.Document.Save();
            }
        }
        return ms.ToArray();
    }

    private static void ReplaceIn(DocumentFormat.OpenXml.OpenXmlElement root, IDictionary<string, string> tokens)
    {
        foreach (var para in root.Descendants<Paragraph>())
        {
            var texts = para.Descendants<Text>().ToList();
            if (texts.Count == 0) continue;
            var combined = string.Concat(texts.Select(t => t.Text));
            if (!combined.Contains('@')) continue;
            var replaced = Apply(combined, tokens);
            if (replaced == combined) continue;
            texts[0].Text = replaced;
            texts[0].Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve;
            for (var i = 1; i < texts.Count; i++) texts[i].Text = "";
        }
    }

    private static string Apply(string input, IDictionary<string, string> tokens) =>
        TokenRx.Replace(input, m => tokens.TryGetValue(m.Value, out var v) ? v : m.Value);

    // =========================================================================
    //  Raqamlash rejimi — K-6 (docs/modules/students-parity.md §2.10.3).
    // =========================================================================

    /// <summary>Qo'lda rejimda raqam bo'sh qoldirilganda qaytariladigan xabar.</summary>
    public const string ManualNumberRequiredMessage =
        "Qo'lda nomerlash rejimida shartnoma raqami kiritilishi shart";

    /// <summary>
    /// Joriy raqamlash rejimi (<see cref="SchoolLms.Domain.ContractNumberMode"/>).
    /// Qator umuman yo'q yoki qiymat buzilgan bo'lsa — sukut <c>auto</c>
    /// (<c>SchoolMeta.ContractNumberMode</c> dagi baza DEFAULT'i bilan bir xil).
    /// </summary>
    public async Task<string> GetNumberModeAsync(CancellationToken ct = default)
    {
        var mode = await db.SchoolMeta.AsNoTracking()
            .Select(m => m.ContractNumberMode).FirstOrDefaultAsync(ct);
        return SchoolLms.Domain.ContractNumberMode.IsValid(mode)
            ? mode!
            : SchoolLms.Domain.ContractNumberMode.Auto;
    }

    /// <summary>
    /// K-6 qoidasi: <c>auto</c> rejimida qo'lda kiritilgan qiymat E'TIBORGA
    /// OLINMAYDI — chaqiruvchi uni tashlab, ketma-ket generatsiyaga o'tishi kerak
    /// (masalan <c>StudentContractsController.NextNumberAsync</c>). <c>manual</c>
    /// rejimida esa raqam MAJBURIY.
    ///
    /// <para>
    /// Bu metod DIZAYN BO'YICHA sof (baza bilan ishlamaydi) — <see cref="GetNumberModeAsync"/>
    /// bilan birga chaqiriladi: <c>Resolve(await GetNumberModeAsync(ct), raw)</c>.
    /// Unikallik BU YERDA tekshirilmaydi: u chaqiruvchining ishi (masalan
    /// <c>StudentContractsController.NumberTakenAsync</c>) VA baza darajasida
    /// <c>ux_student_contracts_number</c> qisman unikal indeksi — ikkalovi ham
    /// rejimdan qat'i nazar ishlaydi.
    /// </para>
    /// </summary>
    public static ContractNumberDecision Resolve(string mode, string? rawNumber)
    {
        var trimmed = (rawNumber ?? "").Trim();
        if (mode == SchoolLms.Domain.ContractNumberMode.Manual)
            return trimmed.Length == 0
                ? ContractNumberDecision.Reject(ManualNumberRequiredMessage)
                : ContractNumberDecision.UseManual(trimmed);
        return ContractNumberDecision.GenerateNext;
    }
}

/// <summary>
/// <see cref="ContractService.Resolve"/> natijasi — uchta o'zaro istisno holat:
/// generatsiya qil, berilgan raqamni ishlat, yoki rad et (xabar bilan).
/// </summary>
public readonly record struct ContractNumberDecision
{
    public bool ShouldGenerate { get; private init; }
    public string? ManualNumber { get; private init; }
    public string? Error { get; private init; }

    public bool IsValid => Error is null;

    public static readonly ContractNumberDecision GenerateNext = new() { ShouldGenerate = true };

    public static ContractNumberDecision UseManual(string number) => new() { ManualNumber = number };

    public static ContractNumberDecision Reject(string error) => new() { Error = error };
}

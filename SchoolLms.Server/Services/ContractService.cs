using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Text.RegularExpressions;

namespace SchoolLms.Server.Services;

/// <summary>
/// Shartnoma Word (.docx) andozasini to'ldiradi: `@` bilan boshlanuvchi o'rinbosarlarni
/// (masalan <c>@fish</c>) berilgan qiymatlar bilan almashtiradi. Word matnni bir nechta "run"ga
/// bo'lib yozishi mumkinligi sababli almashtirish PARAGRAF darajasida bajariladi (run matnlari
/// birlashtiriladi, almashtiriladi, birinchi runga yoziladi). Noma'lum tokenlar o'z holicha qoladi.
/// </summary>
public class ContractService(IWebHostEnvironment env)
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
}

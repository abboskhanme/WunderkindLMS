using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  FORMATLANGAN MATN — TELEGRAM HTML QISMI (2026-10-11)
// ===========================================================================
//
//  Mijoz: "Matn yozishda bold, kursiv va boshqa funksiyalarni qo'shish". Foydalanuvchi Telegram
//  ilovasidagi belgilar bilan yozadi (saqlanadigan matn — shu): **qalin**, __kursiv__, ++tagiga
//  chizilgan++, ~~ustidan chizilgan~~, ||spoiler||, `kod`, ```blok```, [matn](https://…) va "> "
//  bilan boshlangan iqtibos qatori. `FromMarkup` uni Telegram'ning HTML qismiga aylantiradi:
//  avval HAMMA matn ekranlanadi, keyin faqat shu belgilar tegga aylanadi — ya'ni natija Telegramga
//  ham, veb-sahifaga ham xavfsiz (atribut yo'q, skript yo'q, faqat http/https havola).
//
//  Teglar muvozanatlanadi: yopilmagani oxirida yopiladi, ochilmagani tashlanadi — aks holda
//  Telegram butun xabarni "can't parse entities" bilan rad etardi.
// ===========================================================================

/// <summary>Telegram HTML qismini tozalash, ekranlash va oddiy matnga aylantirish.</summary>
public static partial class TelegramHtml
{
    private static readonly HashSet<string> Tags = new(StringComparer.Ordinal)
    {
        "b", "strong", "i", "em", "u", "ins", "s", "strike", "del", "code", "pre", "tg-spoiler", "blockquote",
    };

    [GeneratedRegex("""<(/?)(b|strong|i|em|u|ins|s|strike|del|code|pre|tg-spoiler|blockquote)>|<a href="([^"<>\s]+)">|</a>""",
        RegexOptions.IgnoreCase)]
    private static partial Regex TagRegex();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex AnyTag();

    /// <summary>
    /// Ruxsat etilgan teglar qoladi (muvozanatlangan holda), qolgan hammasi ekranlanadi. Natija —
    /// Telegram'ning <c>parse_mode=HTML</c> ga va veb-sahifaga xavfsiz satr.
    /// </summary>
    public static string Sanitize(string? input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        var text = Decode(input);
        var sb = new StringBuilder(text.Length + 16);
        var open = new Stack<string>();
        var last = 0;
        foreach (Match m in TagRegex().Matches(text))
        {
            sb.Append(Escape(text[last..m.Index]));
            last = m.Index + m.Length;

            if (m.Value.Equals("</a>", StringComparison.OrdinalIgnoreCase))
            {
                if (open.Count > 0 && open.Peek() == "a") { sb.Append("</a>"); open.Pop(); }
                continue;
            }
            if (m.Groups[3].Success)
            {
                var href = m.Groups[3].Value;
                if (!href.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    && !href.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) continue;
                sb.Append("<a href=\"").Append(WebUtility.HtmlEncode(href)).Append("\">");
                open.Push("a");
                continue;
            }

            var tag = m.Groups[2].Value.ToLowerInvariant();
            if (!Tags.Contains(tag)) continue;
            if (m.Groups[1].Value == "/")
            {
                if (open.Count > 0 && open.Peek() == tag) { sb.Append("</").Append(tag).Append('>'); open.Pop(); }
                continue;
            }
            sb.Append('<').Append(tag).Append('>');
            open.Push(tag);
        }
        sb.Append(Escape(text[last..]));
        while (open.Count > 0) sb.Append("</").Append(open.Pop()).Append('>');
        return sb.ToString();
    }

    [GeneratedRegex("```(.+?)```|`([^`\n]+)`", RegexOptions.Singleline)]
    private static partial Regex CodeRegex();

    [GeneratedRegex(@"\[([^\]\n]+)\]\((https?://[^\s)]+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex LinkRegex();

    private static readonly (Regex Pattern, string Tag)[] Inline =
    [
        (new Regex(@"\*\*(?=\S)(.+?)(?<=\S)\*\*", RegexOptions.Singleline | RegexOptions.Compiled), "b"),
        (new Regex(@"__(?=\S)(.+?)(?<=\S)__", RegexOptions.Singleline | RegexOptions.Compiled), "i"),
        (new Regex(@"\+\+(?=\S)(.+?)(?<=\S)\+\+", RegexOptions.Singleline | RegexOptions.Compiled), "u"),
        (new Regex(@"~~(?=\S)(.+?)(?<=\S)~~", RegexOptions.Singleline | RegexOptions.Compiled), "s"),
        (new Regex(@"\|\|(?=\S)(.+?)(?<=\S)\|\|", RegexOptions.Singleline | RegexOptions.Compiled), "tg-spoiler"),
    ];

    /// <summary>
    /// Foydalanuvchi yozgan belgilarli matnni (fayl boshidagi ro'yxat) Telegram HTML qismiga
    /// aylantiradi. Belgisiz oddiy matn — shunchaki ekranlangan matn.
    /// </summary>
    public static string FromMarkup(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length + 32);
        var last = 0;
        // Kod ichida formatlash yo'q: avval kod bo'laklari ajratiladi.
        foreach (Match m in CodeRegex().Matches(text))
        {
            sb.Append(Format(text[last..m.Index]));
            sb.Append(m.Groups[1].Success
                ? $"<pre>{Escape(m.Groups[1].Value.Trim('\n'))}</pre>"
                : $"<code>{Escape(m.Groups[2].Value)}</code>");
            last = m.Index + m.Length;
        }
        sb.Append(Format(text[last..]));
        // Tasodifiy noto'g'ri ichma-ich joylashuv ("**a __b** c__") ham muvozanatlanadi.
        return Sanitize(sb.ToString());
    }

    /// <summary>Belgilarli matnning teglarsiz ko'rinishi (Mini App qatori, qisqa matnlar).</summary>
    public static string MarkupToPlain(string? text) => ToPlain(FromMarkup(text));

    private static string Format(string part)
    {
        var s = Escape(part);
        s = LinkRegex().Replace(s, m => $"<a href=\"{m.Groups[2].Value.Replace("\"", "&quot;")}\">{m.Groups[1].Value}</a>");
        foreach (var (pattern, tag) in Inline)
            s = pattern.Replace(s, m => $"<{tag}>{m.Groups[1].Value}</{tag}>");
        // "> " bilan boshlangan ketma-ket qatorlar — bitta iqtibos.
        var lines = s.Split('\n');
        var outLines = new List<string>(lines.Length);
        var quote = new List<string>();
        void Flush()
        {
            if (quote.Count == 0) return;
            outLines.Add($"<blockquote>{string.Join("\n", quote)}</blockquote>");
            quote.Clear();
        }
        foreach (var line in lines)
        {
            if (line.StartsWith("&gt; ", StringComparison.Ordinal)) { quote.Add(line[5..]); continue; }
            Flush();
            outLines.Add(line);
        }
        Flush();
        return string.Join("\n", outLines);
    }

    /// <summary>Matnni HTML ichiga qo'yish uchun ekranlash (o'rinbosar qiymatlari: ism, sinf …).</summary>
    public static string Escape(string? text) =>
        string.IsNullOrEmpty(text) ? "" : text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>Teglarsiz oddiy matn — HTML bilan yuborib bo'lmasa zaxira va qisqa ko'rinishlar uchun.</summary>
    public static string ToPlain(string? html) =>
        string.IsNullOrEmpty(html) ? "" : WebUtility.HtmlDecode(AnyTag().Replace(html, ""));

    /// <summary>
    /// Avval ekranlangan (&amp;lt; …) matn yana ekranlanib ketmasin: bir marta ochiladi, keyin
    /// <see cref="Sanitize"/> o'zi ekranlaydi.
    /// </summary>
    private static string Decode(string s) =>
        s.Contains('&') ? s.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&amp;", "&") : s;
}

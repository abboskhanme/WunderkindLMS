using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  O'quvchilarni Excel'dan IKKI BOSQICHLI import — §2.3 (S-3).
// ===========================================================================
//
//  QOIDA: YARIM IMPORT — ENG YOMON NATIJA
//  --------------------------------------
//  Mavjud bir bosqichli import (`StudentsController.Import`, tegilmagan)
//  to'g'ri qatorlarni yozib, xatolarini ro'yxat qilib qaytaradi. Natijada
//  administratorda YARIM ko'chirilgan fayl qoladi: qaysi bola tushdi, qaysi
//  tushmadi — buni u qo'lda solishtirishi kerak, va faylni qayta yuklasa
//  dublikat paydo bo'ladi.
//
//  Yangi yo'lda bunday holat YO'Q:
//    1) `validate` — BUTUN fayl tekshiriladi, har xato o'z QATOR raqami va
//       USTUN nomi bilan qaytadi, bazaga hech narsa yozilmaydi;
//    2) `commit` — faylni QAYTA tekshiradi va bitta ham xato bo'lsa
//       HECH NARSA yozmaydi (hammasi bitta `SaveChanges` da).
//
//  NEGA FAYL IKKI MARTA YUBORILADI
//  -------------------------------
//  EduSchool yuklangan faylni serverda saqlab, `uploadedFilePath` ni qaytaradi
//  va tasdiqda o'sha yo'lni oladi. Bizda serverda "yarim yuklangan" fayl
//  turmaydi: tasdiqda brauzer aynan o'sha faylni qayta yuboradi va server uni
//  yangidan tekshiradi. Natija xavfsizroq — eskirgan vaqtinchalik fayl ham,
//  yo'l orqali boshqa faylga tegish imkoni ham yo'q.
//
//  BO'SH KATAK — "TEGMA", "O'CHIR" EMAS
//  ------------------------------------
//  Mavjud o'quvchi ustiga yozishda bo'sh katak maydonni TOZALAMAYDI. Aks holda
//  eksport qilingan faylni qaytadan yuklash (round-trip) to'ldirilmagan
//  ustunlarni jimgina o'chirib yuborardi. Majburiy ikkita ustun (F.I.SH,
//  Sinf) esa har doim yoziladi.
// ===========================================================================

/// <summary>Import shabloni: ustunlar va bo'sh kitob.</summary>
public static class StudentImportSheet
{
    /// <summary>
    /// 1-varaq ustunlari. <b>Birinchi sakkiztasi eski shablon bilan AYNAN bir
    /// xil</b> — eski shablonga to'ldirilgan fayl ham yangi import'dan o'tadi
    /// (yo'q ustunlar bo'sh deb qabul qilinadi).
    /// </summary>
    public static readonly string[] Headers =
    {
        "F.I.SH (o'quvchi)*",               // 0
        "Sinf*",                            // 1
        "Tug'ilgan sana (YYYY-MM-DD)",      // 2
        "Jinsi (o'g'il/qiz)",               // 3
        "Manzil",                           // 4
        "Ota-ona F.I.SH",                   // 5
        "Ota-ona telefoni",                 // 6
        "Qabul sanasi (YYYY-MM-DD)",        // 7
        "O'quvchi telefoni",                // 8
        "O'qish tili (uz/ru/en/kaa)",       // 9
        "Holat",                            // 10
    };

    /// <summary>Eksportga qo'shiladigan, import O'QIMAYDIGAN ustunlar (11-dan keyin).</summary>
    public static readonly string[] ExportExtraHeaders =
    {
        "Balans", "Shartnoma raqami", "Arxiv sanasi", "Arxiv sababi",
    };

    public const string SheetName = "O'quvchilar";
    public const string GuideSheetName = "Yo'riqnoma";

    /// <summary>
    /// Bo'sh shablon: 1-varaq — ustunlar, 2-varaq — izohlar, MAVJUD sinflar va
    /// MAVJUD holatlar ro'yxati (import faqat 1-varaqni o'qiydi).
    /// </summary>
    public static byte[] Template(IReadOnlyList<string> classNames, IReadOnlyList<string> statusNames)
    {
        var info = new List<IReadOnlyList<string>>
        {
            new[] { Headers[0], "Majburiy. Masalan: Aliyev Vali Aliyevich" },
            new[] { Headers[1], "Majburiy — pastdagi ro'yxatdagi aniq nom" },
            new[] { Headers[2], "YYYY-MM-DD, masalan 2015-03-21" },
            new[] { Headers[3], "o'g'il yoki qiz" },
            new[] { Headers[4], "ixtiyoriy" },
            new[] { Headers[5], "ixtiyoriy" },
            new[] { Headers[6], "masalan +998901234567" },
            new[] { Headers[7], "YYYY-MM-DD (bo'sh bo'lsa — bugun)" },
            new[] { Headers[8], "o'quvchining O'Z raqami, ixtiyoriy" },
            new[] { Headers[9], "uz | ru | en | kaa (bo'sh bo'lsa — ko'rsatilmagan)" },
            new[] { Headers[10], "pastdagi holatlardan biri (bo'sh bo'lsa — holatsiz)" },
            new[] { "", "" },
            new[] { "Takroriy yuklash", "F.I.SH + tug'ilgan sana + sinf mos kelsa — mavjud o'quvchi YANGILANADI" },
            new[] { "Bo'sh katak", "mavjud o'quvchida maydonni TOZALAMAYDI (o'zgarishsiz qoladi)" },
            new[] { "Chegirma", "Bu yerda EMAS: Moliya → Chegirmalar (direktor tasdiqlaydi)" },
            new[] { "Oylik to'lov", "Bu yerda EMAS: Moliya → Obunalar (toifa va summa)" },
            new[] { "", "" },
            new[] { "Mavjud sinflar:", classNames.Count == 0 ? "(sinf yaratilmagan)" : "" },
        };
        info.AddRange(classNames.Select(c => (IReadOnlyList<string>)new[] { c, "" }));
        info.Add(new[] { "", "" });
        info.Add(new[] { "Mavjud holatlar:", statusNames.Count == 0 ? "(holat qo'shilmagan)" : "" });
        info.AddRange(statusNames.Select(s => (IReadOnlyList<string>)new[] { s, "" }));

        return ExcelExport.Build(new[]
        {
            new ExcelExport.SheetSpec(SheetName, Headers, Array.Empty<IReadOnlyList<string>>()),
            new ExcelExport.SheetSpec(GuideSheetName, new[] { "Maydon", "Izoh" }, info),
        });
    }

    /// <summary>
    /// Sarlavhalarni solishtirish kaliti: harf va raqamdan boshqasi olib
    /// tashlanadi, registr e'tiborga olinmaydi. "Sinf*" va "sinf" — bir xil.
    /// </summary>
    public static string HeaderKey(string? value) =>
        new((value ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}

/// <summary>Tekshiruvdan o'tgan bitta qator — yozishga tayyor.</summary>
/// <param name="Existing">null = yangi o'quvchi; aks holda ustiga yoziladigan mavjud qator.</param>
public sealed record StudentImportItem(
    int Row,
    string FullName,
    string LastName,
    string FirstName,
    string MiddleName,
    string ClassName,
    string? BirthDate,
    string? Gender,
    string? Address,
    string? ParentFullName,
    string? ParentLastName,
    string? ParentFirstName,
    string? ParentMiddleName,
    string? ParentPhone,
    string? EnrollmentDate,
    string? Phone,
    string? Language,
    Guid? StatusId,
    Student? Existing);

/// <summary>Butun faylning tekshiruv natijasi.</summary>
public sealed class StudentImportPlan
{
    public List<ImportCellErrorDto> Errors { get; } = [];
    public List<StudentImportItem> Items { get; } = [];
    public int Skipped { get; set; }

    /// <summary>Faylni umuman o'qib bo'lmadi (buzilgan .xlsx, noto'g'ri sarlavha).</summary>
    public string? FatalMessage { get; set; }

    public bool Ok => FatalMessage is null && Errors.Count == 0;

    public int CreatedCount => Items.Count(i => i.Existing is null);
    public int UpdatedCount => Items.Count(i => i.Existing is not null);

    /// <summary>Eng ko'p shuncha xato qaytariladi — 5000 qatorli buzuq fayl javobni yormasin.</summary>
    public const int MaxErrors = 200;

    /// <summary>Ko'rib chiqish jadvalida ko'rsatiladigan qatorlar soni.</summary>
    public const int PreviewRows = 20;

    public StudentImportPreviewDto ToPreview() => new(
        Ok,
        Items.Count + Errors.Select(e => e.Row).Distinct().Count(),
        CreatedCount,
        UpdatedCount,
        Skipped,
        Errors,
        [.. Items.Take(PreviewRows).Select(i => new StudentImportPreviewRowDto(
            i.Row, i.FullName, i.ClassName, i.Existing is null ? "create" : "update"))],
        FatalMessage);
}

/// <summary>
/// Faylni o'qiydi va TEKSHIRADI. Hech narsa YOZMAYDI — yozish chaqiruvchida
/// (u yerda tizim akkaunti yaratiladi, bu esa Infrastructure qatlami).
/// </summary>
public sealed class StudentImportService(IAppDbContext db)
{
    public const string BadFileMessage =
        "Faylni o'qib bo'lmadi — buzilmagan .xlsx ekanini tekshiring";

    public const string BadHeaderMessage =
        "Ustunlar shablonga mos emas. \"Shablon\" tugmasi orqali yangi shablonni yuklab oling "
        + "va ustun nomlarini o'zgartirmasdan to'ldiring.";

    public const string EmptyFileMessage = "Faylda ma'lumot qatori yo'q";

    /// <summary>Bir faylda eng ko'p shuncha qator — tasodifiy ulkan fayldan himoya.</summary>
    public const int MaxRows = 5000;

    public static readonly string[] Languages = { "uz", "ru", "en", "kaa" };

    public async Task<StudentImportPlan> ValidateAsync(Stream xlsx, CancellationToken ct = default)
    {
        var plan = new StudentImportPlan();

        List<string[]> rows;
        try
        {
            rows = ExcelImport.ReadRows(xlsx, StudentImportSheet.Headers.Length);
        }
        catch
        {
            plan.FatalMessage = BadFileMessage;
            return plan;
        }

        if (rows.Count < 2) { plan.FatalMessage = EmptyFileMessage; return plan; }
        if (rows.Count - 1 > MaxRows)
        {
            plan.FatalMessage = $"Faylda {rows.Count - 1} ta qator — bir martada eng ko'pi {MaxRows} ta";
            return plan;
        }
        if (!HeadersMatch(rows[0])) { plan.FatalMessage = BadHeaderMessage; return plan; }

        // Ma'lumotnomalar bir marta yuklanadi — qator ichida DB so'rovi YO'Q.
        var classByName = (await db.Classes.AsNoTracking().ToListAsync(ct))
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

        var statusByName = (await db.StudentStatuses.AsNoTracking().Where(s => s.IsActive).ToListAsync(ct))
            .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        // Mos keladigan o'quvchilar — FAQAT arxivda bo'lmaganlar: arxivdagi
        // bola ro'yxatdan chiqqan, uni import jimgina "tiriltirmasligi" kerak.
        var existing = await db.Students.Where(s => !s.IsArchived).ToListAsync(ct);
        var existingByKey = existing
            .GroupBy(s => MatchKey(s.FullName, s.BirthDate, s.ClassName), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var seenInFile = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 1; i < rows.Count; i++)
        {
            var r = rows[i];
            var excelRow = i + 1; // Excel 1-asosli; 1-qator — sarlavha
            if (r.All(string.IsNullOrWhiteSpace)) { plan.Skipped++; continue; }
            if (plan.Errors.Count >= StudentImportPlan.MaxErrors) break;

            ReadRow(plan, r, excelRow, classByName, statusByName, existingByKey, seenInFile);
        }

        if (plan.Errors.Count == 0 && plan.Items.Count == 0 && plan.FatalMessage is null)
            plan.FatalMessage = EmptyFileMessage;

        return plan;
    }

    // =====================================================================
    //  Bitta qator
    // =====================================================================

    private static void ReadRow(
        StudentImportPlan plan,
        string[] r,
        int excelRow,
        Dictionary<string, string> classByName,
        Dictionary<string, Guid> statusByName,
        Dictionary<string, List<Student>> existingByKey,
        Dictionary<string, int> seenInFile)
    {
        var before = plan.Errors.Count;
        void Fail(int column, string message) =>
            plan.Errors.Add(new ImportCellErrorDto(excelRow, StudentImportSheet.Headers[column], message));

        var fullName = Collapse(r[0]);
        if (fullName.Length == 0) Fail(0, "F.I.SH bo'sh");
        else if (fullName.Length < 3) Fail(0, "F.I.SH juda qisqa (kamida 3 belgi)");

        var className = Collapse(r[1]);
        var resolvedClass = "";
        if (className.Length == 0) Fail(1, "Sinf bo'sh");
        else if (classByName.TryGetValue(className, out var foundClass)) resolvedClass = foundClass;
        else Fail(1, $"Sinf topilmadi: \"{className}\"");

        var birthDate = Date(r[2]);
        if (birthDate is null && !string.IsNullOrWhiteSpace(r[2]))
            Fail(2, $"Sana tushunarsiz: \"{r[2].Trim()}\" (kutilgani YYYY-MM-DD)");

        var gender = Gender(r[3]);
        if (gender is null && !string.IsNullOrWhiteSpace(r[3]))
            Fail(3, $"Jinsi tushunarsiz: \"{r[3].Trim()}\" (o'g'il yoki qiz)");

        var enrollment = Date(r[7]);
        if (enrollment is null && !string.IsNullOrWhiteSpace(r[7]))
            Fail(7, $"Sana tushunarsiz: \"{r[7].Trim()}\" (kutilgani YYYY-MM-DD)");

        var parentPhone = Phone(r[6]);
        if (parentPhone is null && !string.IsNullOrWhiteSpace(r[6]))
            Fail(6, $"Telefon tushunarsiz: \"{r[6].Trim()}\"");

        var phone = Phone(r[8]);
        if (phone is null && !string.IsNullOrWhiteSpace(r[8]))
            Fail(8, $"Telefon tushunarsiz: \"{r[8].Trim()}\"");

        string? language = null;
        if (!string.IsNullOrWhiteSpace(r[9]))
        {
            var lang = Collapse(r[9]).ToLowerInvariant();
            if (Languages.Contains(lang)) language = lang;
            else Fail(9, $"Til tushunarsiz: \"{lang}\" (uz, ru, en yoki kaa)");
        }

        Guid? statusId = null;
        if (!string.IsNullOrWhiteSpace(r[10]))
        {
            var statusName = Collapse(r[10]);
            if (statusByName.TryGetValue(statusName, out var sid)) statusId = sid;
            else Fail(10, $"Holat topilmadi: \"{statusName}\" (avval \"O'quvchi holatlari\" da qo'shing)");
        }

        if (plan.Errors.Count > before) return;

        // Takror: fayl ichida va bazada.
        var key = MatchKey(fullName, birthDate ?? "", resolvedClass);
        if (seenInFile.TryGetValue(key, out var firstRow))
        {
            Fail(0, $"Shu o'quvchi faylda takrorlanyapti ({firstRow}-qatorda ham bor)");
            return;
        }
        seenInFile[key] = excelRow;

        Student? match = null;
        if (existingByKey.TryGetValue(key, out var candidates))
        {
            if (candidates.Count > 1)
            {
                Fail(0, "Bazada shu nom, sana va sinf bilan bir nechta o'quvchi bor — "
                        + "avval ularni qo'lda tuzating");
                return;
            }
            match = candidates[0];
        }

        var (last, first, middle) = SplitName(fullName);
        var parentFull = Collapse(r[5]);
        var (pLast, pFirst, pMiddle) = SplitName(parentFull);

        plan.Items.Add(new StudentImportItem(
            excelRow, fullName, last, first, middle, resolvedClass,
            birthDate, gender, Collapse(r[4]),
            parentFull.Length == 0 ? null : parentFull,
            parentFull.Length == 0 ? null : pLast,
            parentFull.Length == 0 ? null : pFirst,
            parentFull.Length == 0 ? null : pMiddle,
            parentPhone, enrollment, phone, language, statusId, match));
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private static bool HeadersMatch(string[] header)
    {
        for (var i = 0; i < StudentImportSheet.Headers.Length; i++)
        {
            var actual = StudentImportSheet.HeaderKey(header[i]);
            // Ustun umuman yo'q (eski, 8 ustunli shablon) — qabul qilinadi:
            // yetishmagan ustunlarning kataklari baribir bo'sh bo'ladi.
            if (actual.Length == 0) continue;
            if (actual != StudentImportSheet.HeaderKey(StudentImportSheet.Headers[i])) return false;
        }
        return true;
    }

    /// <summary>Solishtirish kaliti: F.I.SH (bo'shliq va registrsiz) + tug'ilgan sana + sinf.</summary>
    public static string MatchKey(string? fullName, string? birthDate, string? className) =>
        string.Join('',
            Collapse(fullName).ToLowerInvariant(),
            (birthDate ?? "").Trim(),
            Collapse(className).ToLowerInvariant());

    /// <summary>Ortiqcha bo'shliqlarni bittaga siqadi va chetlarini kesadi.</summary>
    public static string Collapse(string? value) =>
        string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>"Aliyev Vali Aliyevich" → (Aliyev, Vali, Aliyevich). Uchtadan ortig'i sharifga qo'shiladi.</summary>
    public static (string Last, string First, string Middle) SplitName(string fullName)
    {
        var parts = Collapse(fullName).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => ("", "", ""),
            1 => (parts[0], "", ""),
            2 => (parts[0], parts[1], ""),
            _ => (parts[0], parts[1], string.Join(' ', parts.Skip(2))),
        };
    }

    private static readonly string[] DateFormats =
    {
        "yyyy-MM-dd", "yyyy/MM/dd", "dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "d/M/yyyy", "MM/dd/yyyy",
    };

    /// <summary>ISO sanaga keltiradi. Bo'sh — null (xato emas); tushunarsiz — null (chaqiruvchi xato yozadi).</summary>
    public static string? Date(string? raw)
    {
        var v = (raw ?? "").Trim();
        if (v.Length == 0) return null;
        if (DateTime.TryParseExact(v, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d.ToString("yyyy-MM-dd");
        // Excel raqamli (OADate) sana.
        if (double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var oa) && oa is > 1 and < 600000)
        {
            try { return DateTime.FromOADate(oa).ToString("yyyy-MM-dd"); } catch { /* e'tiborsiz */ }
        }
        return null;
    }

    /// <summary>
    /// <c>male</c> | <c>female</c>. Bo'sh — null. Tushunarsiz — null:
    /// eski import "hammasini o'g'il" deb yozardi, bu esa JIM xato edi.
    /// </summary>
    public static string? Gender(string? raw)
    {
        var v = (raw ?? "").Trim().ToLowerInvariant().Replace("'", "").Replace("‘", "").Replace("’", "");
        if (v.Length == 0) return null;
        if (v is "qiz" or "female" or "ayol" or "f" or "q" or "2") return "female";
        if (v is "ogil" or "o'g'il" or "male" or "erkak" or "m" or "o" or "1") return "male";
        return null;
    }

    /// <summary>
    /// Telefon: faqat raqam, bo'shliq, <c>+</c>, <c>-</c>, qavs. Mahalliy qismi
    /// kamida 7 raqam bo'lishi kerak. Saqlanadigan shakl — kiritilganicha
    /// (siqilgan), chunki mavjud qatorlar ham shunday saqlangan.
    /// </summary>
    public static string? Phone(string? raw)
    {
        var v = Collapse(raw);
        if (v.Length == 0) return null;
        if (v.Any(ch => !char.IsDigit(ch) && ch is not ('+' or '-' or '(' or ')' or ' '))) return null;
        return PhoneUtil.DigitsOnly(v).Length >= 7 ? v : null;
    }
}

namespace SchoolLms.Server.Controllers;

/// <summary>
/// CVs of job applicants — personal data. Stored under <c>uploads/private/recruitment/</c> (the
/// persistent volume), which Program.cs never serves statically; read back only through the gated
/// admin endpoint.
/// </summary>
internal static class RecruitmentFiles
{
    public const long MaxBytes = 10_000_000;
    private const string Folder = "private/recruitment/";

    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
    };

    public static string? Validate(IFormFile? file)
    {
        if (file is null || file.Length == 0) return "Fayl bo'sh";
        if (file.Length > MaxBytes) return "Fayl 10 MB dan katta";
        return Types.ContainsKey(Path.GetExtension(file.FileName)) ? null : "CV — PDF, Word yoki rasm (JPG, PNG) bo'lsin";
    }

    private static string Root(IWebHostEnvironment env) => Path.Combine(env.ContentRootPath, "uploads");

    public static async Task<string> SaveAsync(IWebHostEnvironment env, IFormFile file, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var relative = $"{Folder}{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{Path.GetExtension(file.FileName).ToLowerInvariant()}";
        var full = Path.Combine(Root(env), relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using var fs = File.Create(full);
        await file.CopyToAsync(fs, ct);
        return relative;
    }

    /// <summary>The file on disk — only for paths this class wrote.</summary>
    public static string? FullPath(IWebHostEnvironment env, string? relative)
    {
        if (relative is null || !relative.StartsWith(Folder, StringComparison.Ordinal) || relative.Contains("..", StringComparison.Ordinal))
            return null;
        var full = Path.Combine(Root(env), relative.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(full) ? full : null;
    }

    public static void Delete(IWebHostEnvironment env, string? relative)
    {
        if (FullPath(env, relative) is { } full) File.Delete(full);
    }

    public static string ContentType(string path) =>
        Types.TryGetValue(Path.GetExtension(path), out var type) ? type : "application/octet-stream";
}

using System.Globalization;
using System.Text.RegularExpressions;

namespace SchoolLms.Domain;

// ===========================================================================
//  HR topshiriqlari — staff task management (docs/modules/hr-tasks.md).
//  A 1:1 port of the retired WunderkindHR app (FastAPI). Its rules are the
//  reference: domain/entities.py, recurrence.py, reminders.py, enums.py,
//  value_objects.py, exceptions.py. Texts, codes and numbers are the old ones.
//
//  Enum-like columns are varchar + CHECK (no native PG enums), values lowercase
//  exactly as the old API — they are part of the JSON contract
//  (schoollms.client/src/types/hrTasks.ts).
// ===========================================================================

/// <summary>Who may use the module and the permission key a staff role carries.</summary>
public static class HrTaskAccess
{
    /// <summary>
    /// Admin-panel permission key. A <see cref="Roles.Staff"/> account with it opens the HR
    /// panel (scope from <see cref="HrEmployee.HrRole"/>); admin/superadmin always pass.
    /// </summary>
    public const string PermissionKey = "hrTasks";
}

/// <summary>A rule violation: HTTP status + stable machine code + Uzbek message (old exceptions.py).</summary>
public sealed class HrTasksException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

/// <summary>The old domain exceptions, one factory per class (same code, same Uzbek detail).</summary>
public static class HrErrors
{
    public static HrTasksException EmployeeNotFound() => new(404, "hr_employee_not_found", "Xodim topilmadi");
    public static HrTasksException PositionNotFound() => new(404, "hr_position_not_found", "Lavozim topilmadi");
    public static HrTasksException ManagerNotFound() => new(404, "hr_manager_not_found", "Rahbar qilib ko'rsatilgan xodim topilmadi");
    public static HrTasksException TemplateNotFound() => new(404, "hr_template_not_found", "Takroriy topshiriq shabloni topilmadi");
    public static HrTasksException TaskNotFound() => new(404, "hr_task_not_found", "Topshiriq topilmadi");
    public static HrTasksException AttachmentNotFound() => new(404, "hr_attachment_not_found", "Biriktirilgan fayl topilmadi");
    public static HrTasksException UserNotFound() => new(404, "hr_user_not_found", "Foydalanuvchi topilmadi");

    /// <summary>Our identity rule (hr-tasks.md §6): an authenticated user without an active HR row.</summary>
    public static HrTasksException NotEmployee() =>
        new(403, "hr_not_employee", "Siz ro'yxatda yo'qsiz — direktorga murojaat qiling");
    public static HrTasksException TaskNotOwned() => new(403, "hr_task_not_owned", "Bu topshiriq sizniki emas");
    public static HrTasksException PanelAccessForbidden() =>
        new(403, "hr_panel_access_forbidden", "Xodimga panelga kirish huquqini berish uchun ruxsatingiz yo'q");
    public static HrTasksException DirectorOnly() =>
        new(403, "forbidden", "Bu amal faqat direktor uchun");

    public static HrTasksException ManagerCycle() =>
        new(409, "hr_manager_cycle", "Bunday rahbar biriktirib bo'lmaydi: bo'ysunishda halqa hosil bo'ladi");
    public static HrTasksException PhoneAlreadyUsed() =>
        new(409, "hr_phone_already_used", "Bu telefon raqami boshqa xodimga biriktirilgan");
    public static HrTasksException UserAlreadyEmployee() =>
        new(409, "hr_user_already_employee", "Bu foydalanuvchi allaqachon xodimlar ro'yxatida");
    public static HrTasksException LoginTaken() =>
        new(409, "hr_login_taken", "Bu telefon raqami bilan foydalanuvchi allaqachon bor — mavjud foydalanuvchini tanlang");
    public static HrTasksException EmployeeInactive() =>
        new(409, "hr_employee_inactive", "Xodim faol emas — avval uni qayta faollashtiring");
    public static HrTasksException PositionInUse() =>
        new(409, "hr_position_in_use", "Bu lavozimga xodim yoki shablon bog'langan — avval ularni ko'chiring");
    public static HrTasksException PositionNameTaken() => new(409, "conflict", "Bu nomli lavozim allaqachon mavjud");
    public static HrTasksException ProofRequired() =>
        new(409, "hr_proof_required", "Isbot kerak: fayl biriktiring yoki nima qilganingizni izohda yozing");
    public static HrTasksException ProofLimitExceeded(int limit) =>
        new(409, "hr_proof_limit_exceeded", $"Bitta topshiriqqa ko'pi bilan {limit} ta isbot qo'shish mumkin");
    public static HrTasksException TaskNotEditable(string action = "") =>
        new(409, "hr_task_not_editable",
            "Bu holatdagi topshiriqni o'zgartirib bo'lmaydi" + (action.Length > 0 ? $" ({action})" : ""));
    public static HrTasksException UserAlreadyLinked() =>
        new(409, "user_already_linked", "Bu foydalanuvchiga Telegram akkaunti allaqachon bog'langan. Avval uni uzing.");

    public static HrTasksException Validation(string message) => new(422, "validation_error", message);
    public static HrTasksException InvalidTemplate(string message) => new(422, "hr_invalid_template", message);
    public static HrTasksException InvalidRecurrence(string message) => new(422, "hr_invalid_recurrence", message);
    public static HrTasksException InvalidAmount(string message) => new(422, "hr_invalid_amount", message);
    public static HrTasksException BatchTooLarge(int limit, int requested) =>
        new(422, "hr_batch_too_large", $"Bir so'rovda ko'pi bilan {limit} ta bo'lishi mumkin (so'ralgani: {requested})");
    public static HrTasksException UnsupportedProofType() =>
        new(422, "hr_unsupported_proof_type",
            "Bu turdagi faylni isbot qilib bo'lmaydi (HTML, SVG yoki skript). "
            + "Boshqa formatda yuboring — rasm, hujjat, jadval, arxiv, ovoz yoki video.");
    public static HrTasksException ProofTooLarge(long maxBytes) =>
        new(422, "hr_proof_too_large", $"Fayl hajmi {maxBytes / (1024 * 1024)} MB dan oshmasligi kerak");
    /// <summary>413 at the HTTP edge (old router.py AttachmentPayloadTooLarge).</summary>
    public static HrTasksException AttachmentTooLarge(long maxBytes) =>
        new(413, "hr_attachment_too_large", $"Biriktirilgan fayl hajmi {maxBytes / (1024 * 1024)} MB dan oshmasligi kerak");
    /// <summary>415 at the HTTP edge (old router.py AttachmentTypeNotAccepted).</summary>
    public static HrTasksException UnsupportedAttachmentType() =>
        new(415, "hr_unsupported_attachment_type",
            "Bu turdagi faylni topshiriqqa biriktirib bo'lmaydi (HTML, SVG yoki skript). "
            + "Boshqa formatda yuboring — ovoz, rasm, hujjat yoki video.");
}

// ---------------------------------------------------------------------------
//  Enum values (old enums.py) — lowercase strings, part of the API contract.
// ---------------------------------------------------------------------------

/// <summary><see cref="HrEmployee.HrRole"/>: the HR scope of a person.</summary>
public static class HrRoles
{
    /// <summary>Own tasks only.</summary>
    public const string Employee = "employee";
    /// <summary>Self + the whole subtree (<c>hr_employee_managers</c>, recursive).</summary>
    public const string Manager = "manager";
    /// <summary>Whole organisation.</summary>
    public const string Director = "director";
    public static readonly string[] All = [Employee, Manager, Director];
    public static bool IsValid(string? value) => value is Employee or Manager or Director;
}

public static class HrTaskStatuses
{
    public const string Open = "open";
    public const string Done = "done";
    /// <summary>A director returned a done task — works as open for the employee.</summary>
    public const string Returned = "returned";
    /// <summary>Only legacy <c>overdue_policy = close</c> rows (08.09.2026 rule).</summary>
    public const string Missed = "missed";
    public const string Cancelled = "cancelled";
    public static readonly string[] All = [Open, Done, Returned, Missed, Cancelled];
    public static readonly string[] Actionable = [Open, Returned];
    public static bool IsValid(string? v) => v is Open or Done or Returned or Missed or Cancelled;
    /// <summary>The employee may mark it now (<c>open</c>/<c>returned</c>).</summary>
    public static bool IsActionable(string s) => s is Open or Returned;
    /// <summary>No transition leaves these.</summary>
    public static bool IsFinal(string s) => s is Missed or Cancelled;
}

public static class HrTaskKinds
{
    public const string Check = "check";
    public const string Numeric = "numeric";
    public static bool IsValid(string? v) => v is Check or Numeric;
}

public static class HrTaskSources
{
    public const string Director = "director";
    public const string Self = "self";
    public const string Template = "template";
    public static readonly string[] All = [Director, Self, Template];
    public static bool IsValid(string? v) => v is Director or Self or Template;
}

public static class HrRecurrences
{
    public const string Daily = "daily";
    public const string Weekly = "weekly";
    public const string Monthly = "monthly";
    public static bool IsValid(string? v) => v is Daily or Weekly or Monthly;
}

public static class HrTargetTypes
{
    public const string Employee = "employee";
    public const string Position = "position";
    public static bool IsValid(string? v) => v is Employee or Position;
}

public static class HrOverduePolicies
{
    /// <summary>Legacy: closed as <c>missed</c> at <c>close_overdue_time</c>.</summary>
    public const string Close = "close";
    /// <summary>The only policy for new rows since 08.09.2026.</summary>
    public const string KeepOpen = "keep_open";
}

public static class HrProofKinds
{
    public const string Photo = "photo";
    public const string File = "file";
    public const string Link = "link";
    public const string Text = "text";
}

public static class HrAttachmentKinds
{
    public const string Voice = "voice";
    public const string File = "file";
    public static bool IsValid(string? v) => v is Voice or File;
}

public static class HrBranches
{
    public const string Center = "center";
    public const string School = "school";
    public static bool IsValid(string? v) => v is Center or School;
}

/// <summary><c>hr_job_runs.job_name</c> values — one run per job per local day.</summary>
public static class HrJobs
{
    public const string Generate = "generate";
    public const string Morning = "morning";
    public const string Digest = "digest";
    public const string CloseOverdue = "close_overdue";
    public const string CleanDrafts = "clean_drafts";
    public const string StatusOk = "ok";
    public const string StatusFailed = "failed";
}

/// <summary>Limits (old dto.py / value_objects.py / enums.py) — one place.</summary>
public static class HrLimits
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;
    public const int MaxProofsPerTask = 5;
    public const long MaxProofBytes = 10 * 1024 * 1024;
    public const long MaxAttachmentBytes = 10 * 1024 * 1024;
    public const int MaxVoiceSeconds = 300;
    public const int MaxBatchEmployees = 100;
    public const int MaxBatchItems = 20;
    public const int MaxBatchTotal = 500;
    public const int MaxManagers = 20;
    public const int MaxEventTitles = 5;
    public const int ReminderOffsetMin = 5;
    public const int ReminderOffsetMax = 1440;
    public const int MaxReminderOffsets = 5;
    public const int DraftTtlHours = 24;

    public const int PositionNameMax = 120;
    public const int EmployeeNameMax = 160;
    public const int TitleMax = 200;
    public const int UnitMax = 32;
    public const int NoteMax = 4000;
    public const int CommentMax = 4000;
    public const int DescriptionMax = 4000;
    public const int ProgressNoteMax = 200;

    /// <summary>
    /// Types a browser executes — refused for proofs and attachments (stored XSS: files are
    /// served from the panel's own origin). Everything else is allowed (old BLOCKED_PROOF_TYPES).
    /// </summary>
    public static readonly HashSet<string> BlockedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/html", "application/xhtml+xml", "image/svg+xml", "text/xml", "application/xml",
        "text/javascript", "application/javascript", "application/x-javascript", "application/xhtml",
    };

    /// <summary>
    /// Extensions refused regardless of the declared type: <c>/uploads</c> is served by
    /// extension, so an <c>.html</c> sent as <c>image/png</c> would still run in the browser.
    /// </summary>
    public static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".htm", ".xhtml", ".shtml", ".svg", ".svgz", ".js", ".mjs", ".cjs",
        ".xml", ".xsl", ".xslt", ".php", ".asp", ".aspx", ".jsp",
    };
}

// ---------------------------------------------------------------------------
//  Value helpers (old value_objects.py)
// ---------------------------------------------------------------------------

public static partial class HrValues
{
    public static readonly decimal MaxAmount = 9_999_999_999.99m;
    /// <summary>A task without a due time ends at 23:59 local (old END_OF_DAY).</summary>
    public static readonly TimeOnly EndOfDay = new(23, 59);

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>Trim; blank → null; longer than <paramref name="max"/> → 422 (never truncated silently).</summary>
    public static string? CleanText(string? raw, string label, int? max = null, bool required = false)
    {
        var value = (raw ?? "").Trim();
        if (value.Length == 0)
        {
            if (required) throw HrErrors.Validation($"{label} bo'sh bo'lishi mumkin emas");
            return null;
        }
        if (max is { } m && value.Length > m) throw HrErrors.Validation($"{label} {m} belgidan oshmasligi kerak");
        return value;
    }

    public static string RequireText(string? raw, string label, int max) => CleanText(raw, label, max, required: true)!;

    /// <summary>Required name; inner whitespace collapsed ("Marketolog " == "Marketolog").</summary>
    public static string CleanName(string? raw, string label, int max) => Spaces().Replace(RequireText(raw, label, max), " ");

    /// <summary><c>numeric(12,2)</c>: 2 decimals half-up, |x| ≤ 9 999 999 999.99.</summary>
    public static decimal Quantize(decimal value)
    {
        var result = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        if (Math.Abs(result) > MaxAmount) throw HrErrors.InvalidAmount($"Miqdor {MaxAmount.ToString(CultureInfo.InvariantCulture)} dan oshmasligi kerak");
        return result;
    }

    /// <summary>A KPI step: not zero; negative allowed (a correction).</summary>
    public static decimal StepAmount(decimal value)
    {
        var amount = Quantize(value);
        if (amount == 0) throw HrErrors.InvalidAmount("Miqdor nol bo'lishi mumkin emas");
        return amount;
    }

    /// <summary><c>kind</c> ↔ <c>target_amount</c>/<c>unit</c>: numeric needs a positive target; check has neither.</summary>
    public static (decimal? Target, string? Unit) KindAmount(string kind, decimal? target, string? unit)
    {
        if (kind == HrTaskKinds.Numeric)
        {
            if (target is null) throw HrErrors.InvalidAmount("Raqamli (KPI) topshiriq uchun maqsad miqdori kerak");
            var amount = Quantize(target.Value);
            if (amount <= 0) throw HrErrors.InvalidAmount("Maqsad miqdori noldan katta bo'lishi kerak");
            return (amount, CleanText(unit, "O'lchov birligi", HrLimits.UnitMax));
        }
        if (target is not null) throw HrErrors.InvalidAmount("«Belgilash» turdagi topshiriqda maqsad miqdori bo'lmaydi");
        if (CleanText(unit, "O'lchov birligi", HrLimits.UnitMax) is not null)
            throw HrErrors.InvalidAmount("«Belgilash» turdagi topshiriqda o'lchov birligi bo'lmaydi");
        return (null, null);
    }

    /// <summary>
    /// Uzbek phone → canonical <c>+998XXXXXXXXX</c> (old <c>normalize_uz_phone</c>); null if not a
    /// valid Uzbek number. Accepts <c>+998 90 123 45 67</c>, <c>998901234567</c>, <c>901234567</c>.
    /// </summary>
    public static string? NormalizePhone(string? raw)
    {
        var digits = new string((raw ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length == 9) digits = "998" + digits;
        if (digits.Length != 12 || !digits.StartsWith("998", StringComparison.Ordinal)) return null;
        return "+" + digits;
    }

    public static string RequirePhone(string? raw) =>
        NormalizePhone(raw) ?? throw HrErrors.Validation($"Telefon raqami noto'g'ri: '{raw}'");

    /// <summary><c>+998901234567</c> → <c>+998 90 123 45 67</c> (how <c>users.phone</c> is stored).</summary>
    public static string DisplayPhone(string canonical)
    {
        var d = canonical.TrimStart('+');
        return d.Length == 12 ? $"+{d[..3]} {d[3..5]} {d[5..8]} {d[8..10]} {d[10..]}" : canonical;
    }

    /// <summary>Resolves the settings time zone; unknown name → Tashkent (+5), never an exception.</summary>
    public static TimeZoneInfo Zone(string? id)
    {
        foreach (var candidate in new[] { id, "Asia/Tashkent", "West Asia Standard Time" })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            try { return TimeZoneInfo.FindSystemTimeZoneById(candidate); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("UZT+5", TimeSpan.FromHours(5), "UZT", "UZT");
    }

    public static bool IsKnownZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        try { TimeZoneInfo.FindSystemTimeZoneById(id); return true; }
        catch (TimeZoneNotFoundException) { return id == "Asia/Tashkent"; }
        catch (InvalidTimeZoneException) { return false; }
    }

    /// <summary>Wall-clock time of an instant in <paramref name="tz"/>.</summary>
    public static DateTime Local(DateTimeOffset instant, TimeZoneInfo tz) =>
        TimeZoneInfo.ConvertTimeFromUtc(instant.UtcDateTime, tz);

    /// <summary>"Today" is always the LOCAL date (old §15.8).</summary>
    public static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo tz) => DateOnly.FromDateTime(Local(instant, tz));

    /// <summary>The instant of local <paramref name="day"/> + <paramref name="time"/> (UTC offset 0 for Npgsql).</summary>
    public static DateTimeOffset At(DateOnly day, TimeOnly time, TimeZoneInfo tz)
    {
        var local = day.ToDateTime(time, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, tz), TimeSpan.Zero);
    }

    /// <summary>Start of the local day as an instant.</summary>
    public static DateTimeOffset DayStart(DateOnly day, TimeZoneInfo tz) => At(day, TimeOnly.MinValue, tz);
}

// ---------------------------------------------------------------------------
//  Recurrence — pure functions (old recurrence.py). ISO weekdays: 1 = Monday … 7 = Sunday.
// ---------------------------------------------------------------------------

public static class HrRecurrenceRules
{
    public static int IsoWeekday(DateOnly day) => day.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)day.DayOfWeek;

    public static int[] NormalizeDays(IEnumerable<int>? values) =>
        [.. (values ?? []).Distinct().OrderBy(v => v)];

    /// <summary>
    /// Invariants of old §3.3 + normalisation: returns (weekdays, daysOfMonth) with the one that
    /// does not belong to the recurrence emptied (no "ghost" schedule after switching the type).
    /// </summary>
    public static (int[] Weekdays, int[] DaysOfMonth) Validate(string recurrence, IEnumerable<int>? weekdays, IEnumerable<int>? daysOfMonth)
    {
        if (!HrRecurrences.IsValid(recurrence)) throw HrErrors.InvalidRecurrence("Takroriylik turi noto'g'ri");
        if (recurrence == HrRecurrences.Monthly)
        {
            var days = NormalizeDays(daysOfMonth);
            if (days.Length == 0) throw HrErrors.InvalidRecurrence("Oylik takroriylik uchun kamida bitta sana tanlang");
            foreach (var d in days)
                if (d is < 1 or > 31) throw HrErrors.InvalidRecurrence($"Oy kuni 1 dan 31 gacha bo'lishi kerak (kiritilgan: {d})");
            return ([], days);
        }
        var week = NormalizeDays(weekdays);
        if (week.Length == 0) throw HrErrors.InvalidRecurrence("Kamida bitta hafta kunini tanlang");
        foreach (var d in week)
            if (d is < 1 or > 7) throw HrErrors.InvalidRecurrence($"Hafta kuni 1 dan 7 gacha bo'lishi kerak (kiritilgan: {d})");
        return (week, []);
    }

    /// <summary>
    /// Is an instance due on <paramref name="day"/>? Short-month rule: a day of month that does
    /// not exist (31 in April) fires on the LAST day of the month, never skipped.
    /// </summary>
    public static bool OccursOn(string recurrence, IEnumerable<int>? weekdays, IEnumerable<int>? daysOfMonth, DateOnly day)
    {
        if (recurrence == HrRecurrences.Monthly)
        {
            var wanted = NormalizeDays(daysOfMonth);
            if (wanted.Length == 0) return false;
            if (wanted.Contains(day.Day)) return true;
            var last = DateTime.DaysInMonth(day.Year, day.Month);
            return day.Day == last && wanted.Any(v => v > last);
        }
        return NormalizeDays(weekdays).Contains(IsoWeekday(day));
    }

    /// <summary>
    /// Every date of the MONTH of <paramref name="day"/> for a monthly template, ascending
    /// ({14, 28} → two). A missing day is clamped to the month's last day; duplicates collapse.
    /// </summary>
    public static DateOnly[] MonthlyOccurrences(IEnumerable<int> daysOfMonth, DateOnly day)
    {
        var last = DateTime.DaysInMonth(day.Year, day.Month);
        return [.. daysOfMonth.Where(v => v >= 1)
            .Select(v => new DateOnly(day.Year, day.Month, Math.Min(v, last)))
            .Distinct().OrderBy(d => d)];
    }

    /// <summary>Period start: the day itself (daily/weekly) or the first of the month (monthly).</summary>
    public static DateOnly PeriodStartFor(string recurrence, DateOnly day) =>
        recurrence == HrRecurrences.Monthly ? new DateOnly(day.Year, day.Month, 1) : day;

    /// <summary>Due instant: the local day + due time (23:59 when none).</summary>
    public static DateTimeOffset DueAtFor(DateOnly day, TimeOnly? dueTime, TimeZoneInfo tz) =>
        HrValues.At(day, dueTime ?? HrValues.EndOfDay, tz);
}

// ---------------------------------------------------------------------------
//  Deadline reminders — pure functions (old reminders.py).
// ---------------------------------------------------------------------------

public static class HrReminderRules
{
    /// <summary>
    /// Offsets to send now, LARGEST first: <c>due − offset ≤ now &lt; due</c>. Past-due → none;
    /// non-positive offsets ignored. A late tick may return several (the caller sends one).
    /// </summary>
    public static int[] DueOffsets(DateTimeOffset dueAt, DateTimeOffset now, IEnumerable<int> offsets)
    {
        if (now >= dueAt) return [];
        return [.. offsets.Where(o => o > 0 && dueAt.AddMinutes(-o) <= now).Distinct().OrderByDescending(o => o)];
    }

    /// <summary>No explicit due time — local 23:59 (skipped unless <c>remind_end_of_day</c>).</summary>
    public static bool IsEndOfDay(DateTimeOffset dueAt, TimeZoneInfo tz)
    {
        var local = HrValues.Local(dueAt, tz);
        return local.Hour == HrValues.EndOfDay.Hour && local.Minute == HrValues.EndOfDay.Minute;
    }
}

// ---------------------------------------------------------------------------
//  Entities (tables in hr-tasks.md §4, old models.py with the hr_ prefix).
//  Ids are string GUIDs; migrated rows keep their old UUID.
// ---------------------------------------------------------------------------

/// <summary>A position — a label and a template target, NOT a permission.</summary>
public class HrPosition
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// An HR employee — always one of our users (1:1 on <see cref="UserId"/>). Never deleted:
/// firing = <see cref="IsActive"/> false (history stays). Telegram comes from the user's
/// <c>telegram_accounts</c> row, not from a column here (hr-tasks.md §2).
/// </summary>
public class HrEmployee
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    /// <summary><see cref="HrRoles"/>.</summary>
    public string HrRole { get; set; } = HrRoles.Employee;
    public string FullName { get; set; } = string.Empty;
    /// <summary>
    /// Canonical <c>+998901234567</c>, unique when present. Null for people added from the LMS
    /// lists without a phone (every teacher/staff member is an HR employee — client,
    /// 2026-09-28); Telegram goes by <see cref="UserId"/>, so the phone is only a label.
    /// </summary>
    public string? Phone { get; set; }
    public string? PositionId { get; set; }
    /// <summary><see cref="HrBranches"/> or null — a label only (filter/report), never a permission.</summary>
    public string? Branch { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Reporting edge: <see cref="EmployeeId"/> reports to <see cref="ManagerId"/> (many managers allowed).</summary>
public class HrEmployeeManager
{
    public string EmployeeId { get; set; } = string.Empty;
    public string ManagerId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>A recurring task RULE — instances are snapshots of it (old §3.3).</summary>
public class HrTaskTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string TargetType { get; set; } = HrTargetTypes.Employee;
    public string? TargetEmployeeId { get; set; }
    public string? TargetPositionId { get; set; }
    public string Recurrence { get; set; } = HrRecurrences.Daily;
    public short[] Weekdays { get; set; } = [];
    public short[] DaysOfMonth { get; set; } = [];
    public TimeOnly? DueTime { get; set; }
    public string Kind { get; set; } = HrTaskKinds.Check;
    public decimal? TargetAmount { get; set; }
    public string? Unit { get; set; }
    public bool ProofRequired { get; set; } = true;
    /// <summary>Always <c>keep_open</c> for new rows; <c>close</c> only on legacy rows.</summary>
    public string OverduePolicy { get; set; } = HrOverduePolicies.KeepOpen;
    public bool IsActive { get; set; } = true;
    /// <summary>users.id of the author (no FK: the rule outlives the account).</summary>
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public int[] WeekdayList => [.. Weekdays.Select(x => (int)x)];
    public int[] DayOfMonthList => [.. DaysOfMonth.Select(x => (int)x)];
}

/// <summary>A task instance — the aggregate; every state transition lives here (old §5.1).</summary>
public class HrTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string EmployeeId { get; set; } = string.Empty;
    /// <summary>null — one-off task, or the template was deleted (history stays).</summary>
    public string? TemplateId { get; set; }
    public string Source { get; set; } = HrTaskSources.Director;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Kind { get; set; } = HrTaskKinds.Check;
    public decimal? TargetAmount { get; set; }
    /// <summary>Sum of <c>hr_task_progress</c>, updated in the same transaction.</summary>
    public decimal CurrentAmount { get; set; }
    public string? Unit { get; set; }
    public bool ProofRequired { get; set; } = true;
    public string OverduePolicy { get; set; } = HrOverduePolicies.KeepOpen;
    public DateOnly PeriodStart { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public string Status { get; set; } = HrTaskStatuses.Open;
    public bool IsLate { get; set; }
    public string? EmployeeComment { get; set; }
    public string? DirectorComment { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? ReturnedAt { get; set; }
    /// <summary>"Ko'rdim" — NOT a status; filled on completion too.</summary>
    public DateTimeOffset? AcceptedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsOpen => HrTaskStatuses.IsActionable(Status);
    public bool IsFinal => HrTaskStatuses.IsFinal(Status);
    public bool IsOverdue(DateTimeOffset now) => IsOpen && DueAt < now;

    /// <summary>A hand-given task (source=director), not yet acknowledged, still open.</summary>
    public bool NeedsAcceptance => Source == HrTaskSources.Director && AcceptedAt is null && IsOpen;

    /// <summary>"Bajardim": proof = at least one file OR a non-empty comment. False if already done.</summary>
    public bool Complete(DateTimeOffset now, string? comment, int proofCount)
    {
        EnsureNotFinal("complete");
        if (Status == HrTaskStatuses.Done) return false;
        if (proofCount <= 0 && string.IsNullOrWhiteSpace(comment)) throw HrErrors.ProofRequired();
        MarkDone(now, comment);
        return true;
    }

    /// <summary>Undo a mistaken "done". The same-day / before-digest window is the caller's rule.</summary>
    public bool Reopen(DateTimeOffset now)
    {
        EnsureNotFinal("reopen");
        if (Status != HrTaskStatuses.Done) return false;
        Status = HrTaskStatuses.Open;
        CompletedAt = null;
        IsLate = false;
        UpdatedAt = now;
        return true;
    }

    /// <summary>A director returns a DONE task (completed_at is kept for history).</summary>
    public bool MarkReturned(DateTimeOffset now, string? comment)
    {
        EnsureNotFinal("return");
        if (Status == HrTaskStatuses.Returned) return false;
        if (Status != HrTaskStatuses.Done) throw HrErrors.TaskNotEditable("return");
        Status = HrTaskStatuses.Returned;
        ReturnedAt = now;
        DirectorComment = HrValues.CleanText(comment, "Izoh", HrLimits.CommentMax);
        UpdatedAt = now;
        return true;
    }

    /// <summary>Legacy <c>close</c> policy only; anything else is a no-op (false), never an error.</summary>
    public bool MarkMissed(DateTimeOffset now)
    {
        if (Status == HrTaskStatuses.Missed) return false;
        if (IsFinal || Status == HrTaskStatuses.Done) throw HrErrors.TaskNotEditable("miss");
        if (OverduePolicy != HrOverduePolicies.Close) return false;
        if (now <= DueAt) return false;
        Status = HrTaskStatuses.Missed;
        UpdatedAt = now;
        return true;
    }

    public bool Cancel(DateTimeOffset now, string? reason = null)
    {
        if (Status == HrTaskStatuses.Cancelled) return false;
        if (!IsOpen) throw HrErrors.TaskNotEditable("cancel");
        Status = HrTaskStatuses.Cancelled;
        if (!string.IsNullOrWhiteSpace(reason)) DirectorComment = HrValues.CleanText(reason, "Izoh", HrLimits.CommentMax);
        UpdatedAt = now;
        return true;
    }

    /// <summary>
    /// A KPI step (numeric only; zero refused, negative allowed). Reaching the target marks the
    /// task DONE — the accumulated steps are the proof.
    /// </summary>
    public void AddAmount(DateTimeOffset now, decimal amount)
    {
        EnsureNotFinal("progress");
        if (Kind != HrTaskKinds.Numeric) throw HrErrors.InvalidAmount("Bu topshiriq raqamli (KPI) emas — miqdor qo'shib bo'lmaydi");
        var step = HrValues.StepAmount(amount);
        CurrentAmount += step;
        UpdatedAt = now;
        if (TargetAmount is { } target && CurrentAmount >= target && IsOpen) MarkDone(now, null);
    }

    /// <summary>"Ko'rdim". False the second time (the first time is kept).</summary>
    public bool Accept(DateTimeOffset now)
    {
        EnsureNotFinal("accept");
        if (AcceptedAt is not null) return false;
        AcceptedAt = now;
        UpdatedAt = now;
        return true;
    }

    public bool UpdateDetails(DateTimeOffset now, string? title, bool setDescription, string? description, DateTimeOffset? dueAt)
    {
        EnsureNotFinal("edit");
        var changed = false;
        if (title is not null)
        {
            var cleaned = HrValues.RequireText(title, "Sarlavha", HrLimits.TitleMax);
            if (cleaned != Title) { Title = cleaned; changed = true; }
        }
        if (setDescription)
        {
            var cleaned = HrValues.CleanText(description, "Tavsif", HrLimits.DescriptionMax);
            if (cleaned != Description) { Description = cleaned; changed = true; }
        }
        if (dueAt is { } due && due != DueAt) { DueAt = due; changed = true; }
        if (changed) UpdatedAt = now;
        return changed;
    }

    private void MarkDone(DateTimeOffset now, string? comment)
    {
        Status = HrTaskStatuses.Done;
        CompletedAt = now;
        AcceptedAt ??= now;
        // lateness is fixed at this moment: a later due_at edit does not rewrite history
        IsLate = now > DueAt;
        if (comment is not null) EmployeeComment = HrValues.CleanText(comment, "Izoh", HrLimits.CommentMax);
        UpdatedAt = now;
    }

    private void EnsureNotFinal(string action)
    {
        if (IsFinal) throw HrErrors.TaskNotEditable(action);
    }
}

/// <summary>An employee's proof: photo/file (or legacy link/text).</summary>
public class HrTaskProof
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TaskId { get; set; } = string.Empty;
    public string Kind { get; set; } = HrProofKinds.File;
    /// <summary>Relative to <c>uploads/</c>: <c>hr/proofs/yyyy/MM/&lt;guid&gt;.ext</c>.</summary>
    public string? FilePath { get; set; }
    public string? OriginalName { get; set; }
    public string? ContentType { get; set; }
    public int? SizeBytes { get; set; }
    public string? Url { get; set; }
    public string? Text { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>A KPI step. Never deleted — a correction is a negative step.</summary>
public class HrTaskProgress
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TaskId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// A manager's file on a task (usually a voice note). <see cref="TaskId"/> null = DRAFT (uploaded
/// before the tasks exist); binding copies the row per task and shares the file.
/// </summary>
public class HrTaskAttachment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? TaskId { get; set; }
    public string Kind { get; set; } = HrAttachmentKinds.Voice;
    public string FilePath { get; set; } = string.Empty;
    public string? OriginalName { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public int SizeBytes { get; set; }
    public int? DurationSeconds { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>A sent reminder — <c>UNIQUE (task_id, offset_minutes)</c> is the no-duplicate guarantee.</summary>
public class HrTaskReminder
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TaskId { get; set; } = string.Empty;
    public short OffsetMinutes { get; set; }
    public DateTimeOffset SentAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>HR settings — exactly ONE row (<c>id = 1</c>). Defaults are the old ones.</summary>
public class HrSettings
{
    public const short SingletonId = 1;
    public static readonly short[] DefaultDigestWeekdays = [1, 2, 3, 4, 5, 6];
    public static readonly short[] DefaultReminderOffsets = [120, 60, 30];

    public short Id { get; set; } = SingletonId;
    public string Timezone { get; set; } = "Asia/Tashkent";
    public TimeOnly DigestTime { get; set; } = new(19, 0);
    public short[] DigestWeekdays { get; set; } = [.. DefaultDigestWeekdays];
    public bool MorningListEnabled { get; set; } = true;
    public TimeOnly MorningTime { get; set; } = new(7, 30);
    public TimeOnly GenerationTime { get; set; } = new(0, 10);
    public TimeOnly CloseOverdueTime { get; set; } = new(23, 50);
    public bool ReminderEnabled { get; set; } = true;
    public short[] ReminderOffsets { get; set; } = [.. DefaultReminderOffsets];
    public bool RemindEndOfDay { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public TimeZoneInfo Zone => HrValues.Zone(Timezone);

    /// <summary>Distinct, largest first; 5..1440; at most 5. Empty = "no reminders".</summary>
    public static short[] CleanReminderOffsets(IEnumerable<int> values)
    {
        var offsets = values.Distinct().OrderByDescending(v => v).ToArray();
        if (offsets.Length > HrLimits.MaxReminderOffsets)
            throw HrErrors.Validation($"Eslatma vaqtlari {HrLimits.MaxReminderOffsets} tadan oshmasligi kerak (kiritilgan: {offsets.Length})");
        foreach (var o in offsets)
            if (o < HrLimits.ReminderOffsetMin || o > HrLimits.ReminderOffsetMax)
                throw HrErrors.Validation(
                    $"Eslatma vaqti {HrLimits.ReminderOffsetMin} dan {HrLimits.ReminderOffsetMax} daqiqagacha bo'lishi kerak (kiritilgan: {o})");
        return [.. offsets.Select(o => (short)o)];
    }

    public static short[] CleanWeekdays(IEnumerable<int> values)
    {
        var days = values.Distinct().OrderBy(v => v).ToArray();
        foreach (var d in days)
            if (d is < 1 or > 7) throw HrErrors.InvalidRecurrence($"Hafta kuni 1 dan 7 gacha bo'lishi kerak (kiritilgan: {d})");
        return [.. days.Select(d => (short)d)];
    }
}

/// <summary>One run of a daily job — <c>UNIQUE (job_name, run_date)</c> (local date).</summary>
public class HrJobRun
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string JobName { get; set; } = string.Empty;
    public DateOnly RunDate { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
    public string Status { get; set; } = HrJobs.StatusOk;
    /// <summary>jsonb — counts for diagnostics.</summary>
    public string? Details { get; set; }
}

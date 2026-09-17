using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;

using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Admin "Ota-onalar" bo'limi — har bir o'quvchi akkaunti oila uchun (parent role tashlanggan).
/// Telefon raqami bo'yicha guruhlangan: bir ota-ona bir nechta farzandga ega bo'lishi mumkin.
/// Ilova aktivlashtirilganligi (birinchi login) va oxirgi kirish vaqti ko'rsatiladi.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("app")]
[Route("api/admin/parents")]
public class ParentsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ParentRowDto>>> GetAll()
    {
        // Faqat faol (arxivlanmagan) o'quvchilar. UserId bo'lsa login kuzatuvi bor.
        var students = await db.Students
            .Where(s => !s.IsArchived)
            .ToListAsync();

        var userIds = students.Where(s => s.UserId != null).Select(s => s.UserId!).Distinct().ToList();
        var users = await db.Users.Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u);

        // Har foydalanuvchining oxirgi faol qurilmasi (push token).
        var latestDevice = (await db.DeviceTokens.Where(d => userIds.Contains(d.UserId)).ToListAsync())
            .GroupBy(d => d.UserId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.LastSeenAt).First());

        ParentChildDto ChildOf(Student s)
        {
            string? firstLogin = null;
            string? lastLogin = null;
            var devName = ""; var platform = ""; var appId = "";
            if (s.UserId is not null && users.TryGetValue(s.UserId, out var u))
            {
                firstLogin = u.FirstLoginAt;
                lastLogin = u.LastLoginAt;
            }
            if (s.UserId is not null && latestDevice.TryGetValue(s.UserId, out var d))
            {
                devName = d.DeviceName; platform = d.Platform; appId = d.AppId;
            }
            return new ParentChildDto(s.Id, s.FullName, s.ClassName, firstLogin, lastLogin, devName, platform, appId);
        }

        // Guruhlash kaliti: telefon (raqamlar normallashtirilgan) yoki nom (telefon bo'sh bo'lsa).
        string Key(Student s)
        {
            var phone = new string((s.ParentPhone ?? "").Where(char.IsDigit).ToArray());
            if (!string.IsNullOrEmpty(phone)) return "tel:" + phone;
            return "name:" + (s.ParentFullName ?? "").Trim().ToLowerInvariant();
        }

        var groups = students
            .GroupBy(Key)
            .Select(g =>
            {
                var first = g.First();
                var children = g.Select(ChildOf).ToList();
                var firstLogins = children.Where(c => c.FirstLoginAt != null).Select(c => c.FirstLoginAt!).ToList();
                var lastLogins = children.Where(c => c.LastLoginAt != null).Select(c => c.LastLoginAt!).ToList();
                var activatedAt = firstLogins.Count > 0 ? firstLogins.Min(StringComparer.Ordinal) : null;
                var lastSeenAt = lastLogins.Count > 0 ? lastLogins.Max(StringComparer.Ordinal) : null;
                // Oxirgi faol qurilma (farzandlar bo'yicha).
                var dev = g.Where(s => s.UserId != null && latestDevice.ContainsKey(s.UserId!))
                    .Select(s => latestDevice[s.UserId!])
                    .OrderByDescending(d => d.LastSeenAt).FirstOrDefault();
                return new ParentRowDto(
                    first.ParentFullName ?? "",
                    first.ParentPhone ?? "",
                    children.Count,
                    activatedAt != null,
                    activatedAt,
                    lastSeenAt,
                    children,
                    dev?.DeviceName ?? "",
                    dev?.Platform ?? "");
            })
            // Oxirgi kirgani eng yangi bo'lganlari yuqorida.
            .OrderByDescending(p => p.LastSeenAt, StringComparer.Ordinal)
            .ThenBy(p => p.FullName)
            .ToList();

        return groups;
    }

    // =====================================================================
    //  P-1, P-2 — ro'yxat endi VASIY JADVALIDAN
    //  (docs/modules/students-parity.md §2.9).
    // =====================================================================
    //
    //  YUQORIDAGI `GET /api/admin/parents` TEGILMAGAN va o'z shaklida
    //  qoladi: u telefon raqamining raqamlari bo'yicha o'quvchilarni
    //  guruhlaydi. Bu ikki xato tug'dirardi:
    //
    //    · ota-onaning raqami o'zgarib, bir farzandida yangilanib
    //      ikkinchisida eski qolsa — BIR ODAM IKKI QATOR bo'lardi;
    //    · raqamsiz vasiy (buvi, ishonchli shaxs) umuman ko'rinmasdi.
    //
    //  Yangi yo'l `guardians` + `student_guardians` dan o'qiydi, ya'ni
    //  qator = ODAM. "Ilova o'rnatilganmi" o'rnini Telegram bog'lanishi
    //  egallaydi (CLAUDE.md: yagona kanal — Telegram).
    // =====================================================================

    /// <summary>Filtrlangan vasiylar ro'yxati (P-1).</summary>
    [HttpGet("guardians")]
    public async Task<ActionResult<IEnumerable<GuardianRowDto>>> Guardians(
        [FromQuery] GuardianListFilter filter, CancellationToken ct = default) =>
        await GuardianRowsAsync(filter, ct);

    /// <summary>P-2 — o'sha filtrdagi ro'yxatning .xlsx eksporti.</summary>
    [HttpGet("guardians/export")]
    public async Task<IActionResult> GuardiansExport(
        [FromQuery] GuardianListFilter filter, CancellationToken ct = default)
    {
        var rows = await GuardianRowsAsync(filter, ct);

        var headers = new[]
        {
            "Vasiy F.I.SH", "Telefon", "Vasiylik turi", "Telegram", "Login",
            "Farzandlar soni", "Farzandlar", "Oxirgi kirish",
        };

        var data = rows.Select(r => (IReadOnlyList<string>)new[]
        {
            r.FullName,
            r.Phone,
            string.Join(", ", r.Children.Select(c => RelationLabel(c.Relation, c.RelationNote)).Distinct()),
            r.TelegramLinked ? "bor" : "yo'q",
            r.Login ?? "",
            r.ChildrenCount.ToString(),
            string.Join("; ", r.Children.Select(c => $"{c.FullName} ({c.ClassName})")),
            r.LastSeenAt ?? "",
        });

        var bytes = ExcelExport.Build(new[]
        {
            new ExcelExport.SheetSpec("Ota-onalar", headers, data),
        });

        return File(bytes, XlsxMime, $"ota-onalar_{AppClock.Now:yyyy-MM-dd}.xlsx");
    }

    private const string XlsxMime =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Vasiylik turining o'zbekcha nomi (eksport ustuni uchun).</summary>
    private static string RelationLabel(string relation, string? note) => relation switch
    {
        GuardianRelation.Father => "otasi",
        GuardianRelation.Mother => "onasi",
        GuardianRelation.Grandparent => "bobo/buvi",
        GuardianRelation.Trustee => "ishonchli shaxs",
        GuardianRelation.Other => string.IsNullOrWhiteSpace(note) ? "boshqa" : note.Trim(),
        _ => "ota-ona",
    };

    /// <summary>
    /// Filtrga mos vasiylar. To'rtta to'plamli so'rov — vasiy soniga bog'liq
    /// emas (N+1 yo'q): bog'lanishlar + o'quvchilar, vasiylar, akkauntlar,
    /// Telegram bog'lanishlari.
    /// </summary>
    private async Task<List<GuardianRowDto>> GuardianRowsAsync(
        GuardianListFilter f, CancellationToken ct)
    {
        var state = (f.State ?? "").Trim().ToLowerInvariant();

        var pairs = await (from l in db.StudentGuardians.AsNoTracking()
                           join s in db.Students.AsNoTracking() on l.StudentId equals s.Id
                           select new
                           {
                               l.GuardianId, l.Relation, l.RelationNote, l.IsPrimary,
                               s.Id, s.FullName, s.ClassName, s.Phone, s.IsArchived,
                           })
            .ToListAsync(ct);

        // Farzand holati: sukut bo'yicha FAOL bolalar (arxivdagi bolaning
        // ota-onasi ro'yxatda turishi kutilmaydi).
        pairs = state switch
        {
            "archived" => [.. pairs.Where(p => p.IsArchived)],
            "all" => pairs,
            _ => [.. pairs.Where(p => !p.IsArchived)],
        };

        if (!string.IsNullOrWhiteSpace(f.ClassName))
        {
            var className = f.ClassName.Trim();
            pairs = [.. pairs.Where(p => string.Equals(p.ClassName, className, StringComparison.Ordinal))];
        }

        if (!string.IsNullOrWhiteSpace(f.StudentId))
        {
            var studentId = f.StudentId.Trim();
            pairs = [.. pairs.Where(p => p.Id == studentId)];
        }

        if (f.GroupId is { } groupId)
        {
            var members = (await db.StudyGroupMembers.AsNoTracking()
                    .Where(m => m.GroupId == groupId && m.LeftOn == null)
                    .Select(m => m.StudentId).ToListAsync(ct))
                .ToHashSet(StringComparer.Ordinal);
            pairs = [.. pairs.Where(p => members.Contains(p.Id))];
        }

        if (!string.IsNullOrWhiteSpace(f.Relation))
        {
            var relation = f.Relation.Trim().ToLowerInvariant();
            pairs = [.. pairs.Where(p => p.Relation == relation)];
        }

        if (pairs.Count == 0) return [];

        var guardianIds = pairs.Select(p => p.GuardianId).Distinct().ToList();
        var guardians = await db.Guardians.AsNoTracking()
            .Where(g => guardianIds.Contains(g.Id)).ToListAsync(ct);

        var userIds = guardians.Where(g => g.UserId is not null).Select(g => g.UserId!).ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u, ct);
        var linked = (await db.TelegramAccounts.AsNoTracking()
                .Where(a => userIds.Contains(a.UserId))
                .Select(a => a.UserId).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        if (f.Connected is { } connected)
            guardians = [.. guardians.Where(g =>
                (g.UserId is not null && linked.Contains(g.UserId)) == connected)];

        var search = (f.Search ?? "").Trim();
        if (search.Length > 0)
        {
            var key = PhoneUtil.Key(search);
            var byChild = pairs
                .Where(p => p.FullName.Contains(search, StringComparison.OrdinalIgnoreCase))
                .Select(p => p.GuardianId)
                .ToHashSet(StringComparer.Ordinal);

            guardians = [.. guardians.Where(g =>
                g.FullName.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (key.Length >= 3 && g.PhoneKey.Contains(key, StringComparison.Ordinal))
                || byChild.Contains(g.Id))];
        }

        var childrenOf = pairs
            .GroupBy(p => p.GuardianId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        return [.. guardians
            .Where(g => childrenOf.ContainsKey(g.Id))
            .Select(g =>
            {
                var user = g.UserId is not null ? users.GetValueOrDefault(g.UserId) : null;
                var children = childrenOf[g.Id];
                return new GuardianRowDto(
                    g.Id, g.FullName, g.Phone,
                    user?.Email, user is not null,
                    g.UserId is not null && linked.Contains(g.UserId),
                    user?.LastLoginAt,
                    children.Count,
                    [.. children
                        .OrderByDescending(c => c.IsPrimary)
                        .ThenBy(c => c.ClassName, StringComparer.Ordinal)
                        .ThenBy(c => c.FullName, StringComparer.Ordinal)
                        .Select(c => new GuardianChildRowDto(
                            c.Id, c.FullName, c.ClassName, c.Relation, c.RelationNote,
                            c.IsPrimary, c.Phone, c.IsArchived))]);
            })
            .OrderBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)];
    }
}

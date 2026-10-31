using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  O'QUVCHINING YO'NALISHI — docs/modules/track-groups-as-classes.md
// ===========================================================================
//
//  9–11-sinflar sinf holatida emas, yo'nalish guruhida (Ijtimoiy, Filologiya,
//  Iqtisodiyot ...) o'qiydi. Gamifikatsiya (Ballar nazorati, guruhga ball) va
//  blok test (ishtirokchi qo'shish, natijalar) o'quvchini SINF bilan bir qatorda
//  YO'NALISH bo'yicha ham ko'rsatishi va filtrlashi kerak (mijoz, 2026-10-30).
//
//  O'quvchi ko'pi bilan BITTA faol yo'nalish guruhida bo'ladi (StudyGroupService
//  tekshiradi), a'zolik esa sanali — shuning uchun "qaysi kunda qaysi yo'nalish"
//  ham aniq javob oladi.
// ===========================================================================

public static class TrackMembership
{
    /// <summary>Faol (arxivlanmagan) yo'nalish guruhi.</summary>
    public sealed record TrackRef(Guid Id, string Name);

    /// <summary>
    /// Har o'quvchining <paramref name="onDay"/> kuni a'zo bo'lgan yo'nalish guruhi
    /// (<paramref name="onDay"/> = null → hozirgi faol a'zolik). Yo'nalishi yo'q o'quvchi
    /// lug'atda bo'lmaydi. <paramref name="studentIds"/> = null → hamma o'quvchi.
    /// </summary>
    public static async Task<Dictionary<string, TrackRef>> ByStudentAsync(
        IAppDbContext db, IReadOnlyCollection<string>? studentIds = null, DateOnly? onDay = null,
        CancellationToken ct = default)
    {
        var q =
            from m in db.StudyGroupMembers.AsNoTracking()
            join g in db.StudyGroups.AsNoTracking() on m.GroupId equals g.Id
            where g.IsTrack
            select new { m.StudentId, m.JoinedOn, m.LeftOn, g.Id, g.Name, g.IsArchived };
        if (studentIds is not null) q = q.Where(x => studentIds.Contains(x.StudentId));
        q = onDay is { } d
            ? q.Where(x => x.JoinedOn <= d && (x.LeftOn == null || x.LeftOn > d))
            : q.Where(x => x.LeftOn == null && !x.IsArchived);

        return (await q.ToListAsync(ct))
            .GroupBy(x => x.StudentId, StringComparer.Ordinal)
            // Bir kunda ikki a'zolik bo'lmasligi kerak; bo'lsa — eng so'nggi qo'shilgani.
            .ToDictionary(g => g.Key, g =>
            {
                var x = g.OrderByDescending(x => x.JoinedOn).First();
                return new TrackRef(x.Id, x.Name);
            }, StringComparer.Ordinal);
    }

    /// <summary>
    /// Yo'nalish guruhining hozirgi faol a'zolari (arxivlanmagan o'quvchilar). Id yo'nalish
    /// guruhi bo'lmasa (yoki arxivda bo'lsa) — null.
    /// </summary>
    public static async Task<(TrackRef Track, List<string> StudentIds)?> ActiveMembersAsync(
        IAppDbContext db, string groupId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(groupId, out var gid)) return null;
        var group = await db.StudyGroups.AsNoTracking()
            .Where(g => g.Id == gid && g.IsTrack && !g.IsArchived)
            .Select(g => new { g.Id, g.Name }).FirstOrDefaultAsync(ct);
        if (group is null) return null;

        var ids = await (
                from m in db.StudyGroupMembers.AsNoTracking()
                join s in db.Students.AsNoTracking() on m.StudentId equals s.Id
                where m.GroupId == gid && m.LeftOn == null && !s.IsArchived
                select s.Id)
            .Distinct().ToListAsync(ct);
        return (new TrackRef(group.Id, group.Name), ids);
    }
}

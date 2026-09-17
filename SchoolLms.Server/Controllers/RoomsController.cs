using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Xonalar reyestri — docs/modules/students-parity.md §2.6 (R-1):
/// ro'yxat, CRUD, ommaviy yaratish va sinflardagi erkin matnli xona
/// nomlaridan to'ldirish.
///
/// <para>
/// <b>Ruxsat — <c>schedule</c>, <c>classes</c> emas.</b> Jadval bilan bir
/// xil kalit, chunki reyestrning O'ZI jadval uchun qurilmoqda: §2.6.3 xonani
/// darsga biriktirish, to'qnashuvlarni tekshirish va
/// <c>classes.home_room_id</c> ni JADVAL moduliga qoldiradi, ya'ni ertaga
/// xonani tanlaydigan ekran — dars jadvali. Xonani qo'shadigan odam ham
/// o'sha: jadval tuzuvchi. Menyuda ham u "Dars jadvali → SOZLAMA" ostida
/// (dars vaqtlari, choraklar bilan yonma-yon), ya'ni menyu va server bitta
/// qoidaga bo'ysunadi. <c>classes</c> ruxsati sinf KATALOGI haqida; sinfning
/// xonasi esa bugun ham erkin matn va shunday qoladi.
/// </para>
/// <para>
/// <b>O'CHIRISH va "faolsizlantirish".</b> Sinf ko'rsatgan xona
/// o'chirilmaydi (400). Bazada <c>rooms.is_active</c> ustuni YO'Q
/// (§3.2 dagi shakl), shuning uchun "faolsizlantirish" yo'li ham yo'q —
/// xonani ro'yxatdan olib tashlash uchun avval uni sinflardan bo'shatish
/// kerak. Ustun qo'shilishi kerakligi hisobotda alohida qayd etilgan.
/// </para>
/// <para>
/// <b>Turlar</b> (<see cref="RoomKind"/>) bazada CHECK bilan cheklanmagan
/// (Rooms.cs dagi qaror) — demak ro'yxatni SHU YERDA tekshirmasak, xohlagan
/// matn tushib ketardi va jadval moduli uni tushunmasdi.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("schedule")]
[Route("api/admin/rooms")]
public class RoomsController(AppDbContext db) : ControllerBase
{
    public const string NameRequiredMessage = "Xona nomini yozing";
    public const string NameTakenMessage = "Bunday nomli xona allaqachon bor";
    public const string KindMessage = "Xona turi: classroom, lab, gym yoki hall";
    public const string CapacityMessage = "Sig'im 1 dan 1000 gacha bo'lsin";
    public const string FloorMessage = "Qavat -5 dan 100 gacha bo'lsin";
    public const string CountMessage = "Nechta xona: 1 dan 50 gacha";
    public const string StartFromMessage = "Boshlang'ich raqam 0 dan 100000 gacha bo'lsin";

    /// <summary>EduSchool ham <c>rooms/multiple</c> da shu chegarada to'xtatadi (§2.6.1).</summary>
    public const int MaxBulk = 50;

    // =====================================================================
    //  1. RO'YXAT
    // =====================================================================

    /// <summary>Reyestr: nom bo'yicha qidiruv, bino va tur filtri.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<RoomDto>>> GetAll(
        [FromQuery] string? search, [FromQuery] string? building, [FromQuery] string? kind,
        CancellationToken ct = default)
    {
        var q = db.Rooms.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            q = q.Where(r => EF.Functions.ILike(r.Name, term)
                || (r.Building != null && EF.Functions.ILike(r.Building, term)));
        }

        if (!string.IsNullOrWhiteSpace(building))
            q = q.Where(r => r.Building == building);

        if (!string.IsNullOrWhiteSpace(kind))
        {
            if (!RoomKind.IsValid(kind)) return BadRequest(new { message = KindMessage });
            q = q.Where(r => r.Kind == kind);
        }

        var rooms = await q.OrderBy(r => r.Building).ThenBy(r => r.Name).ToListAsync(ct);
        return (await WithUsageAsync(rooms, ct)).ToList();
    }

    /// <summary>Binolar ro'yxati (filtr uchun). Alohida <c>buildings</c> jadvali YO'Q — §2.6.3.</summary>
    [HttpGet("buildings")]
    public async Task<ActionResult<IEnumerable<string>>> Buildings(CancellationToken ct = default) =>
        await db.Rooms.AsNoTracking()
            .Where(r => r.Building != null && r.Building != "")
            .Select(r => r.Building!)
            .Distinct().OrderBy(b => b).ToListAsync(ct);

    // =====================================================================
    //  2. CRUD
    // =====================================================================

    [HttpPost]
    public async Task<ActionResult<RoomDto>> Create(SaveRoomRequest req, CancellationToken ct = default)
    {
        var name = (req.Name ?? "").Trim();
        if (name.Length == 0) return BadRequest(new { message = NameRequiredMessage });
        if (await db.Rooms.AnyAsync(r => r.Name == name, ct))
            return BadRequest(new { message = NameTakenMessage });

        if (Validate(req.Kind, req.Capacity, req.Floor) is { } error)
            return BadRequest(new { message = error });

        var room = new Room
        {
            Name = name,
            Building = Blank(req.Building),
            Floor = req.Floor,
            Capacity = req.Capacity ?? 30,
            Kind = Blank(req.Kind) ?? RoomKind.Classroom,
        };
        db.Rooms.Add(room);
        await db.SaveChangesAsync(ct);
        return ToDto(room, 0);
    }

    /// <summary>
    /// Tahrirlash. Nom, bino va qavat — TO'LIQ almashtiriladi (forma har doim
    /// hammasini yuboradi, va binoni TOZALASH imkoni bo'lishi kerak); sig'im
    /// va tur berilmasa joyida qoladi — ularni jimgina 30/classroom ga
    /// qaytarib qo'yish yo'qotishdan battar bo'lardi.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<RoomDto>> Update(
        Guid id, SaveRoomRequest req, CancellationToken ct = default)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (room is null) return NotFound();

        var name = (req.Name ?? "").Trim();
        if (name.Length == 0) return BadRequest(new { message = NameRequiredMessage });
        if (await db.Rooms.AnyAsync(r => r.Name == name && r.Id != id, ct))
            return BadRequest(new { message = NameTakenMessage });

        if (Validate(req.Kind, req.Capacity, req.Floor) is { } error)
            return BadRequest(new { message = error });

        room.Name = name;
        room.Building = Blank(req.Building);
        room.Floor = req.Floor;
        if (req.Capacity is { } capacity) room.Capacity = capacity;
        if (Blank(req.Kind) is { } kind) room.Kind = kind;

        await db.SaveChangesAsync(ct);
        return ToDto(room, await UsedByAsync(room.Name, ct));
    }

    /// <summary>
    /// O'chirish — faqat birorta sinf ko'rsatmagan xona uchun. Darsga
    /// biriktirilgan xona hozircha YO'Q (dars jadvalida xona maydoni jadval
    /// modulida qo'shiladi, §2.6.3), shuning uchun tekshiruv sinflar bo'yicha.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (room is null) return NoContent();

        var used = await UsedByAsync(room.Name, ct);
        if (used > 0)
            return BadRequest(new
            {
                message = $"Bu xona {used} ta sinfda ko'rsatilgan — o'chirib bo'lmaydi. "
                    + "Avval sinf kartochkasidagi xonani o'zgartiring.",
            });

        db.Rooms.Remove(room);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // =====================================================================
    //  3. OMMAVIY YARATISH (EduSchool `rooms/multiple`, §2.6.1)
    // =====================================================================

    /// <summary>
    /// Ketma-ket raqamlangan N ta xona. Nomi band bo'lganlari o'tkazib
    /// yuboriladi (butun so'rov bitta takroriy nom tufayli yiqilmasin).
    /// </summary>
    [HttpPost("multiple")]
    public async Task<ActionResult<BulkCreateRoomsResultDto>> CreateMany(
        BulkCreateRoomsRequest req, CancellationToken ct = default)
    {
        var count = req.Count ?? 0;
        if (count < 1 || count > MaxBulk) return BadRequest(new { message = CountMessage });

        if (req.StartFrom is { } given && (given < 0 || given > 100_000))
            return BadRequest(new { message = StartFromMessage });

        if (Validate(req.Kind, req.Capacity, req.Floor) is { } error)
            return BadRequest(new { message = error });

        var prefix = (req.Prefix ?? "").Trim();
        var existing = await db.Rooms.AsNoTracking().Select(r => r.Name).ToListAsync(ct);
        var taken = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var start = req.StartFrom ?? NextNumber(existing, prefix);
        var kind = Blank(req.Kind) ?? RoomKind.Classroom;
        var building = Blank(req.Building);
        var capacity = req.Capacity ?? 30;

        var created = new List<Room>();
        var skipped = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var name = prefix + (start + i).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!taken.Add(name)) { skipped.Add(name); continue; }
            created.Add(new Room
            {
                Name = name,
                Building = building,
                Floor = req.Floor,
                Capacity = capacity,
                Kind = kind,
            });
        }

        db.Rooms.AddRange(created);
        await db.SaveChangesAsync(ct);

        return new BulkCreateRoomsResultDto(
            created.Select(r => ToDto(r, 0)).ToList(), skipped);
    }

    /// <summary>
    /// Sinflardagi erkin matnli xona nomlaridan reyestrni to'ldiradi
    /// (§2.6.3 "seeded from select distinct room from classes").
    ///
    /// <para>
    /// Migratsiya buni QILMAGAN va ataylab: seed — bir martalik MA'MURIY amal,
    /// migratsiya esa har muhitda (bo'sh test bazasida ham) yuradi. Shuning
    /// uchun u tugma ortida va natijasi ko'rinadigan qilib qo'yilgan.
    /// Sinflardagi matn O'ZGARMAYDI.
    /// </para>
    /// </summary>
    [HttpPost("import-from-classes")]
    public async Task<ActionResult<RoomImportResultDto>> ImportFromClasses(CancellationToken ct = default)
    {
        var names = (await db.Classes.AsNoTracking()
                .Where(c => c.Room != null && c.Room != "")
                .Select(c => c.Room!)
                .Distinct().ToListAsync(ct))
            .Select(n => n.Trim()).Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var taken = (await db.Rooms.AsNoTracking().Select(r => r.Name).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var created = new List<Room>();
        var skipped = new List<string>();
        foreach (var name in names)
        {
            if (!taken.Add(name)) { skipped.Add(name); continue; }
            created.Add(new Room { Name = name });
        }

        db.Rooms.AddRange(created);
        await db.SaveChangesAsync(ct);

        var withUsage = await WithUsageAsync(created, ct);
        return new RoomImportResultDto(withUsage, skipped);
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    /// <summary>Nomi <c>prefix</c> bilan boshlanadigan raqamli xonalarning keyingisi.</summary>
    private static int NextNumber(IEnumerable<string> existing, string prefix)
    {
        var max = 0;
        foreach (var name in existing)
        {
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var tail = name[prefix.Length..];
            if (tail.Length == 0) continue;
            if (int.TryParse(tail, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var v) && v > max) max = v;
        }
        return max + 1;
    }

    private async Task<int> UsedByAsync(string roomName, CancellationToken ct) =>
        await db.Classes.AsNoTracking().CountAsync(c => c.Room == roomName, ct);

    private async Task<List<RoomDto>> WithUsageAsync(List<Room> rooms, CancellationToken ct)
    {
        if (rooms.Count == 0) return [];

        var names = rooms.Select(r => r.Name).ToList();
        var used = await db.Classes.AsNoTracking()
            .Where(c => c.Room != null && names.Contains(c.Room))
            .GroupBy(c => c.Room!)
            .Select(g => new { Room = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Room, x => x.Count, ct);

        return rooms.Select(r => ToDto(r, used.GetValueOrDefault(r.Name))).ToList();
    }

    /// <summary>Tur, sig'im va qavat — bazada CHECK yo'q, shuning uchun shu yerda.</summary>
    private static string? Validate(string? kind, short? capacity, short? floor)
    {
        if (Blank(kind) is { } k && !RoomKind.IsValid(k)) return KindMessage;
        if (capacity is { } c && (c < 1 || c > 1000)) return CapacityMessage;
        if (floor is { } f && (f < -5 || f > 100)) return FloorMessage;
        return null;
    }

    private static RoomDto ToDto(Room r, int usedByClasses) =>
        new(r.Id, r.Name, r.Building, r.Floor, r.Capacity, r.Kind, usedByClasses);

    private static string? Blank(string? value)
    {
        var v = (value ?? "").Trim();
        return v.Length == 0 ? null : v;
    }
}

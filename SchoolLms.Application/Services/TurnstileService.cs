using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// Turniket/FaceID qurilmasidan o'qituvchilar davomatini AVTOMATIK yuklash.
/// Qurilma (Hikvision/ZKTeco) o'tish hodisalarini beradi → ular xom log (<see cref="TurnstileEvent"/>)
/// sifatida saqlanadi va kunlik davomatga (<see cref="TeacherAttendance"/>) aylantiriladi:
/// birinchi KIRISH = kelgan vaqt, oxirgi hodisa = ketgan vaqt; holat (keldi/kechikdi) — ish boshlanish
/// vaqti VA dars jadvalidagi birinchi dars (qaysi biri erta bo'lsa) + grace ga qarab aniqlanadi.
/// </summary>
public class TurnstileService
{
    /// <summary>Tanlangan kun uchun dashboard — har o'qituvchi: holat, kelgan/ketgan vaqt, kechikish.</summary>
    public async Task<TeacherAttendanceDashboardDto> BuildDashboardAsync(IAppDbContext db, string date)
    {
        var meta = await db.SchoolMeta.FirstOrDefaultAsync();
        var quarters = await TeacherSalaryCalc.QuarterRangesAsync(db);
        var inPeriod = TeacherSalaryCalc.InQuarter(date, quarters);
        var today = AppClock.Today.ToString("yyyy-MM-dd");
        var isPast = string.CompareOrdinal(date, today) < 0;

        var teachers = await db.Teachers.Where(t => !t.IsArchived).OrderBy(t => t.FullName).ToListAsync();
        var entries = (await db.TeacherAttendances.Where(a => a.Date == date).ToListAsync())
            .ToDictionary(a => a.TeacherId);

        var firstPeriod = await FirstPeriodByWeekdayAsync(db);
        var lessonStart = await LessonStartByPeriodAsync(db);

        var rows = new List<TeacherDashboardRowDto>();
        int present = 0, late = 0, absent = 0, notArrived = 0;
        foreach (var t in teachers)
        {
            var start = TeacherSalaryCalc.StartDateOf(t);
            var notYet = start is not null && string.CompareOrdinal(date, start) < 0;
            var expected = ExpectedArrival(t, date, meta?.WorkStartTime ?? "", firstPeriod, lessonStart);

            entries.TryGetValue(t.Id, out var e);
            var status = e?.Status ?? "";
            // Davomat yozuvi yo'q + chorak davri + ishga kirgan + o'tgan kun → kelmadi (jonli).
            if (status == "" && inPeriod && !notYet && isPast) status = "absent";

            var lateMin = 0;
            if (status == "late" && e is not null && e.CheckIn.Length == 5 && expected.Length == 5)
                lateMin = Math.Max(0, MinutesBetween(expected, e.CheckIn));

            switch (status)
            {
                case "present": present++; break;
                case "late": late++; break;
                case "absent": absent++; break;
                default: if (!notYet && inPeriod) notArrived++; break;
            }

            rows.Add(new TeacherDashboardRowDto(
                t.Id, t.FullName, t.PhotoUrl, t.DeviceUserId,
                status, e?.CheckIn ?? "", e?.CheckOut ?? "", expected, lateMin, e?.Source ?? ""));
        }

        var summary = new AttendanceSummaryDto(teachers.Count, present, late, absent, notArrived);
        return new TeacherAttendanceDashboardDto(
            date, meta?.TurnstileEnabled ?? false, meta?.TurnstileLastSync ?? "", inPeriod, summary, rows);
    }

    /// <summary>Qurilmadan so'nggi (≈2 kun) hodisalarni tortib olib, davomatni qayta hisoblaydi.</summary>
    public async Task<TurnstileSyncResultDto> SyncAsync(IAppDbContext db)
    {
        var meta = await db.SchoolMeta.FirstOrDefaultAsync();
        if (meta is null || !meta.TurnstileEnabled)
            return new TurnstileSyncResultDto(false, "Turniket integratsiyasi yoqilmagan (Sozlamalar).", 0, 0, "");
        if (string.IsNullOrWhiteSpace(meta.TurnstileHost))
            return new TurnstileSyncResultDto(false, "Qurilma manzili (host) kiritilmagan.", 0, 0, "");

        var to = AppClock.Now;
        var from = AppClock.Today.AddDays(-1).ToDateTime(TimeOnly.MinValue);

        List<RawEvent> raw;
        try
        {
            raw = meta.TurnstileVendor?.ToLowerInvariant() switch
            {
                "zkteco" => throw new NotSupportedException(
                    "ZKTeco hozircha qo'llab-quvvatlanmaydi — Hikvision (ISAPI) ishlatiladi yoki webhook orqali yuboring."),
                _ => await FetchHikvisionAsync(meta, from, to),
            };
        }
        catch (NotSupportedException ex)
        {
            return new TurnstileSyncResultDto(false, ex.Message, 0, 0, "");
        }
        catch (Exception ex)
        {
            return new TurnstileSyncResultDto(false, $"Qurilmaga ulanib bo'lmadi: {ex.Message}", 0, 0, "");
        }

        var fetched = await IngestAsync(db, raw);
        var updated = await RecomputeAsync(db, from, to);

        meta.TurnstileLastSync = AppClock.Iso();
        await db.SaveChangesAsync();
        return new TurnstileSyncResultDto(true,
            $"{fetched} ta hodisa olindi, {updated} ta davomat yangilandi.", fetched, updated, meta.TurnstileLastSync);
    }

    /// <summary>Xom hodisalarni saqlash (deviceUserId+vaqt bo'yicha takrorlanmaydigan). Yangilar sonini qaytaradi.</summary>
    public async Task<int> IngestAsync(IAppDbContext db, List<RawEvent> raw)
    {
        if (raw.Count == 0) return 0;
        var deviceMap = (await db.Teachers.Where(t => t.DeviceUserId != "")
                .Select(t => new { t.Id, t.DeviceUserId }).ToListAsync())
            .GroupBy(x => x.DeviceUserId).ToDictionary(g => g.Key, g => g.First().Id);

        var existing = (await db.TurnstileEvents
                .Where(e => e.EventAt != "").Select(e => e.DeviceUserId + "|" + e.EventAt).ToListAsync())
            .ToHashSet();

        var now = AppClock.Iso();
        var added = 0;
        foreach (var r in raw)
        {
            var key = r.DeviceUserId + "|" + r.EventAt;
            if (existing.Contains(key)) continue;
            existing.Add(key);
            db.TurnstileEvents.Add(new TurnstileEvent
            {
                TeacherId = deviceMap.GetValueOrDefault(r.DeviceUserId, ""),
                DeviceUserId = r.DeviceUserId,
                EventAt = r.EventAt,
                Direction = r.Direction,
                DeviceName = r.DeviceName,
                CreatedAt = now,
            });
            added++;
        }
        if (added > 0) await db.SaveChangesAsync();
        return added;
    }

    /// <summary>[from..to] oralig'idagi har KUN uchun davomatni hodisalardan qayta hisoblaydi
    /// (faqat "manual" bo'lmagan yozuvlar ustiga yoziladi — admin tuzatgan yozuvlar saqlanadi).</summary>
    public async Task<int> RecomputeAsync(IAppDbContext db, DateTime from, DateTime to)
    {
        var meta = await db.SchoolMeta.FirstOrDefaultAsync();
        var quarters = await TeacherSalaryCalc.QuarterRangesAsync(db);
        var firstPeriod = await FirstPeriodByWeekdayAsync(db);
        var lessonStart = await LessonStartByPeriodAsync(db);
        var today = AppClock.Today.ToString("yyyy-MM-dd");

        var teachers = (await db.Teachers.Where(t => !t.IsArchived && t.DeviceUserId != "").ToListAsync());
        if (teachers.Count == 0) return 0;

        var fromDate = DateOnly.FromDateTime(from);
        var toDate = DateOnly.FromDateTime(to);

        // Oraliqdagi barcha hodisalar (teacherId|date → vaqtlar).
        var fromIso = fromDate.ToString("yyyy-MM-dd");
        var events = await db.TurnstileEvents
            .Where(e => e.TeacherId != "" && string.Compare(e.EventAt, fromIso) >= 0)
            .ToListAsync();
        var byTeacherDay = events
            .Where(e => e.EventAt.Length >= 16)
            .GroupBy(e => e.TeacherId + "|" + e.EventAt[..10])
            .ToDictionary(g => g.Key, g => g.Select(e => e.EventAt.Substring(11, 5)).OrderBy(x => x).ToList());

        var existing = (await db.TeacherAttendances
                .Where(a => string.Compare(a.Date, fromIso) >= 0).ToListAsync())
            .ToDictionary(a => a.TeacherId + "|" + a.Date);

        var updated = 0;
        for (var d = fromDate; d <= toDate; d = d.AddDays(1))
        {
            var date = d.ToString("yyyy-MM-dd");
            if (!TeacherSalaryCalc.InQuarter(date, quarters)) continue;
            var isPast = string.CompareOrdinal(date, today) < 0;
            foreach (var t in teachers)
            {
                var start = TeacherSalaryCalc.StartDateOf(t);
                if (start is not null && string.CompareOrdinal(date, start) < 0) continue;

                var key = t.Id + "|" + date;
                existing.TryGetValue(key, out var att);
                if (att is not null && att.Source == "manual") continue; // admin tuzatgan — tegmaymiz

                var times = byTeacherDay.GetValueOrDefault(key);
                string status, checkIn = "", checkOut = "";
                if (times is { Count: > 0 })
                {
                    checkIn = times[0];
                    checkOut = times.Count > 1 ? times[^1] : "";
                    var expected = ExpectedArrival(t, date, meta?.WorkStartTime ?? "", firstPeriod, lessonStart);
                    var grace = meta?.LateGraceMinutes ?? 0;
                    status = expected.Length == 5 && MinutesBetween(expected, checkIn) > grace ? "late" : "present";
                }
                else if (isPast) status = "absent"; // o'tgan kun, hodisa yo'q → kelmadi
                else continue;                       // bugun, hali kelmadi → yozmaymiz

                if (att is null)
                {
                    db.TeacherAttendances.Add(new TeacherAttendance
                    {
                        TeacherId = t.Id, Date = date, Status = status,
                        CheckIn = checkIn, CheckOut = checkOut, Source = "turnstile",
                    });
                }
                else
                {
                    att.Status = status; att.CheckIn = checkIn; att.CheckOut = checkOut; att.Source = "turnstile";
                }
                updated++;
            }
        }
        if (updated > 0) await db.SaveChangesAsync();
        return updated;
    }

    // ---------- Kutilgan kelish vaqti (ish boshlanishi + birinchi dars, qaysi erta) ----------

    private static string ExpectedArrival(Teacher t, string date, string workStart,
        Dictionary<string, int[]> firstPeriod, Dictionary<int, string> lessonStart)
    {
        var candidates = new List<string>();
        if (workStart.Length == 5) candidates.Add(workStart);
        if (DateOnly.TryParse(date, out var d))
        {
            var wd = ((int)d.DayOfWeek + 6) % 7; // Dushanba=0..Shanba=5
            if (wd < 6 && firstPeriod.TryGetValue(t.Id, out var arr) && arr[wd] > 0
                && lessonStart.TryGetValue(arr[wd], out var st) && st.Length == 5)
                candidates.Add(st);
        }
        return candidates.Count == 0 ? "" : candidates.Min()!;
    }

    /// <summary>teacherId → int[6]: har hafta kunidagi ENG ERTA (eng kichik) dars raqami (0 = dars yo'q).</summary>
    private static async Task<Dictionary<string, int[]>> FirstPeriodByWeekdayAsync(IAppDbContext db)
    {
        var classSet = (await db.Classes.Where(c => !c.IsArchived).Select(c => c.Id).ToListAsync()).ToHashSet();
        var templates = (await db.ScheduleTemplates.Include(x => x.Lessons).ToListAsync())
            .Where(x => classSet.Contains(x.ClassId)).ToList();
        var main = templates.GroupBy(x => x.ClassId)
            .Select(g => g.OrderByDescending(x => x.Lessons.Count).ThenBy(x => x.Id).First());

        var res = new Dictionary<string, int[]>();
        foreach (var tpl in main)
            foreach (var l in tpl.Lessons.Where(l => !string.IsNullOrEmpty(l.TeacherId) && l.Day is >= 0 and < 6 && l.Period > 0))
            {
                if (!res.TryGetValue(l.TeacherId, out var arr)) res[l.TeacherId] = arr = new int[6];
                if (arr[l.Day] == 0 || l.Period < arr[l.Day]) arr[l.Day] = l.Period;
            }
        return res;
    }

    private static async Task<Dictionary<int, string>> LessonStartByPeriodAsync(IAppDbContext db) =>
        (await db.LessonTimes.Where(t => t.StartTime != "").ToListAsync())
        .GroupBy(t => t.Period).ToDictionary(g => g.Key, g => g.First().StartTime.Length >= 5 ? g.First().StartTime[..5] : g.First().StartTime);

    private static int MinutesBetween(string fromHhmm, string toHhmm)
    {
        if (!TimeOnly.TryParse(fromHhmm, out var a) || !TimeOnly.TryParse(toHhmm, out var b)) return 0;
        return (int)(b - a).TotalMinutes;
    }

    // ---------- Hikvision ISAPI (AcsEvent) ----------

    /// <summary>Bitta xom o'tish hodisasi.</summary>
    public record RawEvent(string DeviceUserId, string EventAt, string Direction, string DeviceName);

    private static async Task<List<RawEvent>> FetchHikvisionAsync(SchoolMeta meta, DateTime from, DateTime to)
    {
        var port = meta.TurnstilePort > 0 ? meta.TurnstilePort : 80;
        var baseUrl = $"http://{meta.TurnstileHost}:{port}";
        using var handler = new HttpClientHandler
        {
            Credentials = new NetworkCredential(meta.TurnstileUsername, meta.TurnstilePassword),
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };

        var result = new List<RawEvent>();
        var pos = 0;
        const int pageSize = 50;
        var startIso = from.ToString("yyyy-MM-ddTHH:mm:ss") + "+05:00";
        var endIso = to.ToString("yyyy-MM-ddTHH:mm:ss") + "+05:00";

        for (var page = 0; page < 50; page++) // xavfsizlik chegarasi: 50 sahifa (2500 hodisa)
        {
            var body = JsonSerializer.Serialize(new
            {
                AcsEventCond = new
                {
                    searchID = "schoollms",
                    searchResultPosition = pos,
                    maxResults = pageSize,
                    major = 5,   // 5 = Access Control hodisasi
                    minor = 0,   // 0 = barcha kichik turlar
                    startTime = startIso,
                    endTime = endIso,
                },
            });
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/ISAPI/AccessControl/AcsEvent?format=json")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            using var resp = await http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("AcsEvent", out var acs)) break;

            if (acs.TryGetProperty("InfoList", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    var emp = Str(item, "employeeNoString") ?? Str(item, "employeeNo");
                    var time = Str(item, "time");
                    if (string.IsNullOrEmpty(emp) || string.IsNullOrEmpty(time)) continue;
                    var at = DateTimeOffset.TryParse(time, out var dto)
                        ? dto.ToString("yyyy-MM-ddTHH:mm:ss")
                        : time.Length >= 19 ? time[..19] : time;
                    var dir = (Str(item, "attendanceStatus") ?? "").Contains("out", StringComparison.OrdinalIgnoreCase) ? "out" : "in";
                    result.Add(new RawEvent(emp!, at, dir, Str(item, "doorName") ?? meta.TurnstileHost));
                }
            }

            var statusStr = Str(acs, "responseStatusStrg");
            var num = acs.TryGetProperty("numOfMatches", out var n) && n.TryGetInt32(out var ni) ? ni : 0;
            pos += num;
            if (num < pageSize || !string.Equals(statusStr, "MORE", StringComparison.OrdinalIgnoreCase)) break;
        }
        return result;
    }

    private static string? Str(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v)
            ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString()
            : null;
}

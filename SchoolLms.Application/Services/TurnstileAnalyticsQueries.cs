using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  TURNIKET ANALITIKASI — FAQAT O'QISH.
//  docs/modules/existing-module-gaps.md §4: #11, #12, #13.
// ===========================================================================
//
//  NEGA "QUERIES", "SERVICE" EMAS
//  ------------------------------
//  Bu klass hech narsa YOZMAYDI: `Add` ham, `SaveChanges` ham yo'q, hamma
//  so'rov `AsNoTracking`. `FinanceReportQueries` bilan bir xil naqsh —
//  nom o'zi shuni aytib turadi. Jonli sinxronlash (`TurnstileService`,
//  `TurnstileLiveService`) hech qanday o'zgarishsiz ishlashda davom etadi.
//
//  YANGI JADVAL YO'Q
//  -----------------
//  Hodisalar allaqachon yig'iladi (`turnstile_events`) — biz EduSchool
//  ko'rsatganidan KO'PROQ saqlaymiz. Yetishmayotgani ekran edi.
//
//  UCHTA QOIDA, BUTUN FAYL BO'YLAB BIR XIL
//  ---------------------------------------
//  1. Kunning BIRINCHI o'tishi = kirish, OXIRGI o'tishi = chiqish. Bu aynan
//     "O'quvchilar turniketi" sahifasidagi qoida (`BuildStudentDashboardAsync`),
//     shuning uchun ikki ekran bir xil raqamni ko'rsatadi. Qurilmaning
//     `Direction` maydoniga TAYANMAYMIZ: Hikvision ISAPI uni faqat
//     `attendanceStatus` matnida "out" bo'lsa beradi, ko'p o'rnatmada esa
//     hamma hodisa "in" bo'lib keladi — yo'nalishga ishonsak chiqishlar
//     jimgina nolga aylanardi.
//  2. Qurilma ID biriktirilmagan o'quvchi hisobotdan TASHQARIDA. Turniket
//     uni ko'ra olmaydi; "kelmadi" deb yozish yolg'on bo'lardi. Ular alohida
//     sanaladi (`Unlinked`) va `status=unlinked` filtri bilan ro'yxatlanadi —
//     direktor kimni biriktirish kerakligini ko'rsin.
//  3. O'quv kuni = yakshanba emas + bayram emas + chorak (dars jadvali)
//     davri ichida + sinfda o'sha hafta kuni darsi bor. Shu kunlardagina
//     "kelmadi" deyish mumkin.
//
//  KECHIKISH — "O'QITUVCHILAR DAVOMATI" BILAN BIR XIL FORMULA
//  ----------------------------------------------------------
//  `TurnstileService.RecomputeAsync` o'qituvchi uchun: kelgan vaqt kutilgan
//  vaqt + grace dan keyin bo'lsa — kechikdi. Bu yerda ham xuddi shunday,
//  faqat kutilgan vaqt boshqa manbadan: o'quvchi uchun bu SINFINING o'sha
//  kungi BIRINCHI darsi boshlanishi (dars jadvali + qo'ng'iroqlar jadvali).
//  Sinfda jadval umuman bo'lmasa — maktabning ish boshlanish vaqti
//  (`SchoolMeta.WorkStartTime`) zaxira sifatida ishlatiladi.
//
//  UNUMDORLIK
//  ----------
//  Har bir hisobot sanab bo'ladigan miqdordagi so'rov yuboradi (6-9 ta),
//  o'quvchilar yoki kunlar soniga BOG'LIQ EMAS — sikl ichida `await` yo'q.
//  `turnstile_events` da `(device_user_id, event_at)` indeksi BOR
//  (`ParityWave2Schema` migratsiyasi), shuning uchun oraliq bo'yicha filtr
//  butun jadvalni skanerlamaydi. Oraliq baribir <see cref="MaxRangeDays"/>
//  kun bilan cheklangan — bu endi indeks emas, JAVOB HAJMI chegarasi.

/// <summary>
/// Turniket hisobotlari: davomat (#11), kirib-chiqish statistikasi (#12) va
/// kunlik davomat hisoboti (#13). Fayl boshidagi izohda — qoidalar.
/// </summary>
public class TurnstileAnalyticsQueries(IAppDbContext db)
{
    /// <summary>
    /// Eng uzun so'raladigan oraliq (kun). Undan uzunini controller rad etadi.
    ///
    /// <para>
    /// Chegara 92 kundan bir o'quv yiliga (366 kun) kengaytirildi:
    /// <c>ParityWave2Schema</c> migratsiyasi <c>turnstile_events</c> ga
    /// <c>(device_user_id, event_at)</c> indeksini qo'shdi, ya'ni oraliq
    /// bo'yicha filtr endi butun jadvalni skanerlamaydi. Chegara mutlaqo
    /// olib tashlanmadi: hisobot xotirada yig'iladi va "butun tarix" so'rovi
    /// bitta sahifada o'qib bo'lmaydigan javob qaytarardi.
    /// </para>
    /// </summary>
    public const int MaxRangeDays = 366;

    /// <summary>Kelishmovchiliklar ro'yxati shu sondan uzun bo'lsa kesiladi (jami soni baribir qaytadi).</summary>
    private const int MaxMismatches = 300;

    // =====================================================================
    //  #11 — Turniket davomati (kim kirdi, kim umuman kirmadi)
    // =====================================================================

    /// <summary>
    /// Tanlangan oraliq uchun har o'quvchi: necha kun kirgan, necha kun kirmagan,
    /// necha marta kechikkan va erta ketgan.
    /// </summary>
    /// <param name="status">all | entered | missing | late | early | unlinked.</param>
    public async Task<TurnstileAttendanceReportDto> AttendanceAsync(
        DateOnly from, DateOnly to, string? className, string? status, CancellationToken ct = default)
    {
        var snap = await LoadAsync(from, to, className, ct);
        var facts = Facts(snap).ToList();
        var byStudent = facts.GroupBy(f => f.Student.Id).ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<TurnstileStudentRowDto>();
        int entered = 0, never = 0, lateStudents = 0, earlyStudents = 0;
        int lateDays = 0, earlyDays = 0, noExitDays = 0, enteredDays = 0, expectedDays = 0;

        foreach (var st in snap.Students.Where(s => !string.IsNullOrEmpty(s.DeviceUserId)))
        {
            var days = byStudent.GetValueOrDefault(st.Id) ?? [];
            var withPass = days.Where(d => d.Passes > 0).ToList();
            var missed = days.Count(d => d.Passes == 0 && d.IsPast);
            var late = withPass.Count(d => d.Late);
            var early = withPass.Count(d => d.Early);

            enteredDays += withPass.Count;
            expectedDays += days.Count;
            lateDays += late;
            earlyDays += early;
            noExitDays += withPass.Count(d => d.Passes == 1);
            if (withPass.Count > 0) entered++; else never++;
            if (late > 0) lateStudents++;
            if (early > 0) earlyStudents++;

            var avgIn = AverageTime(withPass.Select(d => d.CheckIn));
            var last = withPass.Count > 0 ? withPass[^1] : null;

            rows.Add(new TurnstileStudentRowDto(
                st.Id, st.FullName, st.ClassName ?? "", st.DeviceUserId,
                withPass.Count, missed, late, early,
                withPass.Sum(d => d.LateMinutes),
                avgIn,
                last is null ? "" : $"{last.Date} {(last.CheckOut.Length == 5 ? last.CheckOut : last.CheckIn)}",
                Pct(withPass.Count, days.Count)));
        }

        // Biriktirilmaganlar — ATAYLAB alohida ro'yxat: ular "kelmagan" emas,
        // ular UMUMAN KO'RINMAYDI. Ikkalasini aralashtirish hisobotni yolg'onga aylantiradi.
        var unlinked = snap.Students.Where(s => string.IsNullOrEmpty(s.DeviceUserId)).ToList();
        if (Same(status, "unlinked"))
            rows = [.. unlinked.Select(s => new TurnstileStudentRowDto(
                s.Id, s.FullName, s.ClassName ?? "", "", 0, 0, 0, 0, 0, "", "", 0))];
        else
            rows = [.. rows.Where(r => Same(status, "entered") ? r.DaysEntered > 0
                : Same(status, "missing") ? r.DaysEntered == 0
                : Same(status, "late") ? r.LateDays > 0
                : Same(status, "early") ? r.EarlyDays > 0
                : true)];

        var linked = snap.Students.Count - unlinked.Count;
        var summary = new TurnstileAttendanceSummaryDto(
            snap.Students.Count, linked, unlinked.Count,
            entered, never, lateStudents, lateDays, earlyStudents, earlyDays, noExitDays,
            Pct(enteredDays, expectedDays), Pct(lateDays, enteredDays));

        return new TurnstileAttendanceReportDto(
            Iso(from), Iso(to), snap.SchoolDays.Count,
            snap.Meta?.TurnstileEnabled ?? false, snap.Meta?.TurnstileLastSync ?? "",
            summary, rows);
    }

    /// <summary>
    /// Buzilishlar ro'yxati — sahifalangan: kechikib kelgan va darslar tugamasdan
    /// chiqib ketgan o'quvchi-kunlar. Eng yangisi tepada.
    /// </summary>
    /// <param name="type">all | late | early.</param>
    public async Task<TurnstileViolationsPageDto> ViolationsAsync(
        DateOnly from, DateOnly to, string? className, string? type,
        int page, int pageSize, CancellationToken ct = default)
    {
        var snap = await LoadAsync(from, to, className, ct);
        var facts = Facts(snap).Where(f => f.Passes > 0).ToList();

        var all = new List<TurnstileViolationDto>();
        foreach (var f in facts)
        {
            if (f.Late)
                all.Add(new TurnstileViolationDto(
                    f.Date, f.Student.Id, f.Student.FullName, f.Student.ClassName ?? "",
                    "late", f.CheckIn, f.CheckOut, f.ExpectedIn, f.LateMinutes));
            if (f.Early)
                all.Add(new TurnstileViolationDto(
                    f.Date, f.Student.Id, f.Student.FullName, f.Student.ClassName ?? "",
                    "early", f.CheckIn, f.CheckOut, f.ExpectedOut, f.EarlyMinutes));
        }

        var lateTotal = all.Count(v => v.Type == "late");
        var earlyTotal = all.Count - lateTotal;

        var filtered = all
            .Where(v => Same(type, "late") ? v.Type == "late" : Same(type, "early") ? v.Type == "early" : true)
            .OrderByDescending(v => v.Date, StringComparer.Ordinal)
            .ThenByDescending(v => v.Minutes)
            .ThenBy(v => v.ClassName, StringComparer.Ordinal)
            .ThenBy(v => v.FullName, StringComparer.Ordinal)
            .ToList();

        var size = Math.Clamp(pageSize, 1, 200);
        var pages = (int)Math.Ceiling(filtered.Count / (double)size);
        var current = Math.Clamp(page, 1, Math.Max(1, pages));

        return new TurnstileViolationsPageDto(
            Iso(from), Iso(to), filtered.Count, lateTotal, earlyTotal,
            current, size, pages,
            [.. filtered.Skip((current - 1) * size).Take(size)]);
    }

    /// <summary>Kechikish/erta ketish jamlamasi — kunlar bo'yicha qator va sinflar kesimi.</summary>
    public async Task<TurnstileLateEarlySummaryDto> LateEarlyAsync(
        DateOnly from, DateOnly to, string? className, CancellationToken ct = default)
    {
        var snap = await LoadAsync(from, to, className, ct);
        var facts = Facts(snap).ToList();

        var days = facts
            .GroupBy(f => f.Date)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var entered = g.Count(f => f.Passes > 0);
                return new TurnstileDaySummaryDto(
                    g.Key, g.Count(), entered, g.Count() - entered,
                    g.Count(f => f.Late), g.Count(f => f.Early),
                    Pct(g.Count(f => f.Late), entered));
            })
            .ToList();

        var classes = facts
            .GroupBy(f => f.Student.ClassName ?? "")
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var withPass = g.Where(f => f.Passes > 0).ToList();
                return new TurnstileClassSummaryDto(
                    g.Key, g.Select(f => f.Student.Id).Distinct().Count(),
                    withPass.Count, g.Count(f => f.Late), g.Count(f => f.Early),
                    Pct(g.Count(f => f.Late), withPass.Count),
                    AverageTime(withPass.Select(f => f.CheckIn)));
            })
            .ToList();

        var lateTotal = facts.Count(f => f.Late);
        var earlyTotal = facts.Count(f => f.Early);
        var enteredTotal = facts.Count(f => f.Passes > 0);

        return new TurnstileLateEarlySummaryDto(
            Iso(from), Iso(to), snap.SchoolDays.Count,
            lateTotal, earlyTotal, Pct(lateTotal, enteredTotal), Pct(earlyTotal, enteredTotal),
            days, classes);
    }

    /// <summary>Bugungi kechikkanlar soni — bosh sahifa/sarlavha uchun bitta raqam.</summary>
    public async Task<TurnstileTodayLateDto> TodayLateAsync(DateOnly? day = null, CancellationToken ct = default)
    {
        var date = day ?? AppClock.Today;
        var snap = await LoadAsync(date, date, null, ct);
        var facts = Facts(snap).ToList();

        var entered = facts.Count(f => f.Passes > 0);
        var lastEvent = snap.Passes.Values
            .Where(v => v.Count > 0).Select(v => v[^1])
            .DefaultIfEmpty("").Max(StringComparer.Ordinal) ?? "";

        return new TurnstileTodayLateDto(
            Iso(date), snap.Meta?.TurnstileEnabled ?? false, snap.Meta?.TurnstileLastSync ?? "",
            snap.SchoolDays.Contains(Iso(date)),
            facts.Count(f => f.Late), entered, facts.Count, facts.Count - entered,
            lastEvent);
    }

    // =====================================================================
    //  #12 — Kirib-chiqish statistikasi (kun davomidagi taqsimot)
    // =====================================================================

    /// <summary>
    /// Kirish va chiqishlarning kun davomidagi taqsimoti — soat yoki dars vaqti kesimida,
    /// maktab bo'yicha va sinflar kesimida.
    /// </summary>
    /// <param name="groupBy">"hour" (sukut) yoki "period" (dars vaqtlari).</param>
    public async Task<TurnstileFlowReportDto> FlowAsync(
        DateOnly from, DateOnly to, string? className, string? groupBy, CancellationToken ct = default)
    {
        var snap = await LoadAsync(from, to, className, ct);
        var facts = Facts(snap).Where(f => f.Passes > 0).ToList();

        var byPeriod = Same(groupBy, "period") && snap.Periods.Count > 0;
        var mode = byPeriod ? "period" : "hour";

        // Har bir kirish/chiqish/o'tish o'z oralig'iga tushadi.
        var entered = new Dictionary<int, int>();
        var exited = new Dictionary<int, int>();
        var passes = new Dictionary<int, int>();

        foreach (var f in facts)
        {
            Bump(entered, Bucket(snap, f.CheckIn, byPeriod));
            if (f.CheckOut.Length == 5) Bump(exited, Bucket(snap, f.CheckOut, byPeriod));
            foreach (var t in f.Times) Bump(passes, Bucket(snap, t, byPeriod));
        }

        var keys = entered.Keys.Concat(exited.Keys).Concat(passes.Keys).Distinct().ToList();
        var buckets = new List<TurnstileFlowBucketDto>();
        if (keys.Count > 0)
        {
            // Soat kesimida bo'shliq qoldirmaymiz: eng erta va eng kech oraliq orasidagi
            // hamma soat chiqadi, aks holda ustunli grafik kunni noto'g'ri ko'rsatardi.
            List<int> ordered = byPeriod
                ? [.. keys.OrderBy(k => k)]
                : [.. Enumerable.Range(keys.Min(), keys.Max() - keys.Min() + 1)];
            var totalEntered = entered.Values.Sum();
            buckets = [.. ordered.Select(k => new TurnstileFlowBucketDto(
                BucketLabel(k, byPeriod), BucketStart(snap, k, byPeriod),
                entered.GetValueOrDefault(k), exited.GetValueOrDefault(k), passes.GetValueOrDefault(k),
                Pct(entered.GetValueOrDefault(k), totalEntered)))];
        }

        var peakKey = entered.Count == 0 ? int.MinValue : entered.MaxBy(kv => kv.Value).Key;
        var afterPeak = entered.Where(kv => kv.Key > peakKey).Sum(kv => kv.Value);

        var classes = facts
            .GroupBy(f => f.Student.ClassName ?? "")
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var own = new Dictionary<int, int>();
                foreach (var f in g) Bump(own, Bucket(snap, f.CheckIn, byPeriod));
                var pk = own.Count == 0 ? int.MinValue : own.MaxBy(kv => kv.Value).Key;
                return new TurnstileFlowClassDto(
                    g.Key, g.Select(f => f.Student.Id).Distinct().Count(),
                    g.Count(), g.Count(f => f.CheckOut.Length == 5),
                    own.Count == 0 ? "" : BucketLabel(pk, byPeriod),
                    own.Count == 0 ? 0 : own[pk],
                    own.Where(kv => kv.Key > pk).Sum(kv => kv.Value),
                    AverageTime(g.Select(f => f.CheckIn)));
            })
            .ToList();

        var checkIns = facts.Select(f => f.CheckIn).ToList();
        return new TurnstileFlowReportDto(
            Iso(from), Iso(to), mode, snap.SchoolDays.Count,
            entered.Values.Sum(), exited.Values.Sum(), passes.Values.Sum(),
            entered.Count == 0 ? "" : BucketLabel(peakKey, byPeriod),
            entered.Count == 0 ? 0 : entered[peakKey],
            afterPeak, Pct(afterPeak, entered.Values.Sum()),
            AverageTime(checkIns),
            checkIns.Count == 0 ? "" : checkIns.Min(StringComparer.Ordinal)!,
            checkIns.Count == 0 ? "" : checkIns.Max(StringComparer.Ordinal)!,
            buckets, classes);
    }

    // =====================================================================
    //  #13 — Kunlik davomat hisoboti (turniket ↔ jurnal)
    // =====================================================================

    /// <summary>
    /// Bir kun, bir sahifa: har sinf bo'yicha kutilgan, turniketdan o'tgan, jurnalda "bor"
    /// belgilangan — va ular ORASIDAGI FARQ. Farq — hisobotning butun mag'zi.
    /// </summary>
    public async Task<TurnstileDailyReportDto> DailyReportAsync(DateOnly date, CancellationToken ct = default)
    {
        var iso = Iso(date);
        var snap = await LoadAsync(date, date, null, ct);
        var facts = Facts(snap).ToDictionary(f => f.Student.Id);

        // Jurnal — "bor" ning ta'rifi Bosh sahifadagi davomat bloki bilan AYNAN bir xil:
        // yo'qlik = sababi bor va u "kech keldi" turidan emas. Kech kelgan bola DARSDA EDI.
        var lateReasonIds = (await db.AbsenceReasons.AsNoTracking()
            .Where(r => r.IsLate).Select(r => r.Id).ToListAsync(ct)).ToHashSet();
        var reasonNames = await db.AbsenceReasons.AsNoTracking()
            .ToDictionaryAsync(r => r.Id, r => r.Name, ct);
        var entries = await db.JournalEntries.AsNoTracking()
            .Where(e => e.Date == iso)
            .Select(e => new { e.StudentId, e.ReasonId })
            .ToListAsync(ct);
        var marks = entries.GroupBy(e => e.StudentId).ToDictionary(g => g.Key, g => g.ToList());

        var classIds = await db.Classes.AsNoTracking()
            .Where(c => !c.IsArchived)
            .OrderBy(c => c.Grade).ThenBy(c => c.Name)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct);
        var idByName = classIds
            .GroupBy(c => c.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);
        var order = classIds.Select((c, i) => (c.Name, i))
            .GroupBy(x => x.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().i, StringComparer.Ordinal);

        var wd = Weekday(iso);
        var rows = new List<TurnstileDailyClassRowDto>();
        var mismatches = new List<TurnstileDailyMismatchDto>();
        int expected = 0, linked = 0, tsEntered = 0, jPresent = 0, jAbsent = 0, jUnchecked = 0,
            tsOnly = 0, jOnly = 0;

        foreach (var group in snap.Students
            .GroupBy(s => s.ClassName ?? "", StringComparer.Ordinal)
            .OrderBy(g => order.GetValueOrDefault(g.Key, int.MaxValue))
            .ThenBy(g => g.Key, StringComparer.Ordinal))
        {
            // Jadvalga ko'ra bu sinfda bugun dars yo'q bo'lsa — qatorni umuman chiqarmaymiz,
            // aks holda shanba kuni "davomat olinmagan" degan soxta raqam paydo bo'lardi.
            if (!DayExpectation(snap, group.Key, wd).SchoolDay) continue;

            int cExpected = 0, cLinked = 0, cTs = 0, cPresent = 0, cAbsent = 0, cUnchecked = 0,
                cTsOnly = 0, cJOnly = 0;

            foreach (var st in group)
            {
                cExpected++;
                var hasDevice = !string.IsNullOrEmpty(st.DeviceUserId);
                if (hasDevice) cLinked++;

                var fact = facts.GetValueOrDefault(st.Id);
                var seen = fact is { Passes: > 0 };
                if (seen) cTs++;

                var own = marks.GetValueOrDefault(st.Id);
                var status = own is null || own.Count == 0
                    ? "unchecked"
                    : own.Any(e => e.ReasonId is null || lateReasonIds.Contains(e.ReasonId))
                        ? "present"
                        : "absent";
                if (status == "present") cPresent++;
                else if (status == "absent") cAbsent++;
                else cUnchecked++;

                // Kelishmovchilik faqat BIRIKTIRILGAN o'quvchi uchun ma'noga ega:
                // qurilma ID yo'q bolani turniket ko'rmaydi, bu farq emas — bu bo'shliq.
                if (!hasDevice) continue;
                var reason = own?.FirstOrDefault(e => e.ReasonId is not null && !lateReasonIds.Contains(e.ReasonId));
                if (seen && status == "absent")
                {
                    cTsOnly++;
                    mismatches.Add(new TurnstileDailyMismatchDto(
                        st.Id, st.FullName, group.Key, "turnstile-only",
                        fact!.CheckIn, fact.CheckOut, status,
                        reason?.ReasonId is { } rid ? reasonNames.GetValueOrDefault(rid, "") : ""));
                }
                else if (!seen && status == "present")
                {
                    cJOnly++;
                    mismatches.Add(new TurnstileDailyMismatchDto(
                        st.Id, st.FullName, group.Key, "journal-only", "", "", status, ""));
                }
            }

            expected += cExpected; linked += cLinked; tsEntered += cTs;
            jPresent += cPresent; jAbsent += cAbsent; jUnchecked += cUnchecked;
            tsOnly += cTsOnly; jOnly += cJOnly;

            rows.Add(new TurnstileDailyClassRowDto(
                idByName.GetValueOrDefault(group.Key, ""), group.Key,
                cExpected, cLinked, cTs, cPresent, cAbsent, cUnchecked,
                cTs - cPresent, cTsOnly, cJOnly,
                Pct(cTs, cExpected), Pct(cPresent, cExpected)));
        }

        return new TurnstileDailyReportDto(
            iso, snap.SchoolDays.Contains(iso),
            snap.Meta?.TurnstileEnabled ?? false, snap.Meta?.TurnstileLastSync ?? "",
            expected, linked, expected - linked,
            tsEntered, jPresent, jAbsent, jUnchecked,
            tsEntered - jPresent, tsOnly, jOnly,
            Pct(tsEntered, expected), Pct(jPresent, expected),
            rows,
            [.. mismatches.Take(MaxMismatches)], mismatches.Count);
    }

    // =====================================================================
    //  Umumiy yuklash — barcha hisobot shu bitta suratdan oziqlanadi
    // =====================================================================

    /// <summary>Bir o'quvchining bir kuni — hamma hisobotning qurilish g'ishti.</summary>
    private sealed record DayFact(
        Student Student, string Date, List<string> Times,
        string ExpectedIn, string ExpectedOut, bool IsPast)
    {
        public int Passes => Times.Count;
        public string CheckIn => Times.Count > 0 ? Times[0] : "";
        public string CheckOut => Times.Count > 1 ? Times[^1] : "";
        public bool Late { get; init; }
        public int LateMinutes { get; init; }
        public bool Early { get; init; }
        public int EarlyMinutes { get; init; }
    }

    private sealed record Snapshot(
        SchoolMeta? Meta, int Grace, List<Student> Students, List<string> SchoolDays,
        Dictionary<string, string[]> ClassStart, Dictionary<string, string[]> ClassEnd,
        Dictionary<string, List<string>> Passes, List<LessonTime> Periods, string Today);

    private async Task<Snapshot> LoadAsync(DateOnly from, DateOnly to, string? className, CancellationToken ct)
    {
        var meta = await db.SchoolMeta.AsNoTracking().FirstOrDefaultAsync(ct);

        var studentsQuery = db.Students.AsNoTracking().Where(s => !s.IsArchived);
        if (!string.IsNullOrWhiteSpace(className))
            studentsQuery = studentsQuery.Where(s => s.ClassName == className);
        var students = await studentsQuery
            .OrderBy(s => s.ClassName).ThenBy(s => s.FullName)
            .ToListAsync(ct);

        // Hodisalar: [from .. to+1kun) — satr sanalar leksikografik tartibda xronologik.
        var fromIso = Iso(from);
        var toExclusive = Iso(to.AddDays(1));
        var events = await db.TurnstileEvents.AsNoTracking()
            .Where(e => e.DeviceUserId != ""
                && string.Compare(e.EventAt, fromIso) >= 0
                && string.Compare(e.EventAt, toExclusive) < 0)
            .Select(e => new { e.DeviceUserId, e.EventAt })
            .ToListAsync(ct);

        // Bitta qurilma ID bir nechta o'quvchiga tegishli bo'lib qolsa — birinchisi olinadi
        // (bu ma'lumot xatosi, hisobot uni yashirmaydi, shunchaki yiqilmaydi).
        var byDevice = students
            .Where(s => !string.IsNullOrEmpty(s.DeviceUserId))
            .GroupBy(s => s.DeviceUserId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);

        var passes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var e in events)
        {
            if (e.EventAt.Length < 16) continue;
            if (!byDevice.TryGetValue(e.DeviceUserId, out var studentId)) continue;
            var key = studentId + "|" + e.EventAt[..10];
            if (!passes.TryGetValue(key, out var list)) passes[key] = list = [];
            list.Add(e.EventAt.Substring(11, 5));
        }
        foreach (var list in passes.Values) list.Sort(StringComparer.Ordinal);

        var quarters = await TeacherSalaryCalc.QuarterRangesAsync(db);
        var holidays = (await db.Holidays.AsNoTracking().Select(h => h.Date).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var schoolDays = new List<string>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var iso = Iso(d);
            if (Weekday(iso) >= 6) continue;                       // yakshanba
            if (holidays.Contains(iso)) continue;                  // bayram
            if (!TeacherSalaryCalc.InQuarter(iso, quarters)) continue; // ta'til / chorakdan tashqari
            schoolDays.Add(iso);
        }

        var periods = await db.LessonTimes.AsNoTracking()
            .Where(t => t.StartTime != "").OrderBy(t => t.Period).ToListAsync(ct);
        var (start, end) = await ClassBoundsAsync(periods, ct);

        return new Snapshot(
            meta, meta?.LateGraceMinutes ?? 0, students, schoolDays,
            start, end, passes, periods, Iso(AppClock.Today));
    }

    /// <summary>
    /// Sinf nomi → har hafta kunidagi BIRINCHI dars boshlanishi va OXIRGI dars tugashi.
    /// Manba: sinfning asosiy dars jadvali varianti (eng ko'p darsli) + qo'ng'iroqlar jadvali.
    /// </summary>
    private async Task<(Dictionary<string, string[]> Start, Dictionary<string, string[]> End)>
        ClassBoundsAsync(List<LessonTime> periods, CancellationToken ct)
    {
        var classNames = await db.Classes.AsNoTracking()
            .Where(c => !c.IsArchived)
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var templates = (await db.ScheduleTemplates.AsNoTracking().Include(t => t.Lessons).ToListAsync(ct))
            .Where(t => classNames.ContainsKey(t.ClassId))
            .GroupBy(t => t.ClassId)
            .Select(g => g.OrderByDescending(t => t.Lessons.Count).ThenBy(t => t.Id).First());

        var startTime = periods.GroupBy(t => t.Period)
            .ToDictionary(g => g.Key, g => Clock(g.First().StartTime));
        var endTime = periods.GroupBy(t => t.Period)
            .ToDictionary(g => g.Key, g => Clock(g.First().EndTime));

        var start = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var end = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var tpl in templates)
        {
            var name = classNames[tpl.ClassId];
            var first = new int[6];
            var last = new int[6];
            foreach (var l in tpl.Lessons.Where(l => l.Day is >= 0 and < 6 && l.Period > 0))
            {
                if (first[l.Day] == 0 || l.Period < first[l.Day]) first[l.Day] = l.Period;
                if (l.Period > last[l.Day]) last[l.Day] = l.Period;
            }
            start[name] = [.. first.Select(p => p > 0 ? startTime.GetValueOrDefault(p, "") : "")];
            end[name] = [.. last.Select(p => p > 0 ? endTime.GetValueOrDefault(p, "") : "")];
        }
        return (start, end);
    }

    /// <summary>
    /// Sinf uchun o'sha hafta kunidagi kutilgan kelish va ketish vaqti.
    /// Jadvali bor, lekin o'sha kuni darsi yo'q sinf uchun — bu O'QUV KUNI EMAS.
    /// Jadvali umuman yo'q sinf uchun zaxira: maktabning ish boshlanish vaqti.
    /// </summary>
    private static (bool SchoolDay, string In, string Out) DayExpectation(Snapshot snap, string className, int weekday)
    {
        if (weekday is < 0 or > 5) return (false, "", "");
        if (snap.ClassStart.TryGetValue(className, out var starts))
        {
            var inTime = starts[weekday];
            if (inTime.Length != 5) return (false, "", "");
            var outTime = snap.ClassEnd.TryGetValue(className, out var ends) ? ends[weekday] : "";
            return (true, inTime, outTime);
        }
        return (true, Clock(snap.Meta?.WorkStartTime ?? ""), "");
    }

    /// <summary>O'quvchi-kun faktlari: kelish, ketish, kechikish va erta ketish.</summary>
    private IEnumerable<DayFact> Facts(Snapshot snap)
    {
        foreach (var st in snap.Students)
        {
            // Biriktirilmagan o'quvchi — turniket uchun ko'rinmas (fayl boshidagi 2-qoida).
            if (string.IsNullOrEmpty(st.DeviceUserId)) continue;
            var enrolled = st.EnrollmentDate.Length >= 10 ? st.EnrollmentDate[..10] : null;

            foreach (var date in snap.SchoolDays)
            {
                // Maktabga hali qabul qilinmagan kun — "kelmadi" emas.
                if (enrolled is not null && string.CompareOrdinal(date, enrolled) < 0) continue;
                var (schoolDay, expIn, expOut) = DayExpectation(snap, st.ClassName ?? "", Weekday(date));
                if (!schoolDay) continue;

                var times = snap.Passes.GetValueOrDefault(st.Id + "|" + date) ?? [];
                var fact = new DayFact(st, date, times, expIn, expOut,
                    string.CompareOrdinal(date, snap.Today) < 0);

                if (times.Count == 0) { yield return fact; continue; }

                var lateBy = Diff(expIn, fact.CheckIn);
                var late = expIn.Length == 5 && lateBy > snap.Grace;
                // Erta ketish faqat CHIQISH qayd etilgan kunda aniqlanadi: bitta o'tish
                // bo'lsa bola qachon chiqqani NOMA'LUM, taxmin qilmaymiz.
                var earlyBy = fact.CheckOut.Length == 5 ? Diff(fact.CheckOut, expOut) : 0;
                var early = expOut.Length == 5 && fact.CheckOut.Length == 5 && earlyBy > snap.Grace;

                yield return fact with
                {
                    Late = late,
                    LateMinutes = late ? Math.Max(0, lateBy) : 0,
                    Early = early,
                    EarlyMinutes = early ? Math.Max(0, earlyBy) : 0,
                };
            }
        }
    }

    // =====================================================================
    //  Kichik yordamchilar
    // =====================================================================

    /// <summary>#12 uchun oraliq kaliti: soat (0-23) yoki dars tartibi (-1 = darsgacha, 99 = darsdan keyin).</summary>
    private static int Bucket(Snapshot snap, string hhmm, bool byPeriod)
    {
        var m = Minutes(hhmm);
        if (m < 0) return byPeriod ? -1 : 0;
        if (!byPeriod) return m / 60;

        var first = Minutes(Clock(snap.Periods[0].StartTime));
        if (first >= 0 && m < first) return -1;                       // darslardan oldin
        var lastEnd = Minutes(Clock(snap.Periods[^1].EndTime));
        if (lastEnd >= 0 && m >= lastEnd) return 99;                  // darslardan keyin
        var current = snap.Periods[0].Period;
        foreach (var p in snap.Periods)
        {
            var s = Minutes(Clock(p.StartTime));
            if (s >= 0 && m >= s) current = p.Period;
        }
        return current;
    }

    private static string BucketLabel(int key, bool byPeriod) =>
        !byPeriod ? $"{key:D2}:00"
        : key == -1 ? "Darslardan oldin"
        : key == 99 ? "Darslardan keyin"
        : $"{key}-dars";

    private static string BucketStart(Snapshot snap, int key, bool byPeriod)
    {
        if (!byPeriod) return $"{key:D2}:00";
        if (key == -1) return "";
        if (key == 99) return Clock(snap.Periods[^1].EndTime);
        var p = snap.Periods.FirstOrDefault(x => x.Period == key);
        return p is null ? "" : Clock(p.StartTime);
    }

    private static void Bump(Dictionary<int, int> map, int key) =>
        map[key] = map.GetValueOrDefault(key) + 1;

    /// <summary>"HH:mm" → yarim tundan beri o'tgan daqiqalar; noto'g'ri satr uchun -1.</summary>
    private static int Minutes(string hhmm)
    {
        if (hhmm.Length < 5 || hhmm[2] != ':') return -1;
        return int.TryParse(hhmm[..2], out var h) && int.TryParse(hhmm.Substring(3, 2), out var m)
            && h is >= 0 and < 24 && m is >= 0 and < 60 ? h * 60 + m : -1;
    }

    /// <summary>Ikki "HH:mm" orasidagi farq (daqiqa). Biri noto'g'ri bo'lsa 0 — taxmin qilmaymiz.</summary>
    private static int Diff(string fromHhmm, string toHhmm)
    {
        int a = Minutes(fromHhmm), b = Minutes(toHhmm);
        return a < 0 || b < 0 ? 0 : b - a;
    }

    /// <summary>Vaqtni "HH:mm" ga keltiradi ("08:30:00" ham keladi).</summary>
    private static string Clock(string time) => time.Length >= 5 ? time[..5] : time;

    /// <summary>O'rtacha vaqt "HH:mm" — bo'sh ro'yxat uchun bo'sh satr.</summary>
    private static string AverageTime(IEnumerable<string> times)
    {
        var mins = times.Select(Minutes).Where(m => m >= 0).ToList();
        if (mins.Count == 0) return "";
        var avg = (int)Math.Round(mins.Average());
        return $"{avg / 60:D2}:{avg % 60:D2}";
    }

    /// <summary>Foiz — SERVERDA yaxlitlanadi, brauzerda emas (ikki ekran bir xil raqam ko'rsatsin).</summary>
    private static double Pct(int part, int whole) =>
        whole <= 0 ? 0 : Math.Round(part * 100.0 / whole, 1);

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd");

    /// <summary>Dushanba=0 ... Shanba=5, Yakshanba=6 (loyiha bo'ylab bir xil).</summary>
    private static int Weekday(string date) =>
        DateOnly.TryParse(date, out var d) ? ((int)d.DayOfWeek + 6) % 7 : 6;

    private static bool Same(string? value, string expected) =>
        string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
}

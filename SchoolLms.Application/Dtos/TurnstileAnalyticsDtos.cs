namespace SchoolLms.Application.Dtos;

// ===========================================================================
//  TURNIKET ANALITIKASI — hisobot DTO'lari (faqat o'qish).
//  docs/modules/existing-module-gaps.md §4, #11, #12, #13.
//
//  Hodisalar ALLAQACHON yig'iladi (`TurnstileEvent`) — bu yerda yangi jadval
//  ham, yangi ustun ham yo'q. Faqat ekranlar yetishmayotgan edi.
//
//  Barcha foiz va o'rtachalar SERVERDA hisoblanadi: brauzerda hisoblansa
//  ikkita sahifa bir xil raqamni ikki xil yaxlitlab ko'rsatardi.
// ===========================================================================

// ---------------------------------------------------------------------------
//  #11 — Turniket analitikasi (kim kirdi, kim umuman kirmadi, kechikkanlar)
// ---------------------------------------------------------------------------

/// <summary>
/// Turniket davomati jamlamasi (tanlangan oraliq uchun).
/// <para>
/// <b>Diqqat:</b> qurilma ID biriktirilmagan o'quvchi (<see cref="Unlinked"/>) hisobotdan
/// TASHQARIDA qoladi — turniket uni ko'ra olmaydi, ya'ni "kelmadi" deyish yolg'on bo'lardi.
/// </para>
/// </summary>
/// <param name="Students">Filtrga tushgan o'quvchilar soni.</param>
/// <param name="Linked">Qurilma ID biriktirilgan (hisobotga kiradiganlar).</param>
/// <param name="Unlinked">Biriktirilmagan — hisobotdan tashqarida.</param>
/// <param name="Entered">Oraliqda kamida bir kun turniketdan o'tganlar.</param>
/// <param name="NeverEntered">Birorta kun ham o'tmaganlar.</param>
/// <param name="LateStudents">Kamida bir kun kechikkan o'quvchilar soni.</param>
/// <param name="LateDays">Kechikish hodisalari (o'quvchi-kun) soni.</param>
/// <param name="EarlyStudents">Kamida bir kun erta ketgan o'quvchilar soni.</param>
/// <param name="EarlyDays">Erta ketish hodisalari (o'quvchi-kun) soni.</param>
/// <param name="NoExitDays">Chiqishi qayd etilmagan kunlar (bitta o'tish) — erta ketishni
/// bu kunlar uchun aniqlab bo'lmaydi.</param>
/// <param name="AttendanceRate">Kelish foizi: o'tilgan kunlar / (biriktirilganlar × o'quv kunlari).</param>
/// <param name="LateRate">Kechikish foizi: kechikkan kunlar / o'tilgan kunlar.</param>
public record TurnstileAttendanceSummaryDto(
    int Students, int Linked, int Unlinked,
    int Entered, int NeverEntered,
    int LateStudents, int LateDays,
    int EarlyStudents, int EarlyDays, int NoExitDays,
    double AttendanceRate, double LateRate);

/// <summary>Bitta o'quvchining oraliq bo'yicha turniket qatori.</summary>
/// <param name="DaysEntered">Necha kun turniketdan o'tgan.</param>
/// <param name="DaysMissed">O'quv kunlaridan necha kun umuman o'tmagan.</param>
/// <param name="LateMinutes">Jami kechikish (daqiqa).</param>
/// <param name="AvgCheckIn">O'rtacha kelish vaqti "HH:mm" (faqat o'tgan kunlar bo'yicha).</param>
/// <param name="LastSeen">Oxirgi o'tish "yyyy-MM-dd HH:mm".</param>
public record TurnstileStudentRowDto(
    string StudentId, string FullName, string ClassName, string DeviceUserId,
    int DaysEntered, int DaysMissed, int LateDays, int EarlyDays, int LateMinutes,
    string AvgCheckIn, string LastSeen, double AttendanceRate);

/// <summary>#11 — turniket davomati hisoboti (kun yoki oraliq).</summary>
/// <param name="SchoolDays">Oraliqdagi o'quv kunlari (yakshanba, bayram va chorakdan
/// tashqari kunlar chiqarib tashlangan).</param>
public record TurnstileAttendanceReportDto(
    string From, string To, int SchoolDays, bool TurnstileEnabled, string LastSync,
    TurnstileAttendanceSummaryDto Summary, List<TurnstileStudentRowDto> Rows);

/// <summary>Bitta buzilish: kechikib kelgan yoki darslar tugamasdan chiqib ketgan o'quvchi-kun.</summary>
/// <param name="Type">"late" (kechikdi) | "early" (erta ketdi).</param>
/// <param name="Expected">Kutilgan vaqt "HH:mm" — kechikish uchun birinchi dars boshlanishi,
/// erta ketish uchun oxirgi dars tugashi.</param>
/// <param name="Minutes">Kechikish yoki erta ketish (daqiqa).</param>
public record TurnstileViolationDto(
    string Date, string StudentId, string FullName, string ClassName,
    string Type, string CheckIn, string CheckOut, string Expected, int Minutes);

/// <summary>#11 — buzilishlar ro'yxati (sahifalangan).</summary>
/// <param name="Pages">Jami sahifalar soni (server hisoblaydi).</param>
public record TurnstileViolationsPageDto(
    string From, string To, int Total, int LateTotal, int EarlyTotal,
    int Page, int PageSize, int Pages, List<TurnstileViolationDto> Items);

/// <summary>Kunlik kesim: o'sha kuni nechta o'quvchi kirdi, nechtasi kechikdi.</summary>
public record TurnstileDaySummaryDto(
    string Date, int Expected, int Entered, int Missing, int Late, int Early, double LateRate);

/// <summary>Sinf kesimi: sinfning kechikish intizomi.</summary>
public record TurnstileClassSummaryDto(
    string ClassName, int Students, int EnteredDays, int LateDays, int EarlyDays,
    double LateRate, string AvgCheckIn);

/// <summary>#11 — kechikish/erta ketish jamlamasi (kunlar va sinflar kesimida).</summary>
public record TurnstileLateEarlySummaryDto(
    string From, string To, int SchoolDays,
    int LateTotal, int EarlyTotal, double LateRate, double EarlyRate,
    List<TurnstileDaySummaryDto> Days, List<TurnstileClassSummaryDto> Classes);

/// <summary>#11 — bosh sahifadagi raqam: bugun nechta o'quvchi kechikdi.</summary>
/// <param name="SchoolDay">Bugun o'quv kunimi (yakshanba/bayram/chorakdan tashqari emasmi).</param>
/// <param name="NotEntered">Biriktirilgan, lekin hali o'tmaganlar.</param>
public record TurnstileTodayLateDto(
    string Date, bool TurnstileEnabled, string LastSync, bool SchoolDay,
    int Late, int Entered, int Expected, int NotEntered, string LastEventAt);

// ---------------------------------------------------------------------------
//  #12 — Turniket kirib-chiqish statistikasi (kun davomidagi taqsimot)
// ---------------------------------------------------------------------------

/// <summary>
/// Kun davomidagi bitta oraliq (soat yoki dars vaqti).
/// <para>
/// <b>Kirish/chiqish qanday sanaladi.</b> Qurilma yo'nalishni (in/out) har doim ham ishonchli
/// bermaydi, shuning uchun bu yerda O'quvchilar turniketi sahifasi bilan BIR XIL qoida
/// ishlatiladi: kunning BIRINCHI o'tishi = kirish, OXIRGI o'tishi = chiqish (o'tish bittagina
/// bo'lsa — chiqish yo'q). Shu tufayli ikki ekran bir xil raqamni ko'rsatadi.
/// </para>
/// </summary>
/// <param name="Label">Ko'rsatiladigan nom: "08:00" yoki "3-dars".</param>
/// <param name="StartsAt">Oraliq boshlanishi "HH:mm" (tartiblash uchun).</param>
/// <param name="Passes">Shu oraliqdagi BARCHA o'tishlar (kirish + chiqish + oraliq o'tishlar).</param>
/// <param name="EnteredPct">Kirishlarning shu oraliqqa to'g'ri kelgan ulushi (%).</param>
public record TurnstileFlowBucketDto(
    string Label, string StartsAt, int Entered, int Exited, int Passes, double EnteredPct);

/// <summary>Sinf kesimidagi kirish-chiqish manzarasi.</summary>
/// <param name="AfterPeak">Eng gavjum oraliqdan KEYIN kirganlar — "ertalabki to'lqindan
/// keyin sudralib kelganlar" aynan shu raqam.</param>
public record TurnstileFlowClassDto(
    string ClassName, int Students, int Entered, int Exited,
    string PeakLabel, int PeakEntered, int AfterPeak, string AvgCheckIn);

/// <summary>#12 — kirib-chiqish statistikasi hisoboti.</summary>
/// <param name="GroupBy">"hour" (soat kesimi) | "period" (dars vaqti kesimi).</param>
/// <param name="AfterPeakPct">Eng gavjum oraliqdan keyin kirganlarning ulushi (%).</param>
public record TurnstileFlowReportDto(
    string From, string To, string GroupBy, int Days,
    int Entered, int Exited, int Passes,
    string PeakLabel, int PeakEntered, int AfterPeak, double AfterPeakPct,
    string AvgCheckIn, string EarliestCheckIn, string LatestCheckIn,
    List<TurnstileFlowBucketDto> Buckets, List<TurnstileFlowClassDto> Classes);

// ---------------------------------------------------------------------------
//  #13 — Kunlik davomat hisoboti (turniket ↔ jurnal farqi)
// ---------------------------------------------------------------------------

/// <summary>
/// Kunlik hisobotning bitta sinf qatori.
/// <para>
/// <b>Hisobotning butun mag'zi — <paramref name="Gap"/>.</b> Turniket ko'rgan, jurnal esa
/// "yo'q" degan bola yo buzilgan jurnal, yo maktabdan chiqib ketgan bola. Ikkalasi ham
/// darhol tekshirilishi kerak.
/// </para>
/// <para>
/// <b>"Tekshirilmagan" HECH QACHON "bor" ga qo'shilmaydi</b> (Bosh sahifadagi davomat
/// blokidagi qoida bilan bir xil): davomati belgilanmagan o'quvchini "keldi" deb
/// hisoblash — eng oson va eng zararli xato.
/// </para>
/// </summary>
/// <param name="Expected">Sinfdagi faol o'quvchilar.</param>
/// <param name="Linked">Ulardan qurilma ID biriktirilganlar.</param>
/// <param name="TurnstileEntered">Turniketdan o'tganlar.</param>
/// <param name="JournalPresent">Jurnalda kamida bitta darsda "bor" belgilanganlar.</param>
/// <param name="JournalAbsent">Jurnalda belgilangan, lekin barcha darsda yo'q.</param>
/// <param name="Unchecked">Jurnalda umuman belgilanmaganlar (davomat olinmagan).</param>
/// <param name="Gap">Turniket − jurnal (ishorali): musbat = turniket ko'proq ko'rgan.</param>
/// <param name="TurnstileOnly">Turniket ko'rdi, jurnal "yo'q" dedi (faqat biriktirilganlar).</param>
/// <param name="JournalOnly">Jurnal "bor" dedi, turniket ko'rmadi (faqat biriktirilganlar).</param>
public record TurnstileDailyClassRowDto(
    string ClassId, string ClassName,
    int Expected, int Linked,
    int TurnstileEntered, int JournalPresent, int JournalAbsent, int Unchecked,
    int Gap, int TurnstileOnly, int JournalOnly,
    double TurnstilePct, double JournalPct);

/// <summary>Turniket bilan jurnal kelishmagan bitta o'quvchi.</summary>
/// <param name="Kind">"turnstile-only" (turniket ko'rdi, jurnal yo'q dedi)
/// | "journal-only" (jurnal bor dedi, turniket ko'rmadi).</param>
/// <param name="JournalStatus">"present" | "absent" | "unchecked".</param>
/// <param name="Reason">Jurnaldagi yo'qlik sababi (bo'lsa).</param>
public record TurnstileDailyMismatchDto(
    string StudentId, string FullName, string ClassName, string Kind,
    string CheckIn, string CheckOut, string JournalStatus, string Reason);

/// <summary>#13 — bir kunlik davomat hisoboti: turniket, jurnal va ular orasidagi farq.</summary>
/// <param name="MismatchTotal">Jami kelishmovchilik (ro'yxat kesilgan bo'lishi mumkin).</param>
public record TurnstileDailyReportDto(
    string Date, bool SchoolDay, bool TurnstileEnabled, string LastSync,
    int Expected, int Linked, int Unlinked,
    int TurnstileEntered, int JournalPresent, int JournalAbsent, int Unchecked,
    int Gap, int TurnstileOnly, int JournalOnly,
    double TurnstilePct, double JournalPct,
    List<TurnstileDailyClassRowDto> Classes,
    List<TurnstileDailyMismatchDto> Mismatches, int MismatchTotal);

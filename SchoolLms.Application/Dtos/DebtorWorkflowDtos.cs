namespace SchoolLms.Application.Dtos.Billing;

// ===========================================================================
//  Qarzdorlar bilan ISHLASH — tashqi shakllar (§3.5).
// ===========================================================================
//
//  BU YERDA PUL YO'Q, DEYARLI
//  --------------------------
//  Butun modul qarz haqidagi SUHBATNI yozadi: kim bilan gaplashildi, nima
//  deyildi, qachonga va'da berildi. Qarzning O'ZI boshqa joyda hisoblanadi
//  (`FinanceReportQueries.DebtorsAsync`, `StudentBalanceQuery`) va bu yerdan
//  HECH QACHON o'zgarmaydi. Yagona pul maydoni — <see cref="BrokenPromiseDto.Debt"/>
//  va u ham FAQAT serverda hisoblangan, faqat ko'rsatish uchun.
//
//  "JORIY HOLAT" USTUNI YO'Q — U HISOBLANADI
//  -----------------------------------------
//  <see cref="DebtorWorkflowRowDto.StatusId"/> bazadagi ustun EMAS: u eng
//  oxirgi tirik amalning holati (`Debtors.cs` izohi). Saqlangan "joriy holat"
//  tarix bilan bir kunda ziddiyatga tushardi — `students.balance` ni P1-21 da
//  nega o'chirganimiz bilan bir xil sabab.
//
//  SANALAR: `DateOnly` — kelishilgan TO'LOV KUNI (soati yo'q), `DateTimeOffset`
//  — yozuv qachon YOZILGANI (lahza). Ikkovi aralashmaydi.

/* ---------- Holat ma'lumotnomasi (`debtor_statuses`) ---------- */

/// <summary>
/// Rangli holat katalogining bitta qatori (§3.5: <c>status_name</c>,
/// <c>status_color</c>, <c>status_hint</c>).
/// </summary>
/// <param name="Id">Barqaror id — seed qatorlari <c>…0000d1</c>–<c>…0000d4</c>.</param>
/// <param name="Name">Ko'rsatiladigan nom. Unikal (baza indeksi).</param>
/// <param name="Color">Nishon rangi <c>#RRGGBB</c>. Bo'sh satr = rang yo'q,
/// UI neytral ranggni ishlatadi.</param>
/// <param name="Hint">Bu holat AYNAN qachon qo'yiladi — administratorlar
/// almashganda ma'no shu yerda qoladi.</param>
/// <param name="Position">Ro'yxatdagi tartib (kichikdan kattaga).</param>
/// <param name="IsActive">false = yangi amalda tanlab bo'lmaydi, lekin ESKI
/// amallarda ko'rinib turaveradi.</param>
public record DebtorStatusDto(
    Guid Id,
    string Name,
    string Color,
    string? Hint,
    int Position,
    bool IsActive);

/// <summary>
/// Holat yaratish va tahrirlash so'rovi (ikkovida bir xil shakl).
///
/// <para>
/// <see cref="IsActive"/> so'rovda BOR: katalogdan chiqarilgan holatni
/// qaytarishning boshqa yo'li bo'lmasligi kerak edi. Qator hech qachon
/// o'chirilmaydi — <c>DELETE</c> endpoint'i ham shu bayroqni <c>false</c>
/// qiladi, xolos.
/// </para>
/// </summary>
public record SaveDebtorStatusRequest(
    string Name,
    string? Color = null,
    string? Hint = null,
    int Position = 0,
    bool IsActive = true);

/* ---------- Amallar tarixi (`debtor_actions`) ---------- */

/// <summary>
/// Qarzdor bo'yicha bitta amal — o'quvchi kartochkasidagi tarix qatori.
/// </summary>
/// <param name="Id">Amal id'si (yumshoq o'chirish shuni oladi).</param>
/// <param name="StudentId">Qaysi o'quvchi.</param>
/// <param name="StatusId">Shu amaldan keyingi holat. null = holat o'zgarmadi.</param>
/// <param name="StatusName">Holat nomi — UI o'z lug'atini saqlamaydi.</param>
/// <param name="StatusColor">Holat rangi (<c>#RRGGBB</c> yoki bo'sh).</param>
/// <param name="Comment">Nima qilingani — MAJBURIY.</param>
/// <param name="PromisedOn">Ota-ona va'da qilgan yangi to'lov sanasi. null = va'da yo'q.</param>
/// <param name="PromiseOverdue">Va'da sanasi o'tib ketganmi. <b>Qarz ochiqligini
/// bu maydon BILMAYDI</b> — "buzilgan va'da" to'liq ta'rifi
/// <see cref="BrokenPromiseDto"/> da, chunki u qarzni ham tekshiradi.</param>
/// <param name="CreatedBy">Kim yozgan (users.id) — JWT'dan, so'rov tanasidan EMAS.</param>
/// <param name="CreatedByName">Kim yozgan (ism).</param>
/// <param name="CreatedAt">Qachon yozilgan.</param>
public record DebtorActionDto(
    Guid Id,
    string StudentId,
    Guid? StatusId,
    string? StatusName,
    string? StatusColor,
    string Comment,
    DateOnly? PromisedOn,
    bool PromiseOverdue,
    string CreatedBy,
    string CreatedByName,
    DateTimeOffset CreatedAt);

/// <summary>
/// Yangi amal yozish so'rovi.
///
/// <para>
/// <b>Kim yozayotgani so'rovda YO'Q</b> — u JWT'dan olinadi (SPEC §4.4).
/// Sana ham yo'q: amal HOZIR yozildi, orqaga surish moliyaviy yozuvda ham
/// taqiqlangan (SPEC §4.1) va bu yerda ham kerak emas.
/// </para>
/// </summary>
/// <param name="Comment">Nima qilindi. Bo'sh yoki faqat probel — 400.</param>
/// <param name="StatusId">Yangi holat (ixtiyoriy). Faol bo'lmagan holat — 400.</param>
/// <param name="PromisedOn">Kelishilgan yangi to'lov sanasi (ixtiyoriy).</param>
public record CreateDebtorActionRequest(
    string Comment,
    Guid? StatusId = null,
    DateOnly? PromisedOn = null);

/* ---------- Qarzdorlar ro'yxatiga qo'shiladigan ustunlar ---------- */

/// <summary>
/// Bitta o'quvchining ish oqimi holati — qarzdorlar jadvalining YANGI
/// ustunlari (§3.5: "joriy holat, oxirgi amal sanasi, va'da sanasi").
///
/// <para>
/// <b>Qarz summasi bu yerda YO'Q.</b> U <c>GET /admin/finance/debtors</c>
/// javobida keladi va ekranda <c>studentId</c> bo'yicha birlashtiriladi.
/// Ikki endpoint bitta raqamni ikki xil hisoblab qo'yishi mumkin bo'lgan
/// joyni ataylab yaratmadik.
/// </para>
/// </summary>
/// <param name="StudentId">O'quvchi id'si — ro'yxat shu bo'yicha birlashtiriladi.</param>
/// <param name="FullName">F.I.SH. (alohida ekranlar uchun).</param>
/// <param name="ClassName">Sinf.</param>
/// <param name="StatusId">JORIY holat = eng oxirgi tirik amalning holati.
/// null = oxirgi amal holatni o'zgartirmagan yoki hech qachon qo'yilmagan.</param>
/// <param name="StatusName">Joriy holat nomi.</param>
/// <param name="StatusColor">Joriy holat rangi.</param>
/// <param name="LastActionAt">Oxirgi amal qachon yozilgan.</param>
/// <param name="LastComment">Oxirgi izoh (jadvalda qisqartirib ko'rsatiladi).</param>
/// <param name="LastActionByName">Oxirgi amalni kim yozgan.</param>
/// <param name="PromisedOn">Eng oxirgi kelishilgan to'lov sanasi.</param>
/// <param name="PromiseBroken">Va'da o'tib ketgan VA qarz hali ochiq —
/// beshinchi <c>AnomalyKind</c> bilan AYNAN bir xil shart (server hisoblaydi).</param>
/// <param name="ActionCount">Tirik amallar soni.</param>
public record DebtorWorkflowRowDto(
    string StudentId,
    string FullName,
    string ClassName,
    Guid? StatusId,
    string? StatusName,
    string? StatusColor,
    DateTimeOffset? LastActionAt,
    string? LastComment,
    string? LastActionByName,
    DateOnly? PromisedOn,
    bool PromiseBroken,
    int ActionCount);

/* ---------- Buzilgan va'da (beshinchi anomaliya) ---------- */

/// <summary>
/// Buzilgan va'da: <c>promised_on</c> o'tib ketgan, qarz esa hali ochiq
/// (§3.5). Direktor paneliga beshinchi <c>AnomalyKind</c> sifatida chiqadi.
///
/// <para>
/// <b>Bu qator bazada SAQLANMAYDI</b> — u har so'rovda hisoblanadi.
/// Sabab <see cref="SchoolLms.Application.Billing.BrokenPromiseScan"/> boshida
/// batafsil yozilgan: va'da ertaga to'lov tushishi bilan o'z-o'zidan yopiladi,
/// ya'ni u HODISA emas, HOLAT.
/// </para>
/// </summary>
/// <param name="ActionId">Va'dani yozgan amal (tarixga o'tish uchun).</param>
/// <param name="StudentId">O'quvchi.</param>
/// <param name="FullName">F.I.SH.</param>
/// <param name="ClassName">Sinf.</param>
/// <param name="ParentPhone">Ota-ona telefoni — bog'lanish uchun.</param>
/// <param name="PromisedOn">Va'da qilingan sana (o'tib ketgan).</param>
/// <param name="DaysLate">Va'dadan beri necha kun o'tdi.</param>
/// <param name="Debt">Hali ochiq qarz (so'm) — SERVER hisoblaydi.</param>
/// <param name="StatusName">Oxirgi holat nomi (bo'lsa).</param>
/// <param name="Comment">Va'da yozilgan izoh.</param>
/// <param name="CreatedByName">Va'dani kim yozgan.</param>
/// <param name="CreatedAt">Va'da qachon yozilgan.</param>
public record BrokenPromiseDto(
    Guid ActionId,
    string StudentId,
    string FullName,
    string ClassName,
    string ParentPhone,
    DateOnly PromisedOn,
    int DaysLate,
    decimal Debt,
    string? StatusName,
    string Comment,
    string CreatedByName,
    DateTimeOffset CreatedAt);
